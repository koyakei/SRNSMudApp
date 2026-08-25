using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Identity;
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

public sealed class ItemListAutocompleteTests : IAsyncLifetime
{
    private const string UserId = "user1-id";
    private const string TagName = "Item1_Tag";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListDataProvider> _itemListDataMock = new();

    public ItemListAutocompleteTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemListDataMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("user1");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Suggestion_IsTagPlusAtMark_AndSelectionFillsSearchBox()
    {
        var expected = TagName + " @";

        _ = _itemListDataMock
            .Setup(d => d.SearchTagNameSuggestionsAsync("Item1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([expected]);

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, SRNSMudApp.Data.Tag>());

        _ = _itemListDataMock
            .Setup(d => d.LoadItemsAndTagsAsync(It.IsAny<IReadOnlyList<ItemListFilter>>(), It.IsAny<IReadOnlyList<ItemListSort>>()))
            .ReturnsAsync(new ItemListPageData([], []));

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();

        cut.WaitForState(() => cut.FindAll("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']").Count > 0);
        IRenderedComponent<MudAutocomplete<string>> autocomplete =
            cut.FindComponents<MudAutocomplete<string>>().First();

        // Act 1: "Item1" 入力時のサジェスト候補を取得
        IEnumerable<string> suggestions =
            await autocomplete.Instance.SearchFunc!("Item1", CancellationToken.None);

        // Assert 1: 候補は「タグ名 @」形式そのもの
        var actual = Assert.Single(suggestions);
        Assert.Equal(expected, actual);

        // Act 2: 候補選択を再現
        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(expected));

        // Assert 2: 検索欄の値が「タグ名 @」になっている
        var value = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']")
            .GetAttribute("value");
        Assert.Equal(expected, value);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}