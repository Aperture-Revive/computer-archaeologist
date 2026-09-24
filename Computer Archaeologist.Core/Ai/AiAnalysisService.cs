using System.ClientModel;
using System.Diagnostics;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using ComputerArchaeologist.Core.Security;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace ComputerArchaeologist.Core.Ai;

/// <summary>An AI failure that already knows how it should be shown to the user.</summary>
public sealed class AiRequestException : Exception
{
    public AiRequestException(string messageKey, string? detail = null, int? statusCode = null, Exception? inner = null)
        : base(detail ?? messageKey, inner)
    {
        MessageKey = messageKey;
        Detail = detail;
        StatusCode = statusCode;
    }

    public string MessageKey { get; }

    public string? Detail { get; }

    public int? StatusCode { get; }
}

/// <summary>
/// OpenAI-compatible implementation of <see cref="IAiAnalysisService"/> built on the official
/// OpenAI .NET SDK.
/// <para>
/// Responsibilities kept here and nowhere else: endpoint/model configuration, bounded exponential
/// backoff for 429/5xx/timeouts, one JSON repair round trip, and mapping every failure onto a
/// human-readable localisation key. The API key is read from secure storage on demand and is never
/// logged, never persisted in settings and never surfaced in an exception message.
/// </para>
/// </summary>
public sealed class AiAnalysisService : IAiAnalysisService, IDisposable
{
    private readonly OpenAiOptions _options;
    private readonly ArchaeologyOptions _archaeology;
    private readonly ISecureStorage _secureStorage;
    private readonly ILogger<AiAnalysisService>? _logger;

    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private OpenAIClient? _client;
    private string? _clientKeyFingerprint;
    private string? _clientEndpoint;
    private volatile bool _jsonModeDisabled;

    public AiAnalysisService(
        OpenAiOptions options,
        ArchaeologyOptions archaeology,
        ISecureStorage secureStorage,
        ILogger<AiAnalysisService>? logger = null)
    {
        _options = options;
        _archaeology = archaeology;
        _secureStorage = secureStorage;
        _logger = logger;
    }

    public string Model => string.IsNullOrWhiteSpace(_options.Model) ? "gpt-4o-mini" : _options.Model.Trim();

    public bool IsConfigured =>
        _archaeology.PrivacyMode != PrivacyMode.Disabled &&
        _options.HasUsableEndpoint &&
        !string.IsNullOrWhiteSpace(_secureStorage.GetApiKey());

    // ------------------------------------------------------------------ public API

    public async Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var key = _secureStorage.GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new AiConnectionTestResult { Success = false, MessageKey = "Ai_Error_NoKey" };
        }

        if (!_options.HasUsableEndpoint)
        {
            return new AiConnectionTestResult { Success = false, MessageKey = "Ai_Error_InvalidEndpoint", Detail = _options.BaseUrl };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var messages = new ChatMessage[]
            {
                new SystemChatMessage("You are a connectivity probe. Answer only with JSON."),
                new UserChatMessage(AiPrompts.ConnectionProbePrompt),
            };

            var completion = await CompleteAsync(messages, maxTokens: 64, jsonMode: false, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(completion))
            {
                return new AiConnectionTestResult { Success = false, MessageKey = "Ai_TestFailed", Detail = "empty-response", Model = Model, Latency = stopwatch.Elapsed };
            }

            _logger?.LogInformation("AI connection test succeeded with model {Model} in {Elapsed} ms", Model, stopwatch.ElapsedMilliseconds);
            return new AiConnectionTestResult
            {
                Success = true,
                MessageKey = "Ai_Connected",
                Model = Model,
                Latency = stopwatch.Elapsed,
            };
        }
        catch (AiRequestException ex)
        {
            stopwatch.Stop();
            return new AiConnectionTestResult
            {
                Success = false,
                MessageKey = ex.MessageKey,
                Detail = ex.Detail,
                StatusCode = ex.StatusCode,
                Model = Model,
                Latency = stopwatch.Elapsed,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogWarning(ex, "AI connection test failed unexpectedly");
            return new AiConnectionTestResult { Success = false, MessageKey = "Ai_Error_Network", Detail = ex.Message, Model = Model, Latency = stopwatch.Elapsed };
        }
    }

    public async Task<AiAnalysisResult> AnalyzeFileAsync(AiFileRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_archaeology.PrivacyMode == PrivacyMode.Disabled)
        {
            return AiAnalysisResult.Unavailable("ai-disabled", Model);
        }

        if (string.IsNullOrWhiteSpace(_secureStorage.GetApiKey()))
        {
            return AiAnalysisResult.Unavailable("ai-not-configured", Model);
        }

        var payload = AiPrompts.BuildAnalysisPayload(request);
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(AiPrompts.SystemAnalysis),
            new UserChatMessage(payload),
        };

        try
        {
            var raw = await CompleteAsync(messages, _options.MaxOutputTokens, jsonMode: true, cancellationToken).ConfigureAwait(false);
            var parsed = AiJsonParser.ParseAnalysis(raw, Model);

            if (parsed.Success)
            {
                if (parsed.Warnings.Count > 0)
                {
                    _logger?.LogDebug("AI response for {Path} needed coercion: {Warnings}", request.File.FullPath, string.Join(", ", parsed.Warnings));
                }

                return parsed.Result!;
            }

            _logger?.LogWarning("AI returned unusable JSON for {Path}: {Error}. Requesting one repair.", request.File.FullPath, parsed.Error);

            // Exactly one repair attempt, as required by the specification.
            var repairMessages = new List<ChatMessage>(messages)
            {
                new AssistantChatMessage(Truncate(raw, 4000)),
                new UserChatMessage(AiPrompts.BuildRepairInstruction(raw)),
            };

            var repairedRaw = await CompleteAsync(repairMessages, _options.MaxOutputTokens, jsonMode: true, cancellationToken).ConfigureAwait(false);
            var repaired = AiJsonParser.ParseAnalysis(repairedRaw, Model);

            if (repaired.Success)
            {
                return repaired.Result! with { RequiredRepair = true };
            }

            _logger?.LogWarning("AI JSON repair failed for {Path}: {Error}", request.File.FullPath, repaired.Error);
            return AiAnalysisResult.Unavailable("Ai_Error_InvalidJson", Model);
        }
        catch (AiRequestException ex)
        {
            _logger?.LogWarning("AI analysis failed for {Path}: {Key} {Detail}", request.File.FullPath, ex.MessageKey, ex.Detail);
            return AiAnalysisResult.Unavailable(ex.MessageKey, Model);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected AI failure while analysing {Path}", request.File.FullPath);
            return AiAnalysisResult.Unavailable("Ai_Error_Network", Model);
        }
    }

    public async Task<AiReportNarrative?> GenerateReportNarrativeAsync(AiReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_archaeology.PrivacyMode == PrivacyMode.Disabled || request.Discoveries.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_secureStorage.GetApiKey()))
        {
            return null;
        }

        var payload = AiPrompts.BuildReportPayload(request);
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(AiPrompts.SystemReport),
            new UserChatMessage(payload),
        };

        try
        {
            var raw = await CompleteAsync(messages, maxTokens: 2000, jsonMode: true, cancellationToken).ConfigureAwait(false);
            var narrative = AiPrompts.ParseReportNarrative(raw);
            if (narrative is not null)
            {
                return narrative;
            }

            var repairMessages = new List<ChatMessage>(messages)
            {
                new AssistantChatMessage(Truncate(raw, 4000)),
                new UserChatMessage(AiPrompts.BuildRepairInstruction(raw)),
            };

            var repairedRaw = await CompleteAsync(repairMessages, maxTokens: 2000, jsonMode: true, cancellationToken).ConfigureAwait(false);
            return AiPrompts.ParseReportNarrative(repairedRaw);
        }
        catch (AiRequestException ex)
        {
            _logger?.LogWarning("AI report narrative failed: {Key} {Detail}", ex.MessageKey, ex.Detail);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected AI failure while generating the report narrative");
            return null;
        }
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Retries only what is safe to retry: 429, 5xx, timeouts and transport failures.</summary>
    private async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        int? maxTokens,
        bool jsonMode,
        CancellationToken cancellationToken)
    {
        var attempts = _options.SafeMaxRetries + 1;
        Exception? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await CompleteOnceAsync(messages, maxTokens, jsonMode, cancellationToken).ConfigureAwait(false);
            }
            catch (AiRequestException ex) when (ex.MessageKey is "Ai_Error_RateLimited" or "Ai_Error_Server" or "Ai_Error_Timeout" or "Ai_Error_Network")
            {
                last = ex;
                if (attempt >= attempts)
                {
                    break;
                }

                var delay = TimeSpan.FromSeconds(
                    Math.Clamp(_options.BaseRetryDelaySeconds, 0.25, 30) * Math.Pow(2, attempt - 1));

                // Jitter keeps several concurrent candidates from retrying in lockstep.
                delay += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));

                _logger?.LogWarning(
                    "AI request failed with {Key}; retry {Attempt}/{Total} in {Delay:0.0}s",
                    ex.MessageKey,
                    attempt,
                    attempts - 1,
                    delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw last ?? new AiRequestException("Ai_Error_Network");
    }

    private async Task<string> CompleteOnceAsync(
        IReadOnlyList<ChatMessage> messages,
        int? maxTokens,
        bool jsonMode,
        CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        var chat = client.GetChatClient(Model);

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxTokens ?? _options.MaxOutputTokens,
            Temperature = 0.2f,
        };

        if (jsonMode && _options.UseJsonResponseFormat && !_jsonModeDisabled)
        {
            options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.Timeout);

        try
        {
            ClientResult<ChatCompletion> result = await chat.CompleteChatAsync(messages, options, timeoutSource.Token).ConfigureAwait(false);
            var completion = result.Value;

            if (completion.Content is null || completion.Content.Count == 0)
            {
                throw new AiRequestException("Ai_Error_EmptyResponse", completion.FinishReason.ToString());
            }

            var text = string.Concat(completion.Content.Select(part => part.Text));
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new AiRequestException("Ai_Error_EmptyResponse", completion.FinishReason.ToString());
            }

            return text;
        }
        catch (ClientResultException ex)
        {
            var status = ex.Status;

            // Some OpenAI-compatible endpoints reject response_format: json_object outright.
            if (status == 400 && jsonMode && _options.UseJsonResponseFormat && !_jsonModeDisabled)
            {
                _jsonModeDisabled = true;
                _logger?.LogInformation("Endpoint rejected response_format=json_object; continuing without it for this session");
                return await CompleteOnceAsync(messages, maxTokens, jsonMode: false, cancellationToken).ConfigureAwait(false);
            }

            throw new AiRequestException(MapStatusToKey(status), SafeDetail(ex), status, ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiRequestException("Ai_Error_Timeout", $"No answer within {_options.Timeout.TotalSeconds:0}s");
        }
        catch (HttpRequestException ex)
        {
            throw new AiRequestException("Ai_Error_Network", ex.Message, null, ex);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiRequestException("Ai_Error_Timeout", "Request cancelled by the transport timeout");
        }
        catch (UriFormatException ex)
        {
            throw new AiRequestException("Ai_Error_InvalidEndpoint", ex.Message, null, ex);
        }
    }

    private async Task<OpenAIClient> GetClientAsync(CancellationToken cancellationToken)
    {
        var key = _secureStorage.GetApiKey() ?? string.Empty;
        var endpoint = NormalizeEndpoint(_options.BaseUrl);

        // Reuse the client unless the key or the endpoint changed.
        var fingerprint = $"{endpoint}|{key.Length}|{key.GetHashCode(StringComparison.Ordinal)}";

        await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null &&
                string.Equals(_clientKeyFingerprint, fingerprint, StringComparison.Ordinal) &&
                string.Equals(_clientEndpoint, endpoint, StringComparison.OrdinalIgnoreCase))
            {
                return _client;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new AiRequestException("Ai_Error_NoKey");
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                throw new AiRequestException("Ai_Error_InvalidEndpoint", endpoint);
            }

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = uri,
                NetworkTimeout = _options.Timeout,
                UserAgentApplicationId = "ComputerArchaeologist",
            };

            // The key is passed straight into the SDK credential; it is never stored on this object.
            _client = new OpenAIClient(new ApiKeyCredential(key), clientOptions);
            _clientKeyFingerprint = fingerprint;
            _clientEndpoint = endpoint;
            _logger?.LogInformation("AI client configured for endpoint {Endpoint} with model {Model}", endpoint, Model);
            return _client;
        }
        finally
        {
            _clientGate.Release();
        }
    }

    internal static string NormalizeEndpoint(string? baseUrl)
    {
        var value = (baseUrl ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "https://api.openai.com/v1";
        }

        return value.TrimEnd('/');
    }

    private static string MapStatusToKey(int status) => status switch
    {
        400 => "Ai_Error_BadRequest",
        401 => "Ai_Error_Unauthorized",
        403 => "Ai_Error_Forbidden",
        404 => "Ai_Error_NotFound",
        408 => "Ai_Error_Timeout",
        409 => "Ai_Error_BadRequest",
        422 => "Ai_Error_BadRequest",
        429 => "Ai_Error_RateLimited",
        >= 500 => "Ai_Error_Server",
        _ => "Ai_Error_Network",
    };

    /// <summary>Trims a provider error so no accidental credential ever lands in a log file.</summary>
    private static string SafeDetail(Exception ex)
    {
        var text = ex.Message ?? string.Empty;
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"sk-[A-Za-z0-9_\-]{8,}",
            "sk-***",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(100));
        return text.Length > 500 ? text[..500] : text;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    public void Dispose()
    {
        _clientGate.Dispose();
    }
}
