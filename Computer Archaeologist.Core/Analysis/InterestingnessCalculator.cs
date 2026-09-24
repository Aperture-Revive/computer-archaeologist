using System.Globalization;
using System.Text.RegularExpressions;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using ComputerArchaeologist.Core.Utilities;

namespace ComputerArchaeologist.Core.Analysis;

/// <summary>
/// The local, fully explainable half of the interestingness algorithm. It turns observable file
/// facts into eight normalised features and a weighted local score, and it is the piece that keeps
/// the AI honest: the model can only ever influence the result through
/// <see cref="Fuse"/>, scaled by its own reported confidence.
/// </summary>
public sealed partial class InterestingnessCalculator
{
    private readonly InterestingnessWeightsOptions _weights;

    public InterestingnessCalculator(InterestingnessWeightsOptions? weights = null) =>
        _weights = weights ?? new InterestingnessWeightsOptions();

    public InterestingnessWeightsOptions Weights => _weights;

    // ---------------------------------------------------------------- public API

    public LocalScoreBreakdown ComputeLocalScore(FileMetadata file, ScoringContext context)
    {
        ArgumentNullException.ThrowIfNull(file);
        context ??= ScoringContext.Empty;

        var reasons = new List<ScoreReason>();
        var ages = FormatHelpers.YearsSince(file.CreatedUtc ?? file.ModifiedUtc, context.Now);

        var age = AgeScore(ages, file.CreatedUtc, file.ModifiedUtc, context.Now, reasons);
        var rarity = RarityScore(file, context, reasons);
        var pathContext = PathContextScore(file, reasons);
        var filename = FilenameScore(file, reasons);
        var (cluster, clusterInfo) = ClusterScore(file, context, reasons);
        var personal = PersonalArtifactScore(file, reasons);
        var modification = ModificationPatternScore(file, context.Now, ages, reasons);
        var unusualness = UnusualnessScore(file, reasons);

        var weightMap = _weights.AsLocalWeightMap();
        var values = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Age"] = age,
            ["Rarity"] = rarity,
            ["PathContext"] = pathContext,
            ["Filename"] = filename,
            ["Cluster"] = cluster,
            ["PersonalArtifact"] = personal,
            ["ModificationPattern"] = modification,
            ["Unusualness"] = unusualness,
        };

        var totalWeight = weightMap.Values.Sum();
        var weighted = new List<ScoreReason>(reasons.Count);
        foreach (var reason in reasons)
        {
            var weight = weightMap.TryGetValue(reason.Feature, out var w) ? w : 0;

            // The authoritative value is the clamped feature value, so the per-feature contributions
            // always reconcile exactly with LocalScore no matter what the individual feature method
            // used internally before clamping.
            var value = values.TryGetValue(reason.Feature, out var computed) ? computed : reason.Value;

            weighted.Add(reason with
            {
                Value = value,
                Weight = weight,
                Weighted = totalWeight <= 0 ? 0 : Round(value * weight / totalWeight),
            });
        }

        foreach (var pair in values)
        {
            if (!weighted.Any(r => r.Feature == pair.Key))
            {
                var extraWeight = weightMap[pair.Key];
                weighted.Add(new ScoreReason(
                    pair.Key,
                    extraWeight,
                    pair.Value,
                    totalWeight <= 0 ? 0 : Round(pair.Value * extraWeight / totalWeight),
                    $"Reason_{pair.Key}_None"));
            }
        }

        var local = totalWeight <= 0
            ? 0
            : values.Sum(kv => kv.Value * weightMap[kv.Key]) / totalWeight;

        return new LocalScoreBreakdown
        {
            Age = Round(age),
            Rarity = Round(rarity),
            PathContext = Round(pathContext),
            Filename = Round(filename),
            Cluster = Round(cluster),
            PersonalArtifact = Round(personal),
            ModificationPattern = Round(modification),
            Unusualness = Round(unusualness),
            LocalScore = Round(InterestingnessResult.Clamp(local)),
            Reasons = weighted
                .Select(r => r with { Value = Round(r.Value) })
                .OrderByDescending(r => r.Value * r.Weight)
                .ToArray(),
            DirectorySiblingCount = clusterInfo.SiblingCount,
            HasHash = !string.IsNullOrEmpty(file.Sha256),
        };
    }

    /// <summary>
    /// Combines local evidence with AI judgement. The AI weight is multiplied by the confidence the
    /// model reported, so a low-confidence answer can never dominate the ranking
    /// (specification section 24).
    /// </summary>
    public InterestingnessResult Fuse(LocalScoreBreakdown local, AiAnalysisResult? ai)
    {
        ArgumentNullException.ThrowIfNull(local);

        var configuredAiWeight = Math.Clamp(_weights.AiWeight, 0, 1);

        // Defensive clamp: a hand-built or deserialised breakdown must never be able to push the
        // final score outside 0..100.
        var clampedLocal = InterestingnessResult.Clamp(local.LocalScore);

        if (ai is null || !ai.Succeeded)
        {
            return new InterestingnessResult
            {
                LocalScore = clampedLocal,
                AiScore = 0,
                EffectiveAiWeight = 0,
                EffectiveLocalWeight = 1,
                FinalScore = clampedLocal,
                Confidence = 0,
                AiApplied = false,
                Local = local,
                Ai = ai,
            };
        }

        var confidence = InterestingnessResult.Clamp(ai.Confidence);
        var effectiveAiWeight = ComputeEffectiveAiWeight(confidence, configuredAiWeight);
        var effectiveLocalWeight = 1 - effectiveAiWeight;
        var aiScore = InterestingnessResult.Clamp(ai.Interestingness);

        var final = (clampedLocal * effectiveLocalWeight) + (aiScore * effectiveAiWeight);

        return new InterestingnessResult
        {
            LocalScore = clampedLocal,
            AiScore = aiScore,
            EffectiveAiWeight = Math.Round(effectiveAiWeight, 4),
            EffectiveLocalWeight = Math.Round(effectiveLocalWeight, 4),
            FinalScore = InterestingnessResult.Clamp(final),
            Confidence = confidence,
            AiApplied = true,
            Local = local,
            Ai = ai,
        };
    }

    public double ComputeEffectiveAiWeight(double confidence)
    {
        var configured = Math.Clamp(_weights.AiWeight, 0, 1);
        return ComputeEffectiveAiWeight(confidence, configured);
    }

    private double ComputeEffectiveAiWeight(double confidence, double configured)
    {
        var normalised = InterestingnessResult.Clamp(confidence) / 100.0;

        if (confidence < _weights.LowConfidenceThreshold)
        {
            normalised = Math.Min(normalised, _weights.MinConfidenceScale);
        }

        var scale = Math.Clamp(normalised, Math.Clamp(_weights.MinConfidenceScale, 0, 1), 1);
        return Math.Clamp(configured * scale, 0, 1);
    }

    // ---------------------------------------------------------------- features

    /// <summary>
    /// Non-linear age curve: one month is almost nothing, six months is a little, a year is
    /// moderate, three years is high, ten years saturates. Old is never automatically interesting,
    /// so the feature is capped below 100 by <see cref="InterestingnessWeightsOptions.AgeCeiling"/>.
    /// </summary>
    public double AgeScore(
        double years,
        DateTimeOffset? created,
        DateTimeOffset? modified,
        DateTimeOffset now,
        List<ScoreReason>? reasons = null)
    {
        var value = 0.0;
        var low = Math.Max(0.25, _weights.AgeLowYears);
        var mid = Math.Max(low + 0.25, _weights.AgeMidYears);
        var high = Math.Max(mid + 0.25, _weights.AgeHighYears);
        var veryHigh = Math.Max(high + 0.25, _weights.AgeVeryHighYears);
        var ceiling = InterestingnessResult.Clamp(_weights.AgeCeiling);

        if (years <= 0)
        {
            value = 0;
        }
        else if (years < 0.0833)
        {
            value = 3;                                                     // under a month
        }
        else if (years < 0.5)
        {
            value = Lerp(3, 14, (years - 0.0833) / (0.5 - 0.0833));        // under six months
        }
        else if (years < low)
        {
            value = Lerp(14, 32, (years - 0.5) / (low - 0.5));             // under a year
        }
        else if (years < mid)
        {
            value = Lerp(32, 58, (years - low) / (mid - low));
        }
        else if (years < high)
        {
            value = Lerp(58, 78, (years - mid) / (high - mid));
        }
        else if (years < veryHigh)
        {
            value = Lerp(78, ceiling, (years - high) / (veryHigh - high));
        }
        else
        {
            value = ceiling;
        }

        if (reasons is not null && years >= _weights.AgeLowYears)
        {
            var anchor = created ?? modified;
            reasons.Add(new ScoreReason(
                "Age",
                0,
                value,
                0,
                "Reason_Age_Years",
                new[] { Math.Floor(years).ToString(CultureInfo.InvariantCulture), FormatHelpers.Year(anchor) }));
        }

        return InterestingnessResult.Clamp(value);
    }

    /// <summary>
    /// Rarity from how often the extension occurs in the scanned set. The curve is logarithmic so
    /// that a handful of occurrences already reads as "rare" while a million .jpg files read as
    /// completely ordinary.
    /// </summary>
    public double RarityScore(FileMetadata file, ScoringContext context, List<ScoreReason>? reasons = null)
    {
        var extension = file.Extension;
        var count = context.CountOf(extension);
        var total = Math.Max(1, context.TotalFilesScanned);

        double value;
        if (string.IsNullOrEmpty(extension))
        {
            value = 96;
        }
        else if (count <= 1)
        {
            value = 100;
        }
        else
        {
            // Anchor points on log10(count): 1 -> 100, 10 -> 74, 100 -> 52, 1k -> 33, 10k -> 18, 1M -> 5
            var l = Math.Log10(count);
            value = l switch
            {
                <= 1 => Lerp(100, 74, (l - 0) / 1),
                <= 2 => Lerp(74, 52, l - 1),
                <= 3 => Lerp(52, 33, l - 2),
                <= 4 => Lerp(33, 18, l - 3),
                <= 6 => Lerp(18, 5, (l - 4) / 2),
                _ => 5,
            };
        }

        if (CommonExtensions.Contains(extension))
        {
            value *= 0.65;
        }

        if (FileTypeCatalog.FamilyOf(extension) == FileFamily.Unknown && !string.IsNullOrEmpty(extension))
        {
            value = Math.Max(value, 70);
        }

        if (reasons is not null)
        {
            reasons.Add(count <= 8
                ? new ScoreReason("Rarity", 0, value, 0, "Reason_Rarity_Rare", new[] { file.DisplayExtension, count.ToString("N0", CultureInfo.InvariantCulture), total.ToString("N0", CultureInfo.InvariantCulture) })
                : new ScoreReason("Rarity", 0, value, 0, "Reason_Rarity_Common", new[] { file.DisplayExtension, count.ToString("N0", CultureInfo.InvariantCulture) }));
        }

        return InterestingnessResult.Clamp(value);
    }

    /// <summary>Semantic weight of the folder a file lives in.</summary>
    public double PathContextScore(FileMetadata file, List<ScoreReason>? reasons = null)
    {
        var path = (file.DirectoryPath ?? string.Empty).Replace('/', '\\').ToLowerInvariant();
        if (path.Length == 0)
        {
            return 30;
        }

        var value = 42.0;
        var hits = new List<string>();

        foreach (var (keyword, delta) in PathKeywords)
        {
            if (path.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                value += delta;
                hits.Add(keyword);
            }
        }

        var penalty = 0.0;
        foreach (var (keyword, delta) in PathPenalties)
        {
            if (path.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                penalty += delta;
            }
        }

        // A year in the path is strong evidence of a dated personal folder.
        var yearMatch = YearRegex().Match(path);
        if (yearMatch.Success)
        {
            var year = int.Parse(yearMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var now = DateTimeOffset.UtcNow.Year;
            if (year >= 1990 && year <= now)
            {
                var age = now - year;
                value += Math.Min(18, 6 + (age * 1.5));
                hits.Add(yearMatch.Groups[1].Value);
            }
        }

        if (PenaltyKeywords.Any(k => path.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            value = Math.Min(value, 22);
        }

        value -= penalty;

        if (reasons is not null)
        {
            if (penalty >= 25 || value <= 22)
            {
                reasons.Add(new ScoreReason("PathContext", 0, value, 0, "Reason_Path_Transient"));
            }
            else
            {
                var highlight = hits.Count > 0 ? string.Join(", ", hits.Take(3)) : ShortFolder(path);
                reasons.Add(new ScoreReason("PathContext", 0, value, 0, "Reason_Path_Personal", new[] { highlight }));
            }
        }

        return InterestingnessResult.Clamp(value);
    }

    /// <summary>Signals carried by the file name itself. Diminishing returns keep one keyword from dominating.</summary>
    public double FilenameScore(FileMetadata file, List<ScoreReason>? reasons = null)
    {
        var name = (file.FileName ?? string.Empty).ToLowerInvariant();
        if (name.Length == 0)
        {
            return 0;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var accumulated = 0.0;
        var matched = new List<string>();

        foreach (var (keyword, points) in FilenameKeywords)
        {
            if (ContainsToken(stem, keyword))
            {
                accumulated += points;
                matched.Add(keyword);
            }
        }

        if (VersionSuffixRegex().IsMatch(stem))
        {
            accumulated += 9;
            matched.Add(VersionSuffixRegex().Match(stem).Value);
        }

        if (YearRegex().IsMatch(stem))
        {
            accumulated += 7;
            matched.Add(YearRegex().Match(stem).Value);
        }

        if (DatedSuffixRegex().IsMatch(stem))
        {
            accumulated += 5;
        }

        // Saturating curve: 0 -> 0, 10 -> 40, 25 -> 66, 50 -> 86, 90+ -> ~96
        var value = 100 * (1 - Math.Exp(-accumulated / 22.0));

        if (reasons is not null && matched.Count > 0)
        {
            reasons.Add(new ScoreReason("Filename", 0, value, 0, "Reason_Filename_Keyword", new[] { string.Join(", ", matched.Distinct().Take(4)) }));
        }

        return InterestingnessResult.Clamp(value);
    }

    /// <summary>Does the surrounding folder look like a project rather than an incidental pile of files?</summary>
    public (double Value, DirectoryClusterInfo Info) ClusterScore(
        FileMetadata file,
        ScoringContext context,
        List<ScoreReason>? reasons = null)
    {
        var info = context.ClusterOf(file.DirectoryPath);
        if (info.SiblingCount <= 0 && !info.LooksLikeProject)
        {
            if (reasons is not null)
            {
                reasons.Add(new ScoreReason("Cluster", 0, 0, 0, "Reason_Cluster_Alone"));
            }

            return (0, info);
        }

        var value = info.SiblingCount switch
        {
            <= 0 => 0,
            1 or 2 => 26,
            <= 9 => 50,
            <= 49 => 70,
            <= 199 => 78,
            _ => 74,
        };

        if (info.LooksLikeProject)
        {
            value += 26;
            if (info.HasReadme)
            {
                value += 8;
            }

            if (info.HasManifest)
            {
                value += 8;
            }

            if (info.SourceFileCount >= 3)
            {
                value += 6;
            }

            if (info.AssetFileCount >= 2)
            {
                value += 4;
            }
        }

        if (reasons is not null)
        {
            reasons.Add(info.LooksLikeProject
                ? new ScoreReason("Cluster", 0, value, 0, "Reason_Cluster_Project", new[] { info.SiblingCount.ToString("N0", CultureInfo.InvariantCulture), info.SourceFileCount.ToString(CultureInfo.InvariantCulture) })
                : new ScoreReason("Cluster", 0, value, 0, "Reason_Cluster_Siblings", new[] { info.SiblingCount.ToString("N0", CultureInfo.InvariantCulture) }));
        }

        return (InterestingnessResult.Clamp(value), info);
    }

    /// <summary>How likely it is that a human made this on purpose.</summary>
    public double PersonalArtifactScore(FileMetadata file, List<ScoreReason>? reasons = null)
    {
        var family = FileTypeCatalog.FamilyOf(file.Extension);
        var value = family switch
        {
            FileFamily.GameSave => 86,
            FileFamily.Project => 88,
            FileFamily.Design => 88,
            FileFamily.ThreeD => 84,
            FileFamily.Code => 72,
            FileFamily.Document => 70,
            FileFamily.Text => 64,
            FileFamily.Image => 62,
            FileFamily.Spreadsheet => 56,
            FileFamily.Presentation => 58,
            FileFamily.Audio or FileFamily.Video => 52,
            FileFamily.Data => 48,
            FileFamily.Database => 46,
            FileFamily.Archive => 44,
            FileFamily.Configuration => 40,
            FileFamily.Log => 34,
            FileFamily.Executable => 30,
            FileFamily.Font => 26,
            _ => 32,
        };

        if (FileTypeCatalog.PersonalLikelihood(file.Extension) > 0)
        {
            value = Math.Max(value, 82);
        }

        var path = (file.DirectoryPath ?? string.Empty).ToLowerInvariant();
        if (PersonalFolders.Any(folder => path.Contains(folder, StringComparison.OrdinalIgnoreCase)))
        {
            value += 10;
        }

        if (reasons is not null)
        {
            reasons.Add(new ScoreReason("PersonalArtifact", 0, value, 0, "Reason_Personal_Type", new[] { file.DisplayExtension }));
        }

        return InterestingnessResult.Clamp(value);
    }

    /// <summary>
    /// Created long ago and never touched again means abandoned, which is exactly what an
    /// archaeologist wants. Created long ago but edited yesterday means it is still alive.
    /// </summary>
    public double ModificationPatternScore(
        FileMetadata file,
        DateTimeOffset now,
        double ageYears,
        List<ScoreReason>? reasons = null)
    {
        var created = file.CreatedUtc;
        var modified = file.ModifiedUtc;
        var sinceModified = FormatHelpers.YearsSince(modified, now);
        var activeSpan = created is not null && modified is not null
            ? Math.Abs((modified.Value - created.Value).TotalDays)
            : 0;

        var value = 38.0;
        var abandoned = false;

        if (sinceModified >= 5)
        {
            value += 42;
            abandoned = true;
        }
        else if (sinceModified >= 3)
        {
            value += 34;
            abandoned = true;
        }
        else if (sinceModified >= 1.5)
        {
            value += 22;
        }
        else if (sinceModified < 0.25)
        {
            value -= _weights.RecentlyTouchedPenalty;
        }

        if (abandoned && activeSpan > 0 && activeSpan <= 180)
        {
            // Written in one short burst years ago and then dropped: the classic abandoned prototype.
            value += _weights.AbandonedBonus * 0.6;
        }
        else if (activeSpan > 730)
        {
            // Maintained for years; still historic but not "forgotten".
            value -= 12;
        }

        if (created is not null && modified is null)
        {
            value += 6;
        }

        value = InterestingnessResult.Clamp(value);

        if (reasons is not null)
        {
            if (abandoned)
            {
                reasons.Add(new ScoreReason("ModificationPattern", 0, value, 0, "Reason_Mod_Abandoned", new[] { FormatHelpers.Year(created), FormatHelpers.Year(modified) }));
            }
            else if (sinceModified < 0.25)
            {
                reasons.Add(new ScoreReason("ModificationPattern", 0, value, 0, "Reason_Mod_Active", new[] { FormatHelpers.Date(modified) }));
            }
            else
            {
                reasons.Add(new ScoreReason("ModificationPattern", 0, value, 0, "Reason_Mod_Steady", new[] { FormatHelpers.Date(modified) }));
            }
        }

        return value;
    }

    /// <summary>Structural oddities: the things that make a file look like a curiosity.</summary>
    public double UnusualnessScore(FileMetadata file, List<ScoreReason>? reasons = null)
    {
        var name = file.FileName ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(name);
        var value = 6.0;
        var notes = new List<string>();

        if (string.IsNullOrEmpty(file.Extension))
        {
            value += 44;
            notes.Add("no-extension");
        }

        if (stem.Length >= 40)
        {
            value += Math.Min(24, 8 + ((stem.Length - 40) / 8.0));
            notes.Add("long-name");
        }

        if (MultiExtensionRegex().IsMatch(name))
        {
            value += 24;
            notes.Add("multi-extension");
        }

        if (name.Any(c => c > 127))
        {
            value += 18;
            notes.Add("non-ascii");
        }

        if (BracketChars.Any(c => stem.Contains(c)) && stem.Count(char.IsDigit) >= 4)
        {
            value += 10;
            notes.Add("bracketed");
        }

        if (stem.Length > 0 && stem.All(char.IsDigit))
        {
            value += 14;
            notes.Add("numeric-name");
        }

        if (OddNameRegex().IsMatch(stem))
        {
            value += 16;
            notes.Add("odd-characters");
        }

        if (file.IsHidden)
        {
            value += 10;
            notes.Add("hidden");
        }

        if (file.IsSystem)
        {
            value += 4;
            notes.Add("system");
        }

        if (notes.Count == 0 && reasons is not null)
        {
            reasons.Add(new ScoreReason("Unusualness", 0, value, 0, "Reason_Unusual_None"));
        }
        else if (reasons is not null)
        {
            reasons.Add(new ScoreReason("Unusualness", 0, value, 0, "Reason_Unusual_Flags", new[] { string.Join(", ", notes) }));
        }

        return InterestingnessResult.Clamp(value);
    }

    // ---------------------------------------------------------------- streaming pre-score

    /// <summary>
    /// Deliberately cheap pre-score used while streaming hundreds of thousands of indexed files.
    /// It avoids every regular expression and every long keyword table so the discovery stage stays
    /// fast, and it only has to be good enough to keep the best <c>MaxCandidates</c> files.
    /// The survivors are then rescored with the full algorithm.
    /// </summary>
    public double ComputeCheapScore(FileMetadata file, DateTimeOffset now)
    {
        var years = FormatHelpers.YearsSince(file.CreatedUtc ?? file.ModifiedUtc, now);
        var age = AgeScore(years, null, null, now, null);
        var personal = CheapPersonal(file);
        var modification = CheapModification(file, now);
        var name = CheapKeyword(file.FileName, CheapFilenameKeywords);
        var path = CheapKeyword(file.DirectoryPath, CheapPathKeywords);
        var odd = CheapUnusualness(file);

        var score = (age * 0.30) + (personal * 0.22) + (modification * 0.18) +
                    (name * 0.12) + (path * 0.12) + (odd * 0.06);

        return InterestingnessResult.Clamp(score);
    }

    private static double CheapPersonal(FileMetadata file) => FileTypeCatalog.FamilyOf(file.Extension) switch
    {
        FileFamily.Project => 90,
        FileFamily.GameSave => 88,
        FileFamily.Design or FileFamily.ThreeD => 86,
        FileFamily.Code => 74,
        FileFamily.Document => 70,
        FileFamily.Text => 64,
        FileFamily.Image => 60,
        FileFamily.Spreadsheet or FileFamily.Presentation => 56,
        FileFamily.Audio or FileFamily.Video => 52,
        FileFamily.Archive => 46,
        FileFamily.Database => 46,
        FileFamily.Configuration => 40,
        FileFamily.Log => 32,
        FileFamily.Executable => 30,
        FileFamily.Font => 26,
        _ => 34,
    };

    private static double CheapModification(FileMetadata file, DateTimeOffset now)
    {
        var sinceModified = FormatHelpers.YearsSince(file.ModifiedUtc, now);
        if (sinceModified >= 5)
        {
            return 88;
        }

        if (sinceModified >= 3)
        {
            return 74;
        }

        if (sinceModified >= 1.5)
        {
            return 58;
        }

        return sinceModified < 0.25 ? 18 : 40;
    }

    private static double CheapKeyword(string? text, string[] keywords)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var hits = 0;
        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                hits++;
                if (hits >= 3)
                {
                    break;
                }
            }
        }

        return hits switch
        {
            0 => 0,
            1 => 46,
            2 => 72,
            _ => 88,
        };
    }

    private static double CheapUnusualness(FileMetadata file)
    {
        var value = 6.0;

        if (string.IsNullOrEmpty(file.Extension))
        {
            value += 44;
        }

        var stemLength = Path.GetFileNameWithoutExtension(file.FileName ?? string.Empty).Length;
        if (stemLength >= 40)
        {
            value += 20;
        }

        if (file.IsHidden)
        {
            value += 10;
        }

        return InterestingnessResult.Clamp(value);
    }

    private static readonly string[] CheapFilenameKeywords =
    {
        "old", "backup", "final", "draft", "prototype", "abandoned", "diary", "journal",
        "homework", "school", "assignment", "notes", "archive", "original", "unfinished",
        "project", "game", "thesis", "save", "mygame", "test", "copy",
    };

    private static readonly string[] CheapPathKeywords =
    {
        "documents", "desktop", "pictures", "photos", "school", "university", "college",
        "homework", "projects", "source", "repos", "notes", "personal", "old", "backup",
        "archive", "saves", "games", "art", "writing", "diary", "temp", "cache",
        "node_modules", "appdata", "windows", "program files",
    };

    // ---------------------------------------------------------------- helpers

    private static double Lerp(double a, double b, double t) => a + ((b - a) * Math.Clamp(t, 0, 1));

    private static double Round(double value) => Math.Round(InterestingnessResult.Clamp(value), 1, MidpointRounding.AwayFromZero);

    private static bool ContainsToken(string stem, string keyword)
    {
        var index = stem.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(stem[index - 1]);
            var afterIndex = index + keyword.Length;
            var afterOk = afterIndex >= stem.Length || !char.IsLetterOrDigit(stem[afterIndex]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            index = stem.IndexOf(keyword, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string ShortFolder(string path)
    {
        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length <= 2 ? path : string.Join('\\', segments.Skip(Math.Max(0, segments.Length - 2)));
    }

    private static readonly char[] BracketChars = { '[', ']', '(', ')', '{', '}' };

    private static readonly HashSet<string> CommonExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".doc", ".docx", ".xls", ".xlsx",
        ".ppt", ".pptx", ".zip", ".rar", ".mp3", ".mp4", ".avi", ".mkv", ".exe", ".dll",
        ".log", ".ini", ".xml", ".json", ".csv", ".html", ".htm", ".css", ".js", ".lnk",
    };

    private static readonly string[] PersonalFolders =
    {
        "\\documents\\", "\\desktop\\", "\\pictures\\", "\\photos\\", "\\music\\",
        "\\videos\\", "\\downloads\\", "\\onedrive\\", "\\dropbox\\", "\\google drive\\",
        "\\my documents\\", "\\home\\", "\\personal\\", "\\projects\\", "\\source\\",
        "\\repos\\", "\\school\\", "\\university\\", "\\college\\",
    };

    private static readonly string[] PenaltyKeywords =
    {
        "\\temp\\", "\\tmp\\", "\\cache", "\\node_modules\\", "\\.git\\", "\\obj\\", "\\bin\\",
        "\\appdata\\local\\packages\\", "\\windows\\", "\\program files", "\\$recycle.bin\\",
    };

    private static readonly (string Keyword, double Delta)[] PathKeywords =
    {
        ("\\documents\\", 12), ("\\desktop\\", 10), ("\\pictures\\", 8), ("\\photos\\", 10),
        ("\\downloads\\", 6), ("\\school\\", 20), ("\\university\\", 20), ("\\college\\", 20),
        ("\\homework\\", 20), ("\\assignments\\", 18), ("\\projects\\", 16), ("\\project\\", 12),
        ("\\source\\", 12), ("\\repos\\", 12), ("\\dev\\", 10), ("\\code\\", 10),
        ("\\notes\\", 14), ("\\personal\\", 16), ("\\my ", 12), ("\\old\\", 16),
        ("\\backup", 12), ("\\backups\\", 12), ("\\archive", 14), ("\\saves\\", 14),
        ("\\savegames\\", 16), ("\\save games\\", 16), ("\\games\\", 8), ("\\game\\", 6),
        ("\\art\\", 12), ("\\writing\\", 14), ("\\stories\\", 14), ("\\diary\\", 20),
        ("\\thesis\\", 18), ("\\music\\", 8), ("\\videos\\", 6), ("\\onedrive\\", 6),
        ("\\dropbox\\", 6), ("\\experiments\\", 16), ("\\prototypes\\", 16), ("\\legacy\\", 18),
        ("\\misc\\", 8), ("\\stuff\\", 10), ("\\random\\", 8), ("\\uni\\", 16),
        ("\\graduated\\", 18), ("\\2019\\", 6), ("\\2020\\", 6),
    };

    private static readonly (string Keyword, double Delta)[] PathPenalties =
    {
        ("\\appdata\\", 26), ("\\temp\\", 30), ("\\tmp\\", 26), ("\\cache", 28),
        ("\\node_modules\\", 40), ("\\.git\\", 34), ("\\obj\\", 30), ("\\bin\\", 22),
        ("\\packages\\", 18), ("\\windows\\", 40), ("\\program files", 40),
        ("\\programdata\\", 30), ("\\system32", 45), ("\\driverstore", 40),
        ("\\$recycle.bin", 45), ("\\recovery\\", 40), ("\\installer\\", 30),
    };

    private static readonly (string Keyword, double Points)[] FilenameKeywords =
    {
        ("old", 12), ("backup", 14), ("bak", 10), ("final", 11), ("final2", 14),
        ("project", 9), ("homework", 18), ("school", 15), ("assignment", 16), ("game", 8),
        ("test", 5), ("prototype", 15), ("proto", 10), ("diary", 20), ("journal", 18),
        ("notes", 14), ("note", 9), ("archive", 13), ("abandoned", 22), ("source", 9),
        ("original", 10), ("copy", 6), ("draft", 12), ("wip", 12), ("idea", 12),
        ("ideas", 12), ("todo", 8), ("unfinished", 20), ("experiment", 15), ("scratch", 8),
        ("thesis", 20), ("essay", 15), ("report", 7), ("resume", 12), ("cv", 10),
        ("letter", 10), ("story", 14), ("save", 12), ("savegame", 16), ("profile", 7),
        ("config", 5), ("setup", 6), ("install", 5), ("world", 8), ("level", 8),
        ("mymap", 12), ("mygame", 16), ("first", 10), ("learning", 14), ("practice", 12),
        ("tutorial", 10), ("tutorials", 8), ("demo", 7), ("v1", 9), ("v2", 9),
        ("2025", 5), ("2019", 8), ("2018", 9), ("2017", 10), ("2016", 11), ("2015", 12),
        ("2014", 13), ("2013", 13), ("2012", 14), ("2011", 14), ("2010", 15),
    };

    [GeneratedRegex(@"\b(19[89]\d|20[0-2]\d)\b", RegexOptions.CultureInvariant)]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"[\s_\-]v?\d{1,2}(\.\d{1,2}){0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionSuffixRegex();

    [GeneratedRegex(@"[-_ ](19|20)\d{2}[-_ ]?\d{0,2}[-_ ]?\d{0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatedSuffixRegex();

    [GeneratedRegex(@"\.[a-z0-9]{1,6}\.(zip|rar|7z|tar|gz|bak|old|txt|exe|jpg|png|doc|docx)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MultiExtensionRegex();

    [GeneratedRegex(@"[^\p{L}\p{Nd}\s._\-\[\]\(\){}@#+,&'!]", RegexOptions.CultureInvariant)]
    private static partial Regex OddNameRegex();
}
