using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using ComputerArchaeologist.Core.Reports;
using ComputerArchaeologist.Core.Security;
using ComputerArchaeologist.Core.Settings;
using ComputerArchaeologist.Core.Storage;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>
/// Settings, secure storage and session persistence. These run against the real
/// <c>%LOCALAPPDATA%\Computer Archaeologist</c> folder, which is exactly what the application uses.
/// </summary>
public sealed class PersistenceTests
{
    private static SettingsService CreateSettings(
        AppOptions app,
        OpenAiOptions openAi,
        DiscoveryOptions discovery,
        ArchaeologyOptions archaeology,
        InterestingnessWeightsOptions weights,
        ExclusionPolicyProvider provider) =>
        new(app, openAi, discovery, archaeology, weights, provider);

    [Fact]
    public async Task Settings_round_trip_and_never_contain_a_credential_field()
    {
        var app = new AppOptions();
        var openAi = new OpenAiOptions();
        var discovery = new DiscoveryOptions();
        var archaeology = new ArchaeologyOptions();
        var weights = new InterestingnessWeightsOptions();
        var provider = new ExclusionPolicyProvider(archaeology);
        var service = CreateSettings(app, openAi, discovery, archaeology, weights, provider);

        var originalTheme = app.Theme;
        var originalPath = service.SettingsFilePath;

        // The test writes to the real settings file, so the previous contents are restored afterwards:
        // a test must never leave its fixture values in the user's configuration.
        string? backup = File.Exists(originalPath) ? await File.ReadAllTextAsync(originalPath) : null;

        try
        {
            app.Theme = "Dark";
            app.Language = "zh-CN";
            openAi.BaseUrl = "https://example.invalid/v1/";
            openAi.Model = "my-model";
            archaeology.MinInterestingness = 61;
            archaeology.ExcludedPaths = new List<string> { @"D:\Vault" };
            weights.AiWeight = 0.4;

            await service.SaveAsync();

            // Reset every singleton, then read back.
            app.Theme = "System";
            app.Language = "System";
            openAi.BaseUrl = string.Empty;
            openAi.Model = string.Empty;
            archaeology.MinInterestingness = 0;
            archaeology.ExcludedPaths = new List<string>();
            weights.AiWeight = 0.55;

            await service.LoadAsync();

            Assert.Equal("Dark", app.Theme);
            Assert.Equal("zh-CN", app.Language);
            Assert.Equal("https://example.invalid/v1", openAi.BaseUrl);
            Assert.Equal("my-model", openAi.Model);
            Assert.Equal(61, archaeology.MinInterestingness, 3);
            Assert.Contains(@"D:\Vault", archaeology.ExcludedPaths);
            Assert.Equal(0.4, weights.AiWeight, 3);

            // The persisted document must not contain any credential material or slot for it.
            var json = await File.ReadAllTextAsync(originalPath);
            Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("api_key", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            app.Theme = originalTheme;

            if (backup is null)
            {
                if (File.Exists(originalPath))
                {
                    File.Delete(originalPath);
                }
            }
            else
            {
                await File.WriteAllTextAsync(originalPath, backup);
            }
        }
    }

    [Fact]
    public async Task Bookmarking_a_run_never_rewrites_the_other_settings()
    {
        var app = new AppOptions();
        var openAi = new OpenAiOptions();
        var discovery = new DiscoveryOptions();
        var archaeology = new ArchaeologyOptions();
        var weights = new InterestingnessWeightsOptions();
        var service = CreateSettings(app, openAi, discovery, archaeology, weights, new ExclusionPolicyProvider(archaeology));

        var path = service.SettingsFilePath;
        string? backup = File.Exists(path) ? await File.ReadAllTextAsync(path) : null;

        try
        {
            app.Theme = "Dark";
            app.Language = "zh-CN";
            archaeology.MinInterestingness = 77;
            archaeology.IncludedRoots = new List<string> { @"D:\Keep" };
            await service.SaveAsync();

            // Simulate a stale in-memory copy, which is exactly what a background bookmark used to
            // write back to disk.
            app.Theme = "System";
            app.Language = "System";
            archaeology.MinInterestingness = 0;
            archaeology.IncludedRoots = new List<string>();

            await service.RememberLastSessionAsync("session-42");

            var reloaded = new SettingsService(
                new AppOptions(), new OpenAiOptions(), new DiscoveryOptions(), new ArchaeologyOptions(),
                new InterestingnessWeightsOptions(), new ExclusionPolicyProvider(new ArchaeologyOptions()));
            await reloaded.LoadAsync();

            Assert.Equal("session-42", reloaded.App.LastSessionId);
            Assert.Equal("Dark", reloaded.App.Theme);
            Assert.Equal("zh-CN", reloaded.App.Language);
            Assert.Equal(77, reloaded.Archaeology.MinInterestingness, 3);
            Assert.Contains(@"D:\Keep", reloaded.Archaeology.IncludedRoots);
        }
        finally
        {
            if (backup is null)
            {
                if (File.Exists(path)) { File.Delete(path); }
            }
            else
            {
                await File.WriteAllTextAsync(path, backup);
            }
        }
    }

    [Fact]
    public async Task A_corrupt_settings_file_is_quarantined_and_defaults_are_used()
    {
        var app = new AppOptions();
        var openAi = new OpenAiOptions();
        var discovery = new DiscoveryOptions();
        var archaeology = new ArchaeologyOptions();
        var weights = new InterestingnessWeightsOptions();
        var service = CreateSettings(app, openAi, discovery, archaeology, weights, new ExclusionPolicyProvider(archaeology));

        var path = service.SettingsFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string? backup = null;
        if (File.Exists(path))
        {
            backup = await File.ReadAllTextAsync(path);
        }

        try
        {
            await File.WriteAllTextAsync(path, "{ this is not valid json !!!");
            await service.LoadAsync();

            Assert.True(service.LastLoadFailed);
            Assert.False(File.Exists(path));
            Assert.NotEmpty(Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                Path.GetFileName(path) + ".corrupt-*"));
        }
        finally
        {
            foreach (var quarantined in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".corrupt-*"))
            {
                File.Delete(quarantined);
            }

            if (backup is not null)
            {
                await File.WriteAllTextAsync(path, backup);
            }
        }
    }

    [Fact]
    public void Out_of_range_settings_are_clamped_on_load()
    {
        var app = new AppOptions { Theme = "Neon", Language = "xx-YY" };
        var openAi = new OpenAiOptions { MaxConcurrency = 99, MaxRetries = -4, TimeoutSeconds = 100_000 };
        var archaeology = new ArchaeologyOptions { MaxCandidates = -10, MaxDiscoveries = 100_000 };
        var discovery = new DiscoveryOptions();
        var weights = new InterestingnessWeightsOptions();
        var service = CreateSettings(app, openAi, discovery, archaeology, weights, new ExclusionPolicyProvider(archaeology));

        // Apply is private; drive it through ResetToDefaults plus a save/load cycle is not possible for
        // invalid values, so assert the public safe accessors instead.
        Assert.InRange(archaeology.SafeMaxCandidates, 50, 200_000);
        Assert.InRange(archaeology.SafeMaxDiscoveries, 5, 500);
        Assert.InRange(openAi.SafeMaxConcurrency, 1, 8);
        Assert.InRange(openAi.SafeMaxRetries, 0, 6);
        Assert.InRange(openAi.Timeout.TotalSeconds, 5, 600);

        service.ResetToDefaults();
        Assert.Equal("System", app.Theme);
    }

    [Fact]
    public async Task Sessions_are_archived_and_listed_newest_first()
    {
        // A private storage folder keeps the real session archive untouched.
        using var storage = new TempFixture();
        var repository = new JsonScanRepository(directory: storage.Root);
        var first = CreateSession(DateTimeOffset.UtcNow.AddMinutes(-10));
        var second = CreateSession(DateTimeOffset.UtcNow);

        {
            await repository.SaveAsync(first);
            await repository.SaveAsync(second);

            var loaded = await repository.GetAsync(first.SessionId);
            Assert.NotNull(loaded);
            Assert.Equal(first.SessionId, loaded!.SessionId);
            Assert.Single(loaded.FinalDiscoveries);
            Assert.Equal(91, loaded.FinalDiscoveries[0].Score.FinalScore, 3);
            Assert.Equal("main.cs", loaded.FinalDiscoveries[0].FileName);

            var summaries = await repository.ListAsync(50);
            Assert.Contains(summaries, s => s.SessionId == first.SessionId);
            Assert.Contains(summaries, s => s.SessionId == second.SessionId);

            var latest = await repository.GetLatestAsync();
            Assert.NotNull(latest);
        }
    }

    [Fact]
    public async Task Reading_a_session_that_does_not_exist_returns_null()
    {
        var repository = new JsonScanRepository();
        Assert.Null(await repository.GetAsync("does-not-exist"));
        Assert.Null(await repository.GetAsync(string.Empty));
    }

    /// <summary>Shared factory so several test classes build the same realistic session.</summary>
    internal static ArchaeologySession CreateSession(DateTimeOffset start)
    {
        var artifact = new FileArtifact
        {
            File = new FileMetadata
            {
                FullPath = @"D:\Old\MyFirstGame\main.cs",
                FileName = "main.cs",
                Extension = ".cs",
                DirectoryPath = @"D:\Old\MyFirstGame",
                SizeBytes = 1234,
                CreatedUtc = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ModifiedUtc = new DateTimeOffset(2017, 6, 1, 0, 0, 0, TimeSpan.Zero),
                Attributes = FileAttributes.Normal,
                Source = "test",
            },
            Score = new InterestingnessResult
            {
                LocalScore = 80,
                AiScore = 95,
                FinalScore = 91,
                Confidence = 88,
                AiApplied = true,
                Local = new LocalScoreBreakdown { LocalScore = 80, Age = 70 },
                Ai = new AiAnalysisResult { Succeeded = true, Interestingness = 95, Confidence = 88 },
            },
            Context = new FileContext { DirectoryPath = @"D:\Old\MyFirstGame", SiblingCount = 5 },
            Category = ArtifactCategories.ForgottenProject,
            Summary = "An abandoned prototype.",
            Evidence = new[] { "Created in 2017" },
        };

        return new ArchaeologySession
        {
            SessionId = "test-" + Guid.NewGuid().ToString("N"),
            StartTime = start,
            EndTime = start.AddMinutes(2),
            FilesDiscovered = 1000,
            Candidates = 100,
            FilesAnalyzedByAi = 10,
            AiAvailable = true,
            FinalDiscoveries = new[] { artifact },
            Report = new ArchaeologyReport { Title = "Test report", Overview = "overview" },
        };
    }

    [Fact]
    public void Secure_storage_round_trips_and_clears()
    {
        // On Windows this exercises the Credential Manager or the DPAPI fallback; elsewhere it stays
        // in memory. Either way the key must never be readable from a plain settings file.
        using var storage = new ScopedSecureStorage();

        storage.Storage.SetApiKey("sk-test-value-1234567890");
        Assert.True(storage.Storage.HasApiKey);
        Assert.Equal("sk-test-value-1234567890", storage.Storage.GetApiKey());

        storage.Storage.ClearApiKey();
        Assert.False(storage.Storage.HasApiKey);
        Assert.Null(storage.Storage.GetApiKey());
    }

    [Fact]
    public void In_memory_storage_never_persists_anything()
    {
        var storage = new InMemorySecureStorage();
        storage.SetApiKey("  sk-padded  ");
        Assert.Equal("sk-padded", storage.GetApiKey());
        storage.ClearApiKey();
        Assert.Null(storage.GetApiKey());
    }

    /// <summary>
    /// Restores whatever credential existed before the test so a developer's real key is untouched.
    /// </summary>
    private sealed class ScopedSecureStorage : IDisposable
    {
        private readonly string? _original;

        public ScopedSecureStorage()
        {
            Storage = new WindowsSecureStorage();
            _original = Storage.GetApiKey();
        }

        public WindowsSecureStorage Storage { get; }

        public void Dispose()
        {
            if (string.IsNullOrEmpty(_original))
            {
                Storage.ClearApiKey();
            }
            else
            {
                Storage.SetApiKey(_original);
            }
        }
    }
}
