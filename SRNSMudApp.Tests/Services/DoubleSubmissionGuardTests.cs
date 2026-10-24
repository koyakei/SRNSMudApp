using SRNSMudApp.Client.Services;

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     DoubleSubmissionGuard の単体テスト。
///     連続クリック、並行実行、直近重複ウィンドウ、Reset の挙動を検証する。
/// </summary>
public class DoubleSubmissionGuardTests
{
    [Fact]
    public void TryBeginSubmit_FirstCall_ReturnsTrue()
    {
        var guard = new DoubleSubmissionGuard();

        var result = guard.TryBeginSubmit("op-1");

        Assert.True(result);
        Assert.True(guard.IsSubmitting);
    }

    [Fact]
    public void TryBeginSubmit_WhileInFlight_ReturnsFalse()
    {
        var guard = new DoubleSubmissionGuard();

        Assert.True(guard.TryBeginSubmit("op-1"));
        Assert.False(guard.TryBeginSubmit("op-1"));
        Assert.False(guard.TryBeginSubmit("op-2"));
        Assert.True(guard.IsSubmitting);
    }

    [Fact]
    public void TryBeginSubmit_AfterComplete_WithinWindow_ReturnsFalseForSameKey()
    {
        var guard = new DoubleSubmissionGuard(TimeSpan.FromSeconds(5));

        Assert.True(guard.TryBeginSubmit("op-1"));
        guard.Complete("op-1");
        Assert.False(guard.IsSubmitting);

        // 同じキーでの再送信は抑止される
        Assert.False(guard.TryBeginSubmit("op-1"));
    }

    [Fact]
    public void TryBeginSubmit_AfterComplete_AllowsDifferentKey()
    {
        var guard = new DoubleSubmissionGuard(TimeSpan.FromSeconds(5));

        Assert.True(guard.TryBeginSubmit("op-1"));
        guard.Complete("op-1");
        Assert.False(guard.IsSubmitting);

        // 異なるキーであれば許可される
        Assert.True(guard.TryBeginSubmit("op-2"));
    }

    [Fact]
    public async Task TryBeginSubmit_AfterWindowExpires_AllowsSameKey()
    {
        var guard = new DoubleSubmissionGuard(TimeSpan.FromMilliseconds(50));

        Assert.True(guard.TryBeginSubmit("op-1"));
        guard.Complete("op-1");

        await Task.Delay(80);

        Assert.True(guard.TryBeginSubmit("op-1"));
    }

    [Fact]
    public void Reset_ClearsSubmittingState_WithoutRecordingRecentWindow()
    {
        var guard = new DoubleSubmissionGuard(TimeSpan.FromSeconds(5));

        Assert.True(guard.TryBeginSubmit("op-1"));
        guard.Reset();
        Assert.False(guard.IsSubmitting);

        // Reset時は直近完了として記録されないため、同一キーで即座に再試行可能
        Assert.True(guard.TryBeginSubmit("op-1"));
    }

    [Fact]
    public async Task TryBeginSubmit_ConcurrentCalls_OnlyOneSucceeds()
    {
        var guard = new DoubleSubmissionGuard();
        var successCount = 0;

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            if (guard.TryBeginSubmit("concurrent-op"))
            {
                Interlocked.Increment(ref successCount);
            }
        }));

        await Task.WhenAll(tasks);

        Assert.Equal(1, successCount);
        Assert.True(guard.IsSubmitting);
    }

    [Fact]
    public void TryBeginSubmit_MultipleDistinctKeys_TracksEachKeySeparately()
    {
        var guard = new DoubleSubmissionGuard(TimeSpan.FromSeconds(5));

        Assert.True(guard.TryBeginSubmit("op-1"));
        guard.Complete("op-1");

        Assert.True(guard.TryBeginSubmit("op-2"));
        guard.Complete("op-2");

        // 直近ウィンドウ内であれば両方のキーが独立してブロックされる
        Assert.False(guard.TryBeginSubmit("op-1"));
        Assert.False(guard.TryBeginSubmit("op-2"));
        Assert.True(guard.TryBeginSubmit("op-3"));
    }

    [Fact]
    public void Complete_WithoutParameter_UsesCurrentOperationKeyFromTryBeginSubmit()
    {
        var guard = new DoubleSubmissionGuard(TimeSpan.FromSeconds(5));

        Assert.True(guard.TryBeginSubmit("inferred-key"));
        guard.Complete(); // 引数なしで呼ぶと TryBeginSubmit のキーが引き継がれる
        Assert.False(guard.IsSubmitting);

        Assert.False(guard.TryBeginSubmit("inferred-key"));
    }

    [Fact]
    public void TryBeginSubmit_WithWhitespaceInKey_NormalizesAndPreventsDuplicate()
    {
        var guard = new DoubleSubmissionGuard(TimeSpan.FromSeconds(5));

        Assert.True(guard.TryBeginSubmit("  padded-key  "));
        guard.Complete("  padded-key  ");

        // 前後の空白の有無に関わらず同一操作として重複検知される
        Assert.False(guard.TryBeginSubmit("padded-key"));
        Assert.False(guard.TryBeginSubmit("  padded-key  "));
    }
}