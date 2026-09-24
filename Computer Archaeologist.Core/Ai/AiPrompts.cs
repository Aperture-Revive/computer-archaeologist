using System.Globalization;
using System.Text;
using System.Text.Json;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Utilities;

namespace ComputerArchaeologist.Core.Ai;

/// <summary>
/// Builds every prompt the semantic engine sees. Kept in one place so the guarantees the
/// specification demands - metadata-first payloads, hard size limits, no invented facts, explicit
/// separation of fact from interpretation, and answer language - are auditable.
/// </summary>
public static class AiPrompts
{
    /// <summary>Hard ceiling for the excerpt embedded in a prompt, independent of the read limit.</summary>
    public const int MaxExcerptCharsInPrompt = 6000;

    public const string SystemAnalysis = """
        You are the semantic analysis engine of Computer Archaeologist.

        Your job is to determine whether a local computer file may be historically, personally,
        technically, culturally, or otherwise interesting to its owner.

        You must not assume that old files are automatically interesting. A ten-year-old cache file
        is worthless; a two-year-old abandoned prototype may be fascinating.

        You must distinguish, and score separately:
        - age
        - rarity
        - personal significance
        - technical significance
        - historical significance
        - unusualness
        - contextual significance
        - potential privacy sensitivity

        Rules you must never break:
        - You are only shown metadata, folder context and (sometimes) a short excerpt. Never claim to
          have read a file whose content was not supplied.
        - Never invent file creation times, file contents, the owner's identity, the author of a
          project, or the purpose of a file. If something is uncertain, say so with hedging such as
          "appears to be", "likely", "based on the available metadata", "cannot be determined".
        - Do not expose secrets. If the supplied excerpt contains credentials, tokens, passwords or
          personal identifiers, do not repeat them; refer to them abstractly.
        - The local scores you are given were computed on the user's machine from real measurements.
          Treat them as evidence, not as an answer to copy.
        - Answer ONLY with a single JSON object. No prose, no markdown, no code fences.

        The JSON object must use exactly these fields:
        {
          "interestingness": 0-100,
          "confidence": 0-100,
          "category": "forgotten_project|old_document|old_photo|game_save|source_code|strange_file|configuration|log_file|archive|personal_note|school_work|media|unknown",
          "summary": "one or two sentences explaining why this file is or is not interesting",
          "reasons": ["short evidence-backed statements"],
          "historical_significance": 0-100,
          "personal_significance": 0-100,
          "technical_significance": 0-100,
          "rarity": 0-100,
          "unusualness": 0-100,
          "story_potential": 0-100,
          "recommended_action": "include|maybe|exclude"
        }

        "confidence" must reflect how much the supplied evidence actually supports your judgement.
        If the evidence is thin, lower the confidence rather than guessing.
        """;

    public const string SystemReport = """
        You are the report writer of Computer Archaeologist.

        You are given measured facts about one archaeology run on a personal computer, plus the
        artifacts that survived local scoring and semantic analysis. Write the narrative parts of the
        archaeology report.

        Rules:
        - Never invent timestamps, file contents, the owner's identity, project authors or file
          purposes. Everything you state must be traceable to the supplied data.
        - Hedge appropriately: "appears to be", "likely", "based on the available metadata".
        - Never expose secrets or personal identifiers found in excerpts; refer to them abstractly.
        - Write warm, concrete, specific prose. No filler, no marketing language, no emoji.
        - Answer ONLY with a single JSON object. No prose, no markdown, no code fences.

        The JSON object must use exactly these fields:
        {
          "overview": "one short paragraph summarising what was examined and found",
          "observations": "one or two paragraphs of cross-cutting observations about the collection",
          "final_summary": "two or three sentences closing the report",
          "highlights": [ { "path": "the exact full path given to you", "note": "one sentence" } ]
        }
        """;

    public static string LanguageInstruction(string language) =>
        language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "Write every human-readable string value (summary, reasons, overview, observations, final_summary, highlights) in Simplified Chinese (简体中文). Keep JSON keys and the enumerated values in English."
            : "Write every human-readable string value (summary, reasons, overview, observations, final_summary, highlights) in English. Keep JSON keys and the enumerated values in English.";

    /// <summary>Compact, bounded, metadata-first description of one file.</summary>
    public static string BuildAnalysisPayload(AiFileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var file = request.File;
        var context = request.Context;
        var builder = new StringBuilder(2048);

        builder.AppendLine("FILE");

        // Deliberately quoted so the model treats paths as data, not instructions.
        builder.Append("file_name: ").AppendLine(Quote(file.FileName));
        builder.Append("full_path: ").AppendLine(Quote(file.FullPath));
        builder.Append("directory: ").AppendLine(Quote(file.DirectoryPath));
        builder.Append("extension: ").AppendLine(Quote(string.IsNullOrEmpty(file.Extension) ? "(none)" : file.Extension));
        builder.Append("size_bytes: ").AppendLine(file.SizeBytes.ToString(CultureInfo.InvariantCulture));
        builder.Append("size_human: ").AppendLine(FormatHelpers.FileSize(file.SizeBytes));
        builder.Append("created_utc: ").AppendLine(Format(file.CreatedUtc));
        builder.Append("modified_utc: ").AppendLine(Format(file.ModifiedUtc));
        builder.Append("accessed_utc: ").AppendLine(Format(file.AccessedUtc));
        builder.Append("age_years: ").AppendLine(FormatHelpers.YearsSince(file.CreatedUtc ?? file.ModifiedUtc, DateTimeOffset.UtcNow)
            .ToString("0.0", CultureInfo.InvariantCulture));
        builder.Append("attributes: ").AppendLine(file.Attributes.ToString());
        builder.Append("file_family: ").AppendLine(FileTypeCatalog.FamilyOf(file.Extension).ToString());
        builder.Append("detected_language: ").AppendLine(Quote(context.DetectedLanguage ?? "n/a"));
        builder.Append("is_executable: ").AppendLine(FileTypeCatalog.IsExecutable(file.Extension) ? "true" : "false");

        if (!string.IsNullOrEmpty(file.Sha256))
        {
            builder.Append("sha256: ").AppendLine(file.Sha256);
        }

        builder.AppendLine();
        builder.AppendLine("FOLDER CONTEXT");
        builder.Append("sibling_file_count: ").AppendLine(context.SiblingCount.ToString(CultureInfo.InvariantCulture));
        builder.Append("looks_like_a_project: ").AppendLine(context.LooksLikeProject ? "true" : "false");
        builder.Append("nearby_file_names: ");
        builder.AppendLine(context.NearbyFiles.Count == 0
            ? "(none observed)"
            : string.Join(", ", context.NearbyFiles.Take(40).Select(Quote)));

        if (!string.IsNullOrWhiteSpace(context.StructuralSummary))
        {
            builder.Append("structural_summary: ").AppendLine(Quote(context.StructuralSummary));
        }

        builder.AppendLine();
        builder.AppendLine("LOCAL MEASUREMENTS (computed on the user's machine, 0-100)");
        var local = request.Local;
        builder.Append("weighted_local_score: ").AppendLine(local.LocalScore.ToString("0.#", CultureInfo.InvariantCulture));
        builder.Append("age_feature: ").AppendLine(local.Age.ToString("0.#", CultureInfo.InvariantCulture));
        builder.Append("rarity_feature: ").AppendLine(local.Rarity.ToString("0.#", CultureInfo.InvariantCulture));
        builder.Append("path_context_feature: ").AppendLine(local.PathContext.ToString("0.#", CultureInfo.InvariantCulture));
        builder.Append("filename_feature: ").AppendLine(local.Filename.ToString("0.#", CultureInfo.InvariantCulture));
        builder.Append("folder_cluster_feature: ").AppendLine(local.Cluster.ToString("0.#", CultureInfo.InvariantCulture));
        builder.Append("personal_artifact_feature: ").AppendLine(local.PersonalArtifact.ToString("0.#", CultureInfo.InvariantCulture));
        builder.Append("modification_pattern_feature: ").AppendLine(local.ModificationPattern.ToString("0.#", CultureInfo.InvariantCulture));
        builder.Append("unusualness_feature: ").AppendLine(local.Unusualness.ToString("0.#", CultureInfo.InvariantCulture));

        if (context.ContentExcerpt is { Length: > 0 } excerpt)
        {
            builder.AppendLine();
            builder.AppendLine($"CONTENT EXCERPT (first {context.ContentBytesRead} bytes{(context.ContentTruncated ? ", truncated" : string.Empty)})");
            builder.AppendLine("<<<EXCERPT");
            builder.AppendLine(excerpt.Length > MaxExcerptCharsInPrompt ? excerpt[..MaxExcerptCharsInPrompt] : excerpt);
            builder.AppendLine("EXCERPT");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("CONTENT EXCERPT: not supplied. Judge from metadata only and lower your confidence accordingly.");
        }

        builder.AppendLine();
        builder.AppendLine(LanguageInstruction(request.Language));

        return builder.ToString();
    }

    /// <summary>Payload for the narrative report. Only the artifacts themselves, never whole files.</summary>
    public static string BuildReportPayload(AiReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder(8192);
        var facts = request.Facts;

        builder.AppendLine("RUN FACTS (measured)");
        builder.Append("indexed_files_on_machine: ").AppendLine(facts.IndexedFiles.ToString("N0", CultureInfo.InvariantCulture));
        builder.Append("files_discovered: ").AppendLine(facts.FilesDiscovered.ToString("N0", CultureInfo.InvariantCulture));
        builder.Append("candidates_locally_scored: ").AppendLine(facts.Candidates.ToString("N0", CultureInfo.InvariantCulture));
        builder.Append("candidates_analyzed_by_ai: ").AppendLine(facts.AnalyzedByAi.ToString("N0", CultureInfo.InvariantCulture));
        builder.Append("final_discoveries: ").AppendLine(request.Discoveries.Count.ToString("N0", CultureInfo.InvariantCulture));
        builder.Append("duration: ").AppendLine(FormatHelpers.Duration(facts.Duration));
        builder.Append("discovery_engine: ").AppendLine(facts.SourceLabel);
        builder.Append("ai_available_for_whole_run: ").AppendLine(facts.AiAvailable ? "true" : "false");
        builder.AppendLine();

        builder.AppendLine("DISCOVERIES (ranked by fused score)");
        foreach (var artifact in request.Discoveries)
        {
            builder.Append("- path: ").AppendLine(Quote(artifact.FullPath));
            builder.Append("  file_name: ").AppendLine(Quote(artifact.FileName));
            builder.Append("  extension: ").AppendLine(Quote(artifact.File.DisplayExtension));
            builder.Append("  family: ").AppendLine(FileTypeCatalog.FamilyOf(artifact.File.Extension).ToString());
            builder.Append("  created: ").AppendLine(Format(artifact.File.CreatedUtc));
            builder.Append("  modified: ").AppendLine(Format(artifact.File.ModifiedUtc));
            builder.Append("  size_bytes: ").AppendLine(artifact.File.SizeBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append("  local_score: ").AppendLine(artifact.Score.LocalScore.ToString("0.#", CultureInfo.InvariantCulture));
            builder.Append("  ai_score: ").AppendLine(artifact.Score.AiScore.ToString("0.#", CultureInfo.InvariantCulture));
            builder.Append("  fused_score: ").AppendLine(artifact.Score.FinalScore.ToString("0.#", CultureInfo.InvariantCulture));
            builder.Append("  ai_confidence: ").AppendLine(artifact.Score.Confidence.ToString("0.#", CultureInfo.InvariantCulture));
            builder.Append("  category: ").AppendLine(artifact.Category);
            builder.Append("  folder_looks_like_project: ").AppendLine(artifact.Context.LooksLikeProject ? "true" : "false");
            if (!string.IsNullOrWhiteSpace(artifact.Summary))
            {
                builder.Append("  prior_ai_summary: ").AppendLine(Quote(artifact.Summary));
            }

            builder.AppendLine();
        }

        builder.AppendLine(LanguageInstruction(request.Language));
        return builder.ToString();
    }

    /// <summary>Single repair attempt payload when the model did not return usable JSON.</summary>
    public static string BuildRepairInstruction(string invalidOutput) =>
        "Your previous answer was not a single valid JSON object and could not be parsed. " +
        "Return the corrected answer now, as one JSON object only, with no markdown fences and no commentary.\n\n" +
        "PREVIOUS ANSWER:\n" +
        Truncate(invalidOutput, 4000);

    /// <summary>Minimal request used by Test Connection.</summary>
    public const string ConnectionProbePrompt =
        "Reply with exactly this JSON object and nothing else: {\"status\":\"ok\"}";

    /// <summary>Parses the narrative JSON. Returns null when it cannot be understood.</summary>
    public static AiReportNarrative? ParseReportNarrative(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var json = AiJsonParser.ExtractJsonObject(raw);
        if (json is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var highlights = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (TryGet(root, "highlights", out var highlightElement) && highlightElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in highlightElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (TryGet(item, "path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String &&
                        TryGet(item, "note", out var noteElement))
                    {
                        var path = pathElement.GetString();
                        var note = noteElement.ValueKind == JsonValueKind.String ? noteElement.GetString() : noteElement.ToString();
                        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(note))
                        {
                            highlights[path!] = note!;
                        }
                    }
                }
            }

            return new AiReportNarrative
            {
                Overview = ReadString(root, "overview"),
                Observations = ReadString(root, "observations"),
                FinalSummary = ReadString(root, "final_summary"),
                Highlights = highlights,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
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

    private static string ReadString(JsonElement root, string name) =>
        TryGet(root, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string Format(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture) ?? "(unknown)";

    /// <summary>Quotes and flattens a value so untrusted file names cannot smuggle prompt instructions.</summary>
    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var flattened = value.Replace('\r', ' ').Replace('\n', ' ').Replace('"', '\'');
        return $"\"{Truncate(flattened, 400)}\"";
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
