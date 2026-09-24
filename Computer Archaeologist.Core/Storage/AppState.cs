using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Settings;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Storage;

/// <summary>
/// Shared, in-memory view of the most recent archaeology run. Pages observe it instead of passing
/// sessions around, and it is the only place that decides when a run is persisted.
/// </summary>
public sealed class AppState
{
    private readonly IScanRepository _repository;
    private readonly ISettingsService _settings;
    private readonly ILogger<AppState>? _logger;

    public AppState(IScanRepository repository, ISettingsService settings, ILogger<AppState>? logger = null)
    {
        _repository = repository;
        _settings = settings;
        _logger = logger;
    }

    public ArchaeologySession? CurrentSession { get; private set; }

    public ArchaeologyReport? Report => CurrentSession?.Report;

    public IReadOnlyList<FileArtifact> Discoveries =>
        CurrentSession?.FinalDiscoveries ?? Array.Empty<FileArtifact>();

    public bool HasReport => Report is not null;

    public bool HasDiscoveries => Discoveries.Count > 0;

    public IReadOnlyList<SessionSummary> RecentSessions { get; private set; } = Array.Empty<SessionSummary>();

    /// <summary>Raised on the thread that changed the state; subscribers marshal to the UI themselves.</summary>
    public event EventHandler? SessionChanged;

    public event EventHandler? HistoryChanged;

    /// <summary>Loads the previous run (if any) so Home and Reports are populated at start-up.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshHistoryAsync(cancellationToken).ConfigureAwait(false);

        var sessionId = _settings.App.LastSessionId;
        ArchaeologySession? session = null;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            session = await _repository.GetAsync(sessionId!, cancellationToken).ConfigureAwait(false);
        }

        session ??= await _repository.GetLatestAsync(cancellationToken).ConfigureAwait(false);

        if (session is not null)
        {
            SetSession(session, persist: false);
        }
    }

    public async Task RefreshHistoryAsync(CancellationToken cancellationToken = default)
    {
        RecentSessions = await _repository.ListAsync(20, cancellationToken).ConfigureAwait(false);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSession(ArchaeologySession session, bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(session);

        CurrentSession = session;
        SessionChanged?.Invoke(this, EventArgs.Empty);

        if (persist)
        {
            _ = PersistAsync(session, CancellationToken.None);
        }
    }

    public async Task OpenSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is not null)
        {
            SetSession(session, persist: false);
        }
    }

    private async Task PersistAsync(ArchaeologySession session, CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);

            // Only the bookmark is persisted here. Writing the whole settings document from a
            // background task is what previously let a stale in-memory copy overwrite the user's
            // options; option changes are now exclusively written by the Settings page.
            await _settings.RememberLastSessionAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            await RefreshHistoryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "The session could not be archived");
        }
    }
}
