#region

using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     LinkPreviewService の内部リンクプレビュー機能に関する単体テスト (MSSQL Testcontainers)。
/// </summary>
public class LinkPreviewServiceTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext db, LinkPreviewService sut, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(_sharedDb.Options));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var httpClient = new HttpClient(new FakeHttpMessageHandler());
        var sut = new LinkPreviewService(httpClient, scopeFactory);
        var db = new ApplicationDbContext(_sharedDb.Options);

        return (db, sut, tid);
    }

    [Fact]
    public async Task GetPreviewAsync_InternalItemDetail_ExcludesTagsFromDescription_AndPopulatesTags()
    {
        // Arrange
        var (db, sut, tid) = CreateScope();
        var userId = $"owner_{tid}";
        var tagOwnerId = $"tagowner_{tid}";
        await db.SeedUsersAsync(userId, tagOwnerId);

        var item = new Item { Content = $"Hello internal link preview {tid}", OwnerId = userId };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var tag = new Tag { Name = $"Csharp_{tid}", OwnerId = tagOwnerId };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        var relation = new TagRelation
        {
            ItemId = item.Id,
            TagId = tag.Id,
            OwnerId = tagOwnerId,
            Weight = 10
        };
        db.TagRelations.Add(relation);
        await db.SaveChangesAsync();

        // Act
        LinkPreviewData preview = await sut.GetPreviewAsync($"/ItemDetail/{item.Id}");

        // Assert
        Assert.True(preview.IsSuccess);
        Assert.Equal($"Hello internal link preview {tid}", preview.Description);
        Assert.DoesNotContain("Tags:", preview.Description);
        Assert.DoesNotContain(tag.Name, preview.Description);
        Assert.Single(preview.Tags);
        Assert.Equal(tag.Name, preview.Tags[0].Name);
        Assert.Equal(tagOwnerId, preview.Tags[0].OwnerName);
        Assert.Equal(10, preview.Tags[0].Weight);
    }

    [Fact]
    public async Task GetPreviewAsync_InternalItemDetail_WithoutTags_ReturnsEmptyTags()
    {
        // Arrange
        var (db, sut, tid) = CreateScope();
        var userId = $"owner_{tid}";
        await db.SeedUsersAsync(userId);

        var item = new Item { Content = $"No tags content {tid}", OwnerId = userId };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        // Act
        LinkPreviewData preview = await sut.GetPreviewAsync($"/ItemDetail/{item.Id}");

        // Assert
        Assert.True(preview.IsSuccess);
        Assert.Equal($"No tags content {tid}", preview.Description);
        Assert.Empty(preview.Tags);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}