namespace AI_Supporter;

/// <summary>
/// Tracks disposable services and ensures orderly shutdown.
/// </summary>
public sealed class ServiceLifetimeManager : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _asyncDisposables = new();
    private readonly List<IDisposable> _disposables = new();
    private readonly object _lock = new();
    private bool _disposed;

    public void Track(IDisposable service)
    {
        lock (_lock)
            _disposables.Add(service);
    }

    public void Track(IAsyncDisposable service)
    {
        lock (_lock)
            _asyncDisposables.Add(service);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        // Dispose in reverse order (last tracked = first to dispose)
        for (var i = _asyncDisposables.Count - 1; i >= 0; i--)
        {
            try { await _asyncDisposables[i].DisposeAsync(); }
            catch { /* best-effort */ }
        }

        for (var i = _disposables.Count - 1; i >= 0; i--)
        {
            try { _disposables[i].Dispose(); }
            catch { /* best-effort */ }
        }
    }
}
