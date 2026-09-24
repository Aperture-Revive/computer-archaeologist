using System.Diagnostics;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Models;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Services;

/// <summary>
/// Navigation between pages. Views never talk to the <c>Frame</c> directly.
/// </summary>
public interface INavigationService
{
    /// <summary>Route identifiers used by every navigation call.</summary>
    const string Home = "home";
    const string Archaeology = "archaeology";
    const string Discoveries = "discoveries";
    const string Reports = "reports";
    const string Settings = "settings";
    const string Detail = "detail";

    bool CanGoBack { get; }

    event EventHandler? Navigated;

    void Initialize(Microsoft.UI.Xaml.Controls.Frame frame);

    bool Navigate(string route, object? parameter = null);

    void GoBack();
}

public sealed class NavigationService : INavigationService
{
    private Microsoft.UI.Xaml.Controls.Frame? _frame;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public event EventHandler? Navigated;

    public void Initialize(Microsoft.UI.Xaml.Controls.Frame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _frame.Navigated += (_, _) => Navigated?.Invoke(this, EventArgs.Empty);
    }

    public bool Navigate(string route, object? parameter = null)
    {
        if (_frame is null)
        {
            return false;
        }

        var pageType = route switch
        {
            INavigationService.Home => typeof(Views.HomePage),
            INavigationService.Archaeology => typeof(Views.ArchaeologyPage),
            INavigationService.Discoveries => typeof(Views.DiscoveriesPage),
            INavigationService.Reports => typeof(Views.ReportsPage),
            INavigationService.Settings => typeof(Views.SettingsPage),
            INavigationService.Detail => typeof(Views.DiscoveryDetailPage),
            _ => typeof(Views.HomePage),
        };

        return _frame.Navigate(pageType, parameter);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }
}

/// <summary>
/// Opens files and folders on explicit user request only. Executable types are confirmed by the
/// caller before this service is invoked, and nothing here ever writes, moves or deletes anything.
/// </summary>
public interface IFileLauncherService
{
    bool OpenFile(string fullPath, out string? error);

    bool OpenFolder(string fullPath, out string? error);
}

public sealed class FileLauncherService : IFileLauncherService
{
    private readonly ILogger<FileLauncherService>? _logger;

    public FileLauncherService(ILogger<FileLauncherService>? logger = null) => _logger = logger;

    public bool OpenFile(string fullPath, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
        {
            error = "The file no longer exists.";
            return false;
        }

        try
        {
            // ShellExecute is the Windows-native "open with the default handler" path and is only
            // reached after an explicit user gesture.
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true })?.Dispose();
            _logger?.LogInformation("User opened a file from the discoveries list");
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            error = ex.Message;
            _logger?.LogWarning(ex, "Opening the file failed");
            return false;
        }
    }

    public bool OpenFolder(string fullPath, out string? error)
    {
        error = null;

        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            error = "The folder no longer exists.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            error = ex.Message;
            _logger?.LogWarning(ex, "Opening the folder failed");
            return false;
        }
    }
}

/// <summary>Clipboard access used by the "copy path" action.</summary>
public interface IClipboardService
{
    bool SetText(string text);
}

public sealed class ClipboardService : IClipboardService
{
    public bool SetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Shell helpers shared by the views.</summary>
public static class ExternalLinks
{
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing better to do; the link simply does not open.
        }
    }
}

/// <summary>Guards the classification used before opening a potentially executable file.</summary>
public static class ExecutableGuard
{
    /// <summary>True when opening this file needs an explicit confirmation dialog.</summary>
    public static bool RequiresConfirmation(FileMetadata file) =>
        file is not null && FileTypeCatalog.IsExecutable(file.Extension);
}
