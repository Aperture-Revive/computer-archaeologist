using ComputerArchaeologist.Core.Localization;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>
/// The bilingual catalogue is the whole localisation story, so it is verified directly: both
/// languages must load out of the assembly, contain exactly the same keys, and resolve real text.
/// </summary>
public sealed class LocalizationTests
{
    private readonly ReswLocalizationService _localization = new();

    [Fact]
    public void Both_shipped_languages_load_from_the_embedded_resw_catalogue()
    {
        Assert.Contains("en-US", _localization.SupportedLanguages);
        Assert.Contains("zh-CN", _localization.SupportedLanguages);

        var english = _localization.GetCatalogue("en-US");
        var chinese = _localization.GetCatalogue("zh-CN");

        Assert.True(english.Count > 300, $"Only {english.Count} English strings were embedded.");
        Assert.Equal(english.Count, chinese.Count);
    }

    [Fact]
    public void The_two_catalogues_have_identical_key_sets()
    {
        var english = _localization.GetCatalogue("en-US").Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var chinese = _localization.GetCatalogue("zh-CN").Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        var missingInChinese = english.Except(chinese, StringComparer.Ordinal).ToArray();
        var missingInEnglish = chinese.Except(english, StringComparer.Ordinal).ToArray();

        Assert.Empty(missingInChinese);
        Assert.Empty(missingInEnglish);
    }

    [Fact]
    public void No_translation_is_left_empty()
    {
        foreach (var language in _localization.SupportedLanguages)
        {
            foreach (var pair in _localization.GetCatalogue(language))
            {
                Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{language}:{pair.Key} is empty.");
            }
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public void Core_ui_keys_resolve_in_both_languages(string language)
    {
        _localization.SetLanguage(language);

        foreach (var key in new[]
                 {
                     "App_DisplayName", "Nav_Home", "Nav_Archaeology", "Nav_Discoveries", "Nav_Reports",
                     "Nav_Settings", "Home_Start", "Arch_Title", "Disc_Title", "Report_Title",
                     "Settings_Title", "Settings_Privacy_Notice", "Discovery_Source_Local", "Arch_Stage_Preparing",
                     "Ai_Error_Unauthorized", "Category_forgotten_project", "Feature_Age",
                 })
        {
            var value = _localization.Get(key);
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.NotEqual(key, value);
        }

        Assert.Empty(_localization.MissingKeys);
    }

    [Fact]
    public void Unknown_keys_are_reported_rather_than_crashing()
    {
        var value = _localization.Get("This_Key_Does_Not_Exist");
        Assert.Equal("This_Key_Does_Not_Exist", value);
        Assert.Contains("This_Key_Does_Not_Exist", _localization.MissingKeys);
    }

    [Fact]
    public void Language_switching_updates_resolved_text_and_raises_a_notification()
    {
        _localization.SetLanguage("en-US");
        var english = _localization.Get("App_DisplayName");

        var raised = 0;
        _localization.LanguageChanged += (_, _) => raised++;

        _localization.SetLanguage("zh-CN");
        var chinese = _localization.Get("App_DisplayName");

        // Setting the same language again must be a no-op.
        _localization.SetLanguage("zh-CN");

        Assert.Equal(1, raised);
        Assert.NotEqual(english, chinese);
        Assert.Equal("Computer Archaeologist", english);
        Assert.Equal("计算机考古学家", chinese);
    }

    [Fact]
    public void System_language_maps_onto_a_shipped_catalogue()
    {
        _localization.SetLanguage("System");
        Assert.Contains(_localization.CurrentLanguage, _localization.SupportedLanguages);

        _localization.SetLanguage("de-DE");
        Assert.Equal("en-US", _localization.CurrentLanguage);

        _localization.SetLanguage("zh-Hans");
        Assert.Equal("zh-CN", _localization.CurrentLanguage);
    }

    [Fact]
    public void Formatted_templates_substitute_arguments()
    {
        _localization.SetLanguage("en-US");
        var text = _localization.Format("Disc_Subtitle", 23);
        Assert.Contains("23", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Loc_facade_returns_the_active_language()
    {
        var service = new ReswLocalizationService(initialLanguage: "zh-CN");
        Loc.Service = service;

        Assert.Equal("计算机考古学家", Loc.Instance["App_DisplayName"]);

        service.SetLanguage("en-US");
        Assert.Equal("Computer Archaeologist", Loc.Instance["App_DisplayName"]);

        Loc.Service = null;
    }
}
