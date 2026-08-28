using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
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

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}