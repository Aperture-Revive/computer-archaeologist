using ComputerArchaeologist.Core.Ai;
using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using ComputerArchaeologist.Core.Orchestration;
using ComputerArchaeologist.Core.Reports;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>Collects progress synchronously so assertions are deterministic.</summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly List<T> _items = new();

    public IReadOnlyList<T> Items => _items;

    public void Report(T value) => _items.Add(value);
}

/// <summary>
/// End-to-end runs of the seven stage pipeline over a real folder on disk, using the real bounded
/// walker, the real scoring engine and the real report writer. Only the AI service is substituted.
/// </summary>
public sealed class ArchaeologyPipelineTests
{
    private static (ArchaeologyPipeline Pipeline, StubAiAnalysisService Ai, ArchaeologyOptions Options, DiscoveryOptions Discovery)
        BuildPipeline(string? includedRoot = null, bool aiConfigured = true)
    {
        var archaeology = new ArchaeologyOptions
        {
            MinInterestingness = 0,
            MaxDiscoveries = 50,
            MaxCandidates = 500,
            MaxAiAnalysis = 50,
            PrivacyMode = PrivacyMode.MetadataOnly,
            ComputeHashesForTopCandidates = true,
            IncludedRoots = string.IsNullOrEmpty(includedRoot)
                ? new List<string>()
                : new List<string> { includedRoot },
        };

        var discovery = new DiscoveryOptions
        {
            MaxFiles = 10_000,
            BudgetMinutes = 5,
            Concurrency = 4,
        };

        var aiOptions = new OpenAiOptions { MaxConcurrency = 2 };
        var weights = new InterestingnessWeightsOptions();
        var localization = new ReswLocalizationService(initialLanguage: "en-US");

        // The built-in exclusion rules would skip the temp folder and any "bin" segment, so the test
        // host uses a policy built only from the options it sets.
        var policyProvider = new ExclusionPolicyProvider(archaeology, useDefaults: false);

        var ai = new StubAiAnalysisService { IsConfigured = aiConfigured };
        var analysis = new FileAnalysisService(archaeology);
        var calculator = new InterestingnessCalculator(weights);
        var reports = new ReportGenerationService(ai, localization);

        var pipeline = new ArchaeologyPipeline(
            new LocalFileDiscoveryService(policyProvider, discovery),
            analysis,
            calculator,
            ai,
            reports,
            localization,
            archaeology,
            discovery,
            aiOptions);

        return (pipeline, ai, archaeology, discovery);
    }

    private static TempFixture CreateFixture()
    {
        var fixture = new TempFixture();
        var created = new DateTime(2016, 5, 4, 12, 0, 0, DateTimeKind.Utc);
        var modified = new DateTime(2016, 8, 19, 9, 30, 0, DateTimeKind.Utc);

        fixture.WriteFile(@"MyFirstGame\README.txt", "My first game. TODO: finish it.", created, modified);
        fixture.WriteFile(@"MyFirstGame\main.cs", "class Game { static void Main() { } }", created, modified);
        fixture.WriteFile(@"MyFirstGame\player.cs", "class Player { }", created, modified);
        fixture.WriteFile(@"MyFirstGame\map.txt", "level data", created, modified);
        fixture.WriteFile(@"MyFirstGame\project.json", "{\"name\":\"myfirstgame\"}", created, modified);
        fixture.WriteFile(@"School\Grade8\homework.txt", "physics homework", created, modified);
        fixture.WriteFile(@"School\Grade8\essay-final-draft.txt", "an essay", created, modified);
        fixture.WriteFile("plain.txt", "an ordinary recent file", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1));

        return fixture;
    }

    [Fact]
    public async Task A_full_run_discovers_scores_and_reports_real_files()
    {
        using var fixture = CreateFixture();
        var (pipeline, ai, options, _) = BuildPipeline(fixture.Root);

        var progress = new SyncProgress<ScanProgress>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var request = new ArchaeologyRequest
        {
            Roots = new[] { fixture.Root },
            Language = "en-US",
        };

        var session = await pipeline.RunAsync(request, progress, cts.Token);

        // ---- discovery worked over a real directory tree ----
        Assert.False(session.Cancelled);
        Assert.Equal("Discovery_Source_Local", session.DiscoverySource);
        Assert.True(session.FilesDiscovered >= 8, $"Only {session.FilesDiscovered} files were discovered.");
        Assert.True(session.Candidates >= 8, $"Only {session.Candidates} candidates survived scoring.");

        // ---- AI analysis genuinely ran ----
        Assert.True(ai.AnalyzeCallCount > 0, "The AI stage was never invoked.");
        Assert.True(session.AiAvailable);
        Assert.True(session.FilesAnalyzedByAi > 0);

        // ---- discoveries are complete, ranked and bounded ----
        Assert.NotEmpty(session.FinalDiscoveries);
        Assert.True(session.FinalDiscoveries.Count <= options.MaxDiscoveries);

        for (var i = 0; i < session.FinalDiscoveries.Count; i++)
        {
            var artifact = session.FinalDiscoveries[i];
            Assert.InRange(artifact.Score.FinalScore, 0, 100);
            Assert.InRange(artifact.Score.LocalScore, 0, 100);
            Assert.InRange(artifact.Score.Confidence, 0, 100);
            Assert.Equal(i + 1, artifact.Rank);
            Assert.False(string.IsNullOrWhiteSpace(artifact.Summary));
            Assert.True(File.Exists(artifact.File.FullPath), $"{artifact.File.FullPath} does not exist.");
            Assert.False(string.IsNullOrWhiteSpace(artifact.Category));

            if (i > 0)
            {
                Assert.True(
                    session.FinalDiscoveries[i - 1].Score.FinalScore >= artifact.Score.FinalScore,
                    "Discoveries must be ranked by descending fused score.");
            }
        }

        // ---- the abandoned project must out-rank the boring recent file ----
        var project = session.FinalDiscoveries.FirstOrDefault(a => a.FileName == "main.cs");
        var plain = session.FinalDiscoveries.FirstOrDefault(a => a.FileName == "plain.txt");
        Assert.NotNull(project);
        if (plain is not null)
        {
            Assert.True(project!.Score.FinalScore > plain.Score.FinalScore,
                "An abandoned 2016 source project must out-rank a recent scratch file.");
        }

        // ---- hashing happened only for finalists ----
        Assert.Contains(session.FinalDiscoveries, a => !string.IsNullOrEmpty(a.File.Sha256));

        // ---- progress is real and complete ----
        var stages = progress.Items.Select(p => p.Stage).Distinct().ToArray();
        Assert.Contains(ScanStage.Preparing, stages);
        Assert.Contains(ScanStage.DiscoveringFiles, stages);
        Assert.Contains(ScanStage.LocalScoring, stages);
        Assert.Contains(ScanStage.AiAnalysis, stages);
        Assert.Contains(ScanStage.GeneratingReport, stages);
        Assert.Contains(ScanStage.Completed, stages);

        Assert.Contains(progress.Items, p => p.FilesScanned > 0);
        Assert.Contains(progress.Items, p => p.AiAnalyzed > 0);
        Assert.All(progress.Items, p => Assert.InRange(p.OverallProgress, 0, 1));

        // ---- the report is fully formed ----
        var report = session.Report;
        Assert.NotNull(report);
        Assert.False(string.IsNullOrWhiteSpace(report!.Overview));
        Assert.False(string.IsNullOrWhiteSpace(report.FinalSummary));
        Assert.False(string.IsNullOrWhiteSpace(report.AiObservations));
        Assert.NotEmpty(report.Sections);
        Assert.StartsWith("Computer Archaeology Report", report.Title, StringComparison.Ordinal);

        var populated = report.Sections.Where(s => !s.IsEmpty).ToArray();
        Assert.NotEmpty(populated);

        var mostInteresting = report.Sections.Single(s => s.Id == "most_interesting");
        Assert.NotEmpty(mostInteresting.Artifacts);
    }

    [Fact]
    public async Task A_run_without_ai_still_produces_a_report()
    {
        using var fixture = CreateFixture();
        var (pipeline, ai, options, _) = BuildPipeline(fixture.Root, aiConfigured: false);

        var request = new ArchaeologyRequest
        {
            Roots = new[] { fixture.Root },
            Language = "en-US",
            LocalOnly = true,
        };

        var session = await pipeline.RunAsync(request, null, CancellationToken.None);

        Assert.Equal(0, ai.AnalyzeCallCount);
        Assert.False(session.AiAvailable);
        Assert.NotEmpty(session.FinalDiscoveries);
        Assert.NotNull(session.Report);
        Assert.False(session.Report!.NarrativeFromAi);
        Assert.Contains("Report_AiSkipped", session.Report.Warnings);
        Assert.All(session.FinalDiscoveries, a => Assert.False(a.Score.AiApplied));
    }

    [Fact]
    public async Task A_missing_scope_degrades_gracefully_instead_of_crashing()
    {
        // A scope that does not exist yields an empty but valid session rather than an error.
        var (pipeline, _, _, _) = BuildPipeline();

        var session = await pipeline.RunAsync(
            new ArchaeologyRequest { Roots = new[] { Path.Combine(Path.GetTempPath(), "ca-does-not-exist-" + Guid.NewGuid().ToString("N")) } },
            null,
            CancellationToken.None);

        Assert.Empty(session.FinalDiscoveries);
        Assert.NotNull(session.Report);
    }

    [Fact]
    public async Task Cancellation_returns_a_partial_session_instead_of_throwing()
    {
        using var fixture = CreateFixture();
        var (pipeline, _, _, _) = BuildPipeline(fixture.Root);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var session = await pipeline.RunAsync(
            new ArchaeologyRequest { Roots = new[] { fixture.Root } },
            null,
            cts.Token);

        Assert.True(session.Cancelled);
        Assert.NotNull(session.EndTime);
        Assert.NotNull(session.UnwrapReportForTest());
    }

    [Fact]
    public async Task The_chosen_scope_is_actually_honoured()
    {
        // Two sibling trees: only the one in the run scope may ever be touched.
        using var fixture = new TempFixture();
        var created = new DateTime(2015, 3, 2, 10, 0, 0, DateTimeKind.Utc);
        fixture.WriteFile(@"InScope\main.cs", "class A { }", created, created);
        fixture.WriteFile(@"InScope\README.txt", "in scope", created, created);
        fixture.WriteFile(@"OutOfScope\secret.cs", "class B { }", created, created);
        fixture.WriteFile(@"OutOfScope\README.txt", "out of scope", created, created);

        var inScope = Path.Combine(fixture.Root, "InScope");
        var (pipeline, _, _, _) = BuildPipeline();

        var session = await pipeline.RunAsync(
            new ArchaeologyRequest { Roots = new[] { inScope } },
            null,
            CancellationToken.None);

        Assert.NotEmpty(session.FinalDiscoveries);
        Assert.Single(session.Roots);

        // Nothing outside the chosen folder may appear, in the results or in the scan counts.
        Assert.All(session.FinalDiscoveries, a =>
            Assert.StartsWith(inScope, a.File.FullPath, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, session.FilesDiscovered);
        Assert.DoesNotContain(session.FinalDiscoveries, a => a.FileName == "secret.cs");
    }

    [Fact]
    public async Task An_empty_scope_means_the_whole_machine_is_allowed()
    {
        using var fixture = new TempFixture();
        var created = new DateTime(2015, 3, 2, 10, 0, 0, DateTimeKind.Utc);
        fixture.WriteFile(@"Somewhere\note.txt", "hello", created, created);

        // No roots are passed, so the persistent include list must not silently restrict the run:
        // the plan reports the whole machine and the session records it as such.
        var (pipeline, _, options, _) = BuildPipeline();
        options.IncludedRoots = new List<string> { Path.Combine(fixture.Root, "Somewhere") };
        options.MaxDiscoveries = 5;

        var session = await pipeline.RunAsync(
            new ArchaeologyRequest { Roots = Array.Empty<string>() },
            null,
            CancellationToken.None);

        Assert.Empty(session.Roots);
        Assert.NotNull(session.Report);
    }

    [Fact]
    public async Task An_empty_tree_produces_an_empty_but_valid_session()
    {
        using var fixture = new TempFixture();
        var (pipeline, ai, _, _) = BuildPipeline(fixture.Root);

        var session = await pipeline.RunAsync(
            new ArchaeologyRequest { Roots = new[] { fixture.Root } },
            null,
            CancellationToken.None);

        Assert.False(session.Cancelled);
        Assert.Empty(session.FinalDiscoveries);
        Assert.Equal(0, ai.AnalyzeCallCount);
        Assert.NotNull(session.Report);
        Assert.Equal(0, session.Report!.DiscoveriesCount);
    }
}

internal static class SessionTestExtensions
{
    /// <summary>The pipeline always attaches a report, even for a cancelled run.</summary>
    public static ArchaeologyReport? UnwrapReportForTest(this ArchaeologySession session) => session.Report;
}
