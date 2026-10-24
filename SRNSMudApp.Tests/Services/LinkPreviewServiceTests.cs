#region

using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Providers;
using SRNSMudApp.Tests.TestSupport;

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
        _ = services.AddMsSqlDbFactory(_sharedDb.ConnectionString);
        var sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        var httpClient = new HttpClient(new FakeHttpMessageHandler());
        var ogpLogger = NullLogger<ExternalOgpLinkPreviewProvider>.Instance;
        var serviceLogger = NullLogger<LinkPreviewService>.Instance;

        var providers = new ILinkPreviewProvider[]
        {
            new ItemLinkPreviewProvider(dbFactory),
            new TagLinkPreviewProvider(dbFactory),
            new UserLinkPreviewProvider(dbFactory),
            new ExternalOgpLinkPreviewProvider(httpClient, ogpLogger)
        };

        var sut = new LinkPreviewService(providers, serviceLogger);
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

    [Fact]
    public async Task GetPreviewAsync_InternalItemDetail_WhenItemIsPrivate_FiltersOutForUnauthorizedUser()
    {
        // Arrange
        var (db, sut, tid) = CreateScope();
        var userA = $"owner_{tid}";
        var userB = $"viewer_{tid}";
        var adminUser = $"admin_{tid}";
        await db.SeedUsersAsync(userA, userB, adminUser);

        var privateItem = new Item
        {
            Content = $"Secret private content {tid}",
            OwnerId = userA,
            IsPrivate = true,
            TargetUserGroupId = null
        };
        db.Items.Add(privateItem);
        await db.SaveChangesAsync();

        // Act 1: 権限のない第三者 (userB)
        LinkPreviewData previewUserB = await sut.GetPreviewAsync($"/ItemDetail/{privateItem.Id}", userB, isAdmin: false, CancellationToken.None);
        Assert.False(previewUserB.IsSuccess);

        // Act 2: 未ログインユーザー (null)
        LinkPreviewData previewAnon = await sut.GetPreviewAsync($"/ItemDetail/{privateItem.Id}", null, isAdmin: false, CancellationToken.None);
        Assert.False(previewAnon.IsSuccess);

        // Act 3: 投稿者本人 (userA)
        LinkPreviewData previewOwner = await sut.GetPreviewAsync($"/ItemDetail/{privateItem.Id}", userA, isAdmin: false, CancellationToken.None);
        Assert.True(previewOwner.IsSuccess);
        Assert.Equal($"Secret private content {tid}", previewOwner.Description);

        // Act 4: 管理者 (adminUser)
        LinkPreviewData previewAdmin = await sut.GetPreviewAsync($"/ItemDetail/{privateItem.Id}", adminUser, isAdmin: true, CancellationToken.None);
        Assert.True(previewAdmin.IsSuccess);
        Assert.Equal($"Secret private content {tid}", previewAdmin.Description);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}