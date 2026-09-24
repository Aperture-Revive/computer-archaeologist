using ComputerArchaeologist.Core.Ai;
using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Reports;

/// <summary>
/// Builds the archaeology report from measured data. The report always renders, with or without AI:
/// the narrative paragraphs are the only part that comes from the model, and they are clearly
/// separated from the facts (specification section 35).
/// </summary>
public interface IReportGenerationService
{
    Task<ArchaeologyReport> GenerateAsync(
        ArchaeologySession session,
        bool allowAiNarrative,
        CancellationToken cancellationToken = default);
}

public sealed class ReportGenerationService : IReportGenerationService
{
    /// <summary>Section order and localisation keys. Intros are listed explicitly so the key can
    /// never drift out of sync with the catalogue.</summary>
    private static readonly (string Id, string TitleKey, string IntroKey)[] SectionDefinitions =
    {
        ("most_interesting", "Report_MostInteresting", "Report_MostInteresting_Intro"),
        ("forgotten_projects", "Report_ForgottenProjects", "Report_ForgottenProjects_Intro"),
        ("old_documents", "Report_OldDocuments", "Report_OldDocuments_Intro"),
        ("strange_files", "Report_StrangeFiles", "Report_StrangeFiles_Intro"),
        ("historical_artifacts", "Report_HistoricalArtifacts", "Report_HistoricalArtifacts_Intro"),
        ("important_files", "Report_ImportantFiles", "Report_ImportantFiles_Intro"),
    };

    private readonly IAiAnalysisService _ai;
    private readonly ILocalizationService _localization;
    private readonly ILogger<ReportGenerationService>? _logger;

    public ReportGenerationService(
        IAiAnalysisService ai,
        ILocalizationService localization,
        ILogger<ReportGenerationService>? logger = null)
    {
        _ai = ai;
        _localization = localization;
        _logger = logger;
    }

    public async Task<ArchaeologyReport> GenerateAsync(
        ArchaeologySession session,
        bool allowAiNarrative,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var discoveries = session.FinalDiscoveries
            .OrderByDescending(a => a.Score.FinalScore)
            .ToArray();

        var warnings = new List<string>();
        if (!session.AiAvailable)
        {
            warnings.Add("Report_AiUnavailable");
        }
        else if (session.FinalDiscoveries.Any(a => a.Score.Ai is { Succeeded: false }))
        {
            warnings.Add("Report_AiPartial");
        }

        if (session.Cancelled)
        {
            warnings.Add("Report_CancelledPartial");
        }

        if (session.Warnings.Count > 0)
        {
            warnings.AddRange(session.Warnings);
        }

        var facts = new ReportFacts
        {
            IndexedFiles = session.IndexedFileCount,
            FilesDiscovered = session.FilesDiscovered,
            Candidates = session.Candidates,
            AnalyzedByAi = session.FilesAnalyzedByAi,
            Duration = session.Duration,
            SourceLabel = _localization.Get(
                string.IsNullOrWhiteSpace(session.DiscoverySource)
                    ? "Discovery_Source_Local"
                    : session.DiscoverySource),
            AiAvailable = session.AiAvailable,
        };

        var sections = BuildSections(discoveries);
        var timeline = BuildTimeline(discoveries);

        AiReportNarrative? narrative = null;
        if (allowAiNarrative && session.AiAvailable && discoveries.Length > 0)
        {
            try
            {
                narrative = await _ai.GenerateReportNarrativeAsync(
                    new AiReportRequest
                    {
                        Discoveries = discoveries,
                        Facts = facts,
                        Language = session.Language,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "The AI report narrative could not be generated; using the local template");
            }
        }

        var overview = narrative is { Overview.Length: > 0 }
            ? narrative.Overview
            : BuildLocalOverview(facts, discoveries.Length, session.AiAvailable);

        var observations = narrative is { Observations.Length: > 0 }
            ? narrative.Observations
            : BuildLocalObservations(discoveries, session.AiAvailable);

        var finalSummary = narrative is { FinalSummary.Length: > 0 }
            ? narrative.FinalSummary
            : _localization.Format("Report_FinalSummary_Local", discoveries.Length);

        if (narrative is { Highlights.Count: > 0 })
        {
            // Fold the model's per-artifact notes back in, matched strictly by full path.
            foreach (var artifact in discoveries)
            {
                if (narrative.Highlights.TryGetValue(artifact.FullPath, out var note) &&
                    !string.IsNullOrWhiteSpace(note))
                {
                    artifact.NarrativeNote = note;
                }
            }
        }

        return new ArchaeologyReport
        {
            Title = _localization.Get("Report_Title"),
            Language = _localization.CurrentLanguage,
            GeneratedUtc = DateTimeOffset.UtcNow,
            IndexedFileCount = facts.IndexedFiles,
            FilesDiscovered = facts.FilesDiscovered,
            CandidatesExamined = facts.Candidates,
            FilesAnalyzedByAi = facts.AnalyzedByAi,
            DiscoveriesCount = discoveries.Length,
            DiscoverySource = facts.SourceLabel,
            Scope = session.Roots.Count == 0
                ? null
                : _localization.Format("Report_Scope_Roots", session.Roots.Count),
            Duration = facts.Duration,
            AiAvailable = session.AiAvailable,
            PartialAiFailure = warnings.Contains("Report_AiPartial"),
            AiError = session.AiError,
            Overview = overview,
            Sections = sections,
            Timeline = timeline,
            AiObservations = observations,
            FinalSummary = finalSummary,
            NarrativeFromAi = narrative is not null,
            Warnings = warnings.Distinct().ToArray(),
        };
    }

    private IReadOnlyList<ReportSection> BuildSections(IReadOnlyList<FileArtifact> discoveries)
    {
        var result = new List<ReportSection>(SectionDefinitions.Length);

        foreach (var (id, titleKey, introKey) in SectionDefinitions)
        {
            var matches = id switch
            {
                "most_interesting" => discoveries.Take(8).ToArray(),
                "forgotten_projects" => discoveries.Where(IsForgottenProject).Take(12).ToArray(),
                "old_documents" => discoveries.Where(IsOldDocument).Take(12).ToArray(),
                "strange_files" => discoveries.Where(IsStrange).Take(12).ToArray(),
                "historical_artifacts" => discoveries.Where(IsHistorical).Take(12).ToArray(),
                "important_files" => discoveries.Where(IsPotentiallyImportant).Take(12).ToArray(),
                _ => Array.Empty<FileArtifact>(),
            };

            result.Add(new ReportSection
            {
                Id = id,
                TitleKey = titleKey,
                IntroKey = introKey,
                Artifacts = matches,
            });
        }

        return result;
    }

    internal static bool IsForgottenProject(FileArtifact artifact)
    {
        if (artifact.Category == ArtifactCategories.ForgottenProject)
        {
            return true;
        }

        var family = FileTypeCatalog.FamilyOf(artifact.File.Extension);
        var abandoned = artifact.Score.Local.ModificationPattern >= 60;
        return abandoned && family is FileFamily.Project or FileFamily.Code or FileFamily.ThreeD or FileFamily.Design;
    }

    internal static bool IsOldDocument(FileArtifact artifact) =>
        artifact.Category is ArtifactCategories.OldDocument or ArtifactCategories.SchoolWork or ArtifactCategories.PersonalNote
        || (artifact.Score.Local.Age >= 55 && FileTypeCatalog.FamilyOf(artifact.File.Extension) is FileFamily.Document or FileFamily.Text or FileFamily.Spreadsheet or FileFamily.Presentation);

    internal static bool IsStrange(FileArtifact artifact) =>
        artifact.Category == ArtifactCategories.StrangeFile
        || artifact.Score.Local.Unusualness >= 60
        || (artifact.Score.Ai?.Unusualness ?? 0) >= 75;

    internal static bool IsHistorical(FileArtifact artifact)
    {
        var age = FormatHelpers.YearsSince(artifact.File.CreatedUtc ?? artifact.File.ModifiedUtc, DateTimeOffset.UtcNow);
        return age >= 5 || (artifact.Score.Ai?.HistoricalSignificance ?? 0) >= 70 || artifact.Score.Local.Age >= 70;
    }

    internal static bool IsPotentiallyImportant(FileArtifact artifact) =>
        (artifact.Score.Ai?.PersonalSignificance ?? 0) >= 75
        || (artifact.Score.Ai is { Include: true, Confidence: >= 60 })
        || artifact.Category is ArtifactCategories.PersonalNote or ArtifactCategories.GameSave
        || FileTypeCatalog.FamilyOf(artifact.File.Extension) == FileFamily.Database;

    internal static IReadOnlyList<ReportTimelineEntry> BuildTimeline(IReadOnlyList<FileArtifact> discoveries)
    {
        var entries = new List<ReportTimelineEntry>();

        var byYear = discoveries
            .Select(a => new
            {
                Artifact = a,
                Year = (a.File.CreatedUtc ?? a.File.ModifiedUtc)?.ToLocalTime().Year,
            })
            .Where(x => x.Year is >= 1980 and <= 2100)
            .GroupBy(x => x.Year!.Value)
            .OrderBy(g => g.Key);

        foreach (var group in byYear)
        {
            var best = group.OrderByDescending(x => x.Artifact.Score.FinalScore).First().Artifact;
            entries.Add(new ReportTimelineEntry
            {
                Year = group.Key,
                ArtifactCount = group.Count(),
                HighlightName = best.FileName,
                HighlightPath = best.FullPath,
                HighlightScore = best.Score.FinalScore,
            });
        }

        return entries;
    }

    private string BuildLocalOverview(ReportFacts facts, int discoveryCount, bool aiAvailable)
    {
        // Only measured numbers are stated. The run knows how many files it inspected and how many
        // candidates it scored; it never guesses totals for the machine.
        return _localization.Format(
            aiAvailable ? "Report_Overview_Run_Ai" : "Report_Overview_Run_Local",
            facts.FilesDiscovered.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            facts.Candidates.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            discoveryCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
    }

    private string BuildLocalObservations(IReadOnlyList<FileArtifact> discoveries, bool aiAvailable)
    {
        if (discoveries.Count == 0)
        {
            return _localization.Get("Report_Observations_Empty");
        }

        var oldest = discoveries
            .Where(a => a.File.CreatedUtc is not null)
            .OrderBy(a => a.File.CreatedUtc)
            .FirstOrDefault();

        var categories = discoveries
            .GroupBy(a => a.Category)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{_localization.Get($"Category_{g.Key}")} ({g.Count()})");

        return _localization.Format(
            "Report_Observations_Local",
            oldest is null ? "—" : $"{oldest.FileName} ({FormatHelpers.Year(oldest.File.CreatedUtc)})",
            string.Join(", ", categories));
    }
}
