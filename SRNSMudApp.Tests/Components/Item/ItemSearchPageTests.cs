using System;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Client.Components;
using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Pages;
using SRNSMudApp.Client.Services;

using Xunit;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class ItemSearchPageTests
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListApiClient> _apiClientMock = new();

    public ItemSearchPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices();
        _ = _ctx.Services.AddScoped(_ => _apiClientMock.Object);

        _ = _apiClientMock
            .Setup(a => a.QueryItemsAsync(It.IsAny<ItemListQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemListQueryResponse([], [], 0));
    }

    [Fact]
    public void ItemSearchView_WithoutPopoverProvider_ThrowsMissingPopoverProviderException()
    {
        // Act & Assert
        // ItemSearchView 単体では MudPopoverProvider が未配置のため、MudAutocomplete の初期化時に例外が発生することを検証
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => _ctx.Render<ItemSearchView>());
        Assert.Contains("Missing <MudPopoverProvider />", ex.Message);
    }

    [Fact]
    public void ItemSearchPage_RendersSuccessfully_BecausePopoverProviderIsIncluded()
    {
        // Act & Assert
        // ItemSearchPage には MudPopoverProvider が含まれるため、例外がスローされず正常に描画できることを検証
        IRenderedComponent<ItemSearchPage> cut = _ctx.Render<ItemSearchPage>();
        Assert.NotNull(cut);
    }

    [Fact]
    public void ItemSearchPage_RendersItemAndTagCards_WhenDataReturned()
    {
        // Arrange
        var item = new ItemDto(
            Id: 10,
            Content: "Hello Item",
            OwnerUserName: "alice",
            UpdatedDate: DateTimeOffset.UtcNow,
            IsPrivate: false,
            TagRelations: [new TagRelationDto(1, "tag1", 3, "alice")],
            ItemKindJson: "{}",
            OwnerUserId: "user-1",
            Score: 5,
            ShinjiScore: 2,
            ZenScore: 3,
            BiScore: 1,
            ReplyCount: 4,
            QuoteCount: 1
        );
        var tag = new TagDto(
            Id: 100,
            Name: "mytag",
            Content: "tag description",
            OwnerUserName: "bob",
            OwnerUserId: "user-2",
            Score: 2,
            TargetTagRelations: [new TagRelationDto(2, "subtag", 1, "bob")]
        );

        _apiClientMock
            .Setup(a => a.QueryItemsAsync(It.IsAny<ItemListQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemListQueryResponse([item], [tag], 1));

        // Act
        IRenderedComponent<ItemSearchPage> cut = _ctx.Render<ItemSearchPage>();

        // Assert
        Assert.Contains("Hello Item", cut.Markup);
        Assert.Contains("真実 2", cut.Markup);
        Assert.Contains("善 3", cut.Markup);
        Assert.Contains("美 1", cut.Markup);
        Assert.Contains("リプライ (4)", cut.Markup);
        Assert.Contains("mytag", cut.Markup);
        Assert.Contains("tag description", cut.Markup);
    }
}