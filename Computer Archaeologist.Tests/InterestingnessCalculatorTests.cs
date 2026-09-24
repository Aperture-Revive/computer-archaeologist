using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>
/// Tests for the local interestingness algorithm: the eight features, the weighted sum, and the
/// confidence-scaled fusion with the AI judgement.
/// </summary>
public sealed class InterestingnessCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static FileMetadata File(
        string name,
        string directory = @"C:\Users\Test\Documents\Stuff",
        long size = 2048,
        DateTimeOffset? created = null,
        DateTimeOffset? modified = null,
        string? extension = null) =>
        new()
        {
            FullPath = Path.Combine(directory, name),
            FileName = name,
            Extension = FileMetadata.NormalizeExtension(extension ?? Path.GetExtension(name)),
            DirectoryPath = directory,
            SizeBytes = size,
            CreatedUtc = created,
            ModifiedUtc = modified,
            AccessedUtc = modified,
            Attributes = FileAttributes.Normal,
            Source = "test",
        };

    private static InterestingnessCalculator Calculator(InterestingnessWeightsOptions? weights = null) =>
        new(weights ?? new InterestingnessWeightsOptions());

    // ---------------------------------------------------------------- age

    [Fact]
    public void Age_is_not_linear_and_recent_files_score_low()
    {
        var calculator = Calculator();

        var month = calculator.AgeScore(1.0 / 12, null, null, Now);
        var year = calculator.AgeScore(1.0, null, null, Now);
        var threeYears = calculator.AgeScore(3.0, null, null, Now);
        var tenYears = calculator.AgeScore(10.0, null, null, Now);
        var thirtyYears = calculator.AgeScore(30.0, null, null, Now);

        Assert.True(month < year, "A one month old file must score below a one year old file.");
        Assert.True(year < threeYears);
        Assert.True(threeYears < tenYears);
        Assert.True(month < 20, "A one month old file must stay very low.");
        Assert.True(year is > 25 and < 45, $"One year should be moderate, was {year}.");

        // Age saturates: it can never be interesting on its own.
        Assert.True(tenYears <= calculator.Weights.AgeCeiling);
        Assert.Equal(calculator.Weights.AgeCeiling, thirtyYears, 1);
        Assert.True(thirtyYears < 100, "Age alone must never reach 100.");
    }

    [Fact]
    public void Age_without_dates_is_zero()
    {
        Assert.Equal(0, Calculator().AgeScore(0, null, null, Now), 3);
    }

    // ---------------------------------------------------------------- rarity

    [Fact]
    public void Rarity_rewards_extensions_that_are_rare_on_this_machine()
    {
        var calculator = Calculator();
        var context = new ScoringContext
        {
            TotalFilesScanned = 200_000,
            ExtensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"] = 120_000,
                [".txt"] = 40_000,
                [".blend"] = 3,
            },
            Now = Now,
        };

        var rare = calculator.RarityScore(File("scene.blend"), context);
        var common = calculator.RarityScore(File("photo.jpg"), context);
        var unique = calculator.RarityScore(File("mystery.qqq"), context);

        Assert.True(rare > common, $"Rare extension scored {rare}, common scored {common}.");
        Assert.True(unique > rare, "An extension seen once must score highest.");
        Assert.True(common < 40, $"A ubiquitous extension must stay low, was {common}.");
    }

    [Fact]
    public void Rarity_gives_a_file_without_extension_a_high_score()
    {
        var context = new ScoringContext { TotalFilesScanned = 1000 };
        var score = Calculator().RarityScore(File("README", extension: string.Empty), context);
        Assert.True(score > 90);
    }

    // ---------------------------------------------------------------- path

    [Fact]
    public void Path_context_prefers_personal_folders_over_caches()
    {
        var calculator = Calculator();

        var personal = calculator.PathContextScore(File("notes.txt", @"C:\Users\Test\Documents\School\Grade8\Physics"));
        var workspace = calculator.PathContextScore(File("main.cs", @"D:\Projects\MyFirstGame\src"));
        var temp = calculator.PathContextScore(File("tmp1.txt", @"C:\Users\Test\AppData\Local\Temp\build"));
        var system = calculator.PathContextScore(File("x.dll", @"C:\Windows\System32"));

        Assert.True(personal > 60, $"Personal path scored {personal}.");
        Assert.True(workspace > 50, $"Project path scored {workspace}.");
        Assert.True(temp < 30, $"Temp path scored {temp}.");
        Assert.True(system < 30, $"System path scored {system}.");
    }

    // ---------------------------------------------------------------- filename

    [Fact]
    public void Filename_keywords_raise_the_score_with_diminishing_returns()
    {
        var calculator = Calculator();

        var plain = calculator.FilenameScore(File("data.txt"));
        var one = calculator.FilenameScore(File("notes.txt"));
        var many = calculator.FilenameScore(File("abandoned-final-prototype-2015-diary.txt"));

        Assert.Equal(0, plain, 3);
        Assert.True(one > 20, $"A single keyword should already register, was {one}.");
        Assert.True(many > one);
        Assert.True(many <= 100, "The filename feature must stay inside 0..100.");
    }

    [Fact]
    public void Filename_keyword_matching_respects_word_boundaries()
    {
        var calculator = Calculator();

        // "old" inside "scaffold" must not count as the "old" keyword.
        var accidental = calculator.FilenameScore(File("scaffolding.txt"));
        var intentional = calculator.FilenameScore(File("old stuff.txt"));

        Assert.True(intentional > accidental);
    }

    // ---------------------------------------------------------------- cluster

    [Fact]
    public void Cluster_score_rewards_project_shaped_folders()
    {
        var calculator = Calculator();
        var directory = @"D:\OldProjects\MyFirstGame";

        var context = new ScoringContext
        {
            TotalFilesScanned = 100,
            DirectoryClusters = new Dictionary<string, DirectoryClusterInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [directory] = new()
                {
                    SiblingCount = 34,
                    LooksLikeProject = true,
                    HasReadme = true,
                    HasManifest = true,
                    SourceFileCount = 12,
                    AssetFileCount = 6,
                },
            },
            Now = Now,
        };

        var (project, info) = calculator.ClusterScore(File("main.cs", directory), context);
        var (lonely, _) = calculator.ClusterScore(File("stray.txt", @"C:\Users\Test\Desktop"), context);

        Assert.True(project > 85, $"Project cluster scored {project}.");
        Assert.Equal(34, info.SiblingCount);
        Assert.Equal(0, lonely, 3);
    }

    // ---------------------------------------------------------------- fusion

    [Fact]
    public void Fuse_uses_only_local_evidence_when_the_ai_is_unavailable()
    {
        var calculator = Calculator();
        var local = new LocalScoreBreakdown { LocalScore = 72 };

        var result = calculator.Fuse(local, null);
        Assert.False(result.AiApplied);
        Assert.Equal(0, result.EffectiveAiWeight, 4);
        Assert.Equal(1, result.EffectiveLocalWeight, 4);
        Assert.Equal(72, result.FinalScore, 3);

        var failed = calculator.Fuse(local, AiAnalysisResult.Unavailable("boom"));
        Assert.False(failed.AiApplied);
        Assert.Equal(72, failed.FinalScore, 3);
    }

    [Fact]
    public void Fuse_pulls_the_result_towards_the_ai_score()
    {
        var calculator = Calculator(new InterestingnessWeightsOptions { AiWeight = 0.55 });
        var local = new LocalScoreBreakdown { LocalScore = 40 };
        var ai = new AiAnalysisResult { Succeeded = true, Interestingness = 90, Confidence = 100 };

        var result = calculator.Fuse(local, ai);

        Assert.True(result.AiApplied);
        Assert.Equal(0.55, result.EffectiveAiWeight, 4);
        Assert.Equal(0.45, result.EffectiveLocalWeight, 4);
        Assert.Equal((40 * 0.45) + (90 * 0.55), result.FinalScore, 3);
    }

    [Fact]
    public void Low_confidence_ai_barely_moves_the_final_score()
    {
        var calculator = Calculator(new InterestingnessWeightsOptions
        {
            AiWeight = 0.55,
            LowConfidenceThreshold = 30,
            MinConfidenceScale = 0.10,
        });

        var local = new LocalScoreBreakdown { LocalScore = 80 };
        var confident = calculator.Fuse(local, new AiAnalysisResult { Succeeded = true, Interestingness = 5, Confidence = 95 });
        var unsure = calculator.Fuse(local, new AiAnalysisResult { Succeeded = true, Interestingness = 5, Confidence = 10 });

        Assert.True(unsure.EffectiveAiWeight < confident.EffectiveAiWeight);
        Assert.True(unsure.FinalScore > confident.FinalScore,
            "An unconfident negative opinion must not drag a locally strong artifact down.");
        Assert.Equal(0.55 * 0.10, unsure.EffectiveAiWeight, 4);
    }

    [Fact]
    public void Final_score_always_stays_inside_zero_and_one_hundred()
    {
        var calculator = Calculator();

        foreach (var localScore in new[] { -50d, 0d, 33.3, 99.9, 100d, 250d })
        {
            foreach (var aiScore in new[] { -10d, 0d, 50d, 100d, 500d })
            {
                foreach (var confidence in new[] { -5d, 0d, 50d, 100d, 999d })
                {
                    var result = calculator.Fuse(
                        new LocalScoreBreakdown { LocalScore = localScore },
                        new AiAnalysisResult { Succeeded = true, Interestingness = aiScore, Confidence = confidence });

                    Assert.InRange(result.FinalScore, 0, 100);
                    Assert.InRange(result.LocalScore, 0, 100);
                    Assert.InRange(result.AiScore, 0, 100);
                }
            }
        }
    }

    // ---------------------------------------------------------------- full local score

    [Fact]
    public void ComputeLocalScore_returns_every_feature_and_a_bounded_total()
    {
        var calculator = Calculator();
        var created = new DateTimeOffset(2015, 4, 2, 0, 0, 0, TimeSpan.Zero);
        var modified = new DateTimeOffset(2015, 6, 10, 0, 0, 0, TimeSpan.Zero);
        var directory = @"C:\Users\Test\Documents\MyFirstGame";

        var file = File("main.cs", directory, created: created, modified: modified);

        var context = new ScoringContext
        {
            TotalFilesScanned = 50_000,
            ExtensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [".cs"] = 900 },
            DirectoryClusters = new Dictionary<string, DirectoryClusterInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [directory] = new()
                {
                    SiblingCount = 18,
                    LooksLikeProject = true,
                    HasReadme = true,
                    HasManifest = true,
                    SourceFileCount = 7,
                },
            },
            Now = Now,
        };

        var breakdown = calculator.ComputeLocalScore(file, context);

        Assert.InRange(breakdown.LocalScore, 0, 100);
        Assert.InRange(breakdown.Age, 0, 100);
        Assert.InRange(breakdown.Rarity, 0, 100);
        Assert.InRange(breakdown.PathContext, 0, 100);
        Assert.InRange(breakdown.Filename, 0, 100);
        Assert.InRange(breakdown.Cluster, 0, 100);
        Assert.InRange(breakdown.PersonalArtifact, 0, 100);
        Assert.InRange(breakdown.ModificationPattern, 0, 100);
        Assert.InRange(breakdown.Unusualness, 0, 100);

        // Every feature must be explained, never just a bare number.
        Assert.Equal(8, breakdown.Reasons.Count);
        Assert.All(breakdown.Reasons, reason =>
        {
            Assert.False(string.IsNullOrWhiteSpace(reason.Feature));
            Assert.False(string.IsNullOrWhiteSpace(reason.ExplanationKey));
            Assert.True(reason.Weight >= 0);
        });

        // An abandoned source project must be recognised as interesting.
        Assert.True(breakdown.LocalScore > 55, $"Expected a strong local score, got {breakdown.LocalScore}.");

        // The individual contributions must add up to the reported total. Each contribution is
        // rounded for display, so a small drift is expected and bounded.
        var sum = breakdown.Reasons.Sum(r => r.Weighted);
        Assert.InRange(sum, breakdown.LocalScore - 1.0, breakdown.LocalScore + 1.0);
    }

    [Fact]
    public void ComputeLocalScore_keeps_a_temp_file_uninteresting()
    {
        var calculator = Calculator();
        var now = DateTimeOffset.UtcNow;
        var file = File(
            "cache.tmp",
            @"C:\Users\Test\AppData\Local\Temp\session",
            created: now.AddMonths(-2),
            modified: now.AddHours(-1),
            extension: ".txt");

        var context = new ScoringContext
        {
            TotalFilesScanned = 100_000,
            ExtensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [".txt"] = 60_000 },
            Now = now,
        };

        var breakdown = calculator.ComputeLocalScore(file, context);

        Assert.True(breakdown.LocalScore < 45, $"A fresh temp file should stay low, got {breakdown.LocalScore}.");
    }

    [Fact]
    public void Cheap_score_is_monotonic_with_age_for_otherwise_identical_files()
    {
        var calculator = Calculator();
        var recent = calculator.ComputeCheapScore(File("notes.txt", created: Now.AddMonths(-1), modified: Now.AddMonths(-1)), Now);
        var ancient = calculator.ComputeCheapScore(File("notes.txt", created: Now.AddYears(-12), modified: Now.AddYears(-12)), Now);

        Assert.True(ancient > recent);
        Assert.InRange(ancient, 0, 100);
        Assert.InRange(recent, 0, 100);
    }
}
