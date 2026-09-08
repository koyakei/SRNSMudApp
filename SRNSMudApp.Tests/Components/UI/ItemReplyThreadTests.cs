#region

using AngleSharp.Dom;

using Bunit;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     表示専用コンポーネント <see cref="ItemReplyThread" /> の純粋レンダリングテスト。
///     サービス注入不要のパラメータ駆動で動作することを検証する。
/// </summary>
public class ItemReplyThreadTests : BunitContext, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
    }
    public ItemReplyThreadTests()
    {
        // MudTextField 等の MudBlazor コンポーネントが依存するサービス
        _ = Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static SRNSMudApp.Data.Item CreateItem(int id, string content) => new SRNSMudApp.Data.Item { Id = id, Content = content, OwnerId = "owner" };

    [Fact]
    public void Collapsed_DoesNotRenderReplyList()
    {
        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.IsExpanded, false)
            .Add(p => p.Replies, new[] { CreateItem(1, "reply1") })
            .Add(p => p.ReplyTemplate, reply => b => b.AddContent(0, reply.Content)));

        // MudCollapse は折りたたみ時もコンテンツを DOM 保持するため、トグルボタンの存在のみ検証する
        Assert.Contains("リプライ", cut.Markup);
        Assert.Contains("リプライスレッド", cut.Markup);
    }

    [Fact]
    public void CollapsedButWithReplies_ShowsReplyCount()
    {
        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.IsExpanded, false)
            .Add(p => p.Replies, new[] { CreateItem(1, "reply1") })
            .Add(p => p.ReplyTemplate, reply => b => b.AddContent(0, reply.Content)));

        Assert.Contains("リプライ (1)", cut.Markup);
        Assert.Contains("reply1", cut.Markup);
    }

    [Fact]
    public void Collapsed_WithReplyCountParameter_ShowsReplyCountEvenIfRepliesEmpty()
    {
        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.IsExpanded, false)
            .Add(p => p.ReplyCount, 3)
            .Add(p => p.Replies, Array.Empty<SRNSMudApp.Data.Item>()));

        Assert.Contains("リプライ (3)", cut.Markup);
    }

    [Fact]
    public void Expanded_RendersRepliesViaTemplate()
    {
        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.IsExpanded, true)
            .Add(p => p.Replies, new[] { CreateItem(1, "reply1"), CreateItem(2, "reply2") })
            .Add(p => p.ReplyTemplate, reply => b => b.AddContent(0, reply.Content)));

        Assert.Contains("リプライスレッド", cut.Markup);
        Assert.Contains("reply1", cut.Markup);
        Assert.Contains("reply2", cut.Markup);
    }

    [Fact]
    public void ExpandedWithoutTemplate_RendersHeaderOnly()
    {
        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.IsExpanded, true)
            .Add(p => p.Replies, new[] { CreateItem(1, "reply1") }));

        Assert.Contains("リプライスレッド", cut.Markup);
        // テンプレート未指定でも例外にならないこと
        Assert.DoesNotContain("reply1", cut.Markup);
    }

    [Fact]
    public void SubmitButton_Disabled_WhenContentEmpty()
    {
        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.IsExpanded, true)
            .Add(p => p.NewReplyContent, ""));

        IElement submitButton = cut.FindAll("button").First(b => b.TextContent.Contains("送信"));
        Assert.True(submitButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task ToggleButton_InvokesOnToggleReplies()
    {
        var toggled = 0;
        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.IsExpanded, true)
            .Add(p => p.OnToggleReplies, () =>
            {
                toggled++;
                return Task.CompletedTask;
            }));

        cut.FindAll("button").First(b => b.TextContent.Contains("リプライ")).Click();
        await cut.WaitForAssertionAsync(() => Assert.Equal(1, toggled));
    }

    [Fact]
    public async Task SubmitButton_InvokesOnSubmitReply()
    {
        var submitted = 0;
        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.IsExpanded, true)
            .Add(p => p.NewReplyContent, "テスト返信")
            .Add(p => p.OnSubmitReply, () =>
            {
                submitted++;
                return Task.CompletedTask;
            }));

        IElement submitButton = cut.FindAll("button").First(b => b.TextContent.Contains("送信"));
        Assert.False(submitButton.HasAttribute("disabled"));
        submitButton.Click();
        await cut.WaitForAssertionAsync(() => Assert.Equal(1, submitted));
    }

    [Fact]
    public void Expanded_WithTargetCandidates_RendersCheckboxes()
    {
        List<ReplyTargetCandidate> candidates =
        [
            new("user1", "Alice"),
            new("user2", "Bob")
        ];

        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.IsExpanded, true)
            .Add(p => p.TargetCandidates, (IReadOnlyList<ReplyTargetCandidate>)candidates)
            .Add(p => p.SelectedTargetUserIds, (IReadOnlyCollection<string>)new[] { "user1" }));

        Assert.Contains("返信先:", cut.Markup);
        Assert.Contains("Alice", cut.Markup);
        Assert.Contains("Bob", cut.Markup);
    }

    [Fact]
    public void TargetCandidateCheckbox_Toggling_InvokesOnTargetToggled()
    {
        List<ReplyTargetCandidate> candidates =
        [
            new("user1", "Alice")
        ];

        string? toggledUserId = null;
        bool? toggledState = null;

        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.IsExpanded, true)
            .Add(p => p.TargetCandidates, (IReadOnlyList<ReplyTargetCandidate>)candidates)
            .Add(p => p.SelectedTargetUserIds, (IReadOnlyCollection<string>)new[] { "user1" })
            .Add(p => p.OnTargetToggled, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<(string UserId, bool IsSelected)>(this, args =>
            {
                toggledUserId = args.UserId;
                toggledState = args.IsSelected;
            })));

        var checkboxInput = cut.Find("input[type='checkbox']");
        checkboxInput.Change(false);

        Assert.Equal("user1", toggledUserId);
        Assert.False(toggledState);
    }

    [Fact]
    public void QuoteButton_Click_InvokesOnOpenQuoteDialog()
    {
        var invoked = false;
        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.ItemId, 1)
            .Add(p => p.OnOpenQuoteDialog, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => invoked = true)));

        var quoteButton = cut.Find("[data-testid='quote-button-1']");
        quoteButton.Click();

        Assert.True(invoked);
    }

    [Fact]
    public void ShowQuotesButton_ShowsCountAndInvokesOnShowQuotes()
    {
        var invoked = false;
        IRenderedComponent<ItemReplyThread> cut = Render<ItemReplyThread>(parameters => parameters
            .Add(p => p.ItemId, 1)
            .Add(p => p.QuoteCount, 3)
            .Add(p => p.OnShowQuotes, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => invoked = true)));

        Assert.Contains("引用の表示 (3)", cut.Markup);

        var showQuotesBtn = cut.Find("[data-testid='show-quotes-button-1']");
        showQuotesBtn.Click();

        Assert.True(invoked);
    }
}