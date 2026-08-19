#region

using Microsoft.Extensions.DependencyInjection;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Models.Unions;

#endregion

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class TagRelationServiceIntegrationTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _factory.EnsureServer(); // Initialize the host
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory?.Dispose();

    private CustomWebApplicationFactory? _factory;

    [Test]
    public async Task AddTagToItemAsync_WithUntrackedItem_ShouldNotThrowPrimaryKeyException()
    {
        var testUserId = Guid.NewGuid().ToString();
        var itemContent = "Test Item " + Guid.NewGuid();
        var tagName = "Test Tag " + Guid.NewGuid();

        Item untrackedItem;
        Tag untrackedTag;

        // 1. Arrange: 別のDbContextでデータを作成して保存する
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser
            {
                Id = testUserId, UserName = "testuser_" + testUserId, Email = $"testuser_{testUserId}@example.com"
            };
            _ = db.Users.Add(user);

            var item = new Item { Content = itemContent, OwnerId = user.Id };
            _ = db.Items.Add(item);

            var tag = new Tag { Name = tagName, OwnerId = user.Id };
            _ = db.Tags.Add(tag);

            _ = await db.SaveChangesAsync();

            untrackedItem = item;
            untrackedTag = tag;
        } // ここでDbContextがDisposeされ、itemとtagは追跡されなくなる

        // 2. Act: 新しいDbContextを持つTagRelationServiceで操作する
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var service = new TagRelationService(db);

            // ここで例外が発生しないことを確認する
            var result = await service.LinkTagToItemAsync(untrackedItem.Id, untrackedTag.Id, testUserId);

            Assert.That(result is Success<bool>, Is.True, result is Failure f ? f.ErrorMessage : "Unknown Error");
        }

        // 3. Assert: 正しくTagRelationが作成されているか確認する
        using (IServiceScope scope = _factory.AppServices.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var relations = db.TagRelations.Where(tr => tr.ItemId == untrackedItem.Id && tr.TagId == untrackedTag.Id)
                .ToList();

            Assert.That(relations, Has.Count.EqualTo(1));
            Assert.That(relations[0].OwnerId, Is.EqualTo(testUserId));
        }
    }
}