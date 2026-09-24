using System.ComponentModel;

namespace ComputerArchaeologist.Core.Localization;

/// <summary>
/// Binding-friendly facade over <see cref="ILocalizationService"/>.
/// <para>
/// XAML uses it as <c>{Binding [Home_Title], Source={StaticResource Loc}}</c>, which keeps every
/// user-visible string out of the markup and makes live language switching a single
/// <see cref="PropertyChanged"/> notification.
/// </para>
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private static ILocalizationService? _service;
    private static Loc? _instance;

    /// <summary>
    /// Creates the instance declared in <c>App.xaml</c> and registers it as the binding source.
    /// The last instance constructed wins, and <see cref="Instance"/> lazily creates one when the
    /// resource dictionary has not been instantiated yet.
    /// </summary>
    public Loc() => _instance = this;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The instance every binding points at.</summary>
    public static Loc Instance => _instance ??= new Loc();

    /// <summary>Ambient service. Assigned once during start-up and whenever the language changes.</summary>
    public static ILocalizationService? Service
    {
        get => _service;
        set
        {
            if (ReferenceEquals(_service, value))
            {
                return;
            }

            if (_service is not null)
            {
                _service.LanguageChanged -= OnLanguageChanged;
            }

            _service = value;

            if (_service is not null)
            {
                _service.LanguageChanged += OnLanguageChanged;
            }

            Refresh();
        }
    }

    /// <summary>Indexer consumed by XAML bindings.</summary>
    public string this[string key] => _service?.Get(key) ?? key;

    /// <summary>Notifies every localized binding in the visual tree to re-read its value.</summary>
    public static void Refresh() =>
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));

    private static void OnLanguageChanged(object? sender, EventArgs e) => Refresh();
}
