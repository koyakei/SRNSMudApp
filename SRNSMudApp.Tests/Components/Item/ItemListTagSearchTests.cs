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
using SRNSMudApp.Models;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class ItemListTagSearchTests : IAsyncLifetime
{
    private const string UserId = "tagtest-user-id";
    private const string TagName = "SearchTestTag";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListDataProvider> _itemListDataMock = new();

    private readonly IRenderedComponent<MudPopoverProvider> _popoverProvider;

    public ItemListTagSearchTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemListDataMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("tagtest_user");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);
        _popoverProvider = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExecutingTagSearch_ShowsTagChip_AndReflectsFilterQueryParameter()
    {
        var tag = new SRNSMudApp.Data.Tag { Id = 10, Name = TagName, OwnerId = UserId };

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, SRNSMudApp.Data.Tag>());

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, SRNSMudApp.Data.Tag> { [TagName] = tag });

        _ = _itemListDataMock
            .Setup(d => d.LoadItemsAndTagsAsync(It.IsAny<IReadOnlyList<ItemListFilter>>(), It.IsAny<IReadOnlyList<ItemListSort>>()))
            .ReturnsAsync(new ItemListPageData([], []));

        _ = _itemListDataMock
            .Setup(d => d.FindTagByNameAsync(TagName))
            .ReturnsAsync(tag);

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();

        cut.WaitForState(() => cut.FindAll("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']").Count > 0);
        IRenderedComponent<MudAutocomplete<string>> autocomplete =
            cut.FindComponents<MudAutocomplete<string>>()[0];

        // Act 1: 候補「SearchTestTag」の選択を再現
        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(TagName));
        IElement input = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']");
        await cut.InvokeAsync(() => input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" }));

        // Assert 1: 選択タグのチップが表示される
        cut.WaitForAssertion(() =>
        {
            IElement chip = cut.Find(".mud-chip");
            Assert.Contains(TagName, chip.TextContent);
        });

        // Assert 2: URL クエリに f=name: (タグ名フィルタ) が含まれる (URLエンコード対応)
        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        var unescapedUri = Uri.UnescapeDataString(navigationManager.Uri);
        Assert.Contains($"f=name:{TagName}", unescapedUri);

        // Assert 3: 検索実行後も入力フィールドに検索テキストが残る (TagAddDialog 仕様準拠)
        cut.WaitForAssertion(() =>
        {
            var value = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']")
                .GetAttribute("value");
            Assert.Equal(TagName, value);
        });
    }

    [Fact]
    public async Task TypingTagSearch_KeepsSearchTextInInputField_AfterSearchExecution()
    {
        var tag = new SRNSMudApp.Data.Tag { Id = 10, Name = TagName, OwnerId = UserId };

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, SRNSMudApp.Data.Tag>());

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, SRNSMudApp.Data.Tag> { [TagName] = tag });

        _ = _itemListDataMock
            .Setup(d => d.LoadItemsAndTagsAsync(It.IsAny<IReadOnlyList<ItemListFilter>>(), It.IsAny<IReadOnlyList<ItemListSort>>()))
            .ReturnsAsync(new ItemListPageData([], []));

        _ = _itemListDataMock
            .Setup(d => d.FindTagByNameAsync(TagName))
            .ReturnsAsync(tag);

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();

        cut.WaitForState(() => cut.FindAll("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']").Count > 0);
        IRenderedComponent<MudAutocomplete<string>> autocomplete =
            cut.FindComponents<MudAutocomplete<string>>()[0];

        // Act: サジェスト選択ではなく文字を入力して Enter を押下
        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(TagName));
        IElement input = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']");
        await cut.InvokeAsync(() => input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" }));

        // Assert: 検索後も入力フィールドにテキストが保持されていること
        cut.WaitForAssertion(() =>
        {
            var value = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']")
                .GetAttribute("value");
            Assert.Equal(TagName, value);
        });
    }

    [Fact]
    public async Task SelectingSuggestion_WithEnterKey_ShowsTagChip()
    {
        var tag = new SRNSMudApp.Data.Tag { Id = 10, Name = TagName, OwnerId = UserId };
        var tag2 = new SRNSMudApp.Data.Tag { Id = 11, Name = "SearchTestTag2", OwnerId = UserId };

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, SRNSMudApp.Data.Tag>());

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, SRNSMudApp.Data.Tag> { [TagName] = tag, ["SearchTestTag2"] = tag2 });

        _ = _itemListDataMock
            .Setup(d => d.LoadItemsAndTagsAsync(It.IsAny<IReadOnlyList<ItemListFilter>>(), It.IsAny<IReadOnlyList<ItemListSort>>()))
            .ReturnsAsync(new ItemListPageData([], []));

        _ = _itemListDataMock
            .Setup(d => d.FindTagByNameAsync("SearchTestTag2"))
            .ReturnsAsync(tag2);

        _ = _itemListDataMock
            .Setup(d => d.SearchTagNameSuggestionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([TagName + " @", "SearchTestTag2 @"]);

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();
        cut.WaitForState(() => cut.FindAll("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']").Count > 0);

        IElement input = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']");
        input.Input("Search");

        // サジェスト候補の表示を待機
        _popoverProvider.WaitForState(() => _popoverProvider.FindAll(".mud-list-item").Count == 2, TimeSpan.FromSeconds(3));

        var autocompleteComp = cut.FindComponent<MudAutocomplete<string>>();
        var autocomplete = autocompleteComp.Instance;

        // MudInput の OnKeyDown / OnKeyUp コールバックを取得
        var elemRefField = typeof(MudAutocomplete<string>).GetField("_elementReference", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var mudInput = elemRefField.GetValue(autocomplete)!;
        var onKeyDownProp = mudInput.GetType().GetProperty("OnKeyDown")!;
        var onKeyDown = (Microsoft.AspNetCore.Components.EventCallback<Microsoft.AspNetCore.Components.Web.KeyboardEventArgs>)onKeyDownProp.GetValue(mudInput)!;
        var onKeyUpProp = mudInput.GetType().GetProperty("OnKeyUp")!;
        var onKeyUp = (Microsoft.AspNetCore.Components.EventCallback<Microsoft.AspNetCore.Components.Web.KeyboardEventArgs>)onKeyUpProp.GetValue(mudInput)!;

        // 下キーで2番目の候補「SearchTestTag2」にフォーカスを移動
        await cut.InvokeAsync(async () =>
        {
            await onKeyDown.InvokeAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowDown" });
        });

        // エンターキー押下（KeyDown + KeyUp）でサジェスト選択を確定
        await cut.InvokeAsync(async () =>
        {
            await onKeyDown.InvokeAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
            await onKeyUp.InvokeAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        });

        // 選択されたタグ「SearchTestTag2」がフィルタチップとして表示されること
        var chips = cut.FindAll(".mud-chip");
        Assert.Single(chips);
        Assert.Contains("SearchTestTag2", chips[0].TextContent);

        // 入力フィールドの文字がクリアされていること
        cut.WaitForAssertion(() =>
        {
            var value = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']")
                .GetAttribute("value");
            Assert.True(string.IsNullOrEmpty(value));
        });
    }

    [Fact]
    public async Task SelectingUserSuggestion_WithEnterKey_ShowsTagChipWithUser()
    {
        const string userName = "alice";
        var tag = new SRNSMudApp.Data.Tag { Id = 10, Name = TagName, OwnerId = UserId };

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, SRNSMudApp.Data.Tag>());

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, SRNSMudApp.Data.Tag> { [TagName] = tag });

        _ = _itemListDataMock
            .Setup(d => d.LoadItemsAndTagsAsync(It.IsAny<IReadOnlyList<ItemListFilter>>(), It.IsAny<IReadOnlyList<ItemListSort>>()))
            .ReturnsAsync(new ItemListPageData([], []));

        _ = _itemListDataMock
            .Setup(d => d.FindTagByNameAsync(TagName))
            .ReturnsAsync(tag);

        _ = _itemListDataMock
            .Setup(d => d.SearchTagNameSuggestionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _ = _itemListDataMock
            .Setup(d => d.SearchTagUserNamesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([$"{TagName} @{userName}"]);

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();
        cut.WaitForState(() => cut.FindAll("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']").Count > 0);

        IElement input = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']");
        input.Input($"{TagName} @");

        _popoverProvider.WaitForState(() => _popoverProvider.FindAll(".mud-list-item").Count > 0, TimeSpan.FromSeconds(3));

        var autocompleteComp = cut.FindComponent<MudAutocomplete<string>>();
        var autocomplete = autocompleteComp.Instance;

        var elemRefField = typeof(MudAutocomplete<string>).GetField("_elementReference", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var mudInput = elemRefField.GetValue(autocomplete)!;
        var onKeyDownProp = mudInput.GetType().GetProperty("OnKeyDown")!;
        var onKeyDown = (Microsoft.AspNetCore.Components.EventCallback<Microsoft.AspNetCore.Components.Web.KeyboardEventArgs>)onKeyDownProp.GetValue(mudInput)!;
        var onKeyUpProp = mudInput.GetType().GetProperty("OnKeyUp")!;
        var onKeyUp = (Microsoft.AspNetCore.Components.EventCallback<Microsoft.AspNetCore.Components.Web.KeyboardEventArgs>)onKeyUpProp.GetValue(mudInput)!;

        // エンターキー押下（KeyDown + KeyUp）でサジェスト選択を確定
        await cut.InvokeAsync(async () =>
        {
            await onKeyDown.InvokeAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
            await onKeyUp.InvokeAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        });

        // 選択されたタグ+ユーザーがフィルタチップとして表示されること
        var chips = cut.FindAll(".mud-chip");
        Assert.Single(chips);
        Assert.Contains(TagName, chips[0].TextContent);
        Assert.Contains(userName, chips[0].TextContent);

        // 入力フィールドの文字がクリアされていること
        cut.WaitForAssertion(() =>
        {
            var value = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']")
                .GetAttribute("value");
            Assert.True(string.IsNullOrEmpty(value));
        });
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}