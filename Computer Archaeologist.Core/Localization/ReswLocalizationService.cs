using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Localization;

/// <summary>
/// Reads the canonical <c>.resw</c> catalogues that are embedded into this assembly.
/// <para>
/// Using the very same <c>.resw</c> documents that the Windows App SDK compiles into
/// <c>resources.pri</c> keeps exactly one translatable source of truth (specification section 29)
/// while still letting the localization service run inside unit tests and inside the report
/// generator without a Windows UI stack.
/// </para>
/// </summary>
public sealed class ReswLocalizationService : ILocalizationService
{
    public const string DefaultLanguage = "en-US";
    public const string ChineseSimplified = "zh-CN";

    private static readonly string[] Supported = { "en-US", "zh-CN" };

    private readonly ILogger<ReswLocalizationService>? _logger;
    private readonly ConcurrentDictionary<string, string> _missing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _catalogues;

    public ReswLocalizationService(ILogger<ReswLocalizationService>? logger = null, string? initialLanguage = null)
    {
        _logger = logger;
        _catalogues = LoadCatalogues();
        CurrentLanguage = Normalize(initialLanguage ?? CultureInfo.CurrentUICulture.Name);
    }

    public string CurrentLanguage { get; private set; }

    public IReadOnlyList<string> SupportedLanguages => Supported;

    public event EventHandler? LanguageChanged;

    public IReadOnlyCollection<string> MissingKeys => _missing.Keys.ToArray();

    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (TryLookup(CurrentLanguage, key, out var value))
        {
            return value;
        }

        // Fall back to the neutral catalogue before giving up, so a partially translated
        // language never leaves a blank label in the UI.
        if (TryLookup(DefaultLanguage, key, out var fallback))
        {
            return fallback;
        }

        if (_missing.TryAdd(key, key))
        {
            _logger?.LogWarning("Missing localization key {Key} for language {Language}", key, CurrentLanguage);
        }

        return key;
    }

    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException ex)
        {
            _logger?.LogWarning(ex, "Malformed localization template for key {Key}", key);
            return template;
        }
    }

    public void SetLanguage(string language)
    {
        var normalized = Normalize(language);
        if (string.Equals(normalized, CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CurrentLanguage = normalized;
        _missing.Clear();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Maps any incoming tag (including <c>System</c>) onto a supported language.</summary>
    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            return FromCulture(CultureInfo.CurrentUICulture);
        }

        return FromCulture(SafeCulture(language));
    }

    private static CultureInfo SafeCulture(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private static string FromCulture(CultureInfo culture)
    {
        var name = culture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return ChineseSimplified;
        }

        return DefaultLanguage;
    }

    private bool TryLookup(string language, string key, out string value)
    {
        if (_catalogues.TryGetValue(language, out var catalogue) && catalogue.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadCatalogues()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(ReswLocalizationService).Assembly;

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".resw", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var language = ExtractLanguage(resourceName);
            if (language is null)
            {
                continue;
            }

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    continue;
                }

                var entries = ParseResw(stream);
                result[language] = entries;
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
            {
                // A broken catalogue must never take the application down.
            }
        }

        foreach (var language in Supported)
        {
            result.TryAdd(language, new Dictionary<string, string>(StringComparer.Ordinal));
        }

        return result;
    }

    private static string? ExtractLanguage(string resourceName)
    {
        // Embedded logical names look like: ComputerArchaeologist.Core.Strings.zh-CN.Resources.resw,
        // except that the resource-name generator rewrites '-' to '_', so both spellings must be
        // understood here.
        var parts = resourceName.Split('.', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var normalized = part.Replace('_', '-');

            if (normalized.Equals(ChineseSimplified, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return ChineseSimplified;
            }

            if (normalized.Equals(DefaultLanguage, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultLanguage;
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseResw(Stream stream)
    {
        var document = XDocument.Load(stream);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var data in document.Descendants("data"))
        {
            var name = data.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var value = data.Element("value")?.Value ?? string.Empty;
            entries[name] = value;
        }

        return entries;
    }

    /// <summary>Test/diagnostic helper: exposes the merged catalogue of one language.</summary>
    public IReadOnlyDictionary<string, string> GetCatalogue(string language) =>
        _catalogues.TryGetValue(Normalize(language), out var catalogue)
            ? catalogue
            : new Dictionary<string, string>(StringComparer.Ordinal);
}
