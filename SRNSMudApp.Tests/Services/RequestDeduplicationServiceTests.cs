using Microsoft.Extensions.Caching.Memory;

using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     RequestDeduplicationService の単体テスト。
///     同時実行時のキー別ロック排他制御、およびキャッシュによる直近結果返却を検証する。
/// </summary>
public class RequestDeduplicationServiceTests
{
    [Fact]
    public async Task AcquireLockAsync_ProvidesMutualExclusionForSameKey()
    {
        var service = new RequestDeduplicationService();
        var executionOrder = new List<int>();

        using (await service.AcquireLockAsync("test-key"))
        {
            var task = Task.Run(async () =>
            {
                using (await service.AcquireLockAsync("test-key"))
                {
                    executionOrder.Add(2);
                }
            });

            await Task.Delay(50);
            executionOrder.Add(1);
        }

        // ロック解放後に2つ目のタスクが実行される
        await Task.Delay(50);
        Assert.Equal([1, 2], executionOrder);
    }

    [Fact]
    public async Task AcquireLockAsync_AllowsConcurrentExecutionForDifferentKeys()
    {
        var service = new RequestDeduplicationService();
        var key1Started = new TaskCompletionSource<bool>();
        var key2Ran = false;

        var task1 = Task.Run(async () =>
        {
            using (await service.AcquireLockAsync("key-1"))
            {
                key1Started.SetResult(true);
                await Task.Delay(200);
            }
        });

        await key1Started.Task;

        using (await service.AcquireLockAsync("key-2"))
        {
            key2Ran = true;
        }

        Assert.True(key2Ran);
        await task1;
    }

    [Fact]
    public void RecordResult_And_TryGetRecentResult_WorksWithMemoryCache()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new RequestDeduplicationService(cache);

        Assert.False(service.TryGetRecentResult<string>("my-key", out var initialResult));
        Assert.Null(initialResult);

        service.RecordResult("my-key", "result-value", TimeSpan.FromSeconds(10));

        Assert.True(service.TryGetRecentResult<string>("my-key", out var cachedResult));
        Assert.Equal("result-value", cachedResult);
    }

    [Fact]
    public void TryGetRecentResult_WhenCacheIsNull_ReturnsFalseGracefully()
    {
        var service = new RequestDeduplicationService(null);

        Assert.False(service.TryGetRecentResult<string>("my-key", out var result));
        Assert.Null(result);

        // RecordResult も例外を投げずに正常終了すること
        service.RecordResult("my-key", "test-val");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validation_ThrowsOnNullOrWhiteSpace(string? invalidKey)
    {
        var service = new RequestDeduplicationService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.AcquireLockAsync(invalidKey!));
        Assert.ThrowsAny<ArgumentException>(() => service.TryGetRecentResult<int>(invalidKey!, out _));
        Assert.ThrowsAny<ArgumentException>(() => service.RecordResult(invalidKey!, 123));
    }

    [Fact]
    public async Task AcquireLockAsync_CleansUpLockEntryWhenReleased_WithoutDeadlock()
    {
        var service = new RequestDeduplicationService();

        for (int i = 0; i < 50; i++)
        {
            using (await service.AcquireLockAsync("reused-key"))
            {
                // do work
            }
        }

        // 次の呼び出しもデッドロックなく正常にロック取得できること
        using var finalLock = await service.AcquireLockAsync("reused-key");
        Assert.NotNull(finalLock);
    }

    [Fact]
    public async Task AcquireLockAsync_WhenCancelled_DecrementsRefCountSafely()
    {
        var service = new RequestDeduplicationService();
        using var cts = new CancellationTokenSource();

        using (await service.AcquireLockAsync("cancel-key"))
        {
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AcquireLockAsync("cancel-key", cts.Token));
        }

        // キャンセル後でも、元のロックが解放されたら新しいリクエストがロックを取得できること
        using var newLock = await service.AcquireLockAsync("cancel-key");
        Assert.NotNull(newLock);
    }
}