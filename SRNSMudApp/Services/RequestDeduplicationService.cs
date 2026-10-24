namespace SRNSMudApp.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// <see cref="IRequestDeduplicationService"/> のインメモリ実装。
/// 同一キーに対する並行リクエストの排他ロック制御および直近完了結果のキャッシュを提供する。
/// </summary>
public sealed class RequestDeduplicationService : IRequestDeduplicationService
{
    private readonly IMemoryCache? _cache;
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);
    private readonly Lock _syncRoot = new();

    public RequestDeduplicationService(IMemoryCache? cache = null)
    {
        _cache = cache;
    }

    public async Task<IDisposable> AcquireLockAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        LockEntry entry;
        lock (_syncRoot)
        {
            entry = _locks.GetOrAdd(key, _ => new LockEntry());
            entry.RefCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(key, entry, this);
        }
        catch
        {
            ReleaseEntry(key, entry, acquired: false);
            throw;
        }
    }

    private void ReleaseEntry(string key, LockEntry entry, bool acquired)
    {
        if (acquired)
        {
            entry.Semaphore.Release();
        }

        lock (_syncRoot)
        {
            entry.RefCount--;
            if (entry.RefCount <= 0)
            {
                if (((ICollection<KeyValuePair<string, LockEntry>>)_locks).Remove(new KeyValuePair<string, LockEntry>(key, entry)))
                {
                    entry.Semaphore.Dispose();
                }
            }
        }
    }

    public bool TryGetRecentResult<T>(string key, out T? result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_cache != null && _cache.TryGetValue(key, out object? cached) && cached is T typed)
        {
            result = typed;
            return true;
        }

        result = default;
        return false;
    }

    public void RecordResult<T>(string key, T result, TimeSpan? retention = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _cache?.Set(key, result, retention ?? TimeSpan.FromSeconds(30));
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount;
    }

    private sealed class Releaser(string key, LockEntry entry, RequestDeduplicationService service) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                service.ReleaseEntry(key, entry, acquired: true);
            }
        }
    }
}