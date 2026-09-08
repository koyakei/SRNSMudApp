using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class ItemDetailThreadTests : IAsyncLifetime
{
    private const string UserId = "thread-user-id";
    private const string UserName = "thread_user";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemDetailDataProvider> _itemDetailDataMock = new();
    private readonly Mock<IItemTagService> _itemTagServiceMock = new();
    private readonly Mock<IItemReplyService> _itemReplyServiceMock = new();
    private readonly Mock<ITaggingContractService> _contractServiceMock = new();

    public ItemDetailThreadTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemDetailDataMock.Object);
        _ = _ctx.Services.AddScoped(_ => _itemTagServiceMock.Object);
        _ = _ctx.Services.AddScoped(_ => _itemReplyServiceMock.Object);
        _ = _ctx.Services.AddScoped(_ => _contractServiceMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized(UserName);
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ctx.Services.AddScoped(_ => userManagerMock.Object);

        _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ItemDetail_RendersAncestors_FocusedItem_Replies_And_Siblings()
    {
        const int itemId = 2;
        var author = new ApplicationUser { Id = UserId, UserName = UserName };

        var ancestor = new SRNSMudApp.Data.Item
        {
            Id = 1,
            Content = "Ancestor Root Item",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc)
        };

        var earlierSibling = new SRNSMudApp.Data.Item
        {
            Id = 4,
            ParentItemId = 1,
            Content = "Earlier Sibling Item",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 10, 5, 0, DateTimeKind.Utc)
        };

        var currentItem = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            ParentItemId = 1,
            Content = "Focused Current Item",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 10, 10, 0, DateTimeKind.Utc)
        };

        var laterSibling = new SRNSMudApp.Data.Item
        {
            Id = 5,
            ParentItemId = 1,
            Content = "Later Sibling Item",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 10, 15, 0, DateTimeKind.Utc)
        };

        var reply = new SRNSMudApp.Data.Item
        {
            Id = 3,
            ParentItemId = itemId,
            Content = "Child Reply Item",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 10, 20, 0, DateTimeKind.Utc)
        };

        var pageData = new ItemDetailPageData(
            currentItem,
            [],
            [],
            [],
            Ancestors: [ancestor],
            Replies: [reply],
            Siblings: [laterSibling, earlierSibling]); // あえて順不同で設定

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId, default))
            .ReturnsAsync(pageData);

        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId))
            .ReturnsAsync([]);

        IRenderedComponent<SRNSMudApp.Components.Item.ItemDetail> cut =
            _ctx.Render<SRNSMudApp.Components.Item.ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // 親方向 (Ancestors)
        Assert.Contains("スレッドの親アイテム", cut.Markup);
        Assert.Contains("Ancestor Root Item", cut.Markup);

        // 兄弟リプライ＋選択中アイテムが投稿日時順に並んでいることを検証
        var earlierIndex = cut.Markup.IndexOf("Earlier Sibling Item", StringComparison.Ordinal);
        var focusedIndex = cut.Markup.IndexOf("Focused Current Item", StringComparison.Ordinal);
        var laterIndex = cut.Markup.IndexOf("Later Sibling Item", StringComparison.Ordinal);

        Assert.True(earlierIndex >= 0, "Earlier Sibling Item がレンダリングされていること");
        Assert.True(focusedIndex >= 0, "Focused Current Item がレンダリングされていること");
        Assert.True(laterIndex >= 0, "Later Sibling Item がレンダリングされていること");
        Assert.True(earlierIndex < focusedIndex, "Earlier Sibling が Focused Item より前に表示されること");
        Assert.True(focusedIndex < laterIndex, "Focused Item が Later Sibling より前に表示されること");

        // 子方向 (Replies) は Focused Item の直下にネストされていること
        Assert.Contains("リプライ (1)", cut.Markup);
        Assert.Contains("Child Reply Item", cut.Markup);
        var childReplyIndex = cut.Markup.IndexOf("Child Reply Item", StringComparison.Ordinal);
        Assert.True(focusedIndex < childReplyIndex && childReplyIndex < laterIndex,
            "Child Reply Item は Focused Item の直下に配置され、Later Sibling より前に現れること");
    }

    [Fact]
    public async Task ItemDetail_SubmittingReply_CallsItemTagService_AndRefreshesData()
    {
        const int itemId = 2;
        var author = new ApplicationUser { Id = UserId, UserName = UserName };

        var currentItem = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Focused Current Item",
            OwnerId = UserId,
            Owner = author
        };

        var pageData = new ItemDetailPageData(
            currentItem,
            [],
            [],
            [],
            Ancestors: [],
            Replies: [],
            Siblings: []);

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId, default))
            .ReturnsAsync(pageData);

        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId))
            .ReturnsAsync([]);

        var newReply = new SRNSMudApp.Data.Item
        {
            Id = 5,
            ParentItemId = itemId,
            Content = "New reply from test",
            OwnerId = UserId
        };

        _ = _itemReplyServiceMock.Setup(s => s.AddItemReplyAsync(itemId, "New reply from test", UserId, null))
            .ReturnsAsync(newReply);

        IRenderedComponent<SRNSMudApp.Components.Item.ItemDetail> cut =
            _ctx.Render<SRNSMudApp.Components.Item.ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // Act: リプライを入力して送信
        var inputElem = cut.Find("textarea[data-testid='item-detail-reply-input']");
        inputElem.Input("New reply from test");

        var submitBtn = cut.Find("button[data-testid='item-detail-reply-submit']");
        submitBtn.Click();

        // Assert: サービスが呼び出され、データがリロードされる
        _itemReplyServiceMock.Verify(s => s.AddItemReplyAsync(itemId, "New reply from test", UserId, null), Times.Once);
        _itemDetailDataMock.Verify(d => d.GetItemDetailAsync(itemId, default), Times.AtLeast(2));
    }

    [Fact]
    public void ItemDetail_OnLoaded_InvokesScrollToElement_ForFocusedItem()
    {
        const int itemId = 42;
        var author = new ApplicationUser { Id = UserId, UserName = UserName };
        var currentItem = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Target Item",
            OwnerId = UserId,
            Owner = author
        };

        var pageData = new ItemDetailPageData(
            currentItem,
            [],
            [],
            [],
            Ancestors: [],
            Replies: [],
            Siblings: []);

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId, default))
            .ReturnsAsync(pageData);
        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId))
            .ReturnsAsync([]);

        IRenderedComponent<SRNSMudApp.Components.Item.ItemDetail> cut =
            _ctx.Render<SRNSMudApp.Components.Item.ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // JS scrollToElement が呼び出されたか検証
        var invocations = _ctx.JSInterop.Invocations["contentOverflowHelper.scrollToElement"];
        Assert.NotEmpty(invocations);
        var arg = invocations[0].Arguments[0]?.ToString();
        Assert.Contains($"#item-card-{itemId}", arg);
        Assert.Contains($"#current-focused-item-{itemId}", arg);
    }

    [Fact]
    public void ItemDetail_CollapsesAncestorsWhenGreaterThanFour_AndExpandsOnClick()
    {
        const int itemId = 100;
        var author = new ApplicationUser { Id = UserId, UserName = UserName };
        var currentItem = new SRNSMudApp.Data.Item { Id = itemId, Content = "Current", OwnerId = UserId, Owner = author };

        // 5件の親アイテム (Ancestors: 0 最古 〜 4 直前親)
        List<SRNSMudApp.Data.Item> ancestors = Enumerable.Range(1, 5).Select(i => new SRNSMudApp.Data.Item
        {
            Id = i,
            Content = $"Ancestor-{i}",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 10, i, 0, DateTimeKind.Utc)
        }).ToList();

        var pageData = new ItemDetailPageData(currentItem, [], [], [], Ancestors: ancestors, Replies: [], Siblings: []);
        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId, default)).ReturnsAsync(pageData);
        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId)).ReturnsAsync([]);

        IRenderedComponent<SRNSMudApp.Components.Item.ItemDetail> cut =
            _ctx.Render<SRNSMudApp.Components.Item.ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // 折りたたみ状態: 最古 Ancestor-1 と 直前親 Ancestor-5 は表示、中間 Ancestor-2, 3, 4 は非表示
        Assert.Contains("Ancestor-1", cut.Markup);
        Assert.Contains("Ancestor-5", cut.Markup);
        Assert.DoesNotContain("Ancestor-2", cut.Markup);
        Assert.DoesNotContain("Ancestor-3", cut.Markup);
        Assert.DoesNotContain("Ancestor-4", cut.Markup);

        // もっと見るボタンが表示されていること (5 - 2 = 3件)
        var expandBtn = cut.Find("button[data-testid='expand-ancestors-btn']");
        Assert.Contains("前の返信をもっと見る (3 件)", expandBtn.TextContent);

        // ボタンをクリックして展開
        expandBtn.Click();

        // 展開状態: すべて表示され、ボタンは消える
        Assert.Contains("Ancestor-2", cut.Markup);
        Assert.Contains("Ancestor-3", cut.Markup);
        Assert.Contains("Ancestor-4", cut.Markup);
        Assert.Empty(cut.FindAll("button[data-testid='expand-ancestors-btn']"));
    }

    [Fact]
    public void ItemDetail_CollapsesEarlierSiblingsWhenGreaterThanFour_AndExpandsOnClick()
    {
        const int itemId = 100;
        var author = new ApplicationUser { Id = UserId, UserName = UserName };
        var currentItem = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Current Item",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        // 5件の過去兄弟 (11:00, 11:10, 11:20, 11:30, 11:40)
        List<SRNSMudApp.Data.Item> earlierSiblings = Enumerable.Range(1, 5).Select(i => new SRNSMudApp.Data.Item
        {
            Id = i,
            Content = $"Earlier-Sibling-{i}",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 11, i * 10, 0, DateTimeKind.Utc)
        }).ToList();

        var pageData = new ItemDetailPageData(currentItem, [], [], [], Ancestors: [], Replies: [], Siblings: earlierSiblings);
        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId, default)).ReturnsAsync(pageData);
        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId)).ReturnsAsync([]);

        IRenderedComponent<SRNSMudApp.Components.Item.ItemDetail> cut =
            _ctx.Render<SRNSMudApp.Components.Item.ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // 折りたたみ状態: 直近4件 (Earlier-Sibling-2..5) は表示、最も古い Earlier-Sibling-1 は非表示
        Assert.DoesNotContain("Earlier-Sibling-1", cut.Markup);
        Assert.Contains("Earlier-Sibling-2", cut.Markup);
        Assert.Contains("Earlier-Sibling-3", cut.Markup);
        Assert.Contains("Earlier-Sibling-4", cut.Markup);
        Assert.Contains("Earlier-Sibling-5", cut.Markup);

        // もっと見るボタンが表示されていること (5 - 4 = 1件)
        var expandBtn = cut.Find("button[data-testid='expand-earlier-siblings-btn']");
        Assert.Contains("過去の兄弟リプライをもっと見る (1 件)", expandBtn.TextContent);

        // ボタンをクリックして展開
        expandBtn.Click();

        // 展開状態: Earlier-Sibling-1 も表示され、ボタンは消える
        Assert.Contains("Earlier-Sibling-1", cut.Markup);
        Assert.Empty(cut.FindAll("button[data-testid='expand-earlier-siblings-btn']"));
    }

    [Fact]
    public void ItemDetail_CollapsesLaterSiblingsWhenGreaterThanTwo_AndExpandsOnClick()
    {
        const int itemId = 100;
        var author = new ApplicationUser { Id = UserId, UserName = UserName };
        var currentItem = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Current Item",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        // 3件の未来兄弟 (13:10, 13:20, 13:30)
        List<SRNSMudApp.Data.Item> laterSiblings = Enumerable.Range(1, 3).Select(i => new SRNSMudApp.Data.Item
        {
            Id = 200 + i,
            Content = $"Later-Sibling-{i}",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 13, i * 10, 0, DateTimeKind.Utc)
        }).ToList();

        var pageData = new ItemDetailPageData(currentItem, [], [], [], Ancestors: [], Replies: [], Siblings: laterSiblings);
        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId, default)).ReturnsAsync(pageData);
        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId)).ReturnsAsync([]);

        IRenderedComponent<SRNSMudApp.Components.Item.ItemDetail> cut =
            _ctx.Render<SRNSMudApp.Components.Item.ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // 折りたたみ状態: 直近2件 (Later-Sibling-1, 2) は表示、最も新しい Later-Sibling-3 は非表示
        Assert.Contains("Later-Sibling-1", cut.Markup);
        Assert.Contains("Later-Sibling-2", cut.Markup);
        Assert.DoesNotContain("Later-Sibling-3", cut.Markup);

        // もっと見るボタンが表示されていること (3 - 2 = 1件)
        var expandBtn = cut.Find("button[data-testid='expand-later-siblings-btn']");
        Assert.Contains("新しい兄弟リプライをもっと見る (1 件)", expandBtn.TextContent);

        // ボタンをクリックして展開
        expandBtn.Click();

        // 展開状態: Later-Sibling-3 も表示され、ボタンは消える
        Assert.Contains("Later-Sibling-3", cut.Markup);
        Assert.Empty(cut.FindAll("button[data-testid='expand-later-siblings-btn']"));
    }

    [Fact]
    public void ItemDetail_CollapsesRepliesWhenGreaterThanThree_AndExpandsOnClick()
    {
        const int itemId = 100;
        var author = new ApplicationUser { Id = UserId, UserName = UserName };
        var currentItem = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Current Item",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        // 4件のリプライ (1, 2, 3, 4)
        List<SRNSMudApp.Data.Item> replies = Enumerable.Range(1, 4).Select(i => new SRNSMudApp.Data.Item
        {
            Id = 300 + i,
            ParentItemId = itemId,
            Content = $"Reply-Item-{i}",
            OwnerId = UserId,
            Owner = author,
            CreatedDate = new DateTime(2026, 9, 1, 14, i * 10, 0, DateTimeKind.Utc)
        }).ToList();

        var pageData = new ItemDetailPageData(currentItem, [], [], [], Ancestors: [], Replies: replies, Siblings: []);
        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId, default)).ReturnsAsync(pageData);
        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId)).ReturnsAsync([]);

        IRenderedComponent<SRNSMudApp.Components.Item.ItemDetail> cut =
            _ctx.Render<SRNSMudApp.Components.Item.ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // 折りたたみ状態: 最初の3件 (Reply-Item-1..3) は表示、4件目の Reply-Item-4 は非表示
        Assert.Contains("Reply-Item-1", cut.Markup);
        Assert.Contains("Reply-Item-2", cut.Markup);
        Assert.Contains("Reply-Item-3", cut.Markup);
        Assert.DoesNotContain("Reply-Item-4", cut.Markup);

        // もっと見るボタンが表示されていること (4 - 3 = 1件)
        var expandBtn = cut.Find("button[data-testid='expand-replies-btn']");
        Assert.Contains("返信をもっと見る (他 1 件)", expandBtn.TextContent);

        // ボタンをクリックして展開
        expandBtn.Click();

        // 展開状態: Reply-Item-4 も表示され、ボタンは消える
        Assert.Contains("Reply-Item-4", cut.Markup);
        Assert.Empty(cut.FindAll("button[data-testid='expand-replies-btn']"));
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}