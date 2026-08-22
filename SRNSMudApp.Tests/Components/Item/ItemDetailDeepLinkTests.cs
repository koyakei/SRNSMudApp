#region

using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     ItemDetail の「関連リクエスト」タブ・リクエスト行選択と URL クエリ
///     （?tab=requests&amp;requestId=...）の双方向同期を検証する。
///     （ItemDetailDeepLinkE2ETests の移行テスト）
/// </summary>
public class ItemDetailDeepLinkTests : IAsyncDisposable
{
    private const string UserId = "deeplink-user-id";
    private const string UserName = "deeplink_user";

    private readonly BunitContext _ctx;

    public ItemDetailDeepLinkTests()
    {
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();

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

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    /// <summary>
    ///     「関連リクエスト」タブのクリックで URL に tab=requests が反映され、
    ///     続けてリクエスト行をクリックすると requestId={id} が追加されること（状態→URL）。
    /// </summary>
    [Fact]
    public async Task TabAndRowInteraction_UpdatesUrlQuery()
    {
        (int itemId, int requestId) = await SeedDataAsync();

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));

        // Act 1: 関連リクエストタブをクリック
        IElement requestsTab = cut.FindAll(".mud-tab")
            .First(t => t.TextContent.Contains("関連リクエスト"));
        requestsTab.Click();

        // Assert 1: URL に tab=requests が反映される
        Assert.Contains("tab=requests", navigationManager.Uri);

        // Act 2: リクエスト行（投稿者名セル）をクリック
        IElement requestCell = cut.FindAll("td").First(td => td.TextContent.Contains(UserName));
        requestCell.Click();

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
        (int itemId, int requestId) = await SeedDataAsync();

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
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();

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
