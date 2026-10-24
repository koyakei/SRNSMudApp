#region

using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     タグのプライベートモード（フォロワー限定、グループ限定、管理者非公開）における可視性制御およびクロススコープ整合性の単体・統合テスト。
/// </summary>
public class TagPrivateModeTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(
        ApplicationDbContext db,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        TagTableDataProvider tagTableProvider,
        TagDetailDataProvider tagDetailProvider,
        TagTreeDataProvider tagTreeProvider,
        TagCommandService tagCommandService,
        ItemListDataProvider itemListProvider,
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

        var tagTableProvider = new TagTableDataProvider(stubFactory);
        var tagDetailProvider = new TagDetailDataProvider(stubFactory);
        var tagTreeProvider = new TagTreeDataProvider(stubFactory);
        var tagCommandService = new TagCommandService(stubFactory, tagEmbeddingMock.Object);
        var itemListProvider = new ItemListDataProvider(stubFactory, tagEmbeddingMock.Object);

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

        return (db, stubFactory, tagTableProvider, tagDetailProvider, tagTreeProvider, tagCommandService, itemListProvider,
            authorId, followerId, strangerId, groupMemberId, group.Id);
    }

    [Fact]
    public async Task PublicTag_ShouldBeVisibleToEveryone()
    {
        var (db, _, tagTableProvider, tagDetailProvider, _, _, _, authorId, followerId, strangerId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var tag = new Tag
            {
                Name = $"PublicTag_{Guid.NewGuid():N}",
                OwnerId = authorId,
                IsPrivate = false
            };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            // 1. WhereVisibleToUser クエリの検証
            Assert.True(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, null).AnyAsync());
            Assert.True(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, strangerId).AnyAsync());
            Assert.True(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, followerId).AnyAsync());

            // 2. TagTableDataProvider での取得検証
            var tableAnonymous = await tagTableProvider.GetAllTagsAsync(null);
            var tableStranger = await tagTableProvider.GetAllTagsAsync(strangerId);
            Assert.Contains(tableAnonymous, t => t.Id == tag.Id);
            Assert.Contains(tableStranger, t => t.Id == tag.Id);

            // 3. TagDetailDataProvider での取得検証
            var detailAnonymous = await tagDetailProvider.GetTagDetailAsync(tag.Id, null);
            var detailStranger = await tagDetailProvider.GetTagDetailAsync(tag.Id, strangerId);
            Assert.NotNull(detailAnonymous);
            Assert.NotNull(detailStranger);
        }
    }

    [Fact]
    public async Task PrivateTag_FollowersOnly_ShouldBeVisibleToAuthorAndFollowers_AndHiddenFromStrangers()
    {
        var (db, _, tagTableProvider, tagDetailProvider, _, _, _, authorId, followerId, strangerId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var tag = new Tag
            {
                Name = $"FollowersTag_{Guid.NewGuid():N}",
                OwnerId = authorId,
                IsPrivate = true,
                TargetUserGroupId = null
            };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            // 1. 未ログイン・見知らぬ人には非表示
            Assert.False(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, null).AnyAsync());
            Assert.False(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, strangerId).AnyAsync());

            var tableAnonymous = await tagTableProvider.GetAllTagsAsync(null);
            var tableStranger = await tagTableProvider.GetAllTagsAsync(strangerId);
            Assert.DoesNotContain(tableAnonymous, t => t.Id == tag.Id);
            Assert.DoesNotContain(tableStranger, t => t.Id == tag.Id);

            Assert.Null(await tagDetailProvider.GetTagDetailAsync(tag.Id, null));
            Assert.Null(await tagDetailProvider.GetTagDetailAsync(tag.Id, strangerId));

            // 2. 作成者本人およびフォロワーには表示
            Assert.True(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, authorId).AnyAsync());
            Assert.True(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, followerId).AnyAsync());

            var tableAuthor = await tagTableProvider.GetAllTagsAsync(authorId);
            var tableFollower = await tagTableProvider.GetAllTagsAsync(followerId);
            Assert.Contains(tableAuthor, t => t.Id == tag.Id);
            Assert.Contains(tableFollower, t => t.Id == tag.Id);

            Assert.NotNull(await tagDetailProvider.GetTagDetailAsync(tag.Id, authorId));
            Assert.NotNull(await tagDetailProvider.GetTagDetailAsync(tag.Id, followerId));
        }
    }

    [Fact]
    public async Task PrivateTag_GroupOnly_ShouldBeVisibleToGroupMembersAndOwner_AndHiddenFromOthers()
    {
        var (db, _, tagTableProvider, tagDetailProvider, _, _, _, authorId, followerId, strangerId, groupMemberId, groupId) = await CreateScopeAsync();
        await using (db)
        {
            var tag = new Tag
            {
                Name = $"GroupTag_{Guid.NewGuid():N}",
                OwnerId = authorId,
                IsPrivate = true,
                TargetUserGroupId = groupId
            };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            // 1. 未ログイン、見知らぬ人、およびグループ外のフォロワーには非表示
            Assert.False(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, null).AnyAsync());
            Assert.False(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, strangerId).AnyAsync());
            Assert.False(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, followerId).AnyAsync());

            Assert.Null(await tagDetailProvider.GetTagDetailAsync(tag.Id, null));
            Assert.Null(await tagDetailProvider.GetTagDetailAsync(tag.Id, strangerId));
            Assert.Null(await tagDetailProvider.GetTagDetailAsync(tag.Id, followerId));

            // 2. 作成者本人およびグループメンバーには表示
            Assert.True(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, authorId).AnyAsync());
            Assert.True(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, groupMemberId).AnyAsync());

            Assert.NotNull(await tagDetailProvider.GetTagDetailAsync(tag.Id, authorId));
            Assert.NotNull(await tagDetailProvider.GetTagDetailAsync(tag.Id, groupMemberId));
        }
    }

    [Fact]
    public async Task AdminHiddenTag_ShouldNotBeVisibleToRegularUsers_ButVisibleToAdmin()
    {
        var (db, _, _, tagDetailProvider, _, _, _, authorId, followerId, strangerId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var adminId = $"admin_{Guid.NewGuid().ToString("N")[..8]}";
            await db.SeedUsersAsync(adminId);

            var tag = new Tag
            {
                Name = $"AdminHiddenTag_{Guid.NewGuid():N}",
                OwnerId = authorId,
                IsPrivate = false,
                IsAdminHidden = true,
                AdminHiddenAt = DateTimeOffset.UtcNow,
                AdminHiddenReason = "Violation"
            };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            // 1. WhereVisibleToUser クエリの検証 (通常ユーザー vs 管理者)
            Assert.False(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, null, isAdmin: false).AnyAsync());
            Assert.False(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, authorId, isAdmin: false).AnyAsync());
            Assert.False(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, followerId, isAdmin: false).AnyAsync());
            Assert.False(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, strangerId, isAdmin: false).AnyAsync());
            Assert.True(await db.Tags.Where(t => t.Id == tag.Id).WhereVisibleToUser(db, adminId, isAdmin: true).AnyAsync());

            // 2. TagDetailDataProvider での検証
            Assert.Null(await tagDetailProvider.GetTagDetailAsync(tag.Id, authorId, isAdmin: false));
            Assert.NotNull(await tagDetailProvider.GetTagDetailAsync(tag.Id, adminId, isAdmin: true));
        }
    }

    [Fact]
    public async Task CrossScope_PrivateTagOnPublicItem_ShouldBeMaskedForUnprivilegedUser()
    {
        var (db, _, _, _, _, _, itemListProvider, authorId, followerId, strangerId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            // パブリックなアイテム
            var item = new Item
            {
                Content = "Public post with private tag",
                OwnerId = authorId,
                IsPrivate = false
            };
            db.Items.Add(item);

            // フォロワー限定のプライベートタグ
            var privateTag = new Tag
            {
                Name = $"SecretTag_{Guid.NewGuid():N}",
                OwnerId = authorId,
                IsPrivate = true,
                TargetUserGroupId = null
            };
            // パブリックなタグ
            var publicTag = new Tag
            {
                Name = $"PublicTag_{Guid.NewGuid():N}",
                OwnerId = authorId,
                IsPrivate = false
            };
            db.Tags.AddRange(privateTag, publicTag);
            await db.SaveChangesAsync();

            // アイテムに両方のタグを付与
            db.TagRelations.Add(new TagRelation { ItemId = item.Id, TagId = privateTag.Id, OwnerId = authorId });
            db.TagRelations.Add(new TagRelation { ItemId = item.Id, TagId = publicTag.Id, OwnerId = authorId });
            await db.SaveChangesAsync();

            // 1. 未認可ユーザー（部外者・未ログイン）がアイテム一覧を取得したとき
            var strangerResult = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: strangerId);
            var retrievedItemStranger = strangerResult.Items.FirstOrDefault(i => i.Id == item.Id);
            Assert.NotNull(retrievedItemStranger);
            // パブリックタグは見え、プライベートタグはマスクされて除外されていること
            Assert.Contains(retrievedItemStranger.TagRelations, it => it.TagId == publicTag.Id);
            Assert.DoesNotContain(retrievedItemStranger.TagRelations, it => it.TagId == privateTag.Id);

            // 2. 認可ユーザー（フォロワー）がアイテム一覧を取得したとき
            var followerResult = await itemListProvider.LoadItemsAndTagsAsync([], [], currentUserId: followerId);
            var retrievedItemFollower = followerResult.Items.FirstOrDefault(i => i.Id == item.Id);
            Assert.NotNull(retrievedItemFollower);
            // フォロワーには両方のタグが見えること
            Assert.Contains(retrievedItemFollower.TagRelations, it => it.TagId == publicTag.Id);
            Assert.Contains(retrievedItemFollower.TagRelations, it => it.TagId == privateTag.Id);
        }
    }

    [Fact]
    public async Task SystemTag_AlwaysRemainsPublic()
    {
        var (db, _, _, _, _, tagCommandService, _, authorId, _, _, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var systemTag = new Tag
            {
                Name = "good",
                OwnerId = authorId,
                IsPrivate = false
            };
            db.Tags.Add(systemTag);
            await db.SaveChangesAsync();

            // システムタグをプライベートに変更しようと試みる
            await tagCommandService.UpdateTagAsync(systemTag.Id, "good", "content", isPrivate: true, targetUserGroupId: 123);

            // DBから再取得してパブリック固定されていることを確認
            var reloaded = await db.Tags.FindAsync(systemTag.Id);
            Assert.NotNull(reloaded);
            Assert.False(reloaded.IsPrivate);
            Assert.Null(reloaded.TargetUserGroupId);
        }
    }

    [Fact]
    public async Task CreateChildTag_WhenParentIsPrivate_InheritsParentScope()
    {
        var (db, _, _, _, tagTreeProvider, _, _, authorId, _, _, _, groupId) = await CreateScopeAsync();
        await using (db)
        {
            var parentTag = new Tag
            {
                Name = $"PrivateParent_{Guid.NewGuid():N}",
                OwnerId = authorId,
                IsPrivate = true,
                TargetUserGroupId = groupId
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            // 子タグを追加（引数で isPrivate: false を渡しても親が private なら継承されること）
            var childTag = new Tag
            {
                Name = $"ChildTag_{Guid.NewGuid():N}",
                Content = "desc",
                OwnerId = authorId,
                ParentTagId = parentTag.Id,
                IsPrivate = false,
                TargetUserGroupId = null
            };
            await tagTreeProvider.AddTagAsync(childTag);

            Assert.True(childTag.IsPrivate);
            Assert.Equal(groupId, childTag.TargetUserGroupId);
        }
    }

    [Fact]
    public async Task TagSearchQueryService_GetAllTagsAsync_RespectsPrivateVisibility()
    {
        var (db, stubFactory, _, _, _, _, _, authorId, followerId, strangerId, groupMemberId, groupId) = await CreateScopeAsync();
        await using (db)
        {
            var adminId = $"admin_{Guid.NewGuid().ToString("N")[..8]}";
            await db.SeedUsersAsync(adminId);

            var embeddingMock = new Mock<ITagEmbeddingService>();
            var queryService = new TagSearchQueryService(stubFactory, embeddingMock.Object);

            var publicTag = new Tag { Name = $"Pub_{Guid.NewGuid():N}", OwnerId = authorId, IsPrivate = false };
            var followerTag = new Tag { Name = $"Fol_{Guid.NewGuid():N}", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = null };
            var groupTag = new Tag { Name = $"Grp_{Guid.NewGuid():N}", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = groupId };

            db.Tags.AddRange(publicTag, followerTag, groupTag);
            await db.SaveChangesAsync();

            // 1. オーナー本人: すべて取得
            var authorTags = await queryService.GetAllTagsAsync(authorId, isAdmin: false);
            Assert.Contains(authorTags, t => t.Id == publicTag.Id);
            Assert.Contains(authorTags, t => t.Id == followerTag.Id);
            Assert.Contains(authorTags, t => t.Id == groupTag.Id);

            // 2. フォロワー: パブリック + フォロワー限定 (グループ限定は不可視)
            var followerTags = await queryService.GetAllTagsAsync(followerId, isAdmin: false);
            Assert.Contains(followerTags, t => t.Id == publicTag.Id);
            Assert.Contains(followerTags, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(followerTags, t => t.Id == groupTag.Id);

            // 3. グループメンバー: パブリック + グループ限定 (フォロワー限定は不可視)
            var memberTags = await queryService.GetAllTagsAsync(groupMemberId, isAdmin: false);
            Assert.Contains(memberTags, t => t.Id == publicTag.Id);
            Assert.DoesNotContain(memberTags, t => t.Id == followerTag.Id);
            Assert.Contains(memberTags, t => t.Id == groupTag.Id);

            // 4. 部外者: パブリックのみ
            var strangerTags = await queryService.GetAllTagsAsync(strangerId, isAdmin: false);
            Assert.Contains(strangerTags, t => t.Id == publicTag.Id);
            Assert.DoesNotContain(strangerTags, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(strangerTags, t => t.Id == groupTag.Id);

            // 5. 未ログイン: パブリックのみ
            var anonTags = await queryService.GetAllTagsAsync(null, isAdmin: false);
            Assert.Contains(anonTags, t => t.Id == publicTag.Id);
            Assert.DoesNotContain(anonTags, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(anonTags, t => t.Id == groupTag.Id);

            // 6. 管理者: すべて取得
            var adminTags = await queryService.GetAllTagsAsync(adminId, isAdmin: true);
            Assert.Contains(adminTags, t => t.Id == publicTag.Id);
            Assert.Contains(adminTags, t => t.Id == followerTag.Id);
            Assert.Contains(adminTags, t => t.Id == groupTag.Id);
        }
    }

    [Fact]
    public async Task TagSearchQueryService_SearchTagsAsync_RespectsPrivateVisibility()
    {
        var (db, stubFactory, _, _, _, _, _, authorId, followerId, strangerId, groupMemberId, groupId) = await CreateScopeAsync();
        await using (db)
        {
            var adminId = $"admin_{Guid.NewGuid().ToString("N")[..8]}";
            await db.SeedUsersAsync(adminId);

            var keyword = $"Target_{Guid.NewGuid().ToString("N")[..8]}";
            var embeddingMock = new Mock<ITagEmbeddingService>();
            embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>()))
                .ReturnsAsync(new ReadOnlyMemory<float>(new float[] { 0.1f }));

            var queryService = new TagSearchQueryService(stubFactory, embeddingMock.Object);

            var followerTag = new Tag { Name = $"Fol_{keyword}", Content = "Content", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = null };
            var groupTag = new Tag { Name = $"Grp_{keyword}", Content = "Content", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = groupId };

            db.Tags.AddRange(followerTag, groupTag);
            await db.SaveChangesAsync();

            // フォロワーによる検索: フォロワー限定タグのみヒット
            var followerResult = await queryService.SearchTagsAsync(keyword, followerId, isAdmin: false);
            Assert.Contains(followerResult, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(followerResult, t => t.Id == groupTag.Id);

            // グループメンバーによる検索: グループ限定タグのみヒット
            var memberResult = await queryService.SearchTagsAsync(keyword, groupMemberId, isAdmin: false);
            Assert.DoesNotContain(memberResult, t => t.Id == followerTag.Id);
            Assert.Contains(memberResult, t => t.Id == groupTag.Id);

            // 部外者・未ログインによる検索: どちらもヒットしない
            var strangerResult = await queryService.SearchTagsAsync(keyword, strangerId, isAdmin: false);
            Assert.DoesNotContain(strangerResult, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(strangerResult, t => t.Id == groupTag.Id);

            var anonResult = await queryService.SearchTagsAsync(keyword, null, isAdmin: false);
            Assert.DoesNotContain(anonResult, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(anonResult, t => t.Id == groupTag.Id);

            // 管理者による検索: 両方ヒット
            var adminResult = await queryService.SearchTagsAsync(keyword, adminId, isAdmin: true);
            Assert.Contains(adminResult, t => t.Id == followerTag.Id);
            Assert.Contains(adminResult, t => t.Id == groupTag.Id);
        }
    }

    [Fact]
    public async Task TagSearchQueryService_FindTagByNameAsync_RespectsPrivateVisibility()
    {
        var (db, stubFactory, _, _, _, _, _, authorId, followerId, strangerId, groupMemberId, groupId) = await CreateScopeAsync();
        await using (db)
        {
            var adminId = $"admin_{Guid.NewGuid().ToString("N")[..8]}";
            await db.SeedUsersAsync(adminId);

            var embeddingMock = new Mock<ITagEmbeddingService>();
            var queryService = new TagSearchQueryService(stubFactory, embeddingMock.Object);

            var secretFollowerTagName = $"SecretFol_{Guid.NewGuid():N}";
            var secretGroupTagName = $"SecretGrp_{Guid.NewGuid():N}";

            var followerTag = new Tag { Name = secretFollowerTagName, OwnerId = authorId, IsPrivate = true, TargetUserGroupId = null };
            var groupTag = new Tag { Name = secretGroupTagName, OwnerId = authorId, IsPrivate = true, TargetUserGroupId = groupId };

            db.Tags.AddRange(followerTag, groupTag);
            await db.SaveChangesAsync();

            // フォロワー限定タグの名前検索
            Assert.NotNull(await queryService.FindTagByNameAsync(secretFollowerTagName, authorId, isAdmin: false));
            Assert.NotNull(await queryService.FindTagByNameAsync(secretFollowerTagName, followerId, isAdmin: false));
            Assert.Null(await queryService.FindTagByNameAsync(secretFollowerTagName, strangerId, isAdmin: false));
            Assert.Null(await queryService.FindTagByNameAsync(secretFollowerTagName, null, isAdmin: false));
            Assert.NotNull(await queryService.FindTagByNameAsync(secretFollowerTagName, adminId, isAdmin: true));

            // グループ限定タグの名前検索
            Assert.NotNull(await queryService.FindTagByNameAsync(secretGroupTagName, authorId, isAdmin: false));
            Assert.NotNull(await queryService.FindTagByNameAsync(secretGroupTagName, groupMemberId, isAdmin: false));
            Assert.Null(await queryService.FindTagByNameAsync(secretGroupTagName, followerId, isAdmin: false));
            Assert.Null(await queryService.FindTagByNameAsync(secretGroupTagName, strangerId, isAdmin: false));
            Assert.NotNull(await queryService.FindTagByNameAsync(secretGroupTagName, adminId, isAdmin: true));
        }
    }

    [Fact]
    public async Task TagTreeDataProvider_LoadTagsAsync_RespectsPrivateVisibility()
    {
        var (db, _, _, _, tagTreeProvider, _, _, authorId, followerId, strangerId, groupMemberId, groupId) = await CreateScopeAsync();
        await using (db)
        {
            var adminId = $"admin_{Guid.NewGuid().ToString("N")[..8]}";
            await db.SeedUsersAsync(adminId);

            var followerTag = new Tag { Name = $"TreeFol_{Guid.NewGuid():N}", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = null };
            var groupTag = new Tag { Name = $"TreeGrp_{Guid.NewGuid():N}", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = groupId };

            db.Tags.AddRange(followerTag, groupTag);
            await db.SaveChangesAsync();

            // 1. オーナー: 両方取得可能
            var authorTree = await tagTreeProvider.LoadTagsAsync(authorId, isAdmin: false);
            Assert.Contains(authorTree, t => t.Id == followerTag.Id);
            Assert.Contains(authorTree, t => t.Id == groupTag.Id);

            // 2. フォロワー: フォロワー限定のみ取得可能
            var followerTree = await tagTreeProvider.LoadTagsAsync(followerId, isAdmin: false);
            Assert.Contains(followerTree, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(followerTree, t => t.Id == groupTag.Id);

            // 3. グループメンバー: グループ限定のみ取得可能
            var memberTree = await tagTreeProvider.LoadTagsAsync(groupMemberId, isAdmin: false);
            Assert.DoesNotContain(memberTree, t => t.Id == followerTag.Id);
            Assert.Contains(memberTree, t => t.Id == groupTag.Id);

            // 4. 部外者・未ログイン: 不可視
            var strangerTree = await tagTreeProvider.LoadTagsAsync(strangerId, isAdmin: false);
            Assert.DoesNotContain(strangerTree, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(strangerTree, t => t.Id == groupTag.Id);

            var anonTree = await tagTreeProvider.LoadTagsAsync(null, isAdmin: false);
            Assert.DoesNotContain(anonTree, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(anonTree, t => t.Id == groupTag.Id);

            // 5. 管理者: 両方取得可能
            var adminTree = await tagTreeProvider.LoadTagsAsync(adminId, isAdmin: true);
            Assert.Contains(adminTree, t => t.Id == followerTag.Id);
            Assert.Contains(adminTree, t => t.Id == groupTag.Id);
        }
    }

    [Fact]
    public async Task TagVisibilityExtensions_IsTagVisibleToUserAsync_EvaluatesCorrectly()
    {
        var (db, _, _, _, _, _, _, authorId, followerId, strangerId, groupMemberId, groupId) = await CreateScopeAsync();
        await using (db)
        {
            var adminId = $"admin_{Guid.NewGuid().ToString("N")[..8]}";
            await db.SeedUsersAsync(adminId);

            var followerTag = new Tag { Name = "FolTag", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = null };
            var groupTag = new Tag { Name = "GrpTag", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = groupId };
            var adminHiddenTag = new Tag { Name = "AdminHidden", OwnerId = authorId, IsPrivate = false, IsAdminHidden = true };

            db.Tags.AddRange(followerTag, groupTag, adminHiddenTag);
            await db.SaveChangesAsync();

            // フォロワー限定タグの判定
            Assert.True(await followerTag.IsTagVisibleToUserAsync(db, authorId, isAdmin: false));
            Assert.True(await followerTag.IsTagVisibleToUserAsync(db, followerId, isAdmin: false));
            Assert.False(await followerTag.IsTagVisibleToUserAsync(db, strangerId, isAdmin: false));
            Assert.False(await followerTag.IsTagVisibleToUserAsync(db, null, isAdmin: false));
            Assert.True(await followerTag.IsTagVisibleToUserAsync(db, adminId, isAdmin: true));

            // グループ限定タグの判定
            Assert.True(await groupTag.IsTagVisibleToUserAsync(db, authorId, isAdmin: false));
            Assert.True(await groupTag.IsTagVisibleToUserAsync(db, groupMemberId, isAdmin: false));
            Assert.False(await groupTag.IsTagVisibleToUserAsync(db, followerId, isAdmin: false));
            Assert.False(await groupTag.IsTagVisibleToUserAsync(db, strangerId, isAdmin: false));
            Assert.False(await groupTag.IsTagVisibleToUserAsync(db, null, isAdmin: false));
            Assert.True(await groupTag.IsTagVisibleToUserAsync(db, adminId, isAdmin: true));

            // 管理者非公開タグの判定
            Assert.False(await adminHiddenTag.IsTagVisibleToUserAsync(db, authorId, isAdmin: false));
            Assert.False(await adminHiddenTag.IsTagVisibleToUserAsync(db, strangerId, isAdmin: false));
            Assert.True(await adminHiddenTag.IsTagVisibleToUserAsync(db, adminId, isAdmin: true));
        }
    }

    [Fact]
    public async Task TagTableDataProvider_GetAllTagsAsync_RespectsGroupAndFollowerVisibility()
    {
        var (db, _, tagTableProvider, _, _, _, _, authorId, followerId, strangerId, groupMemberId, groupId) = await CreateScopeAsync();
        await using (db)
        {
            var adminId = $"admin_{Guid.NewGuid().ToString("N")[..8]}";
            await db.SeedUsersAsync(adminId);

            var followerTag = new Tag { Name = $"TableFol_{Guid.NewGuid():N}", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = null };
            var groupTag = new Tag { Name = $"TableGrp_{Guid.NewGuid():N}", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = groupId };

            db.Tags.AddRange(followerTag, groupTag);
            await db.SaveChangesAsync();

            // 1. オーナー: 両方取得
            var authorTags = await tagTableProvider.GetAllTagsAsync(authorId, isAdmin: false);
            Assert.Contains(authorTags, t => t.Id == followerTag.Id);
            Assert.Contains(authorTags, t => t.Id == groupTag.Id);

            // 2. フォロワー: フォロワー限定のみ
            var followerTags = await tagTableProvider.GetAllTagsAsync(followerId, isAdmin: false);
            Assert.Contains(followerTags, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(followerTags, t => t.Id == groupTag.Id);

            // 3. グループメンバー: グループ限定のみ
            var memberTags = await tagTableProvider.GetAllTagsAsync(groupMemberId, isAdmin: false);
            Assert.DoesNotContain(memberTags, t => t.Id == followerTag.Id);
            Assert.Contains(memberTags, t => t.Id == groupTag.Id);

            // 4. 部外者・未ログイン: どちらも不可視
            var strangerTags = await tagTableProvider.GetAllTagsAsync(strangerId, isAdmin: false);
            Assert.DoesNotContain(strangerTags, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(strangerTags, t => t.Id == groupTag.Id);

            var anonTags = await tagTableProvider.GetAllTagsAsync(null, isAdmin: false);
            Assert.DoesNotContain(anonTags, t => t.Id == followerTag.Id);
            Assert.DoesNotContain(anonTags, t => t.Id == groupTag.Id);

            // 5. 管理者: 両方取得
            var adminTags = await tagTableProvider.GetAllTagsAsync(adminId, isAdmin: true);
            Assert.Contains(adminTags, t => t.Id == followerTag.Id);
            Assert.Contains(adminTags, t => t.Id == groupTag.Id);
        }
    }

    [Fact]
    public async Task FilterVisibleTagRelationsAsync_MasksPrivateTagsForStrangersAndExposesToAuthorized()
    {
        var (db, _, _, _, _, _, _, authorId, followerId, strangerId, groupMemberId, groupId) = await CreateScopeAsync();
        await using (db)
        {
            var adminId = $"admin_{Guid.NewGuid().ToString("N")[..8]}";
            await db.SeedUsersAsync(adminId);

            var followerTag = new Tag { Name = "FolMask", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = null };
            var groupTag = new Tag { Name = "GrpMask", OwnerId = authorId, IsPrivate = true, TargetUserGroupId = groupId };
            var publicTag = new Tag { Name = "PubMask", OwnerId = authorId, IsPrivate = false };

            db.Tags.AddRange(followerTag, groupTag, publicTag);
            await db.SaveChangesAsync();

            Item CreateTestItem() => new()
            {
                Content = "Post",
                OwnerId = authorId,
                TagRelations =
                [
                    new TagRelation { TagId = followerTag.Id, Tag = followerTag, OwnerId = authorId },
                    new TagRelation { TagId = groupTag.Id, Tag = groupTag, OwnerId = authorId },
                    new TagRelation { TagId = publicTag.Id, Tag = publicTag, OwnerId = authorId }
                ]
            };

            // 1. 部外者: パブリックタグのリレーションのみ残り、フォロワー限定・グループ限定は除外
            var strangerItem = CreateTestItem();
            await new[] { strangerItem }.FilterVisibleTagRelationsAsync(db, strangerId, isAdmin: false);
            Assert.Single(strangerItem.TagRelations);
            Assert.Equal(publicTag.Id, strangerItem.TagRelations.First().TagId);

            // 2. フォロワー: パブリック + フォロワー限定が残り、グループ限定は除外
            var followerItem = CreateTestItem();
            await new[] { followerItem }.FilterVisibleTagRelationsAsync(db, followerId, isAdmin: false);
            Assert.Equal(2, followerItem.TagRelations.Count);
            Assert.Contains(followerItem.TagRelations, tr => tr.TagId == followerTag.Id);
            Assert.Contains(followerItem.TagRelations, tr => tr.TagId == publicTag.Id);
            Assert.DoesNotContain(followerItem.TagRelations, tr => tr.TagId == groupTag.Id);

            // 3. グループメンバー: パブリック + グループ限定が残り、フォロワー限定は除外
            var memberItem = CreateTestItem();
            await new[] { memberItem }.FilterVisibleTagRelationsAsync(db, groupMemberId, isAdmin: false);
            Assert.Equal(2, memberItem.TagRelations.Count);
            Assert.Contains(memberItem.TagRelations, tr => tr.TagId == groupTag.Id);
            Assert.Contains(memberItem.TagRelations, tr => tr.TagId == publicTag.Id);
            Assert.DoesNotContain(memberItem.TagRelations, tr => tr.TagId == followerTag.Id);

            // 4. 管理者: フィルタされずに全リレーションが保持される
            var adminItem = CreateTestItem();
            await new[] { adminItem }.FilterVisibleTagRelationsAsync(db, adminId, isAdmin: true);
            Assert.Equal(3, adminItem.TagRelations.Count);
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