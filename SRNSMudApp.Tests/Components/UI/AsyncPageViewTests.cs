using Bunit;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     AsyncPageView の bUnit レンダリングテスト。
///     AsyncPageState&lt;T&gt; の各状態に応じて、対応するフラグメントのみが描画されることを検証する。
/// </summary>
public class AsyncPageViewTests : IDisposable
{
    private readonly BunitContext _ctx = new();

    public AsyncPageViewTests()
    {
        // 既定描画の MudAlert / MudProgressCircular のために MudServices を登録
        _ = _ctx.Services.AddMudServices();
    }

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private IRenderedComponent<AsyncPageView<string>> Render(
        AsyncPageState<string> state,
        Action<ComponentParameterCollectionBuilder<AsyncPageView<string>>>? configure = null)
    {
        return _ctx.Render<AsyncPageView<string>>(parameters =>
        {
            _ = parameters.Add(p => p.State, state);
            _ = parameters.Add(p => p.LoadedContent,
                value => builder => builder.AddContent(0, $"loaded:{value}"));
            configure?.Invoke(parameters);
        });
    }

    [Fact]
    public void Loading_ShowsDefaultSpinner()
    {
        var cut = Render(new Loading());

        Assert.Contains("mud-progress-circular", cut.Markup);
        Assert.DoesNotContain("loaded:", cut.Markup);
    }

    [Fact]
    public void Loading_CustomFragment_OverridesDefault()
    {
        var cut = Render(new Loading(), parameters =>
            _ = parameters.Add(p => p.LoadingContent,
                builder => builder.AddContent(0, "custom-loading")));

        Assert.Contains("custom-loading", cut.Markup);
        Assert.DoesNotContain("mud-progress-circular", cut.Markup);
    }

    [Fact]
    public void Empty_ShowsMessage()
    {
        var cut = Render(new Empty("見つかりません"));

        Assert.Contains("見つかりません", cut.Markup);
        Assert.DoesNotContain("loaded:", cut.Markup);
    }

    [Fact]
    public void Empty_CustomFragment_OverridesDefault()
    {
        var cut = Render(new Empty("msg"), parameters =>
            _ = parameters.Add(p => p.EmptyContent,
                message => builder => builder.AddContent(0, $"custom-empty:{message}")));

        Assert.Contains("custom-empty:msg", cut.Markup);
    }

    [Fact]
    public void Failed_ShowsAlertWithExceptionMessage()
    {
        var cut = Render(new Failed(new InvalidOperationException("boom")));

        Assert.Contains("mud-alert", cut.Markup);
        Assert.Contains("boom", cut.Markup);
    }

    [Fact]
    public void Failed_CustomFragment_OverridesDefault()
    {
        var cut = Render(new Failed(new InvalidOperationException("boom")), parameters =>
            _ = parameters.Add(p => p.FailedContent,
                error => builder => builder.AddContent(0, $"custom-failed:{error.Message}")));

        Assert.Contains("custom-failed:boom", cut.Markup);
        Assert.DoesNotContain("mud-alert", cut.Markup);
    }

    [Fact]
    public void Loaded_ReceivesDataViaContext()
    {
        var cut = Render(new Loaded<string>("payload"));

        Assert.Contains("loaded:payload", cut.Markup);
        Assert.DoesNotContain("mud-progress-circular", cut.Markup);
        Assert.DoesNotContain("mud-alert", cut.Markup);
    }
}