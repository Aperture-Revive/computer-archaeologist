using System.Text.RegularExpressions;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Reports;
using ComputerArchaeologist.Core.Localization;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>
/// Guards the whole localisation contract: every key the XAML and the code ask for must exist in both
/// catalogues. A missing key only shows up at runtime as a raw identifier on screen, so it is checked
/// here instead of being discovered by a user.
/// </summary>
public sealed partial class LocalizationCoverageTests
{
    private static readonly ReswLocalizationService Localization = new();

    private static IReadOnlyDictionary<string, string> English => Localization.GetCatalogue("en-US");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Computer Archaeologist.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        var root = RepositoryRoot();
        return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_key_bound_in_xaml_exists_in_both_catalogues()
    {
        var missing = new List<string>();

        foreach (var file in SourceFiles("*.xaml"))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in XamlBindingRegex().Matches(text))
            {
                var key = match.Groups[1].Value;
                if (!English.ContainsKey(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_literal_key_looked_up_in_code_exists_in_both_catalogues()
    {
        var missing = new List<string>();

        foreach (var file in SourceFiles("*.cs"))
        {
            if (file.Contains("Tests", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match match in CodeLookupRegex().Matches(text))
            {
                var key = match.Groups[1].Value;
                if (!English.ContainsKey(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_category_has_a_label()
    {
        foreach (var category in ArtifactCategories.All)
        {
            var key = $"Category_{category}";
            Assert.True(English.ContainsKey(key), $"Missing localisation for {key}");
        }

        Assert.True(English.ContainsKey("Category_all"));
    }

    [Fact]
    public void Every_score_feature_has_a_label()
    {
        foreach (var feature in new[]
                 {
                     "Age", "Rarity", "PathContext", "Filename", "Cluster",
                     "PersonalArtifact", "ModificationPattern", "Unusualness",
                 })
        {
            Assert.True(English.ContainsKey($"Feature_{feature}"), $"Missing localisation for Feature_{feature}");
            Assert.True(English.ContainsKey($"Reason_{feature}_None"), $"Missing localisation for Reason_{feature}_None");
        }
    }

    [Fact]
    public void Every_report_section_has_a_title_and_an_intro()
    {
        // Report section keys are produced by the generator, so read the table it actually uses.
        var sectionKeys = typeof(ReportGenerationService)
            .GetField("SectionDefinitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as System.Collections.IEnumerable;

        Assert.NotNull(sectionKeys);
        var count = 0;
        foreach (var entry in sectionKeys!)
        {
            var type = entry.GetType();
            var titleKey = (string)type.GetField("Item2")!.GetValue(entry)!;
            var introKey = (string)type.GetField("Item3")!.GetValue(entry)!;

            Assert.True(English.ContainsKey(titleKey), $"Missing localisation for {titleKey}");
            Assert.True(English.ContainsKey(introKey), $"Missing localisation for {introKey}");
            count++;
        }

        Assert.Equal(6, count);
    }

    [Fact]
    public void The_discovery_engine_label_exists()
    {
        Assert.True(English.ContainsKey("Discovery_Source_Local"));
    }

    /// <summary>Matches <c>{Binding [Some_Key], Source={StaticResource Loc}}</c>.</summary>
    [GeneratedRegex(@"\[([A-Za-z0-9_]+)\]\s*,\s*Source=\{StaticResource Loc\}")]
    private static partial Regex XamlBindingRegex();

    /// <summary>Matches literal keys passed to <c>Get("...")</c> or <c>Format("...")</c>.</summary>
    [GeneratedRegex(@"(?:\.Get|\.Format)\(\s*""([A-Za-z0-9_]+)""")]
    private static partial Regex CodeLookupRegex();
}
