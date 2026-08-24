using System.Threading.Tasks;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Tag;

[Collection(MsSqlCollection.Name)]
public class TagListConcurrencyTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private readonly BunitContext _ctx = new();

    public TagListConcurrencyTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(TagListConcurrencyTests));

        _ = _ctx.Services.AddAuth("testuser");
        _ = _ctx.Services.AddSrnsComponentServices();

        var embeddingMock = new Mock<SRNSMudApp.Services.ITagEmbeddingService>();
        embeddingMock.Setup(s => s.GenerateEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<float>());
        _ctx.Services.AddScoped(_ => embeddingMock.Object);

        _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    [Fact]
    public void RenderingTagList_ShouldNotThrowConcurrencyException()
    {
        // Act & Assert
        // If the components share the same Scoped DbContext and perform async DB operations simultaneously,
        // this will throw an InvalidOperationException from Entity Framework Core.
        // By using IDbContextFactory internally, this error is avoided.
        Exception? exception = Record.Exception(() =>
        {
            // Render ではなく、_ctx.Render<T>() を使用します
            _ = _ctx.Render<TagList>();
        });

        // Verify that no exception was thrown
        Assert.Null(exception);
    }
}