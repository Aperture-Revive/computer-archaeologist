using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using ComputerArchaeologist.Core.Settings;
using ComputerArchaeologist.Core.Storage;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>
/// The shared run state. Restoring the previous run at start-up is what makes Home show the last scan
/// and Reports reopen the last report, so it is pinned down here.
/// </summary>
public sealed class AppStateTests : IDisposable
{
    private readonly TempFixture _storage = new();

    private AppState CreateState(AppOptions app, out JsonScanRepository repository)
    {
        // A private storage folder keeps these tests hermetic: they never read or write the real archive.
        repository = new JsonScanRepository(directory: _storage.Root);
        var settings = new SettingsService(
            app,
            new OpenAiOptions(),
            new DiscoveryOptions(),
            new ArchaeologyOptions(),
            new InterestingnessWeightsOptions(),
            new ExclusionPolicyProvider(new ArchaeologyOptions()));

        return new AppState(repository, settings);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task Initialising_restores_the_last_run_and_the_history()
    {
        var app = new AppOptions();
        var state = CreateState(app, out var repository);
        var session = PersistenceTests.CreateSession(DateTimeOffset.UtcNow);

        await repository.SaveAsync(session);
        app.LastSessionId = session.SessionId;

        await state.InitializeAsync();

        Assert.NotNull(state.CurrentSession);
        Assert.Equal(session.SessionId, state.CurrentSession!.SessionId);
        Assert.True(state.HasDiscoveries);
        Assert.True(state.HasReport);
        Assert.Contains(state.RecentSessions, s => s.SessionId == session.SessionId);
    }

    [Fact]
    public async Task Initialising_with_no_history_leaves_the_state_empty()
    {
        var app = new AppOptions { LastSessionId = null };
        var state = CreateState(app, out _);

        await state.InitializeAsync();

        Assert.Null(state.CurrentSession);
        Assert.False(state.HasReport);
        Assert.False(state.HasDiscoveries);
        Assert.Empty(state.RecentSessions);
    }

    [Fact]
    public async Task A_dangling_session_pointer_falls_back_to_the_latest_session()
    {
        var app = new AppOptions();
        var state = CreateState(app, out var repository);
        var session = PersistenceTests.CreateSession(DateTimeOffset.UtcNow);

        await repository.SaveAsync(session);
        app.LastSessionId = "session-that-no-longer-exists";

        await state.InitializeAsync();

        Assert.NotNull(state.CurrentSession);
        Assert.Equal(session.SessionId, state.CurrentSession!.SessionId);
    }

    [Fact]
    public async Task Setting_a_session_raises_the_change_notification()
    {
        var app = new AppOptions();
        var state = CreateState(app, out _);
        var session = PersistenceTests.CreateSession(DateTimeOffset.UtcNow);

        var raised = 0;
        state.SessionChanged += (_, _) => raised++;

        state.SetSession(session, persist: false);

        Assert.Equal(1, raised);
        Assert.Equal(session.SessionId, state.CurrentSession!.SessionId);
    }
}
