using System.Collections.Concurrent;

namespace LoomX.Assistant.Configuration;

internal static class TomlPathLockPool
{
    private static readonly ConcurrentDictionary<string, Entry> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static async ValueTask<IAsyncDisposable> AcquireAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var key = Path.GetFullPath(path);
        while (true)
        {
            var entry = Entries.GetOrAdd(key, static _ => new Entry());
            lock (entry.Gate)
            {
                if (entry.Removed)
                {
                    continue;
                }

                entry.ReferenceCount++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new Lease(key, entry);
            }
            catch
            {
                ReleaseReference(key, entry, releaseSemaphore: false);
                throw;
            }
        }
    }

    private static void ReleaseReference(string key, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        var remove = false;
        lock (entry.Gate)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                entry.Removed = true;
                remove = true;
            }
        }

        if (remove)
        {
            Entries.TryRemove(key, out _);
            entry.Semaphore.Dispose();
        }
    }

    private sealed class Entry
    {
        public object Gate { get; } = new();

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }

        public bool Removed { get; set; }
    }

    private sealed class Lease(string key, Entry entry) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                ReleaseReference(key, entry, releaseSemaphore: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
