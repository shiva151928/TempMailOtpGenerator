using System.Collections.Concurrent;

namespace TempMailOtp.Api.Services.Inbox;

/// <summary>
/// Serializes provider syncs per inbox within this process so concurrent polls do not download
/// the same messages twice. Across multiple instances, the unique (InboxId, ProviderMessageId)
/// index is the final duplicate guard.
/// </summary>
public sealed class InboxSyncLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid inboxId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(inboxId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    /// <summary>Forgets the lock of a deleted inbox. A sync still holding it finishes normally.</summary>
    public void Remove(Guid inboxId) => _locks.TryRemove(inboxId, out _);

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
