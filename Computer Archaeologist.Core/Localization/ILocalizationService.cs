namespace ComputerArchaeologist.Core.Localization;

/// <summary>
/// Resolves every user-visible string. XAML never hard-codes text: views bind to the
/// <see cref="Loc"/> indexer, view models call <see cref="Get"/> / <see cref="Format"/>.
/// </summary>
public interface ILocalizationService
{
    /// <summary>BCP-47 tag currently used for rendering, for example <c>en-US</c> or <c>zh-CN</c>.</summary>
    string CurrentLanguage { get; }

    /// <summary>Languages this build ships a catalogue for.</summary>
    IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>Raised after the language changed so bindings can refresh.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>Localized string for <paramref name="key"/>, or the key itself when it is missing.</summary>
    string Get(string key);

    /// <summary>Localized and formatted string. Uses invariant culture-independent templates.</summary>
    string Format(string key, params object?[] args);

    /// <summary>Switches language. Accepts a BCP-47 tag or <c>System</c>.</summary>
    void SetLanguage(string language);

    /// <summary>Keys that were requested but not found, for diagnostics.</summary>
    IReadOnlyCollection<string> MissingKeys { get; }
}
