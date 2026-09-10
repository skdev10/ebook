using System.Collections.Concurrent;

namespace EBookDashboard.Services;

/// <summary>
/// Serializes concurrent generate calls for the same (book, chapter) slot
/// so the Writer UI and pipeline cannot overwrite each other.
/// </summary>
public static class ChapterGenerationSlotLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    /// <summary>Wait for exclusive access to one chapter slot. Dispose to release.</summary>
    public static async Task<IDisposable> AcquireAsync(int bookId, int chapterNumber, CancellationToken cancellationToken = default)
    {
        var key = bookId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                  + ":"
                  + Math.Max(1, chapterNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var slot = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await slot.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(slot);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _slot;
        private int _disposed;

        public Releaser(SemaphoreSlim slot) => _slot = slot;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _slot.Release();
        }
    }
}
