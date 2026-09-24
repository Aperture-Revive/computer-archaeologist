using System.Globalization;
using System.Text;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Utilities;

namespace ComputerArchaeologist.Core.Reports;

/// <summary>
/// Renders a report as Markdown for export. The export is a convenience only: reports are always
/// readable inside the application and never require a browser.
/// </summary>
public static class ReportMarkdownWriter
{
    public static string Write(ArchaeologyReport report, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localization);

        var builder = new StringBuilder(8192);

        builder.Append("# ").AppendLine(string.IsNullOrWhiteSpace(report.Title) ? localization.Get("Report_Title") : report.Title);
        builder.AppendLine();
        builder.Append('>').Append(' ')
            .AppendLine(localization.Format("Report_GeneratedAt",
                report.GeneratedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)));
        builder.AppendLine();

        builder.Append("## ").AppendLine(localization.Get("Report_Overview"));
        builder.AppendLine();
        builder.AppendLine(report.Overview);
        builder.AppendLine();

        builder.Append("| ").Append(localization.Get("Report_Fact_Discovered"))
            .Append(" | ").Append(localization.Get("Report_Fact_Candidates"))
            .Append(" | ").Append(localization.Get("Report_Fact_AiAnalyzed"))
            .Append(" | ").Append(localization.Get("Report_Fact_Discoveries"))
            .Append(" | ").Append(localization.Get("Report_Fact_Scope"))
            .Append(" | ").Append(localization.Get("Report_Fact_Duration"))
            .AppendLine(" |");
        builder.Append("| ---: | ---: | ---: | ---: | --- | --- |").AppendLine();
        builder.Append("| ").Append(report.FilesDiscovered.ToString("N0", CultureInfo.CurrentCulture))
            .Append(" | ").Append(report.CandidatesExamined.ToString("N0", CultureInfo.CurrentCulture))
            .Append(" | ").Append(report.FilesAnalyzedByAi.ToString("N0", CultureInfo.CurrentCulture))
            .Append(" | ").Append(report.DiscoveriesCount.ToString("N0", CultureInfo.CurrentCulture))
            .Append(" | ").Append(string.IsNullOrWhiteSpace(report.Scope) ? localization.Get("Report_Scope_WholeMachine") : report.Scope)
            .Append(" | ").Append(FormatHelpers.Duration(report.Duration))
            .AppendLine(" |");
        builder.AppendLine();

        foreach (var warning in report.Warnings)
        {
            builder.Append("> ").AppendLine(localization.Get(warning));
        }

        if (report.Warnings.Count > 0)
        {
            builder.AppendLine();
        }

        foreach (var section in report.Sections.Where(s => !s.IsEmpty))
        {
            builder.Append("## ").AppendLine(localization.Get(section.TitleKey));
            builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(section.IntroKey))
            {
                builder.AppendLine(localization.Get(section.IntroKey!));
                builder.AppendLine();
            }

            var index = 1;
            foreach (var artifact in section.Artifacts)
            {
                builder.Append("### ").Append(localization.Format("Report_Discovery", index++)).Append(" — ").AppendLine(artifact.FileName);
                builder.AppendLine();
                builder.Append("`").Append(artifact.FullPath).Append('`').AppendLine();
                builder.AppendLine();
                builder.Append("- **").Append(localization.Get("Disc_ScoreLabel")).Append(":** ").Append(artifact.Score.FinalScore.ToString("0", CultureInfo.CurrentCulture)).Append(" / 100").AppendLine();
                builder.Append("- **").Append(localization.Get("Disc_Created")).Append(":** ").Append(FormatHelpers.Date(artifact.File.CreatedUtc)).AppendLine();
                builder.Append("- **").Append(localization.Get("Disc_Modified")).Append(":** ").Append(FormatHelpers.Date(artifact.File.ModifiedUtc)).AppendLine();
                builder.Append("- **").Append(localization.Get("Disc_Size")).Append(":** ").Append(FormatHelpers.FileSize(artifact.File.SizeBytes)).AppendLine();
                builder.Append("- **").Append(localization.Get("Disc_Filter")).Append(":** ").Append(localization.Get($"Category_{artifact.Category}")).AppendLine();
                builder.AppendLine();

                builder.Append("**").Append(localization.Get("Report_WhyInteresting")).Append("**").AppendLine();
                builder.AppendLine();
                builder.AppendLine(artifact.Summary);
                builder.AppendLine();

                if (!string.IsNullOrWhiteSpace(artifact.NarrativeNote))
                {
                    builder.Append("> ").AppendLine(artifact.NarrativeNote);
                    builder.AppendLine();
                }

                if (artifact.Evidence.Count > 0)
                {
                    builder.Append("**").Append(localization.Get("Detail_Evidence")).Append("**").AppendLine();
                    builder.AppendLine();
                    foreach (var evidence in artifact.Evidence)
                    {
                        builder.Append("- ").AppendLine(evidence);
                    }

                    builder.AppendLine();
                }
            }
        }

        if (report.Timeline.Count > 0)
        {
            builder.Append("## ").AppendLine(localization.Get("Report_Timeline"));
            builder.AppendLine();
            foreach (var entry in report.Timeline)
            {
                builder.Append("- **").Append(entry.Year.ToString(CultureInfo.InvariantCulture)).Append("** — ")
                    .Append(localization.Format("Report_Timeline_Count", entry.ArtifactCount.ToString("N0", CultureInfo.CurrentCulture)));
                if (!string.IsNullOrWhiteSpace(entry.HighlightName))
                {
                    builder.Append(" · ").Append(entry.HighlightName);
                }

                builder.AppendLine();
            }

            builder.AppendLine();
        }

        builder.Append("## ").AppendLine(localization.Get("Report_AiObservations"));
        builder.AppendLine();
        builder.AppendLine(report.AiObservations);
        builder.AppendLine();

        builder.Append("## ").AppendLine(localization.Get("Report_FinalSummary"));
        builder.AppendLine();
        builder.AppendLine(report.FinalSummary);
        builder.AppendLine();

        return builder.ToString();
    }
}
