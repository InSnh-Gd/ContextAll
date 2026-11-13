using System.Net;
using System.Net.Quic;

namespace ContextAll.Host;

internal sealed class ListenerSet : IAsyncDisposable
{
    readonly Func<IPAddress, CancellationToken, Task<QuicListener>> create;
    readonly Func<QuicConnection, CancellationToken, Task> accepted;
    readonly Action<string> log;
    readonly CancellationToken token;
    readonly object gate = new();
    readonly SemaphoreSlim rebuild = new(1, 1);
    readonly Dictionary<IPAddress, Entry> entries = [];
    readonly HashSet<IPAddress> warned = [];
    bool disposed;

    public ListenerSet(
        Func<IPAddress, CancellationToken, Task<QuicListener>> create,
        Func<QuicConnection, CancellationToken, Task> accepted,
        Action<string> log,
        CancellationToken token)
    {
        this.create = create;
        this.accepted = accepted;
        this.log = log;
        this.token = token;
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return entries.Count;
            }
        }
    }

    public async Task RebuildAsync(IReadOnlyCollection<IPAddress> addresses)
    {
        try
        {
            await rebuild.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var desired = addresses.Distinct().ToArray();
            foreach (var address in desired)
            {
                lock (gate)
                {
                    if (disposed || entries.ContainsKey(address))
                    {
                        continue;
                    }
                }

                try
                {
                    var listener = await create(address, token).ConfigureAwait(false);
                    var entry = new Entry(listener);
                    var added = false;
                    lock (gate)
                    {
                        if (!disposed)
                        {
                            added = entries.TryAdd(address, entry);
                        }
                    }

                    if (added)
                    {
                        lock (gate)
                        {
                            warned.Remove(address);
                        }

                        entry.Accept = Task.Run(() => AcceptLoop(entry), token);
                    }
                    else
                    {
                        await listener.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or System.Net.Sockets.SocketException or QuicException or InvalidOperationException or IOException)
                {
                    lock (gate)
                    {
                        if (warned.Add(address))
                        {
                            log("quic-skipped " + address + " " + ex.GetType().Name + " " + ex.Message);
                        }
                    }
                }
            }

            List<Entry> removed = [];
            lock (gate)
            {
                foreach (var pair in entries.ToArray())
                {
                    if (!desired.Contains(pair.Key))
                    {
                        entries.Remove(pair.Key);
                        warned.Remove(pair.Key);
                        removed.Add(pair.Value);
                    }
                }
            }

            foreach (var entry in removed)
            {
                await entry.Listener.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            rebuild.Release();
        }
    }

    async Task AcceptLoop(Entry entry)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var connection = await entry.Listener.AcceptConnectionAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => accepted(connection, token), token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or QuicException)
        {
            // The listener was removed or the daemon stopped.
        }
    }

    public async ValueTask DisposeAsync()
    {
        Entry[] current;
        lock (gate)
        {
            disposed = true;
            current = entries.Values.ToArray();
            entries.Clear();
            warned.Clear();
        }

        foreach (var entry in current)
        {
            await entry.Listener.DisposeAsync().ConfigureAwait(false);
        }
    }

    sealed class Entry(QuicListener listener)
    {
        public QuicListener Listener { get; } = listener;

        public Task? Accept { get; set; }
    }
}
