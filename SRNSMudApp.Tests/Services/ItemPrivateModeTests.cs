#region

using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     プライベートモード（フォロワー限定、グループ限定）におけるアイテムの可視性制御の単体テスト。
/// </summary>
public class ItemPrivateModeTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(
        ApplicationDbContext db,
        ItemListDataProvider itemListProvider,
        ItemDetailDataProvider itemDetailProvider,
        UserDataProvider userDataProvider,
        string authorId,
        string followerId,
        string strangerId,
        string groupMemberId,
        int groupId)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var tagEmbeddingMock = new Mock<ITagEmbeddingService>();
        var itemListProvider = new ItemListDataProvider(stubFactory, tagEmbeddingMock.Object);
        var itemDetailProvider = new ItemDetailDataProvider(stubFactory);
        var userDataProvider = new UserDataProvider(stubFactory);

        var authorId = $"author_{tid}";
        var followerId = $"follower_{tid}";
        var strangerId = $"stranger_{tid}";
        var groupMemberId = $"member_{tid}";

        await db.SeedUsersAsync(authorId, followerId, strangerId, groupMemberId);

        // フォロー関係: followerId が authorId をフォロー
        db.UserFollows.Add(new UserFollow
        {
            OwnerId = followerId,
            FollowedUserId = authorId
        });

        // ユーザーグループ: authorId がオーナー、groupMemberId がメンバー
        var group = new UserGroup
        {
            Name = $"Group_{tid}",
            OwnerId = authorId
        };
        db.UserGroups.Add(group);
        await db.SaveChangesAsync();

        db.UserGroupMembers.Add(new UserGroupMember
        {
            UserGroupId = group.Id,
            UserId = groupMemberId,
            OwnerId = authorId
        });
        await db.SaveChangesAsync();

        return (db, itemListProvider, itemDetailProvider, userDataProvider, authorId, followerId, strangerId, groupMemberId, group.Id);
    }

    [Fact]
    public async Task PublicItem_ShouldBeVisibleToEveryone()
    {
        var (db, itemListProvider, itemDetailProvider, userDataProvider, authorId, followerId, strangerId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item
            {
                Content = "Public post",
                OwnerId = authorId,
                IsPrivate = false
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            // 1. ItemListDataProvider での取得検証
            var dataAnonymous = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: null);
            var dataStranger = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: strangerId);
            var dataFollower = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: followerId);

            Assert.Contains(dataAnonymous.Items, i => i.Id == item.Id);
            Assert.Contains(dataStranger.Items, i => i.Id == item.Id);
            Assert.Contains(dataFollower.Items, i => i.Id == item.Id);

            // 2. ItemDetailDataProvider での取得検証
            var detailAnonymous = await itemDetailProvider.GetItemDetailAsync(item.Id, null);
            Assert.NotNull(detailAnonymous);
            Assert.Equal(item.Id, detailAnonymous.Item.Id);

            // 3. UserDataProvider での取得検証
            var userDetailAnonymous = await userDataProvider.GetUserDetailAsync(authorId, null);
            Assert.Contains(userDetailAnonymous.UserItems, i => i.Id == item.Id);
        }
    }

    [Fact]
    public async Task PrivateItem_FollowersOnly_ShouldBeVisibleToAuthorAndFollowers_AndHiddenFromStrangers()
    {
        var (db, itemListProvider, itemDetailProvider, userDataProvider, authorId, followerId, strangerId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item
            {
                Content = "Followers only post",
                OwnerId = authorId,
                IsPrivate = true,
                TargetUserGroupId = null
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            // 1. 未ログイン・見知らぬ人には非表示
            var dataAnonymous = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: null);
            var dataStranger = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: strangerId);
            Assert.DoesNotContain(dataAnonymous.Items, i => i.Id == item.Id);
            Assert.DoesNotContain(dataStranger.Items, i => i.Id == item.Id);

            var detailAnonymous = await itemDetailProvider.GetItemDetailAsync(item.Id, null);
            Assert.Null(detailAnonymous);

            var detailStranger = await itemDetailProvider.GetItemDetailAsync(item.Id, strangerId);
            Assert.Null(detailStranger);

            var userDetailStranger = await userDataProvider.GetUserDetailAsync(authorId, strangerId);
            Assert.DoesNotContain(userDetailStranger.UserItems, i => i.Id == item.Id);

            // 2. 投稿者本人およびフォロワーには表示
            var dataAuthor = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: authorId);
            var dataFollower = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: followerId);
            Assert.Contains(dataAuthor.Items, i => i.Id == item.Id);
            Assert.Contains(dataFollower.Items, i => i.Id == item.Id);

            var detailAuthor = await itemDetailProvider.GetItemDetailAsync(item.Id, authorId);
            Assert.NotNull(detailAuthor);

            var detailFollower = await itemDetailProvider.GetItemDetailAsync(item.Id, followerId);
            Assert.NotNull(detailFollower);

            var userDetailFollower = await userDataProvider.GetUserDetailAsync(authorId, followerId);
            Assert.Contains(userDetailFollower.UserItems, i => i.Id == item.Id);
        }
    }

    [Fact]
    public async Task PrivateItem_GroupOnly_ShouldBeVisibleToGroupMembersAndOwner_AndHiddenFromOthersEvenFollowers()
    {
        var (db, itemListProvider, itemDetailProvider, userDataProvider, authorId, followerId, strangerId, groupMemberId, groupId) = await CreateScopeAsync();
        await using (db)
        {
            var item = new Item
            {
                Content = "Group only post",
                OwnerId = authorId,
                IsPrivate = true,
                TargetUserGroupId = groupId
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            // 1. 未ログイン、見知らぬ人、およびグループに入っていないフォロワーには非表示
            var dataAnonymous = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: null);
            var dataStranger = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: strangerId);
            var dataFollower = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: followerId);

            Assert.DoesNotContain(dataAnonymous.Items, i => i.Id == item.Id);
            Assert.DoesNotContain(dataStranger.Items, i => i.Id == item.Id);
            Assert.DoesNotContain(dataFollower.Items, i => i.Id == item.Id);

            Assert.Null(await itemDetailProvider.GetItemDetailAsync(item.Id, null));
            Assert.Null(await itemDetailProvider.GetItemDetailAsync(item.Id, strangerId));
            Assert.Null(await itemDetailProvider.GetItemDetailAsync(item.Id, followerId));

            var userDetailFollower = await userDataProvider.GetUserDetailAsync(authorId, followerId);
            Assert.DoesNotContain(userDetailFollower.UserItems, i => i.Id == item.Id);

            // 2. グループオーナー（投稿者本人）およびグループメンバーには表示
            var dataAuthor = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: authorId);
            var dataMember = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: groupMemberId);

            Assert.Contains(dataAuthor.Items, i => i.Id == item.Id);
            Assert.Contains(dataMember.Items, i => i.Id == item.Id);

            var detailAuthor = await itemDetailProvider.GetItemDetailAsync(item.Id, authorId);
            Assert.NotNull(detailAuthor);

            var detailMember = await itemDetailProvider.GetItemDetailAsync(item.Id, groupMemberId);
            Assert.NotNull(detailMember);

            var userDetailMember = await userDataProvider.GetUserDetailAsync(authorId, groupMemberId);
            Assert.Contains(userDetailMember.UserItems, i => i.Id == item.Id);
        }
    }

    private sealed class DbContextFactoryStub(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}