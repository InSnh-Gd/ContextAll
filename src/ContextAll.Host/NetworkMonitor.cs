namespace ContextAll.Host;

internal sealed class NetworkMonitor : IDisposable
{
    readonly Action changed;
    readonly TimeSpan debounce;
    readonly object gate = new();
    Timer? timer;
    bool disposed;

    public NetworkMonitor(Action changed, TimeSpan? debounce = null)
    {
        this.changed = changed;
        this.debounce = debounce ?? TimeSpan.FromSeconds(1);
    }

    internal TimeSpan Debounce => debounce;

    public void Signal()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            timer ??= new Timer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            timer.Change(debounce, Timeout.InfiniteTimeSpan);
        }
    }

    void Fire()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
        }

        changed();
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            timer?.Dispose();
            timer = null;
        }
    }
}
