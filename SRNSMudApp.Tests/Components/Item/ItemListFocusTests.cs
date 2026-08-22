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

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     ResourceList / ItemCard のフォーカス状態とURLクエリ（focusItem=）の双方向同期を検証する。
///     スクロールによる自動フォーカス（IntersectionObserver）は実ブラウザAPI依存のため
///     ItemListFocusE2ETests に残す。
///     （ItemListFocusE2ETests のうちクリックフォーカス・URL直接遷移・作者リンクの移行テスト）
/// </summary>
public class ItemListFocusTests : IAsyncDisposable
{
    private const string UserId = "focus-user-id";

    private readonly BunitContext _ctx;

    public ItemListFocusTests()
    {
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();

        // ResourceList / ItemCard が認証カスケードを必要とする（bUnit の認可テストダブル）
        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("focus_user");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        // ItemCard 配下のコンポーネントが利用するサービス
        _ctx.Services.AddScoped<TaggingContractService>();
        Mock<IItemTagService> itemTagMock = new();
        _ = itemTagMock.Setup(s => s.GetTaggingRequestsForItemAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ = itemTagMock.Setup(s => s.GetItemRepliesAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ctx.Services.AddScoped(_ => itemTagMock.Object);

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    /// <summary>
    ///     アイテムカードをクリックすると、該当カードのスタイルにフォーカス用の
    ///     border-width: 2px と primary カラーが適用され、URLに focusItem={id} が付与されること。
    /// </summary>
    [Fact]
    public async Task ClickingItemCard_AppliesFocusStyle_AndUpdatesUrl()
    {
        (int firstItemId, int _) = await SeedItemsAsync();

        IRenderedComponent<ResourceList> cut =
            _ctx.Render<ResourceList>(parameters => parameters.Add(p => p.Items, LoadItems()));

        cut.WaitForState(() => cut.Markup.Contains($"item-card-{firstItemId}"));

        // Act: 1件目のカードをクリック
        cut.Find($"#item-card-{firstItemId}").Click();

        // Assert: フォーカススタイルが適用される
        IElement card = cut.Find($"#item-card-{firstItemId}");
        string style = card.GetAttribute("style") ?? "";
        Assert.Contains("border-width: 2px", style);
        Assert.Contains("var(--mud-palette-primary)", style);

        // Assert: URLが更新される
        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        Assert.Contains($"focusItem={firstItemId}", navigationManager.Uri);
    }

    /// <summary>
    ///     focusItem={id} 付きURLで初期化した場合、該当カードに最初からフォーカススタイルが
    ///     適用されていること（URL→状態）。
    /// </summary>
    [Fact]
    public async Task DirectUrlWithFocusItem_RestoresFocusStyle()
    {
        (int _, int secondItemId) = await SeedItemsAsync();

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/?focusItem={secondItemId}");

        IRenderedComponent<ResourceList> cut =
            _ctx.Render<ResourceList>(parameters => parameters.Add(p => p.Items, LoadItems()));

        cut.WaitForState(() => cut.Markup.Contains($"item-card-{secondItemId}"));

        // Assert: 対象カードのみフォーカススタイルが適用されている
        string focusedStyle = cut.Find($"#item-card-{secondItemId}").GetAttribute("style") ?? "";
        Assert.Contains("border-width: 2px", focusedStyle);
        Assert.Contains("var(--mud-palette-primary)", focusedStyle);

        IElement otherCard = cut.FindAll("[id^='item-card-']").First(c =>
            c.GetAttribute("id") != $"item-card-{secondItemId}");
        string otherStyle = otherCard.GetAttribute("style") ?? "";
        Assert.DoesNotContain("border-width: 2px", otherStyle);
    }

    /// <summary>
    ///     フォーカス状態から作者リンクをクリックすると UserDetail へ遷移し、
    ///     focusItem クエリパラメータは引き継がないこと。
    /// </summary>
    [Fact]
    public async Task AuthorLinkClick_NavigatesToUserDetail_WithoutFocusQuery()
    {
        (int itemId, int _) = await SeedItemsAsync();

        IRenderedComponent<ResourceList> cut =
            _ctx.Render<ResourceList>(parameters => parameters.Add(p => p.Items, LoadItems()));

        cut.WaitForState(() => cut.Markup.Contains($"item-card-{itemId}"));

        // フォーカス状態にする
        cut.Find($"#item-card-{itemId}").Click();
        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        Assert.Contains($"focusItem={itemId}", navigationManager.Uri);

        // Act & Assert: 作者リンクの遷移先を検証
        // （bUnit ではアンカーの実際のナビゲーションが発生しないため、リンク先URLを直接検証する。
        //   リンクが相対パスの href を持つため、ブラウザ遷移時に focusItem クエリは引き継がれない）
        IElement authorLink = cut.Find($"#item-card-{itemId} a[href='/User/UserDetail/{UserId}']");
        Assert.Equal($"/User/UserDetail/{UserId}", authorLink.GetAttribute("href"));
        Assert.DoesNotContain("focusItem", authorLink.GetAttribute("href"));
    }

    private async Task<(int, int)> SeedItemsAsync()
    {
        await using ApplicationDbContext db = CreateDbContext();
        _ = db.Users.Add(new ApplicationUser { Id = UserId, UserName = "focus_user" });
        SRNSMudApp.Data.Item item1 = new() { Content = "First focus item", OwnerId = UserId };
        SRNSMudApp.Data.Item item2 = new() { Content = "Second focus item", OwnerId = UserId };
        db.Items.AddRange(item1, item2);
        _ = await db.SaveChangesAsync();
        return (item1.Id, item2.Id);
    }

    private System.Collections.Generic.List<SRNSMudApp.Data.Item> LoadItems()
    {
        IDbContextFactory<ApplicationDbContext> factory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        using ApplicationDbContext db = factory.CreateDbContext();
        return db.Items.OrderBy(i => i.Id).AsNoTracking().ToList();
    }

    private ApplicationDbContext CreateDbContext()
    {
        return _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
    }
}

/// <summary>
///     タグ検索フィルタ適用後も URL に tags= と focusItem= が共存することを検証する。
///     （ItemListFocusE2ETests.ItemFocus_WithTagFilterAndScroll の前半部分の移行テスト）
/// </summary>
public class ItemListFocusWithTagFilterTests : IAsyncDisposable
{
    private const string UserId = "tagfocus-user-id";
    private const string TagName = "CoexistTag";

    private readonly BunitContext _ctx;

    public ItemListFocusWithTagFilterTests()
    {
        _ctx = new BunitContext();

        _ = _ctx.Services.AddMudServices();

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("tagfocus_user");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);

        var embeddingMock = new Mock<ITagEmbeddingService>();
        _ = _ctx.Services.AddScoped(_ => embeddingMock.Object);

        // ItemCard が DI 解決する TaggingContractService
        _ctx.Services.AddScoped<TaggingContractService>();

        // ItemCard 配下のコンポーネントが DI 解決する IItemTagService
        Mock<IItemTagService> itemTagMock = new();
        _ = itemTagMock.Setup(s => s.GetTaggingRequestsForItemAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ = itemTagMock.Setup(s => s.GetItemRepliesAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ctx.Services.AddScoped(_ => itemTagMock.Object);

        _ = _ctx.Services.AddSingleton(new LinkPreviewService(new HttpClient()));

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    /// <summary>
    ///     アイテムをフォーカスした状態でタグ検索を実行しても、フォーカス状態はリセットされず、
    ///     URL に tags={tagId} と focusItem={itemId} の両方が共存すること。
    /// </summary>
    [Fact]
    public async Task TagSearchAfterFocus_KeepsBothQueryParameters()
    {
        // Arrange: タグ・アイテム3件・タグ関係を投入
        int tagId;
        await using (ApplicationDbContext dbContext = CreateDbContext())
        {
            _ = dbContext.Users.Add(new ApplicationUser { Id = UserId, UserName = "tagfocus_user" });
            SRNSMudApp.Data.Tag tag = new() { Name = TagName, OwnerId = UserId };
            _ = dbContext.Tags.Add(tag);
            _ = await dbContext.SaveChangesAsync();
            tagId = tag.Id;

            foreach (var i in Enumerable.Range(1, 3))
            {
                SRNSMudApp.Data.Item item = new() { Content = $"Coexist item {i}", OwnerId = UserId };
                _ = dbContext.Items.Add(item);
                _ = await dbContext.SaveChangesAsync();
                _ = dbContext.TagRelations.Add(new TagRelation
                {
                    ItemId = item.Id,
                    TagId = tagId,
                    OwnerId = UserId,
                    Weight = 1
                });
            }

            _ = await dbContext.SaveChangesAsync();
        }

        System.Collections.Generic.List<SRNSMudApp.Data.Item> items = LoadItems();
        int firstItemId = items.First().Id;

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();

        cut.WaitForState(() => cut.Markup.Contains($"item-card-{firstItemId}"));
        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();

        // Act 1: アイテムをクリックしてフォーカス
        cut.Find($"#item-card-{firstItemId}").Click();
        Assert.Contains($"focusItem={firstItemId}", navigationManager.Uri);

        // Act 2: タグ検索を実行（Phase 2-4 の ItemListTagSearchTests と同じ手順）
        IRenderedComponent<MudAutocomplete<string>> autocomplete =
            cut.FindComponents<MudAutocomplete<string>>().First();
        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(TagName + " @"));
        autocomplete.Find(".mud-input-adornment button").Click();

        // Assert: tags= と focusItem= が共存する
        cut.WaitForAssertion(() =>
        {
            string uri = navigationManager.Uri;
            Assert.Contains($"tags={tagId}", uri);
            Assert.Contains($"focusItem={firstItemId}", uri);
        });
    }

    private System.Collections.Generic.List<SRNSMudApp.Data.Item> LoadItems()
    {
        return CreateDbContext().Items.OrderBy(i => i.Id).ToList();
    }

    private ApplicationDbContext CreateDbContext()
    {
        return _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
    }
}
