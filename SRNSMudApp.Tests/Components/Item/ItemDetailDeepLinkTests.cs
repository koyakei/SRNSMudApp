using System.Security.Claims;

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class ItemDetailDeepLinkTests : IAsyncLifetime
{
    private const string UserId = "deeplink-user-id";
    private const string UserName = "deeplink_user";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemDetailDataProvider> _itemDetailDataMock = new();
    private readonly Mock<TaggingContractService> _contractServiceMock;

    public ItemDetailDeepLinkTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemDetailDataMock.Object);

        var dummyOptions = new Microsoft.EntityFrameworkCore.DbContextOptions<ApplicationDbContext>();
        _contractServiceMock = new Mock<TaggingContractService>(new ApplicationDbContext(dummyOptions));
        _ctx.Services.AddScoped(_ => _contractServiceMock.Object);

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
    public async Task TabAndRowInteraction_UpdatesUrlQuery()
    {
        const int itemId = 1;
        const int requestId = 100;

        var tag = new SRNSMudApp.Data.Tag { Id = 10, Name = "DeeplinkTag", OwnerId = UserId };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Deeplink item",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName }
        };

        var request = new TaggingRequestEntity
        {
            Id = requestId,
            ContractType = "Gratis",
            OwnerId = UserId,
            RequesterUserId = UserId,
            TagOwnerUserId = UserId,
            TargetItemId = itemId,
            RequestedTagId = tag.Id,
            RequestedTag = tag,
            TargetItem = item,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            RequestType = TaggingRequestType.Add,
            Status = TradeStatus.Proposed,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag], [], []));

        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId))
            .ReturnsAsync([request]);

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));
        IRenderedComponent<MudTabs> tabs = cut.FindComponent<MudTabs>();
        await cut.InvokeAsync(() => tabs.Instance.ActivatePanelAsync(1));

        cut.WaitForAssertion(() => Assert.Contains("tab=requests", navigationManager.Uri));

        cut.WaitForState(() => cut.FindAll("td").Any(td => td.TextContent.Contains(UserName)));
        await cut.InvokeAsync(() =>
            cut.FindAll("td").First(td => td.TextContent.Contains(UserName)).Click());

        cut.WaitForAssertion(() =>
            Assert.Contains($"requestId={requestId}", navigationManager.Uri));
    }

    [Fact]
    public void DeepLinkUrl_RestoresTabAndSelection()
    {
        const int itemId = 1;
        const int requestId = 100;

        var tag = new SRNSMudApp.Data.Tag { Id = 10, Name = "DeeplinkTag", OwnerId = UserId };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Deeplink item",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName }
        };

        var request = new TaggingRequestEntity
        {
            Id = requestId,
            ContractType = "Gratis",
            OwnerId = UserId,
            RequesterUserId = UserId,
            TagOwnerUserId = UserId,
            TargetItemId = itemId,
            RequestedTagId = tag.Id,
            RequestedTag = tag,
            TargetItem = item,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            RequestType = TaggingRequestType.Add,
            Status = TradeStatus.Proposed,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag], [], []));

        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId))
            .ReturnsAsync([request]);

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(
            $"http://localhost/ItemDetail/{itemId}?tab=requests&requestId={requestId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        IElement activeTab = cut.FindAll(".mud-tab.mud-tab-active")
            .First(t => t.TextContent.Contains("関連リクエスト"));
        Assert.Contains("関連リクエスト", activeTab.TextContent);

        IElement selectedRow = cut.Find("tr.mud-table-row-selected");
        Assert.Contains(UserName, selectedRow.TextContent);
    }

    [Fact]
    public void DeepLinkUrl_RestoresFilterSearch()
    {
        const int itemId = 1;
        var tag1 = new SRNSMudApp.Data.Tag { Id = 10, Name = "AlphaTag", OwnerId = UserId };
        var tag2 = new SRNSMudApp.Data.Tag { Id = 20, Name = "BetaTag", OwnerId = UserId };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "DeepLink Search Item",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            TagRelations =
            [
                new TagRelation { TagId = tag1.Id, Tag = tag1, ItemId = itemId, OwnerId = UserId },
                new TagRelation { TagId = tag2.Id, Tag = tag2, ItemId = itemId, OwnerId = UserId }
            ]
        };

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag1, tag2], [], []));

        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId))
            .ReturnsAsync([]);

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}?f=name:AlphaTag");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        IRenderedComponent<SRNSMudApp.Components.Tag.ItemTagTable> table =
            cut.FindComponent<SRNSMudApp.Components.Tag.ItemTagTable>();

        table.WaitForState(() => table.Markup.Contains("AlphaTag"));
        Assert.DoesNotContain("BetaTag", table.Markup);
    }

    [Fact]
    public async Task SearchFilterChange_UpdatesUrlQuery()
    {
        const int itemId = 1;
        var tag1 = new SRNSMudApp.Data.Tag { Id = 10, Name = "AlphaTag", OwnerId = UserId };
        var item = new SRNSMudApp.Data.Item
        {
            Id = itemId,
            Content = "Search Change Item",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = UserName },
            TagRelations =
            [
                new TagRelation { TagId = tag1.Id, Tag = tag1, ItemId = itemId, OwnerId = UserId }
            ]
        };

        _ = _itemDetailDataMock.Setup(d => d.GetItemDetailAsync(itemId))
            .ReturnsAsync(new ItemDetailPageData(item, [tag1], [], []));

        _ = _contractServiceMock.Setup(s => s.GetRequestsByItemIdAsync(itemId))
            .ReturnsAsync([]);

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        IRenderedComponent<SRNSMudApp.Components.Tag.ItemTagTable> table =
            cut.FindComponent<SRNSMudApp.Components.Tag.ItemTagTable>();

        await cut.InvokeAsync(() => table.Instance.SearchStringChanged.InvokeAsync("AlphaTag"));

        cut.WaitForAssertion(() => Assert.Contains("f=name%3AAlphaTag", navigationManager.Uri));
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}