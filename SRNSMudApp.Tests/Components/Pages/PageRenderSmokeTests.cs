using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Pages;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Pages;

public sealed class PageRenderSmokeTests : IAsyncLifetime
{
    private const string UserId = "smoke-user-id";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IHomeDataProvider> _homeDataMock = new();
    private readonly Mock<IItemListDataProvider> _itemListDataMock = new();
    private readonly Mock<ITagTreeDataProvider> _treeDataMock = new();

    public PageRenderSmokeTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _homeDataMock.Object);

        _itemListDataMock
            .Setup(d => d.LoadItemsAndTagsAsync(It.IsAny<List<ItemListFilter>>(), It.IsAny<List<ItemListSort>>(), It.IsAny<string>()))
            .ReturnsAsync(new ItemListPageData([], []));
        _itemListDataMock
            .Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, SRNSMudApp.Data.Tag>());
        _itemListDataMock
            .Setup(d => d.GetTagsByNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, SRNSMudApp.Data.Tag>());
        _ = _ctx.Services.AddScoped(_ => _itemListDataMock.Object);

        _treeDataMock
            .Setup(d => d.LoadTagsAsync())
            .ReturnsAsync([]);
        _ = _ctx.Services.AddScoped(_ => _treeDataMock.Object);

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("smoke_user");
        authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, UserId),
            new Claim(ClaimTypes.Name, "smoke_user"),
            new Claim(ClaimTypes.Role, "Admin"));

        _ctx.JSInterop.Setup<Blazor.Diagrams.Core.Geometry.Rectangle>(invocation => invocation.Identifier.Contains("getBoundingClientRect"))
            .SetResult(new Blazor.Diagrams.Core.Geometry.Rectangle(0, 0, 800, 600));

        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void Home_Renders_WithoutException()
    {
        _ = _homeDataMock.Setup(d => d.GetFollowedTagIdsAsync(UserId))
            .ReturnsAsync([]);
        _ = _homeDataMock.Setup(d => d.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));
        _ = _homeDataMock.Setup(d => d.EnsureSystemTagsAsync(UserId))
            .ReturnsAsync(new SystemTagsResult(GoodTagId: 1, BadTagId: 2, Created: false));
        _ = _homeDataMock.Setup(d => d.LoadTimelineAsync(It.IsAny<System.Collections.Generic.IReadOnlyList<int>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HomeTimelinePage([], 0));

        RenderFragment home = builder =>
        {
            builder.OpenComponent<Home>(0);
            builder.CloseComponent();
        };
        IRenderedComponent<AuthHost> host =
            _ctx.Render<AuthHost>(parameters => parameters.Add(p => p.ChildContent, home));

        host.WaitForState(() => host.Markup.Contains("タイムライン") ||
                                 host.Markup.Contains("まだタグをフォローしていません"));

        Assert.Contains("タイムライン", host.Markup);
    }

    [Fact]
    public void TagSearch_Renders_WithoutException()
    {
        IRenderedComponent<TagSearch> cut = _ctx.Render<TagSearch>();

        Assert.Contains("タグ検索", cut.Markup);
        Assert.Contains("タグを検索", cut.Markup);
    }

    [Fact]
    public void TagList_Renders_WithoutException()
    {
        IRenderedComponent<TagList> cut = _ctx.Render<TagList>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void TagTree_Renders_WithoutException()
    {
        IRenderedComponent<TagTree> cut = _ctx.Render<TagTree>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void ItemList_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Item.ItemList> cut =
            _ctx.Render<SRNSMudApp.Components.Item.ItemList>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void UserSearch_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.User.UserSearch> cut =
            _ctx.Render<SRNSMudApp.Components.User.UserSearch>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void UserGroupList_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.UserGroup.UserGroupList> cut =
            _ctx.Render<SRNSMudApp.Components.UserGroup.UserGroupList>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void InvitationManager_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Admin.InvitationManager> cut =
            _ctx.Render<SRNSMudApp.Components.Admin.InvitationManager>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void ReportManager_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Admin.ReportManager> cut =
            _ctx.Render<SRNSMudApp.Components.Admin.ReportManager>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void TagManagement_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Admin.TagManagement> cut =
            _ctx.Render<SRNSMudApp.Components.Admin.TagManagement>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void UserManagement_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.User.UserManagement> cut =
            _ctx.Render<SRNSMudApp.Components.User.UserManagement>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void MakeMeAdmin_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.User.MakeMeAdmin> cut =
            _ctx.Render<SRNSMudApp.Components.User.MakeMeAdmin>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void ContractManagement_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Contract.ContractManagement> cut =
            _ctx.Render<SRNSMudApp.Components.Contract.ContractManagement>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void BountyBoard_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Bounty.BountyBoard> cut =
            _ctx.Render<SRNSMudApp.Components.Bounty.BountyBoard>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void PublicOfferBoard_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.PublicOffer.PublicOfferBoard> cut =
            _ctx.Render<SRNSMudApp.Components.PublicOffer.PublicOfferBoard>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void RightAssetOverview_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Pages.RightAssetOverview> cut =
            _ctx.Render<SRNSMudApp.Components.Pages.RightAssetOverview>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void ItemDetail_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Item.ItemDetail> cut =
            _ctx.Render<SRNSMudApp.Components.Item.ItemDetail>(parameters => parameters.Add(p => p.ItemId, 1));
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void TagDetail_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Tag.TagDetail> cut =
            _ctx.Render<SRNSMudApp.Components.Tag.TagDetail>(parameters => parameters.Add(p => p.TagId, 1));
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void AddTag_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Tag.AddTag> cut =
            _ctx.Render<SRNSMudApp.Components.Tag.AddTag>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void ImportTag_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Tag.ImportTag> cut =
            _ctx.Render<SRNSMudApp.Components.Tag.ImportTag>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void ImportTagging_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Tag.ImportTagging> cut =
            _ctx.Render<SRNSMudApp.Components.Tag.ImportTagging>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void TagDiagramPage_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Pages.TagDiagramPage> cut =
            _ctx.Render<SRNSMudApp.Components.Pages.TagDiagramPage>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void UserDetail_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.User.UserDetail> cut =
            _ctx.Render<SRNSMudApp.Components.User.UserDetail>(parameters => parameters.Add(p => p.UserId, UserId));
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void NotificationsPage_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Pages.NotificationsPage> cut =
            _ctx.Render<SRNSMudApp.Components.Pages.NotificationsPage>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void AuthCallback_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.Pages.AuthCallback> cut =
            _ctx.Render<SRNSMudApp.Components.Pages.AuthCallback>();
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void UserGroupMembers_Renders_WithoutException()
    {
        IRenderedComponent<SRNSMudApp.Components.UserGroup.UserGroupMembers> cut =
            _ctx.Render<SRNSMudApp.Components.UserGroup.UserGroupMembers>(parameters => parameters.Add(p => p.GroupId, 1));
        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void AllFeaturePages_AreCoveredBySmokeTests()
    {
        var excludedPageNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Error",
            "NotFound"
        };

        var featurePages = typeof(SRNSMudApp.Components.App).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IComponent).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: true).Length != 0)
            .Where(t => t.Namespace == null || !t.Namespace.Contains("Components.Account", StringComparison.Ordinal))
            .Where(t => !excludedPageNames.Contains(t.Name))
            .ToList();

        var testMethodNames = typeof(PageRenderSmokeTests)
            .GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(FactAttribute), inherit: false).Length != 0)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingPages = featurePages
            .Where(page => !testMethodNames.Contains($"{page.Name}_Renders_WithoutException"))
            .Select(page => $"{page.FullName} (Expected test method: {page.Name}_Renders_WithoutException)")
            .ToList();

        Assert.True(
            missingPages.Count == 0,
            $"Found {missingPages.Count} feature @page component(s) without a smoke test in PageRenderSmokeTests:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missingPages));
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    private sealed class AuthHost : ComponentBase
    {
        [Parameter] public RenderFragment ChildContent { get; set; } = _ => { };

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)(b =>
            {
                b.AddContent(0, ChildContent);
            }));
            builder.CloseComponent();
        }
    }
}