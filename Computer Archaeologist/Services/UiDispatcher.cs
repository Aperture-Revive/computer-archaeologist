using Microsoft.UI.Dispatching;

namespace ComputerArchaeologist.Services;

/// <summary>
/// Marshals callbacks from the scan pipeline onto the UI thread. Registered once, while the
/// application is already running on the UI thread.
/// </summary>
public interface IUiDispatcher
{
    bool HasThreadAccess { get; }

    void Post(Action action);
}

public sealed class UiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue? _queue;

    public UiDispatcher()
    {
        _queue = DispatcherQueue.GetForCurrentThread();
    }

    public bool HasThreadAccess => _queue?.HasThreadAccess ?? true;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_queue is null || _queue.HasThreadAccess)
        {
            action();
            return;
        }

        _queue.TryEnqueue(() =>
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // A UI callback must never take the process down.
            }
        });
    }
}

/// <summary>Progress implementation that posts every report to the UI thread.</summary>
public sealed class UiProgress<T> : IProgress<T>
{
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<T> _handler;

    public UiProgress(IUiDispatcher dispatcher, Action<T> handler)
    {
        _dispatcher = dispatcher;
        _handler = handler;
    }

    public void Report(T value) => _dispatcher.Post(() => _handler(value));
}
