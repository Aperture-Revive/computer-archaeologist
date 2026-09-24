using ComputerArchaeologist.Core.Ai;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Reports;
using ComputerArchaeologist.Core.Settings;

namespace ComputerArchaeologist.Tests;

/// <summary>Deterministic stand-in for the real AI service.</summary>
internal sealed class StubAiAnalysisService : IAiAnalysisService
{
    public bool IsConfigured { get; set; } = true;

    public string Model => "stub-model";

    /// <summary>Result returned per file path. When absent a neutral result is produced.</summary>
    public Dictionary<string, AiAnalysisResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int AnalyzeCallCount { get; private set; }

    public TimeSpan AnalyzeDelay { get; set; } = TimeSpan.Zero;

    public AiReportNarrative? Narrative { get; set; }

    public AiConnectionTestResult ConnectionResult { get; set; } = new() { Success = true, MessageKey = "Ai_Connected", Model = "stub-model" };

    public Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ConnectionResult);

    public async Task<AiAnalysisResult> AnalyzeFileAsync(AiFileRequest request, CancellationToken cancellationToken = default)
    {
        AnalyzeCallCount++;

        if (AnalyzeDelay > TimeSpan.Zero)
        {
            await Task.Delay(AnalyzeDelay, cancellationToken).ConfigureAwait(false);
        }

        if (Results.TryGetValue(request.File.FullPath, out var result))
        {
            return result;
        }

        return new AiAnalysisResult
        {
            Succeeded = true,
            Interestingness = 60,
            Confidence = 70,
            Category = ArtifactCategories.ForgottenProject,
            Summary = "stub summary",
            RecommendedAction = RecommendedActions.Maybe,
            Model = Model,
        };
    }

    public Task<AiReportNarrative?> GenerateReportNarrativeAsync(AiReportRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Narrative);
}

/// <summary>Creates and removes a throw-away folder tree for a single test.</summary>
internal sealed class TempFixture : IDisposable
{
    /// <param name="outsideTempPath">
    /// When true the fixture is created under the user profile instead of the temporary folder. The
    /// production exclusion policy treats the machine temp directory as noise, so tests that exercise
    /// the default rules need a location the policy would not skip.
    /// </param>
    public TempFixture(bool outsideTempPath = false)
    {
        var parent = outsideTempPath
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.GetTempPath();

        Root = Path.Combine(parent, "ca-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string WriteFile(string relativePath, string content, DateTime? created = null, DateTime? modified = null)
    {
        var full = Path.Combine(Root, relativePath);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(full, content);

        if (created is not null)
        {
            File.SetCreationTimeUtc(full, created.Value);
        }

        if (modified is not null)
        {
            File.SetLastWriteTimeUtc(full, modified.Value);
        }

        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
