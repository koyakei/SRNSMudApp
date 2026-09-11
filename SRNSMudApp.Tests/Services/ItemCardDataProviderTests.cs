#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
/// <see cref="ItemCardDataProvider" /> の投票トグルロジックの単体テスト (MSSQL Testcontainers)。
/// </summary>
public class ItemCardDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, ItemCardDataProvider sut, string userId, int goodTagId, int itemId, string tid)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var sut = new ItemCardDataProvider(new DbContextFactoryStub(_sharedDb.Options));

        var userId = $"voter_{tid}";
        var systemId = $"sys_{tid}";
        var authorId = $"author_{tid}";

        await db.SeedUsersAsync(userId, systemId, authorId);

        var tag = new Tag { Name = $"good_{tid}", IsSystem = true, OwnerId = systemId, CachedWeight = 0 };
        var item = new Item { Content = $"target_{tid}", OwnerId = authorId };

        db.Tags.Add(tag);
        db.Items.Add(item);
        await db.SaveChangesAsync();

        return (db, sut, userId, tag.Id, item.Id, tid);
    }



    [Fact]
    public async Task CreateItemAsync_WithExistingOwner_DoesNotDuplicateUserAndStoresItem()
    {
        var (db, sut, _, goodTagId, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var authorId = $"author_{tid}";
            var newItem = new Item
            {
                Content = $"New item created by existing author_{tid}",
                OwnerId = authorId
            };

            await sut.CreateItemAsync(newItem, [goodTagId]);

            Assert.Equal(1, await db.Users.CountAsync(u => u.Id == authorId));
            var saved = await db.Items.FirstOrDefaultAsync(i => i.Content == $"New item created by existing author_{tid}");
            Assert.NotNull(saved);
            Assert.Equal(authorId, saved.OwnerId);
            Assert.True(await db.TagRelations.AnyAsync(tr => tr.ItemId == saved.Id && tr.TagId == goodTagId));
        }
    }

    [Fact]
    public async Task CanUserAttachTagDirectlyAsync_WhenTagIsAutoApproved_ReturnsTrueAndAddsRelation()
    {
        var (db, sut, userId, _, itemId, tid) = await CreateScopeAsync();
        await using (db)
        {
            var otherOwnerId = $"other_owner_{tid}";
            await db.SeedUsersAsync(otherOwnerId);

            var autoApproveTag = new Tag
            {
                Name = $"auto_tag_{tid}",
                OwnerId = otherOwnerId,
                AutoAcceptIncomingTaggingRequests = true,
                CachedWeight = 5
            };
            db.Tags.Add(autoApproveTag);
            await db.SaveChangesAsync();

            // Act 1: Check can attach directly
            var canAttach = await sut.CanUserAttachTagDirectlyAsync(autoApproveTag.Id, userId);
            Assert.True(canAttach);

            // Act 2: Add free tag relation directly
            var relation = await sut.AddFreeTagRelationAsync(itemId, autoApproveTag.Id, userId);

            // Assert
            Assert.NotNull(relation);
            Assert.Equal(itemId, relation.ItemId);
            Assert.Equal(autoApproveTag.Id, relation.TagId);
            Assert.Equal(userId, relation.OwnerId);

            var updatedTag = await db.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == autoApproveTag.Id);
            Assert.Equal(6, updatedTag!.CachedWeight);

            var ledger = await db.TagWeightLedgers.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
            Assert.NotNull(ledger);
            Assert.Equal("Auto-Approved Tagging", ledger.Reason);
            Assert.Equal(1, ledger.Delta);
        }
    }

    [Fact]
    public async Task CanUserAttachTagDirectlyAsync_WhenUserInDelegatedGroup_ReturnsTrue()
    {
        var (db, sut, userId, _, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var otherOwnerId = $"group_owner_{tid}";
            await db.SeedUsersAsync(otherOwnerId);

            var group = new UserGroup { Name = $"Group_{tid}", OwnerId = otherOwnerId };
            group.Members.Add(new UserGroupMember { UserGroup = group, UserId = userId, OwnerId = otherOwnerId });
            db.UserGroups.Add(group);
            await db.SaveChangesAsync();

            var tag = new Tag
            {
                Name = $"delegated_{tid}",
                OwnerId = otherOwnerId,
                CachedWeight = 0
            };
            tag.AutoApproveUserGroups.Add(new TagAutoApproveUserGroup
            {
                Tag = tag,
                UserGroupId = group.Id,
                OwnerId = otherOwnerId
            });
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            var canAttach = await sut.CanUserAttachTagDirectlyAsync(tag.Id, userId);
            Assert.True(canAttach);
        }
    }

    [Fact]
    public async Task CanUserAttachTagDirectlyAsync_WhenOnlyLegacyGroupColumnIsSet_ReturnsFalse()
    {
        var (db, sut, userId, _, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var otherOwnerId = $"legacy_owner_{tid}";
            await db.SeedUsersAsync(otherOwnerId);

            var group = new UserGroup { Name = $"LegacyGroup_{tid}", OwnerId = otherOwnerId };
            group.Members.Add(new UserGroupMember { UserGroup = group, UserId = userId, OwnerId = otherOwnerId });
            db.UserGroups.Add(group);
            await db.SaveChangesAsync();

#pragma warning disable CS0618 // 旧カラムから新しい中間テーブルへ移行する挙動を検証するため意図的に設定する
            var tag = new Tag
            {
                Name = $"legacy_only_{tid}",
                OwnerId = otherOwnerId,
                AutoApproveUserGroupId = group.Id,
                CachedWeight = 0
            };
#pragma warning restore CS0618
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            var canAttach = await sut.CanUserAttachTagDirectlyAsync(tag.Id, userId);

            Assert.False(canAttach);
        }
    }

    [Fact]
    public async Task CanUserAttachTagDirectlyAsync_WhenNotAutoApproved_ReturnsFalse()
    {
        var (db, sut, userId, _, _, tid) = await CreateScopeAsync();
        await using (db)
        {
            var otherOwnerId = $"normal_owner_{tid}";
            await db.SeedUsersAsync(otherOwnerId);

            var tag = new Tag
            {
                Name = $"normal_{tid}",
                OwnerId = otherOwnerId,
                AutoAcceptIncomingTaggingRequests = false,
                CachedWeight = 0
            };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            var canAttach = await sut.CanUserAttachTagDirectlyAsync(tag.Id, userId);
            Assert.False(canAttach);
        }
    }

    /// <summary>テスト用のシンプルなファクトリ。</summary>
    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}