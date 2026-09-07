using System.IO;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

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
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}