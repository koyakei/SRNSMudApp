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

public sealed class ItemDetailTagWeightTests : IAsyncLifetime
{
    private const string UserId = "weight-user-id";
    private const string UserName = "weight_user";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemDetailDataProvider> _itemDetailDataMock = new();
    private readonly Mock<IItemTagService> _itemTagServiceMock = new();

    public ItemDetailTagWeightTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemDetailDataMock.Object);
        _ = _ctx.Services.AddScoped(_ => _itemTagServiceMock.Object);

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

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}