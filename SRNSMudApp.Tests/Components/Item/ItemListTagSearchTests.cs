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
        _ = _ctx.Render<MudPopoverProvider>();
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

        cut.WaitForState(() => cut.FindAll("input[placeholder='タグ名で検索...']").Count > 0);
        IRenderedComponent<MudAutocomplete<TagSuggestion>> autocomplete =
            cut.FindComponents<MudAutocomplete<TagSuggestion>>()[0];

        // Act 1: 候補「SearchTestTag」の選択を再現
        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(new TagSuggestion(null, TagName, null)));

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
    }

    [Fact]
    public async Task FreeTextSearch_ViaAdornmentClick_AddsTagChip()
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

        cut.WaitForState(() => cut.FindAll("input[placeholder='タグ名で検索...']").Count > 0);
        IElement input = cut.Find("input[placeholder='タグ名で検索...']");
        input.Change(TagName);

        IElement adornment = cut.Find(".mud-input-adornment-start button, .mud-input-adornment-start");
        await cut.InvokeAsync(() => adornment.Click());

        cut.WaitForAssertion(() =>
        {
            IElement chip = cut.Find(".mud-chip");
            Assert.Contains(TagName, chip.TextContent);
        });
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}