using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class ItemListExportTests : IAsyncLifetime
{
    private const string UserId = "export-user-id";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemListDataProvider> _itemListDataMock = new();
    private readonly Mock<IItemListExportService> _exportServiceMock = new();

    public ItemListExportTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemListDataMock.Object);
        _ = _ctx.Services.AddScoped(_ => _exportServiceMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("export_user");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ExportToJson_CallsExportService_AndTriggersDownload()
    {
        var testItem = new SRNSMudApp.Data.Item
        {
            Id = 1,
            Content = "This is a test item with a URL: https://example.com",
            OwnerId = UserId,
            Owner = new ApplicationUser { Id = UserId, UserName = "export_user" }
        };

        _ = _itemListDataMock
            .Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, SRNSMudApp.Data.Tag>());

        _ = _itemListDataMock
            .Setup(d => d.LoadItemsAndTagsAsync(It.IsAny<IReadOnlyList<ItemListFilter>>(), It.IsAny<IReadOnlyList<ItemListSort>>()))
            .ReturnsAsync(new ItemListPageData([testItem], []));

        _ = _itemListDataMock
            .Setup(d => d.LoadExportDataAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(new ItemListExportData(new Dictionary<int, SRNSMudApp.Data.Tag>(), [], []));

        _ = _exportServiceMock
            .Setup(s => s.BuildExportAsync(It.IsAny<ItemListExportData>(), It.IsAny<IEnumerable<SRNSMudApp.Data.Item>>()))
            .ReturnsAsync([new ExportItemDto
            {
                Content = "test",
                LinkPreviews = [new ExportLinkPreviewDto { Url = "https://example.com", Title = "Example Title" }]
            }]);

        var downloadJs = _ctx.JSInterop.SetupVoid("window.downloadFileFromText", _ => true);
        downloadJs.SetVoidResult();

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();

        cut.WaitForState(() => cut.Markup.Contains("item-card-"));

        IRenderedComponent<MudButton> exportButton = cut.FindComponents<MudButton>()
            .First(b => b.Markup.Contains("JSONエクスポート"));
        exportButton.Find("button").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(downloadJs.Invocations));

        var json = Assert.IsType<string>(downloadJs.Invocations.Single().Arguments[1]);
        Assert.Contains("Example Title", json);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}