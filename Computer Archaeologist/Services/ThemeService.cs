using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ComputerArchaeologist.Services;

/// <summary>Applies Light / Dark / Follow-system to the window content.</summary>
public interface IThemeService
{
    string Current { get; }

    event EventHandler<string>? ThemeChanged;

    void Apply(string theme);

    /// <summary>The main window registers itself so the theme can be pushed onto its root element.</summary>
    void Register(Window window);
}

public sealed class ThemeService : IThemeService
{
    private readonly List<WeakReference<Window>> _windows = new();

    public string Current { get; private set; } = "System";

    public event EventHandler<string>? ThemeChanged;

    public void Register(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _windows.Add(new WeakReference<Window>(window));
        ApplyTo(window);
    }

    public void Apply(string theme)
    {
        Current = theme is "Light" or "Dark" ? theme : "System";

        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].TryGetTarget(out var window))
            {
                ApplyTo(window);
            }
            else
            {
                _windows.RemoveAt(i);
            }
        }

        ThemeChanged?.Invoke(this, Current);
    }

    private void ApplyTo(Window window)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = Current switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }
}

/// <summary>Small helper so every page can reuse the same Mica/acrylic material handling.</summary>
public static class BackdropHelper
{
    /// <summary>Applies Mica when the OS supports it, silently falling back to the default material.</summary>
    public static void ApplyMica(Window window)
    {
        try
        {
            window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        catch (Exception)
        {
            // Older builds fall back to the default backdrop; nothing to do.
        }
    }
}
