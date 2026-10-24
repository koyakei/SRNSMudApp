using System.Security.Claims;

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class ItemDetailTagWeightTests : IAsyncLifetime
{
    private const string UserId = "weight-user-id";
    private const string UserName = "weight_user";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemDetailDataProvider> _itemDetailDataMock = new();
    private readonly Mock<IItemTagService> _itemTagServiceMock = new();
    private readonly Mock<ITagWeightLedgerService> _tagWeightLedgerServiceMock = new();
    private readonly Mock<IItemReactionService> _itemReactionServiceMock = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();

    public ItemDetailTagWeightTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemDetailDataMock.Object);
        _ = _ctx.Services.AddScoped(_ => _itemTagServiceMock.Object);
        _ctx.Services.RemoveAll<ITagWeightLedgerService>();
        _ = _ctx.Services.AddScoped(_ => _tagWeightLedgerServiceMock.Object);
        _ctx.Services.RemoveAll<IItemReactionService>();
        _ = _ctx.Services.AddScoped(_ => _itemReactionServiceMock.Object);
        _ctx.Services.RemoveAll<IDialogLauncher>();
        _ = _ctx.Services.AddScoped(_ => _dialogLauncherMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized(UserName);
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ClickingDecreaseWeightButton_CallsServiceAndUpdatesWeight()
    {
        const int itemId = 1;
        const string tagName = "WeightTag";
        var tag = new SRNSMudApp.Data.Tag { Id = 10, Name = tagName, OwnerId = UserId };
        var relation = new TagRelation
        {
            Id = 50,
            ItemId = itemId,
            TagId = tag.Id,
            Tag = tag,
            OwnerId = UserId,
            Weight = 0
        };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Weight item",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            TagRelations = [relation]
        };

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag], [], []));

        _ = _itemTagServiceMock
            .Setup(s => s.UpdateTagWeightAsync(relation.Id, -1, UserId))
            .ReturnsAsync(UpdateWeightResult.Success);

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));
        cut.WaitForState(() => cut.Markup.Contains(tagName));

        // Act: 該当タグの行にある「Weightを減らす」ボタンをクリック
        IElement decreaseButton = cut.FindAll("button[title='Weightを減らす']")[0];
        decreaseButton.Click();

        // サービスの呼び出しを検証
        _itemTagServiceMock.Verify(s => s.UpdateTagWeightAsync(relation.Id, -1, UserId), Times.Once);
    }

    [Fact]
    public void TagHistoryTab_RendersTagRelationComment()
    {
        const int itemId = 1;
        const string tagName = "HistoryTag";
        const string comment = "タグ履歴に表示されるコメントです";

        var tag = new SRNSMudApp.Data.Tag { Id = 10, Name = tagName, OwnerId = UserId };
        var relation = new TagRelation
        {
            Id = 50,
            ItemId = itemId,
            TagId = tag.Id,
            Tag = tag,
            OwnerId = UserId,
            Weight = 1,
            Comment = comment
        };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Item with history",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            TagRelations = [relation]
        };

        var ledger = new TagWeightLedger
        {
            Id = 1,
            TagId = tag.Id,
            Tag = tag,
            ItemId = itemId,
            SourceType = "TagRelationInsert",
            SourceId = relation.Id,
            TagRelation = relation,
            Delta = 1,
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            CreatedDate = DateTime.UtcNow
        };

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag], [], [ledger]));

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // Assert
        Assert.Contains(comment, cut.Markup);
    }

    [Fact]
    public void TagHistoryTab_WhenLedgerHasNoCommentItem_RendersCommentAndTagButtons()
    {
        const int itemId = 2;
        const string tagName = "NoCommentTag";

        var tag = new SRNSMudApp.Data.Tag { Id = 20, Name = tagName, OwnerId = UserId };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Item without comment",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            TagRelations = []
        };

        var ledger = new TagWeightLedger
        {
            Id = 99,
            TagId = tag.Id,
            Tag = tag,
            ItemId = itemId,
            TagNameSnapshot = tagName,
            SourceType = "Manual",
            Delta = 1,
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            CreatedDate = DateTime.UtcNow
        };

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag], [], [ledger]));

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // Assert: コメントボタンとタグ付けボタンが存在すること
        Assert.Contains("data-testid=\"add-comment-btn-99\"", cut.Markup);
        Assert.Contains("data-testid=\"add-tag-btn-99\"", cut.Markup);

        // Assert: コメント前のReactionTagBarが表示されていること
        Assert.Contains("data-testid=\"ledger-reaction-bar-99\"", cut.Markup);
        Assert.Contains("reaction-chip-shinji", cut.Markup);
        Assert.Contains("reaction-chip-zen", cut.Markup);
        Assert.Contains("reaction-chip-bi", cut.Markup);
    }

    [Fact]
    public void TagHistoryTab_WhenLedgerHasDirectCommentItem_RendersItemCard()
    {
        const int itemId = 3;
        const string tagName = "DirectCommentTag";
        const string directComment = "台帳レコードに直接紐づくコメント";

        var tag = new SRNSMudApp.Data.Tag { Id = 30, Name = tagName, OwnerId = UserId };
        var commentItem = new SRNSMudApp.Data.Item
        {
            Id = 300,
            Content = directComment,
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName }
        };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Item with direct ledger comment",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            TagRelations = []
        };

        var ledger = new TagWeightLedger
        {
            Id = 88,
            TagId = tag.Id,
            Tag = tag,
            ItemId = itemId,
            TagNameSnapshot = tagName,
            SourceType = "Manual",
            Delta = 1,
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            CommentItemId = commentItem.Id,
            CommentItem = commentItem,
            CreatedDate = DateTime.UtcNow
        };

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag], [], [ledger]));

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // Assert: 直接紐づくコメントが表示されていること
        Assert.Contains(directComment, cut.Markup);

        // Assert: 既にコメントアイテムが存在する場合でも、返信・コメントボタンが表示されていること
        Assert.Contains("data-testid=\"add-comment-btn-88\"", cut.Markup);
        Assert.Contains("返信・コメント", cut.Markup);
    }

    [Fact]
    public async Task TagHistoryTab_WhenReactionVotedOnLedger_CallsServicesAndRefreshes()
    {
        const int itemId = 4;
        const string tagName = "ReactionTestTag";

        var tag = new SRNSMudApp.Data.Tag { Id = 40, Name = tagName, OwnerId = UserId };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Item for reaction test",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            TagRelations = []
        };
        var ledger = new TagWeightLedger
        {
            Id = 77,
            TagId = tag.Id,
            Tag = tag,
            ItemId = itemId,
            TagNameSnapshot = tagName,
            SourceType = "Manual",
            Delta = 1,
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            CreatedDate = DateTime.UtcNow
        };

        var commentItem = new SRNSMudApp.Data.Item { Id = 400, Content = "Generated item", OwnerId = UserId };
        var reactionTag = new SRNSMudApp.Data.Tag { Id = 500, Name = ReactionTagNames.Shinji, OwnerId = UserId };
        var voteResult = new ItemVoteResult(ItemVoteAction.Added, 600, 1);

        _tagWeightLedgerServiceMock.Setup(s => s.EnsureCommentItemAsync(ledger, UserId))
            .ReturnsAsync(commentItem);
        _itemReactionServiceMock.Setup(s => s.EnsureReactionTagAsync(UserId, ReactionTagNames.Shinji))
            .ReturnsAsync(reactionTag);
        _itemReactionServiceMock.Setup(s => s.ToggleItemReactionAsync(commentItem.Id, UserId, reactionTag.Id, 1, null))
            .ReturnsAsync(voteResult);

        var dialogRefMock = new Mock<IDialogReference>();
        dialogRefMock.Setup(d => d.Result).ReturnsAsync(DialogResult.Ok(new ReactionCommentDialogResult(true, "テストコメント")));
        _dialogLauncherMock.Setup(l => l.ShowAsync(
            typeof(ReactionCommentDialog),
            "リアクションを追加",
            It.IsAny<DialogParameters>(),
            It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRefMock.Object);

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag], [], [ledger]));

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // Act: 台帳レコードの真実 Upvote ボタンをクリック
        var ledgerBar = cut.Find($"[data-testid=\"ledger-reaction-bar-{ledger.Id}\"]");
        var upvoteButton = ledgerBar.QuerySelector("#reaction-up-shinji");
        Assert.NotNull(upvoteButton);
        await cut.InvokeAsync(() => upvoteButton.Click());

        // Assert: EnsureCommentItemAsync, EnsureReactionTagAsync, ToggleItemReactionAsync, UpdateTagRelationCommentAsync が呼ばれること
        _tagWeightLedgerServiceMock.Verify(s => s.EnsureCommentItemAsync(ledger, UserId), Times.Once);
        _itemReactionServiceMock.Verify(s => s.EnsureReactionTagAsync(UserId, ReactionTagNames.Shinji), Times.Once);
        _itemReactionServiceMock.Verify(s => s.ToggleItemReactionAsync(commentItem.Id, UserId, reactionTag.Id, 1, null), Times.Once);
        _itemReactionServiceMock.Verify(s => s.UpdateTagRelationCommentAsync(600, UserId, "テストコメント"), Times.Once);
    }

    [Fact]
    public async Task TagHistoryTab_WhenCommentButtonClickedOnLedger_OpensDialogAndCallsService()
    {
        const int itemId = 5;
        const string tagName = "CommentDialogTag";

        var tag = new SRNSMudApp.Data.Tag { Id = 50, Name = tagName, OwnerId = UserId };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Item for comment dialog test",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            TagRelations = []
        };
        var ledger = new TagWeightLedger
        {
            Id = 99,
            TagId = tag.Id,
            Tag = tag,
            ItemId = itemId,
            TagNameSnapshot = tagName,
            SourceType = "Manual",
            Delta = 1,
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            CreatedDate = DateTime.UtcNow
        };

        var dialogRefMock = new Mock<IDialogReference>();
        dialogRefMock.Setup(d => d.Result).ReturnsAsync(DialogResult.Ok("誰でもコメント可能"));
        _dialogLauncherMock.Setup(l => l.ShowAsync(
            typeof(TagLedgerCommentDialog),
            "タグ履歴にコメントを追加",
            null,
            It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRefMock.Object);

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag], [], [ledger]));

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // Act: コメントボタンをクリック
        var commentButton = cut.Find("[data-testid=\"add-comment-btn-99\"]");
        await cut.InvokeAsync(() => commentButton.Click());

        // Assert: AddCommentAsync が ledger オブジェクトを渡して呼ばれること
        _tagWeightLedgerServiceMock.Verify(s => s.AddCommentAsync(ledger, "誰でもコメント可能", UserId), Times.Once);
    }

    [Fact]
    public async Task TagHistoryTab_WhenLedgerIdIsZero_CallsAddCommentAsyncWithUnpersistedLedger()
    {
        const int itemId = 6;
        const string tagName = "ZeroIdLedgerTag";

        var tag = new SRNSMudApp.Data.Tag { Id = 60, Name = tagName, OwnerId = UserId };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Item for zero id ledger test",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            TagRelations = []
        };
        // Id == 0 の未永続化台帳レコード（ItemDetailDataProvider で補完されたもの）
        var zeroIdLedger = new TagWeightLedger
        {
            Id = 0,
            TagId = tag.Id,
            TagNameSnapshot = tagName,
            ItemId = itemId,
            SourceType = "TagRelationInsert",
            SourceId = 123,
            Delta = 1,
            PreviousWeight = 0,
            NewWeight = 1,
            IsOwnerAction = true,
            Reason = "タグの新規追加",
            OwnerId = "other-owner-id",
            CreatedDate = DateTime.UtcNow
        };

        var dialogRefMock = new Mock<IDialogReference>();
        dialogRefMock.Setup(d => d.Result).ReturnsAsync(DialogResult.Ok("Owner 以外からのコメント"));
        _dialogLauncherMock.Setup(l => l.ShowAsync(
            typeof(TagLedgerCommentDialog),
            "タグ履歴にコメントを追加",
            null,
            It.IsAny<DialogOptions>()))
            .ReturnsAsync(dialogRefMock.Object);

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag], [], [zeroIdLedger]));

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // Act: Id == 0 の台帳レコードのコメントボタンをクリック
        var commentButton = cut.Find("[data-testid=\"add-comment-btn-0\"]");
        await cut.InvokeAsync(() => commentButton.Click());

        // Assert: AddCommentAsync に zeroIdLedger が渡されて呼ばれること（Id == 0 でもエラーにならない）
        _tagWeightLedgerServiceMock.Verify(s => s.AddCommentAsync(zeroIdLedger, "Owner 以外からのコメント", UserId), Times.Once);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}