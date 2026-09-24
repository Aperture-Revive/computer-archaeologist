using ComputerArchaeologist.Core.Discovery;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>
/// The exclusion policy decides what never reaches scoring, so both directions matter: noise must be
/// filtered, and genuine user files must survive.
/// </summary>
public sealed class PathExclusionPolicyTests
{
    private static readonly PathExclusionPolicy Policy = new();

    [Theory]
    [InlineData(@"C:\Windows\System32\kernel32.dll", ".dll")]
    [InlineData(@"C:\Windows\Fonts\segoeui.ttf", ".ttf")]
    [InlineData(@"C:\Program Files\SomeApp\app.exe", ".exe")]
    [InlineData(@"C:\Program Files (x86)\Other\lib.dll", ".dll")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\$RABC.txt", ".txt")]
    [InlineData(@"C:\ProgramData\Package Cache\{guid}\install.msi", ".msi")]
    [InlineData(@"C:\Recovery\WindowsRE\winre.wim", ".wim")]
    [InlineData(@"C:\PerfLogs\Admin\system.log", ".log")]
    public void System_locations_are_excluded(string path, string extension) =>
        Assert.True(Policy.IsExcluded(path, extension), $"{path} should be excluded.");

    [Theory]
    [InlineData(@"C:\Users\Me\AppData\Local\Temp\tmp4A2F.tmp", ".tmp")]
    [InlineData(@"C:\Users\Me\AppData\Local\Google\Chrome\User Data\Default\Cache\f_0001", "")]
    [InlineData(@"C:\Users\Me\AppData\Local\Microsoft\Windows\INetCache\IE\abc.dat", ".dat")]
    [InlineData(@"C:\Users\Me\AppData\Local\CrashDumps\app.exe.1234.dmp", ".dmp")]
    [InlineData(@"C:\Users\Me\AppData\Local\Google\Chrome\User Data\Default\Code Cache\js\1_0", "")]
    public void Browser_and_system_caches_are_excluded(string path, string extension) =>
        Assert.True(Policy.IsExcluded(path, extension), $"{path} should be excluded.");

    [Theory]
    [InlineData(@"D:\Projects\MyGame\node_modules\left-pad\index.js", ".js")]
    [InlineData(@"D:\Projects\MyGame\.git\objects\ab\cdef1234", "")]
    [InlineData(@"D:\Projects\MyGame\bin\Debug\net8.0\MyGame.dll", ".dll")]
    [InlineData(@"D:\Projects\MyGame\obj\Debug\MyGame.pdb", ".pdb")]
    [InlineData(@"C:\Users\Me\.nuget\packages\newtonsoft.json\13.0.1\lib\net45\a.dll", ".dll")]
    public void Build_output_and_package_caches_are_excluded(string path, string extension) =>
        Assert.True(Policy.IsExcluded(path, extension), $"{path} should be excluded.");

    [Theory]
    [InlineData(@"C:\Users\Me\Documents\School\Grade8\Physics\homework.docx", ".docx")]
    [InlineData(@"C:\Users\Me\Desktop\notes.txt", ".txt")]
    [InlineData(@"C:\Users\Me\Pictures\2015\holiday\photo.jpg", ".jpg")]
    [InlineData(@"D:\OldProjects\MyFirstGame\main.cs", ".cs")]
    [InlineData(@"D:\OldProjects\MyFirstGame\README.txt", ".txt")]
    [InlineData(@"C:\Users\Me\Documents\My Game\player.cs", ".cs")]
    [InlineData(@"D:\Games\Skyrim\Saves\Save1.ess", ".ess")]
    [InlineData(@"D:\Games\Stardew Valley\Saves\Farmer_123456", "")]
    [InlineData(@"C:\Users\Me\OneDrive\Documents\thesis.docx", ".docx")]
    [InlineData(@"E:\Backups\2017\photo album.zip", ".zip")]
    public void Genuine_user_files_are_never_excluded(string path, string extension) =>
        Assert.False(Policy.IsExcluded(path, extension), $"{path} must NOT be excluded.");

    [Fact]
    public void Game_save_folders_are_not_treated_as_build_output()
    {
        // "bin" is a build folder, but a game's own save directory must survive.
        Assert.False(Policy.IsExcluded(@"D:\Games\MyRPG\Saves\slot1.sav", ".sav"));
        Assert.False(Policy.IsExcluded(@"D:\Games\MyRPG\Save Games\profile.dat", ".dat"));
        Assert.False(Policy.IsExcluded(@"D:\Games\MyRPG\build-a-base\design.txt", ".txt"));
    }

    [Fact]
    public void User_configured_exclusions_are_added_to_the_defaults()
    {
        var policy = new PathExclusionPolicy(
            extraPaths: new[] { @"D:\SecretVault", @"D:\Media\raw" },
            extraExtensions: new[] { ".iso", "bak" });

        Assert.True(policy.IsExcluded(@"D:\SecretVault\diary.txt", ".txt"));
        Assert.True(policy.IsExcluded(@"D:\Media\raw\clip.mp4", ".mp4"));
        Assert.True(policy.IsExcluded(@"D:\Media\backup.bak", ".bak"));
        Assert.True(policy.IsExcluded(@"D:\Media\image.iso", ".iso"));

        // Everything else keeps working.
        Assert.False(policy.IsExcluded(@"D:\Documents\diary.txt", ".txt"));
    }

    [Fact]
    public void Included_roots_restrict_discovery()
    {
        var policy = new PathExclusionPolicy(includedRoots: new[] { @"D:\OldProjects" });

        Assert.False(policy.IsExcluded(@"D:\OldProjects\game\main.cs", ".cs"));
        Assert.True(policy.IsExcluded(@"C:\Users\Me\Documents\notes.txt", ".txt"));
    }

    [Fact]
    public void Exclusion_policy_is_never_case_or_separator_sensitive()
    {
        Assert.True(Policy.IsExcluded(@"c:/WINDOWS/system32/KERNEL32.DLL", ".DLL"));
        Assert.False(Policy.IsExcluded(@"c:/Users/Me/Documents/notes.TXT", ".TXT"));
    }
}
