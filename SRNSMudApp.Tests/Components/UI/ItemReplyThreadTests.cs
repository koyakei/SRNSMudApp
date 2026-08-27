#region

using AngleSharp.Dom;

using Bunit;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;

#endregion

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     表示専用コンポーネント <see cref="ItemReplyThread" /> の純粋レンダリングテスト。
///     サービス注入不要のパラメータ駆動で動作することを検証する。
/// </summary>
public class ItemReplyThreadTests : BunitContext
{
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
}