using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     ItemDetail の「関連リクエスト」タブ・リクエスト行選択と URL クエリ
///     （?tab=requests&amp;requestId=...）の双方向同期を検証する。
///     （ItemDetailDeepLinkE2ETests の移行テスト）
/// </summary>
[Collection(MsSqlCollection.Name)]
public class ItemDetailDeepLinkTests(MsSqlContainerFixture fixture) : IAsyncLifetime
{
    private const string UserId = "deeplink-user-id";
    private const string UserName = "deeplink_user";

    private readonly BunitContext _ctx = new();
    private MsSqlTestDatabase _testDb = null!;

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(fixture.ConnectionString, nameof(ItemDetailDeepLinkTests));

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized(UserName);
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);

        _ctx.Services.AddScoped<TaggingContractService>();

        Mock<ITaggingService> taggingMock = new();
        _ctx.Services.AddScoped(_ => taggingMock.Object);

        Mock<IItemTagService> itemTagMock = new();
        _ = itemTagMock.Setup(s => s.GetTaggingRequestsForItemAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ = itemTagMock.Setup(s => s.GetItemRepliesAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ctx.Services.AddScoped(_ => itemTagMock.Object);

        _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    /// <summary>
    ///     「関連リクエスト」タブのクリックで URL に tab=requests が反映され、
    ///     続けてリクエスト行をクリックすると requestId={id} が追加されること（状態→URL）。
    /// </summary>
    [Fact]
    public async Task TabAndRowInteraction_UpdatesUrlQuery()
    {
        (var itemId, var requestId) = await SeedDataAsync();

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        // Act 1: 関連リクエストタブをアクティブ化
        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));
        IRenderedComponent<MudTabs> tabs = cut.FindComponent<MudTabs>();
        await cut.InvokeAsync(() => tabs.Instance.ActivatePanelAsync(1));

        // Assert 1: URL に tab=requests が反映される
        cut.WaitForAssertion(() => Assert.Contains("tab=requests", navigationManager.Uri));

        // Act 2: 関連リクエスト一覧のテーブル描画を待ってから行をクリック
        cut.WaitForState(() => cut.FindAll("td").Any(td => td.TextContent.Contains(UserName)));
        await cut.InvokeAsync(() =>
            cut.FindAll("td").First(td => td.TextContent.Contains(UserName)).Click());

        // Assert 2: URL に requestId が反映される
        cut.WaitForAssertion(() =>
            Assert.Contains($"requestId={requestId}", navigationManager.Uri));
    }

    /// <summary>
    ///     ?tab=requests&amp;requestId={id} 付きURLで初期化すると、該当タブがアクティブになり
    ///     該当行が選択状態になること（URL→状態）。
    /// </summary>
    [Fact]
    public async Task DeepLinkUrl_RestoresTabAndSelection()
    {
        (var itemId, var requestId) = await SeedDataAsync();

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(
            $"http://localhost/ItemDetail/{itemId}?tab=requests&requestId={requestId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // Assert 1: 関連リクエストタブがアクティブ
        IElement activeTab = cut.FindAll(".mud-tab.mud-tab-active")
            .First(t => t.TextContent.Contains("関連リクエスト"));
        Assert.Contains("関連リクエスト", activeTab.TextContent);

        // Assert 2: 該当行が選択状態（mud-table-row-selected クラス）
        IElement selectedRow = cut.Find("tr.mud-table-row-selected");
        Assert.Contains(UserName, selectedRow.TextContent);
    }

    private async Task<(int ItemId, int RequestId)> SeedDataAsync()
    {
        await using ApplicationDbContext db =
            await _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();

        ApplicationUser user = new() { Id = UserId, UserName = UserName };
        _ = db.Users.Add(user);

        SRNSMudApp.Data.Tag tag = new() { Name = $"DeeplinkTag_{Guid.NewGuid():N}", Content = "Test", OwnerId = UserId };
        _ = db.Tags.Add(tag);

        SRNSMudApp.Data.Item item = new() { Content = $"Deeplink item {Guid.NewGuid():N}", OwnerId = UserId };
        _ = db.Items.Add(item);
        _ = await db.SaveChangesAsync();

        TaggingRequestEntity request = new()
        {
            ContractType = "Gratis",
            OwnerId = UserId,
            RequesterUserId = UserId,
            TagOwnerUserId = UserId,
            TargetItemId = item.Id,
            RequestedTagId = tag.Id,
            RequestType = TaggingRequestType.Add,
            Status = TradeStatus.Proposed,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        _ = db.TaggingRequestEntities!.Add(request);
        _ = await db.SaveChangesAsync();

        return (item.Id, request.Id);
    }
}