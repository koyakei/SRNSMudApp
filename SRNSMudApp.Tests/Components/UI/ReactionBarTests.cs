using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     表示専用コンポーネント <see cref="ReactionBar" /> の純粋レンダリング・イベントテスト。
/// </summary>
public class ReactionBarTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();

    public ReactionBarTests()
    {
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ReactionBar_RendersAllThreeChipsAndButtons()
    {
        IRenderedComponent<ReactionBar> cut = _ctx.Render<ReactionBar>(parameters => parameters
            .Add(p => p.ShinjiScore, 3)
            .Add(p => p.ZenScore, 0)
            .Add(p => p.BiScore, -1)
            .Add(p => p.IsShinjiUpvoted, true)
            .Add(p => p.IsZenDownvoted, true));

        Assert.Contains("真実 (3)", cut.Markup);
        Assert.Contains("善", cut.Markup);
        Assert.DoesNotContain("善 (0)", cut.Markup);
        Assert.Contains("美 (-1)", cut.Markup);

        // 各リアクションに Up/Down の2つのボタンがあるため合計6個のボタンが存在
        Assert.Equal(6, cut.FindAll("button").Count);
    }

    [Theory]
    [InlineData("真実", "reaction-shinji-upvote", 1)]
    [InlineData("真実", "reaction-shinji-downvote", -1)]
    [InlineData("善", "reaction-zen-upvote", 1)]
    [InlineData("善", "reaction-zen-downvote", -1)]
    [InlineData("美", "reaction-bi-upvote", 1)]
    [InlineData("美", "reaction-bi-downvote", -1)]
    public async Task ReactionBar_ClickVoteButton_TriggersCallback(string expectedTag, string testId, int expectedWeight)
    {
        (string Tag, int Weight)? result = null;
        IRenderedComponent<ReactionBar> cut = _ctx.Render<ReactionBar>(parameters => parameters
            .Add(p => p.OnReactionVoteClicked, EventCallback.Factory.Create<(string, int)>(this, r => result = r)));

        IElement button = cut.Find($"[data-testid='{testId}']");
        button.Click();

        await cut.WaitForAssertionAsync(() =>
        {
            Assert.NotNull(result);
            Assert.Equal(expectedTag, result.Value.Tag);
            Assert.Equal(expectedWeight, result.Value.Weight);
        });
    }

    [Theory]
    [InlineData("真実", "reaction-shinji")]
    [InlineData("善", "reaction-zen")]
    [InlineData("美", "reaction-bi")]
    public async Task ReactionBar_ClickReactionChip_NavigatesToItemDetailWithQuery(string expectedTag, string testId)
    {
        const int itemId = 42;
        NavigationManager navManager = _ctx.Services.GetRequiredService<NavigationManager>();

        IRenderedComponent<ReactionBar> cut = _ctx.Render<ReactionBar>(parameters => parameters
            .Add(p => p.ItemId, itemId));

        IElement chip = cut.Find($"[data-testid='{testId}']");
        chip.Click();

        await cut.WaitForAssertionAsync(() =>
        {
            Assert.Contains($"/ItemDetail/{itemId}", navManager.Uri);
            Assert.Contains($"f=name%3A{Uri.EscapeDataString(expectedTag)}", navManager.Uri);
        });
    }

    [Theory]
    [InlineData("真実", "reaction-shinji")]
    [InlineData("善", "reaction-zen")]
    [InlineData("美", "reaction-bi")]
    public async Task ReactionBar_ClickReactionChip_TriggersOnReactionTagClickedCallback(string expectedTag, string testId)
    {
        string? clickedTag = null;
        IRenderedComponent<ReactionBar> cut = _ctx.Render<ReactionBar>(parameters => parameters
            .Add(p => p.OnReactionTagClicked, EventCallback.Factory.Create<string>(this, tag => clickedTag = tag)));

        IElement chip = cut.Find($"[data-testid='{testId}']");
        chip.Click();

        await cut.WaitForAssertionAsync(() =>
        {
            Assert.Equal(expectedTag, clickedTag);
        });
    }

    [Fact]
    public async Task ReactionBar_KeyDownEnterOnChip_NavigatesToItemDetailWithQuery()
    {
        const int itemId = 100;
        NavigationManager navManager = _ctx.Services.GetRequiredService<NavigationManager>();

        IRenderedComponent<ReactionBar> cut = _ctx.Render<ReactionBar>(parameters => parameters
            .Add(p => p.ItemId, itemId));

        IElement chip = cut.Find("[data-testid='reaction-shinji']");
        chip.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        await cut.WaitForAssertionAsync(() =>
        {
            Assert.Contains($"/ItemDetail/{itemId}", navManager.Uri);
            Assert.Contains($"f=name%3A{Uri.EscapeDataString("真実")}", navManager.Uri);
        });
    }
}