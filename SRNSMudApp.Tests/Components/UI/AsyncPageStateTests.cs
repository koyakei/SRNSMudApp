using System.Diagnostics;

using SRNSMudApp.Components.UI;

using Xunit;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     AsyncPageState&lt;T&gt; の単体テスト。
///     ページ状態 (Loading / Empty / Loaded / Failed) の生成・パターンマッチング網羅性・
///     状態遷移フロー（ItemDetail.LoadDataAsync と同じ流れ）を bUnit なしで検証する。
/// </summary>
public class AsyncPageStateTests
{
    private sealed record PageData(int Id, string Content);

    private static PageData CreateData() => new(Id: 1, Content: "hello");

    // --- 状態生成とデータ保持 ---

    [Fact]
    public void Loading_HasNoPayload()
    {
        AsyncPageState<PageData> state = new Loading();

        _ = Assert.IsType<Loading>(state.Value);
    }

    [Fact]
    public void Empty_KeepsMessage()
    {
        AsyncPageState<PageData> state = new Empty("見つかりません");

        var empty = Assert.IsType<Empty>(state.Value);
        Assert.Equal("見つかりません", empty.Message);
    }

    [Fact]
    public void Loaded_KeepsData()
    {
        var data = CreateData();
        AsyncPageState<PageData> state = new Loaded<PageData>(data);

        var loaded = Assert.IsType<Loaded<PageData>>(state.Value);
        Assert.Same(data, loaded.Data);
    }

    [Fact]
    public void Failed_KeepsError()
    {
        var error = new InvalidOperationException("boom");
        AsyncPageState<PageData> state = new Failed(error);

        var failed = Assert.IsType<Failed>(state.Value);
        Assert.Same(error, failed.Error);
    }

    // --- パターンマッチング網羅性 (マークアップ側と同じ switch 形式) ---

    [Fact]
    public void PatternMatch_CoversAllArms()
    {
        Exception? unreachable = null;
        var states = new List<AsyncPageState<PageData>>
        {
            new Loading(),
            new Empty("empty"),
            new Loaded<PageData>(CreateData()),
            new Failed(new InvalidOperationException("failed"))
        };

        var rendered = states.ConvertAll(state => state switch
        {
            Loading => "loading",
            Empty e => $"empty:{e.Message}",
            Loaded<PageData> d => $"loaded:{d.Data.Content}",
            Failed f => $"failed:{f.Error.Message}",
            _ => throw new UnreachableException()
        });

        string[] expected = ["loading", "empty:empty", "loaded:hello", "failed:failed"];
        Assert.Equal(expected, rendered);
        // 全アームが列挙されたためフォールバックは到達不能であることの明示
        Assert.Null(unreachable);
    }

    // --- 状態遷移フロー (ItemDetail.LoadDataAsync 相当) ---

    [Fact]
    public async Task Transition_SuccessPath_GoesFromLoadingToLoaded()
    {
        AsyncPageState<PageData> pageState = new Loading();

        // 正常系: データ取得に成功した場合
        pageState = new Loading();
        PageData? data = await FetchAsync(success: true);

        pageState = data switch
        {
            null => new Empty("アイテムが見つかりません。"),
            _ => new Loaded<PageData>(data)
        };

        var loaded = Assert.IsType<Loaded<PageData>>(pageState.Value);
        Assert.Equal(1, loaded.Data.Id);
    }

    [Fact]
    public async Task Transition_NotFoundPath_GoesFromLoadingToEmpty()
    {
        PageData? data = await FetchAsync(success: false);

        AsyncPageState<PageData> pageState = data switch
        {
            null => new Empty("アイテムが見つかりません。"),
            _ => new Loaded<PageData>(data)
        };

        var empty = Assert.IsType<Empty>(pageState.Value);
        Assert.Equal("アイテムが見つかりません。", empty.Message);
    }

    [Fact]
    public async Task Transition_ExceptionPath_GoesFromLoadingToFailed()
    {
        AsyncPageState<PageData> pageState = new Loading();

        try
        {
            _ = await ThrowingFetchAsync();
            pageState = new Loaded<PageData>(CreateData());
        }
        catch (Exception ex)
        {
            pageState = new Failed(ex);
        }

        var failed = Assert.IsType<Failed>(pageState.Value);
        Assert.IsType<InvalidOperationException>(failed.Error);
    }

    private static async Task<PageData?> FetchAsync(bool success)
    {
        await Task.Yield();
        return success ? CreateData() : null;
    }

    private static async Task<PageData> ThrowingFetchAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("db error");
    }
}

