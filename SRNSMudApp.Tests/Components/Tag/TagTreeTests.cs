using System.IO;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using Xunit.Abstractions;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TagTreeTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagTreeDataProvider> _treeDataMock = new();

    public TagTreeTests(ITestOutputHelper output)
    {
        _output = output;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _treeDataMock.Object);
        _ = _ctx.Services.AddAuth("test-user-id");

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void JqTree_InitializesWithCorrectJson_WhenSingleRootNodeHasMultipleChildren()
    {
        // Arrange
        var rootTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "Root", IsSystem = false, OwnerId = "test-user-id" };
        var child1 = new SRNSMudApp.Data.Tag
        {
            Id = 2,
            Name = "Child1",
            ParentTagId = 1,
            IsSystem = false,
            OwnerId = "test-user-id"
        };
        var child2 = new SRNSMudApp.Data.Tag
        {
            Id = 3,
            Name = "Child2",
            ParentTagId = 1,
            IsSystem = false,
            OwnerId = "test-user-id"
        };

        _ = _treeDataMock
            .Setup(d => d.LoadTagsAsync())
            .ReturnsAsync([rootTag, child1, child2]);

        List<JSRuntimeInvocation> jsInteropInvocations = [];
        _ = _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        // Act
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();

        // Assert
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));

        JSRuntimeInvocation invocation = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init");
        var treeDataJson = invocation.Arguments[1] as string;
        _output.WriteLine("JSON Output: " + treeDataJson);

        // Verify the JSON structure
        Assert.NotNull(treeDataJson);
        Assert.Contains($"\"id\":{rootTag.Id}", treeDataJson);
        Assert.Contains("\"children\":", treeDataJson);
        Assert.Contains($"\"id\":{child1.Id}", treeDataJson);
        Assert.Contains($"\"id\":{child2.Id}", treeDataJson);
    }

    [Fact]
    public void JqTreeInteropScript_AddsChildButtonToEachNodeTitle()
    {
        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SRNSMudApp", "wwwroot", "js", "jqTreeInterop.js"));

        var script = File.ReadAllText(scriptPath);

        Assert.Contains("onCreateLi(node, $li)", script);
        Assert.Contains("createAddChildButton", script);
        Assert.Contains("$title.after(createAddChildButton(node.id))", script);
        Assert.Contains("add-child-btn", script);
    }

    [Fact]
    public async Task OnTreeMove_WhenTagOwnedByAnotherUser_SendsMoveRequestAndReloadsTree()
    {
        // Arrange
        SRNSMudApp.Data.Tag rootTag = new() { Id = 1, Name = "Root", IsSystem = false, OwnerId = "system" };
        SRNSMudApp.Data.Tag otherTag = new()
        {
            Id = 2,
            Name = "OtherTag",
            ParentTagId = 1,
            IsSystem = false,
            OwnerId = "other-user-id"
        };
        SRNSMudApp.Data.Tag targetTag = new()
        {
            Id = 3,
            Name = "TargetTag",
            ParentTagId = 1,
            IsSystem = false,
            OwnerId = "test-user-id"
        };

        _ = _treeDataMock
            .Setup(d => d.LoadTagsAsync())
            .ReturnsAsync([rootTag, otherTag, targetTag]);

        _ = _treeDataMock
            .Setup(d => d.RequestTagMoveAsync("test-user-id", 2, 3))
            .ReturnsAsync(new Success<TaggingRequestEntity>(new TaggingRequestEntity { Id = 99, OwnerId = "test-user-id" }));

        System.Security.Claims.Claim[] claims = [new(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-id")];
        System.Security.Claims.ClaimsIdentity identity = new(claims, "test");
        Microsoft.AspNetCore.Components.Authorization.AuthenticationState authState = new(new System.Security.Claims.ClaimsPrincipal(identity));

        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>(parameters => parameters
            .AddCascadingValue(Task.FromResult(authState)));
        component.WaitForAssertion(() => Assert.NotNull(component.Instance));

        // Act: move otherTag under targetTag ("inside")
        await component.InvokeAsync(() => component.Instance.OnTreeMove(2, 3, "inside"));

        // Assert
        _treeDataMock.Verify(d => d.RequestTagMoveAsync("test-user-id", 2, 3), Times.Once);
        _treeDataMock.Verify(d => d.UpdateParentAsync(It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
        ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        Assert.Contains(snackbar.ShownSnackbars, s => s.Message.ToString().Contains("移動リクエストが通りました"));
    }

    [Fact]
    public async Task OnTreeMove_WhenTagOwnedByCurrentUser_DirectlyUpdatesAndShowsSnackbar()
    {
        // Arrange
        SRNSMudApp.Data.Tag rootTag = new() { Id = 1, Name = "Root", IsSystem = false, OwnerId = "system" };
        SRNSMudApp.Data.Tag ownTag = new()
        {
            Id = 2,
            Name = "OwnTag",
            ParentTagId = 1,
            IsSystem = false,
            OwnerId = "test-user-id"
        };
        SRNSMudApp.Data.Tag targetTag = new()
        {
            Id = 3,
            Name = "TargetTag",
            ParentTagId = 1,
            IsSystem = false,
            OwnerId = "other-user-id"
        };

        _ = _treeDataMock
            .Setup(d => d.LoadTagsAsync())
            .ReturnsAsync([rootTag, ownTag, targetTag]);

        _ = _treeDataMock
            .Setup(d => d.UpdateParentAsync(2, 3))
            .ReturnsAsync(true);

        System.Security.Claims.Claim[] claims = [new(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-id")];
        System.Security.Claims.ClaimsIdentity identity = new(claims, "test");
        Microsoft.AspNetCore.Components.Authorization.AuthenticationState authState = new(new System.Security.Claims.ClaimsPrincipal(identity));

        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>(parameters => parameters
            .AddCascadingValue(Task.FromResult(authState)));
        component.WaitForAssertion(() => Assert.NotNull(component.Instance));

        // Act: move ownTag under targetTag ("inside")
        await component.InvokeAsync(() => component.Instance.OnTreeMove(2, 3, "inside"));

        // Assert
        _treeDataMock.Verify(d => d.UpdateParentAsync(2, 3), Times.Once);
        _treeDataMock.Verify(d => d.RequestTagMoveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
        ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        Assert.Contains(snackbar.ShownSnackbars, s => s.Message.ToString().Contains("移動リクエストが通りました"));
    }

    [Fact]
    public void JqTreeInteropScript_ContainsCancelButtonAndPendingMoveSupport()
    {
        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SRNSMudApp", "wwwroot", "js", "jqTreeInterop.js"));

        var script = File.ReadAllText(scriptPath);

        Assert.Contains("createCancelButton", script);
        Assert.Contains("isPendingMove", script);
        Assert.Contains("pending-move-node", script);
        Assert.Contains("CancelMoveRequest", script);
    }

    [Fact]
    public void JqTreeInteropScript_SupportsOpeningTagDetailInNewTabOnModifierClick()
    {
        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SRNSMudApp", "wwwroot", "js", "jqTreeInterop.js"));

        var script = File.ReadAllText(scriptPath);

        // Verify metaKey (⌘), ctrlKey, or middle click opens in new tab via window.open
        Assert.Contains("origEvent.metaKey || origEvent.ctrlKey || origEvent.button === 1", script);
        Assert.Contains("window.open('/TagDetail/' + nodeId, '_blank')", script);
        Assert.Contains("window.open('/TagDetail/' + node.id, '_blank')", script);
    }

    [Fact]
    public void JqTreeInteropScript_ContainsHighlightedTagSupport()
    {
        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SRNSMudApp", "wwwroot", "js", "jqTreeInterop.js"));

        var script = File.ReadAllText(scriptPath);

        Assert.Contains("isHighlighted", script);
        Assert.Contains("highlighted-tag-node", script);
        Assert.Contains("highlighted-tag-title", script);
    }

    [Fact]
    public async Task CancelMoveRequest_WhenCalled_InvokesCancelTagMoveAsyncAndReloadsTree()
    {
        // Arrange
        _treeDataMock
            .Setup(d => d.CancelTagMoveAsync(99, "test-user-id"))
            .ReturnsAsync(new Success<string>("移動リクエストをキャンセルしました。"));

        _treeDataMock
            .Setup(d => d.LoadTagsAsync())
            .ReturnsAsync([]);

        _treeDataMock
            .Setup(d => d.LoadPendingTagMovesAsync())
            .ReturnsAsync([]);

        System.Security.Claims.Claim[] claims = [new(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-id")];
        System.Security.Claims.ClaimsIdentity identity = new(claims, "test");
        Microsoft.AspNetCore.Components.Authorization.AuthenticationState authState = new(new System.Security.Claims.ClaimsPrincipal(identity));

        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>(parameters => parameters
            .AddCascadingValue(Task.FromResult(authState)));
        component.WaitForAssertion(() => Assert.NotNull(component.Instance));

        // Act
        await component.InvokeAsync(() => component.Instance.CancelMoveRequest(99));

        // Assert
        _treeDataMock.Verify(d => d.CancelTagMoveAsync(99, "test-user-id"), Times.Once);
        ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        Assert.Contains(snackbar.ShownSnackbars, s => s.Message.ToString().Contains("キャンセルしました"));
    }

    [Fact]
    public async Task SearchFilterChange_UpdatesUrlQuery()
    {
        // Arrange
        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("http://localhost/tag-tree");

        _treeDataMock.Setup(d => d.LoadTagsAsync()).ReturnsAsync([]);

        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();
        component.WaitForAssertion(() => Assert.NotNull(component.Instance));

        IRenderedComponent<MudTextField<string>> searchInput = component.FindComponent<MudTextField<string>>();

        // Act 1: 検索文字列を入力
        await component.InvokeAsync(() => searchInput.Instance.ValueChanged.InvokeAsync("AlphaTag"));

        // Assert 1: search クエリが URL に反映される
        component.WaitForAssertion(() => Assert.Contains("search=AlphaTag", navigationManager.Uri));

        // Act 2: 検索文字列をクリア
        await component.InvokeAsync(() => searchInput.Instance.ValueChanged.InvokeAsync(""));

        // Assert 2: search クエリが URL から除去される
        component.WaitForAssertion(() => Assert.DoesNotContain("search=", navigationManager.Uri));
    }

    [Fact]
    public void DeepLinkUrl_RestoresSearchFilter_WithSearchQuery()
    {
        // Arrange
        var rootTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "Root", IsSystem = false, OwnerId = "test-user-id" };
        var child1 = new SRNSMudApp.Data.Tag { Id = 2, Name = "Child1", ParentTagId = 1, IsSystem = false, OwnerId = "test-user-id" };
        var child2 = new SRNSMudApp.Data.Tag { Id = 3, Name = "Child2", ParentTagId = 1, IsSystem = false, OwnerId = "test-user-id" };

        _treeDataMock.Setup(d => d.LoadTagsAsync()).ReturnsAsync([rootTag, child1, child2]);

        List<JSRuntimeInvocation> jsInteropInvocations = [];
        _ctx.JSInterop.SetupVoid("jqTreeInterop.init", invocation =>
        {
            jsInteropInvocations.Add(invocation);
            return true;
        });

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("http://localhost/tag-tree?search=Child1");

        // Act
        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();

        // Assert: 検索パラメータ "Child1" で初期化され、Child1 がハイライト・Child2 は除外される
        component.WaitForAssertion(() => Assert.NotEmpty(jsInteropInvocations));

        JSRuntimeInvocation invocation = jsInteropInvocations.First(i => i.Identifier == "jqTreeInterop.init");
        var treeDataJson = invocation.Arguments[1] as string;

        Assert.NotNull(treeDataJson);
        Assert.Contains("\"id\":2", treeDataJson);
        Assert.Contains("\"isHighlighted\":true", treeDataJson);
        Assert.DoesNotContain("\"id\":3", treeDataJson);

        IRenderedComponent<MudTextField<string>> searchInput = component.FindComponent<MudTextField<string>>();
        Assert.Equal("Child1", searchInput.Find("input").GetAttribute("value"));
    }

    [Fact]
    public async Task DeepLinkUrl_RestoresSearchFilter_WithQQueryFallback_AndClearsQOnUpdate()
    {
        // Arrange
        var rootTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "Root", IsSystem = false, OwnerId = "test-user-id" };
        var child1 = new SRNSMudApp.Data.Tag { Id = 2, Name = "Child1", ParentTagId = 1, IsSystem = false, OwnerId = "test-user-id" };

        _treeDataMock.Setup(d => d.LoadTagsAsync()).ReturnsAsync([rootTag, child1]);

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("http://localhost/tag-tree?q=Child1");

        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();
        component.WaitForAssertion(() => Assert.NotNull(component.Instance));

        IRenderedComponent<MudTextField<string>> searchInput = component.FindComponent<MudTextField<string>>();
        Assert.Equal("Child1", searchInput.Find("input").GetAttribute("value"));

        // Act: 検索文字列を更新
        await component.InvokeAsync(() => searchInput.Instance.ValueChanged.InvokeAsync("UpdatedTag"));

        // Assert: 新しいパラメータ search が設定され、古いパラメータ q は除去される
        component.WaitForAssertion(() =>
        {
            Assert.Contains("search=UpdatedTag", navigationManager.Uri);
            Assert.DoesNotContain("q=", navigationManager.Uri);
        });
    }

    [Fact]
    public async Task SearchFilterChange_PreservesExistingTagIdQuery()
    {
        // Arrange
        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("http://localhost/tag-tree?tagId=99");

        _treeDataMock.Setup(d => d.LoadTagsAsync()).ReturnsAsync([]);

        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();
        component.WaitForAssertion(() => Assert.NotNull(component.Instance));

        IRenderedComponent<MudTextField<string>> searchInput = component.FindComponent<MudTextField<string>>();

        // Act: 検索文字列を入力
        await component.InvokeAsync(() => searchInput.Instance.ValueChanged.InvokeAsync("Filtered"));

        // Assert: tagId=99 が保持されたまま search=Filtered が付加される
        component.WaitForAssertion(() =>
        {
            Assert.Contains("tagId=99", navigationManager.Uri);
            Assert.Contains("search=Filtered", navigationManager.Uri);
        });
    }

    [Fact]
    public void QueryParameterChange_UpdatesSearchTextAndReloadsTree()
    {
        // Arrange
        var rootTag = new SRNSMudApp.Data.Tag { Id = 1, Name = "Root", IsSystem = false, OwnerId = "test-user-id" };
        var child1 = new SRNSMudApp.Data.Tag { Id = 2, Name = "Child1", ParentTagId = 1, IsSystem = false, OwnerId = "test-user-id" };
        var child2 = new SRNSMudApp.Data.Tag { Id = 3, Name = "Child2", ParentTagId = 1, IsSystem = false, OwnerId = "test-user-id" };

        _treeDataMock.Setup(d => d.LoadTagsAsync()).ReturnsAsync([rootTag, child1, child2]);

        List<JSRuntimeInvocation> loadDataInvocations = [];
        _ctx.JSInterop.SetupVoid("jqTreeInterop.loadData", invocation =>
        {
            loadDataInvocations.Add(invocation);
            return true;
        });

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("http://localhost/tag-tree");

        IRenderedComponent<TagTree> component = _ctx.Render<TagTree>();
        component.WaitForAssertion(() => Assert.NotNull(component.Instance));

        // Act: 外部ナビゲーションまたはブラウザ進む/戻るで URL クエリが search=Child2 に変更
        navigationManager.NavigateTo("http://localhost/tag-tree?search=Child2");

        // Assert: loadData が呼ばれ、Child2 のみ含まれるツリーデータで更新される
        component.WaitForAssertion(() => Assert.NotEmpty(loadDataInvocations));

        var treeDataJson = loadDataInvocations.Last().Arguments[1] as string;
        Assert.NotNull(treeDataJson);
        Assert.Contains("\"id\":3", treeDataJson);
        Assert.Contains("\"isHighlighted\":true", treeDataJson);
        Assert.DoesNotContain("\"id\":2", treeDataJson);

        IRenderedComponent<MudTextField<string>> searchInput = component.FindComponent<MudTextField<string>>();
        Assert.Equal("Child2", searchInput.Find("input").GetAttribute("value"));
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}