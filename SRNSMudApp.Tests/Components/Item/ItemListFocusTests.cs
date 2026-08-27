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
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class ItemListFocusTests : IAsyncLifetime
{
    private const string UserId = "focus-user-id";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IHomeDataProvider> _homeDataMock = new();

    public ItemListFocusTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _homeDataMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("focus_user");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ClickingItemCard_AppliesFocusStyle_AndUpdatesUrl()
    {
        var item1 = new SRNSMudApp.Data.Item { Id = 1, Content = "First focus item", OwnerId = UserId, Owner = new ApplicationUser { Id = UserId, UserName = "focus_user" } };
        var item2 = new SRNSMudApp.Data.Item { Id = 2, Content = "Second focus item", OwnerId = UserId, Owner = new ApplicationUser { Id = UserId, UserName = "focus_user" } };
        List<SRNSMudApp.Data.Item> items = [item1, item2];

        _ = _homeDataMock.Setup(d => d.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        IRenderedComponent<ResourceList> cut =
            _ctx.Render<ResourceList>(parameters => parameters.Add(p => p.Items, items));

        cut.WaitForState(() => cut.Markup.Contains("item-card-1"));

        // Act: 1件目のカードをクリック
        cut.Find("#item-card-1").Click();

        // Assert: フォーカススタイルが適用される
        cut.WaitForAssertion(() =>
        {
            IElement card = cut.Find("#item-card-1");
            var style = card.GetAttribute("style") ?? "";
            Assert.Contains("border-width: 2px", style);
            Assert.Contains("var(--mud-palette-primary)", style);
        });

        // Assert: URLが更新される
        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.Contains("focus=1", navigationManager.Uri));
    }

    [Fact]
    public void DirectUrlWithFocusItem_RestoresFocusStyle()
    {
        var item1 = new SRNSMudApp.Data.Item { Id = 1, Content = "First focus item", OwnerId = UserId, Owner = new ApplicationUser { Id = UserId, UserName = "focus_user" } };
        var item2 = new SRNSMudApp.Data.Item { Id = 2, Content = "Second focus item", OwnerId = UserId, Owner = new ApplicationUser { Id = UserId, UserName = "focus_user" } };
        List<SRNSMudApp.Data.Item> items = [item1, item2];

        _ = _homeDataMock.Setup(d => d.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("http://localhost/?focus=2");

        IRenderedComponent<ResourceList> cut =
            _ctx.Render<ResourceList>(parameters => parameters.Add(p => p.Items, items));

        cut.WaitForState(() => cut.Markup.Contains("item-card-2"));

        // Assert: 対象カードのみフォーカススタイルが適用されている
        var focusedStyle = cut.Find("#item-card-2").GetAttribute("style") ?? "";
        Assert.Contains("border-width: 2px", focusedStyle);
        Assert.Contains("var(--mud-palette-primary)", focusedStyle);

        IElement otherCard = cut.FindAll("[id^='item-card-']").First(c =>
            c.GetAttribute("id") != "item-card-2");
        var otherStyle = otherCard.GetAttribute("style") ?? "";
        Assert.DoesNotContain("border-width: 2px", otherStyle);
    }

    [Fact]
    public void AuthorLinkClick_NavigatesToUserDetail_WithoutFocusQuery()
    {
        var item1 = new SRNSMudApp.Data.Item { Id = 1, Content = "First focus item", OwnerId = UserId, Owner = new ApplicationUser { Id = UserId, UserName = "focus_user" } };
        List<SRNSMudApp.Data.Item> items = [item1];

        _ = _homeDataMock.Setup(d => d.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        IRenderedComponent<ResourceList> cut =
            _ctx.Render<ResourceList>(parameters => parameters.Add(p => p.Items, items));

        cut.WaitForState(() => cut.Markup.Contains("item-card-1"));

        // フォーカス状態にする
        cut.Find("#item-card-1").Click();
        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.Contains("focus=1", navigationManager.Uri));

        IElement authorLink = cut.Find($"#item-card-1 a[href='/User/UserDetail/{UserId}']");
        Assert.Equal($"/User/UserDetail/{UserId}", authorLink.GetAttribute("href"));
        Assert.DoesNotContain("focusItem", authorLink.GetAttribute("href"));
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}

public sealed class ItemListFocusWithTagFilterTests : IAsyncLifetime
{
    private const string UserId = "tagfocus-user-id";
    private const string TagName = "CoexistTag";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListDataProvider> _itemListDataMock = new();

    public ItemListFocusWithTagFilterTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemListDataMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("tagfocus_user");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TagSearchAfterFocus_KeepsBothQueryParameters()
    {
        var tag = new SRNSMudApp.Data.Tag { Id = 10, Name = TagName, OwnerId = UserId };
        var item1 = new SRNSMudApp.Data.Item
        {
            Id = 1,
            Content = "Coexist item 1",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = "tagfocus_user" },
            TagRelations = [new TagRelation { TagId = 10, Tag = tag, ItemId = 1, OwnerId = UserId, Weight = 1 }]
        };

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, SRNSMudApp.Data.Tag>());

        _ = _itemListDataMock
            .Setup(d => d.LoadItemsAndTagsAsync(It.IsAny<IReadOnlyList<ItemListFilter>>(), It.IsAny<IReadOnlyList<ItemListSort>>()))
            .ReturnsAsync(new ItemListPageData([item1], []));

        _ = _itemListDataMock
            .Setup(d => d.FindTagByNameAsync(TagName))
            .ReturnsAsync(tag);

        _ = _itemListDataMock
            .Setup(d => d.SearchTagNameSuggestionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([$"{TagName} @"]);

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();

        cut.WaitForState(() => cut.Markup.Contains("item-card-1"));
        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();

        // Act 1: アイテムをクリックしてフォーカス
        cut.Find("#item-card-1").Click();
        Assert.Contains("focus=1", navigationManager.Uri);

        // Act 2: タグ検索を実行
        IRenderedComponent<MudAutocomplete<string>> autocomplete =
            cut.FindComponents<MudAutocomplete<string>>().First();
        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(TagName + " @"));
        IElement input = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']");
        await cut.InvokeAsync(() => input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" }));

        // Assert: f= (タグフィルタ) と focus= が共存する
        cut.WaitForAssertion(() =>
        {
            var uri = navigationManager.Uri;
            Assert.Contains("f=10", uri);
            Assert.Contains("focus=1", uri);
        });
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}