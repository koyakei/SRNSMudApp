#region

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
///     リンクプレビューにおける不可視タグの祖先フォールバック動作の単体・統合テスト。
///     不可視モードのタグへのリンクプレビューを要求された際、
///     ログイン中の閲覧ユーザーにとって最も近い可視な祖先タグの情報が表示されることを検証する。
/// </summary>
public class TagLinkPreviewAncestorFallbackTests : IAsyncLifetime
{
    private static int s_nextRootSegment = Random.Shared.Next(10_000_000, 90_000_000);
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(
        ApplicationDbContext db,
        TagLinkPreviewProvider provider,
        LinkPreviewService service,
        string authorId,
        string followerId,
        string strangerId,
        int rootSegment)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var rootSegment = Interlocked.Increment(ref s_nextRootSegment);
        var services = new ServiceCollection();
        _ = services.AddMsSqlDbFactory(_sharedDb.ConnectionString);
        var sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        var provider = new TagLinkPreviewProvider(dbFactory);
        var serviceLogger = NullLogger<LinkPreviewService>.Instance;
        var service = new LinkPreviewService([provider], serviceLogger);

        var db = new ApplicationDbContext(_sharedDb.Options);

        var authorId = $"author_{tid}";
        var followerId = $"follower_{tid}";
        var strangerId = $"stranger_{tid}";

        await db.SeedUsersAsync(authorId, followerId, strangerId);

        // フォロー関係: followerId が authorId をフォロー
        db.UserFollows.Add(new UserFollow
        {
            OwnerId = followerId,
            FollowedUserId = authorId
        });
        await db.SaveChangesAsync();

        return (db, provider, service, authorId, followerId, strangerId, rootSegment);
    }

    [Fact]
    public async Task GetPreviewAsync_WhenTagIsPublic_ReturnsTargetTagInfo()
    {
        var (db, provider, _, authorId, _, strangerId, rootSeg) = await CreateScopeAsync();
        await using (db)
        {
            var tag = new Tag
            {
                Name = $"PublicTag_{Guid.NewGuid():N}",
                Content = "Public tag description",
                OwnerId = authorId,
                IsPrivate = false,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/")
            };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            var uri = new Uri($"/TagDetail/{tag.Id}", UriKind.Relative);
            var result = await provider.GetPreviewAsync(uri, $"/TagDetail/{tag.Id}", strangerId, isAdmin: false);

            Assert.True(result.IsSuccess);
            Assert.Contains(tag.Name, result.Title);
            Assert.Equal("Public tag description", result.Description);
            Assert.Single(result.Tags);
            Assert.Equal(tag.Id, result.Tags[0].Id);
            Assert.Equal(tag.Name, result.Tags[0].Name);
        }
    }

    [Fact]
    public async Task GetPreviewAsync_WhenTagIsPrivate_FallsBackToNearestVisibleParentTag()
    {
        var (db, provider, _, authorId, _, strangerId, rootSeg) = await CreateScopeAsync();
        await using (db)
        {
            // 可視な親タグ
            var parentTag = new Tag
            {
                Name = $"PublicParent_{Guid.NewGuid():N}",
                Content = "Visible parent tag description",
                OwnerId = authorId,
                IsPrivate = false,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/")
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            // 不可視な子タグ（フォロワー限定）
            var childTag = new Tag
            {
                Name = $"PrivateChild_{Guid.NewGuid():N}",
                Content = "Secret child tag description",
                OwnerId = authorId,
                ParentTagId = parentTag.Id,
                IsPrivate = true,
                TargetUserGroupId = null,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/1/")
            };
            db.Tags.Add(childTag);
            await db.SaveChangesAsync();

            var uri = new Uri($"/TagDetail/{childTag.Id}", UriKind.Relative);
            // strangerId からプレビューを取得
            var result = await provider.GetPreviewAsync(uri, $"/TagDetail/{childTag.Id}", strangerId, isAdmin: false);

            // 期待結果: 子タグではなく、最も近い可視な親タグ（parentTag）の情報が表示されること
            Assert.True(result.IsSuccess);
            Assert.Equal($"/TagDetail/{childTag.Id}", result.Url); // URL はリクエスト元
            Assert.Contains(parentTag.Name, result.Title);
            Assert.DoesNotContain(childTag.Name, result.Title);
            Assert.Equal("Visible parent tag description", result.Description);
            Assert.Single(result.Tags);
            Assert.Equal(parentTag.Id, result.Tags[0].Id);
            Assert.Equal(parentTag.Name, result.Tags[0].Name);
        }
    }

    [Fact]
    public async Task GetPreviewAsync_WhenParentIsAlsoPrivate_FallsBackToGrandparentTag()
    {
        var (db, provider, _, authorId, _, strangerId, rootSeg) = await CreateScopeAsync();
        await using (db)
        {
            // 可視な祖父タグ
            var grandparentTag = new Tag
            {
                Name = $"PublicGrandparent_{Guid.NewGuid():N}",
                Content = "Grandparent description",
                OwnerId = authorId,
                IsPrivate = false,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/")
            };
            db.Tags.Add(grandparentTag);
            await db.SaveChangesAsync();

            // 不可視な親タグ
            var parentTag = new Tag
            {
                Name = $"PrivateParent_{Guid.NewGuid():N}",
                Content = "Private parent description",
                OwnerId = authorId,
                ParentTagId = grandparentTag.Id,
                IsPrivate = true,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/1/")
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            // 不可視な子タグ
            var childTag = new Tag
            {
                Name = $"PrivateChild_{Guid.NewGuid():N}",
                Content = "Private child description",
                OwnerId = authorId,
                ParentTagId = parentTag.Id,
                IsPrivate = true,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/1/1/")
            };
            db.Tags.Add(childTag);
            await db.SaveChangesAsync();

            var uri = new Uri($"/TagDetail/{childTag.Id}", UriKind.Relative);
            // strangerId からプレビューを取得
            var result = await provider.GetPreviewAsync(uri, $"/TagDetail/{childTag.Id}", strangerId, isAdmin: false);

            // 期待結果: 親タグも不可視のため、祖父タグ（grandparentTag）の情報が表示されること
            Assert.True(result.IsSuccess);
            Assert.Contains(grandparentTag.Name, result.Title);
            Assert.DoesNotContain(parentTag.Name, result.Title);
            Assert.DoesNotContain(childTag.Name, result.Title);
            Assert.Equal("Grandparent description", result.Description);
            Assert.Single(result.Tags);
            Assert.Equal(grandparentTag.Id, result.Tags[0].Id);
        }
    }

    [Fact]
    public async Task GetPreviewAsync_WhenUserHasAccessToPrivateTag_ReturnsTargetTagInfoDirectly()
    {
        var (db, provider, _, authorId, followerId, _, rootSeg) = await CreateScopeAsync();
        await using (db)
        {
            var parentTag = new Tag
            {
                Name = $"Parent_{Guid.NewGuid():N}",
                OwnerId = authorId,
                IsPrivate = false,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/")
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            // フォロワー限定タグ
            var childTag = new Tag
            {
                Name = $"FollowerChild_{Guid.NewGuid():N}",
                Content = "Follower child content",
                OwnerId = authorId,
                ParentTagId = parentTag.Id,
                IsPrivate = true,
                TargetUserGroupId = null,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/1/")
            };
            db.Tags.Add(childTag);
            await db.SaveChangesAsync();

            var uri = new Uri($"/TagDetail/{childTag.Id}", UriKind.Relative);
            // フォロワーがプレビューを取得
            var result = await provider.GetPreviewAsync(uri, $"/TagDetail/{childTag.Id}", followerId, isAdmin: false);

            // フォロワーには可視なため、祖先ではなく childTag 自身の情報が表示されること
            Assert.True(result.IsSuccess);
            Assert.Contains(childTag.Name, result.Title);
            Assert.Equal("Follower child content", result.Description);
            Assert.Single(result.Tags);
            Assert.Equal(childTag.Id, result.Tags[0].Id);
        }
    }

    [Fact]
    public async Task GetPreviewAsync_WhenUserIsAdmin_ReturnsPrivateTagInfoDirectly()
    {
        var (db, provider, _, authorId, _, _, rootSeg) = await CreateScopeAsync();
        await using (db)
        {
            var adminId = $"admin_{Guid.NewGuid():N}";
            await db.SeedUsersAsync(adminId);

            var parentTag = new Tag
            {
                Name = $"Parent_{Guid.NewGuid():N}",
                OwnerId = authorId,
                IsPrivate = false,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/")
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            var childTag = new Tag
            {
                Name = $"HiddenChild_{Guid.NewGuid():N}",
                Content = "Hidden child content",
                OwnerId = authorId,
                ParentTagId = parentTag.Id,
                IsPrivate = true,
                IsAdminHidden = true,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/1/")
            };
            db.Tags.Add(childTag);
            await db.SaveChangesAsync();

            var uri = new Uri($"/TagDetail/{childTag.Id}", UriKind.Relative);
            // 管理者がプレビューを取得
            var result = await provider.GetPreviewAsync(uri, $"/TagDetail/{childTag.Id}", adminId, isAdmin: true);

            // 管理者には可視なため、childTag 自身の情報が表示されること
            Assert.True(result.IsSuccess);
            Assert.Contains(childTag.Name, result.Title);
            Assert.Equal("Hidden child content", result.Description);
        }
    }

    [Fact]
    public async Task LinkPreviewService_SeparatesCacheByViewerContext()
    {
        var (db, _, service, authorId, followerId, strangerId, rootSeg) = await CreateScopeAsync();
        await using (db)
        {
            var parentTag = new Tag
            {
                Name = $"PublicParent_{Guid.NewGuid():N}",
                OwnerId = authorId,
                IsPrivate = false,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/")
            };
            db.Tags.Add(parentTag);
            await db.SaveChangesAsync();

            var childTag = new Tag
            {
                Name = $"FollowerChild_{Guid.NewGuid():N}",
                Content = "Secret content",
                OwnerId = authorId,
                ParentTagId = parentTag.Id,
                IsPrivate = true,
                TargetUserGroupId = null,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/1/")
            };
            db.Tags.Add(childTag);
            await db.SaveChangesAsync();

            var url = $"/TagDetail/{childTag.Id}";

            // 1. フォロワーがプレビューを取得 -> childTag が返る
            var followerResult = await service.GetPreviewAsync(url, followerId, isAdmin: false);
            Assert.True(followerResult.IsSuccess);
            Assert.Contains(childTag.Name, followerResult.Title);

            // 2. 第三者がプレビューを取得 -> キャッシュに汚染されず、parentTag が返る
            var strangerResult = await service.GetPreviewAsync(url, strangerId, isAdmin: false);
            Assert.True(strangerResult.IsSuccess);
            Assert.Contains(parentTag.Name, strangerResult.Title);
            Assert.DoesNotContain(childTag.Name, strangerResult.Title);
        }
    }

    [Fact]
    public async Task GetPreviewAsync_WhenRedirectedToAncestor_PreviewDisplaysRedirectedTagNameAndNotSecretName()
    {
        var (db, provider, _, authorId, _, strangerId, rootSeg) = await CreateScopeAsync();
        await using (db)
        {
            var author = await db.Users.FindAsync(authorId);
            var authorName = author!.UserName!;

            var visibleAncestorTag = new Tag
            {
                Name = $"PublicCategory_{Guid.NewGuid():N}",
                Content = "Visible category description",
                OwnerId = authorId,
                IsPrivate = false,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/")
            };
            db.Tags.Add(visibleAncestorTag);
            await db.SaveChangesAsync();

            var secretChildTag = new Tag
            {
                Name = $"TopSecretProject_{Guid.NewGuid():N}",
                Content = "Classified secret content",
                OwnerId = authorId,
                ParentTagId = visibleAncestorTag.Id,
                IsPrivate = true,
                TargetUserGroupId = null,
                Node = HierarchyId.Parse($"/999999/999999/{rootSeg}/1/")
            };
            db.Tags.Add(secretChildTag);
            await db.SaveChangesAsync();

            var uri = new Uri($"/TagDetail/{secretChildTag.Id}", UriKind.Relative);
            // 権限のない第三者として不可視タグのリンクプレビューを要求
            var result = await provider.GetPreviewAsync(uri, $"/TagDetail/{secretChildTag.Id}", strangerId, isAdmin: false);

            Assert.True(result.IsSuccess);

            // 1. 表示テキスト (DisplayText) がリダイレクトされた祖先タグ名とオーナー名になっていること
            Assert.Equal($"{visibleAncestorTag.Name}:{authorName}", result.DisplayText);
            Assert.Contains(visibleAncestorTag.Name, result.DisplayText);
            Assert.DoesNotContain(secretChildTag.Name, result.DisplayText);

            // 2. タイトル (Title) がリダイレクトされた祖先タグ名を含み、不可視タグ名を含まないこと
            Assert.Equal($"Tag: {visibleAncestorTag.Name} ({authorName})", result.Title);
            Assert.Contains(visibleAncestorTag.Name, result.Title);
            Assert.DoesNotContain(secretChildTag.Name, result.Title);

            // 3. 説明文 (Description) もリダイレクトされた祖先タグの内容であること
            Assert.Equal("Visible category description", result.Description);
            Assert.DoesNotContain("Classified secret", result.Description);

            // 4. Tags リストのタグ情報もリダイレクトされた祖先タグであること
            Assert.Single(result.Tags);
            Assert.Equal(visibleAncestorTag.Id, result.Tags[0].Id);
            Assert.Equal(visibleAncestorTag.Name, result.Tags[0].Name);
            Assert.Equal(authorName, result.Tags[0].OwnerName);
        }
    }
}