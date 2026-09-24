using System.Globalization;
using System.Text.Json;
using ComputerArchaeologist.Core.Models;

namespace ComputerArchaeologist.Core.Ai;

/// <summary>Outcome of validating a model response.</summary>
public sealed record AiParseResult
{
    public AiAnalysisResult? Result { get; init; }

    public bool Success => Result is not null;

    /// <summary>Fields the model omitted or sent in an unusable shape.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string? Error { get; init; }

    public static AiParseResult Failure(string error) => new() { Error = error };
}

/// <summary>
/// Parses and validates the structured JSON the semantic engine must return.
/// <para>
/// Everything the model can get wrong is handled here rather than in the pipeline: fenced code
/// blocks, prose before or after the object, numbers sent as strings, categories outside the
/// catalogue and values outside 0..100. A malformed answer never reaches the scoring stage.
/// </para>
/// </summary>
public static class AiJsonParser
{
    private static readonly string[] RequiredFields =
    {
        "interestingness", "confidence", "category", "summary", "recommended_action",
    };

    public static AiParseResult ParseAnalysis(string? raw, string model = "")
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return AiParseResult.Failure("empty-response");
        }

        var json = ExtractJsonObject(raw);
        if (json is null)
        {
            return AiParseResult.Failure("no-json-object");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 16,
            });
        }
        catch (JsonException ex)
        {
            return AiParseResult.Failure($"invalid-json: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return AiParseResult.Failure("root-is-not-an-object");
            }

            var warnings = new List<string>();

            foreach (var field in RequiredFields)
            {
                if (!TryGetProperty(root, field, out _))
                {
                    warnings.Add($"missing:{field}");
                }
            }

            var interestingness = ReadDouble(root, "interestingness", warnings, 0);
            var confidence = ReadDouble(root, "confidence", warnings, 0);
            var categoryRaw = ReadString(root, "category");
            var summary = ReadString(root, "summary");
            var actionRaw = ReadString(root, "recommended_action");

            var category = ArtifactCategories.Normalize(categoryRaw);
            if (!string.IsNullOrWhiteSpace(categoryRaw) && category == ArtifactCategories.Unknown &&
                !string.Equals(categoryRaw.Trim(), ArtifactCategories.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("category-out-of-catalogue");
            }

            var action = RecommendedActions.IsValid(actionRaw)
                ? actionRaw!.Trim().ToLowerInvariant()
                : RecommendedActions.Maybe;

            if (!RecommendedActions.IsValid(actionRaw))
            {
                warnings.Add("recommended_action-invalid");
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                warnings.Add("summary-empty");
            }

            var reasons = ReadStringArray(root, "reasons");

            var result = new AiAnalysisResult
            {
                Interestingness = Clamp(interestingness),
                Confidence = Clamp(confidence),
                Category = category,
                Summary = summary?.Trim() ?? string.Empty,
                Reasons = reasons,
                HistoricalSignificance = Clamp(ReadDouble(root, "historical_significance", warnings, 0)),
                PersonalSignificance = Clamp(ReadDouble(root, "personal_significance", warnings, 0)),
                TechnicalSignificance = Clamp(ReadDouble(root, "technical_significance", warnings, 0)),
                Rarity = Clamp(ReadDouble(root, "rarity", warnings, 0)),
                Unusualness = Clamp(ReadDouble(root, "unusualness", warnings, 0)),
                StoryPotential = Clamp(ReadDouble(root, "story_potential", warnings, 0)),
                RecommendedAction = action,
                Model = model,
                Succeeded = true,
            };

            return new AiParseResult { Result = result, Warnings = warnings };
        }
    }

    /// <summary>Pulls the first complete JSON object out of a response that may contain prose or fences.</summary>
    internal static string? ExtractJsonObject(string raw)
    {
        var text = raw.Trim();

        // Strip a ```json ... ``` fence when present.
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
            {
                text = text[(firstNewline + 1)..];
            }

            var close = text.LastIndexOf("```", StringComparison.Ordinal);
            if (close >= 0)
            {
                text = text[..close];
            }

            text = text.Trim();
        }

        var start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..(i + 1)];
                    }

                    break;
            }
        }

        return depth > 0 ? text[start..] : null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static double ReadDouble(JsonElement root, string name, List<string> warnings, double fallback)
    {
        if (!TryGetProperty(root, name, out var value))
        {
            return fallback;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.GetDouble();

            case JsonValueKind.String:
                if (double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    warnings.Add($"coerced-string:{name}");
                    return parsed;
                }

                warnings.Add($"unparsable:{name}");
                return fallback;

            case JsonValueKind.True:
            case JsonValueKind.False:
                warnings.Add($"unexpected-bool:{name}");
                return fallback;

            case JsonValueKind.Null:
                return fallback;

            default:
                warnings.Add($"unexpected-type:{name}");
                return fallback;
        }
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => null,
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var value))
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var single = value.GetString();
            return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single.Trim() };
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                results.Add(text.Trim());
            }
        }

        return results;
    }

    private static double Clamp(double value) => InterestingnessResult.Clamp(double.IsFinite(value) ? value : 0);
}
