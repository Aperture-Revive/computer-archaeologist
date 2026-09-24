using ComputerArchaeologist.Core.Ai;
using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Reports;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>Report generation must always succeed, with or without an AI narrative.</summary>
public sealed class ReportGenerationServiceTests
{
    private static FileArtifact Artifact(
        string name,
        string directory,
        string extension,
        double finalScore,
        double localScore,
        DateTimeOffset? created,
        DateTimeOffset? modified,
        string category = ArtifactCategories.ForgottenProject,
        AiAnalysisResult? ai = null) =>
        new()
        {
            File = new FileMetadata
            {
                FullPath = Path.Combine(directory, name),
                FileName = name,
                Extension = extension,
                DirectoryPath = directory,
                SizeBytes = 4096,
                CreatedUtc = created,
                ModifiedUtc = modified,
                AccessedUtc = modified,
                Attributes = FileAttributes.Normal,
                Source = "test",
            },
            Score = new InterestingnessResult
            {
                LocalScore = localScore,
                AiScore = ai?.Interestingness ?? 0,
                FinalScore = finalScore,
                Confidence = ai?.Confidence ?? 0,
                AiApplied = ai is not null,
                Ai = ai,
                Local = new LocalScoreBreakdown
                {
                    LocalScore = localScore,
                    Age = 70,
                    Rarity = 60,
                    PathContext = 65,
                    Filename = 20,
                    Cluster = 80,
                    PersonalArtifact = 75,
                    ModificationPattern = 85,
                    Unusualness = 15,
                },
            },
            Context = new FileContext { DirectoryPath = directory, SiblingCount = 12, LooksLikeProject = true },
            Category = category,
            Summary = $"Summary for {name}",
            Evidence = new[] { "Evidence A", "Evidence B" },
        };

    private static ArchaeologySession Session(params FileArtifact[] artifacts) => new()
    {
        IndexedFileCount = 428_213,
        FilesDiscovered = 9_000,
        Candidates = 400,
        FilesAnalyzedByAi = artifacts.Length,
        AiAvailable = true,
        FinalDiscoveries = artifacts,
        EndTime = DateTimeOffset.UtcNow.AddMinutes(3),
    };

    [Fact]
    public async Task A_report_is_generated_locally_when_the_ai_narrative_is_unavailable()
    {
        var localization = new ReswLocalizationService(initialLanguage: "en-US");
        var ai = new StubAiAnalysisService { Narrative = null };
        var service = new ReportGenerationService(ai, localization);

        var session = Session(
            Artifact("main.cs", @"D:\OldProjects\MyFirstGame", ".cs", 91, 84,
                new DateTimeOffset(2017, 8, 12, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2018, 2, 3, 0, 0, 0, TimeSpan.Zero)),
            Artifact("notes.txt", @"C:\Users\Me\Documents\School", ".txt", 72, 70,
                new DateTimeOffset(2012, 3, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2012, 4, 1, 0, 0, 0, TimeSpan.Zero),
                category: ArtifactCategories.OldDocument));

        var report = await service.GenerateAsync(session, allowAiNarrative: true);

        Assert.False(report.NarrativeFromAi);
        Assert.False(string.IsNullOrWhiteSpace(report.Overview));
        Assert.False(string.IsNullOrWhiteSpace(report.FinalSummary));
        Assert.Contains("9,000", report.Overview, StringComparison.Ordinal);
        Assert.Contains("400", report.Overview, StringComparison.Ordinal);
        Assert.Equal(2, report.DiscoveriesCount);
        Assert.NotEmpty(report.Sections);
        Assert.NotEmpty(report.Timeline);

        // The timeline must be chronological and carry the highlight artifact.
        Assert.Equal(report.Timeline.OrderBy(t => t.Year).Select(t => t.Year), report.Timeline.Select(t => t.Year));
        Assert.Contains(report.Timeline, t => t.Year == 2012);
        Assert.Contains(report.Timeline, t => t.Year == 2017);
    }

    [Fact]
    public async Task The_overview_states_only_measured_numbers()
    {
        var localization = new ReswLocalizationService(initialLanguage: "en-US");
        var service = new ReportGenerationService(new StubAiAnalysisService(), localization);

        var session = Session(Artifact("main.cs", @"D:\Old", ".cs", 80, 80, null, null));
        session.IndexedFileCount = 0;

        var report = await service.GenerateAsync(session, allowAiNarrative: false);

        // Nothing is claimed about the machine beyond what the run actually inspected.
        Assert.Contains("9,000", report.Overview, StringComparison.Ordinal);
        Assert.DoesNotContain("index contains", report.Overview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ai_narrative_is_used_and_marked_as_an_interpretation()
    {
        var localization = new ReswLocalizationService(initialLanguage: "en-US");
        var ai = new StubAiAnalysisService
        {
            Narrative = new AiReportNarrative
            {
                Overview = "AI overview text.",
                Observations = "AI observations text.",
                FinalSummary = "AI final summary text.",
                Highlights = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [Path.Combine(@"D:\OldProjects\MyFirstGame", "main.cs")] = "A forgotten prototype.",
                },
            },
        };

        var service = new ReportGenerationService(ai, localization);
        var artifact = Artifact("main.cs", @"D:\OldProjects\MyFirstGame", ".cs", 91, 84,
            new DateTimeOffset(2017, 8, 12, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2018, 2, 3, 0, 0, 0, TimeSpan.Zero));

        var report = await service.GenerateAsync(Session(artifact), allowAiNarrative: true);

        Assert.True(report.NarrativeFromAi);
        Assert.Equal("AI overview text.", report.Overview);
        Assert.Equal("AI observations text.", report.AiObservations);
        Assert.Equal("AI final summary text.", report.FinalSummary);
        Assert.Equal("A forgotten prototype.", artifact.NarrativeNote);
    }

    [Fact]
    public async Task A_failed_ai_analysis_surfaces_as_a_warning_and_not_as_a_crash()
    {
        var localization = new ReswLocalizationService(initialLanguage: "en-US");
        var ai = new StubAiAnalysisService();
        var service = new ReportGenerationService(ai, localization);

        var failed = AiAnalysisResult.Unavailable("Ai_Error_Timeout");
        var artifact = Artifact("main.cs", @"D:\OldProjects\MyFirstGame", ".cs", 84, 84,
            new DateTimeOffset(2017, 8, 12, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2018, 2, 3, 0, 0, 0, TimeSpan.Zero),
            ai: failed);

        var session = Session(artifact);
        session.AiAvailable = true;

        var report = await service.GenerateAsync(session, allowAiNarrative: true);

        Assert.Contains("Report_AiPartial", report.Warnings);
        Assert.False(string.IsNullOrWhiteSpace(report.Overview));
    }

    [Fact]
    public async Task An_empty_run_still_produces_a_readable_report()
    {
        var localization = new ReswLocalizationService(initialLanguage: "zh-CN");
        var ai = new StubAiAnalysisService();
        var service = new ReportGenerationService(ai, localization);

        var session = new ArchaeologySession
        {
            FilesDiscovered = 0,
            Candidates = 0,
            AiAvailable = false,
            FinalDiscoveries = Array.Empty<FileArtifact>(),
            EndTime = DateTimeOffset.UtcNow,
        };

        var report = await service.GenerateAsync(session, allowAiNarrative: true);

        Assert.Equal(0, report.DiscoveriesCount);
        Assert.DoesNotContain(report.Sections, s => !s.IsEmpty);
        Assert.Equal("计算机考古报告", report.Title);
        Assert.Contains("Report_AiUnavailable", report.Warnings);
        Assert.False(string.IsNullOrWhiteSpace(report.Overview));
    }

    [Fact]
    public async Task Section_classification_rules_are_stable()
    {
        var localization = new ReswLocalizationService(initialLanguage: "en-US");
        var service = new ReportGenerationService(new StubAiAnalysisService(), localization);

        var project = Artifact("main.cs", @"D:\OldProjects\Game", ".cs", 90, 88,
            new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2016, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var strange = Artifact("weird", @"C:\Users\Me\Documents\misc", string.Empty, 70, 68,
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero),
            category: ArtifactCategories.StrangeFile);

        var ancientDocument = Artifact("thesis.docx", @"C:\Users\Me\Documents\University", ".docx", 75, 73,
            new DateTimeOffset(2011, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2011, 6, 1, 0, 0, 0, TimeSpan.Zero),
            category: ArtifactCategories.OldDocument,
            ai: new AiAnalysisResult { Succeeded = true, Interestingness = 80, Confidence = 80, PersonalSignificance = 90 });

        var report = await service.GenerateAsync(
            Session(project, strange, ancientDocument),
            allowAiNarrative: false);

        Assert.Contains(project, report.Sections.Single(s => s.Id == "most_interesting").Artifacts);
        Assert.Contains(project, report.Sections.Single(s => s.Id == "forgotten_projects").Artifacts);
        Assert.Contains(strange, report.Sections.Single(s => s.Id == "strange_files").Artifacts);
        Assert.Contains(ancientDocument, report.Sections.Single(s => s.Id == "old_documents").Artifacts);
        Assert.Contains(ancientDocument, report.Sections.Single(s => s.Id == "historical_artifacts").Artifacts);
        Assert.Contains(ancientDocument, report.Sections.Single(s => s.Id == "important_files").Artifacts);
    }

    [Fact]
    public async Task Markdown_export_contains_every_section_and_no_api_key()
    {
        var localization = new ReswLocalizationService(initialLanguage: "en-US");
        var service = new ReportGenerationService(new StubAiAnalysisService(), localization);

        var artifact = Artifact("main.cs", @"D:\OldProjects\MyFirstGame", ".cs", 91, 84,
            new DateTimeOffset(2017, 8, 12, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2018, 2, 3, 0, 0, 0, TimeSpan.Zero));

        var report = await service.GenerateAsync(Session(artifact), allowAiNarrative: false);
        var markdown = ReportMarkdownWriter.Write(report, localization);

        Assert.StartsWith("# Computer Archaeology Report", markdown, StringComparison.Ordinal);
        Assert.Contains("## Overview", markdown, StringComparison.Ordinal);
        Assert.Contains("## Most Interesting Discoveries", markdown, StringComparison.Ordinal);
        Assert.Contains("## Timeline", markdown, StringComparison.Ordinal);
        Assert.Contains("## Final Summary", markdown, StringComparison.Ordinal);
        Assert.Contains("main.cs", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", markdown, StringComparison.Ordinal);
    }
}
