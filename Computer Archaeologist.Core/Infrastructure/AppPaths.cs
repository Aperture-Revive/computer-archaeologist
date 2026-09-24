namespace ComputerArchaeologist.Core.Infrastructure;

/// <summary>
/// Every file the application writes lives under <c>%LOCALAPPDATA%\Computer Archaeologist</c>.
/// Nothing is ever written next to the executable and nothing outside this folder is modified:
/// the tool is strictly read-only with respect to the user's data.
/// </summary>
public static class AppPaths
{
    public const string FolderName = "Computer Archaeologist";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    public static string LogDirectory => Path.Combine(Root, "Logs");

    public static string SessionDirectory => Path.Combine(Root, "sessions");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string ExportsDirectory => Path.Combine(Root, "exports");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(SessionDirectory);
    }
}
