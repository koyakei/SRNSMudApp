// Services/UserDataProviderFollowTests.cs
#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     <see cref="UserDataProvider" /> のユーザーフォロー機能に関する単体テスト。
/// </summary>
public class UserDataProviderFollowTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ApplicationDbContext db, UserDataProvider sut, string userAId, string userBId, string userCId)> CreateScopeAsync()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var db = new ApplicationDbContext(_sharedDb.Options);
        var stubFactory = new DbContextFactoryStub(_sharedDb.Options);
        var sut = new UserDataProvider(stubFactory);

        var userAId = $"userA_{tid}";
        var userBId = $"userB_{tid}";
        var userCId = $"userC_{tid}";
        await db.SeedUsersAsync(userAId, userBId, userCId);

        return (db, sut, userAId, userBId, userCId);
    }

    [Fact]
    public async Task ToggleFollowUserAsync_WhenNotFollowing_ShouldFollowAndReturnTrue()
    {
        var (db, sut, userAId, userBId, _) = await CreateScopeAsync();
        await using (db)
        {
            var result = await sut.ToggleFollowUserAsync(userAId, userBId);

            Assert.True(result);
            Assert.True(await sut.IsFollowingUserAsync(userAId, userBId));
            Assert.Equal(1, await sut.GetFollowingCountAsync(userAId));
            Assert.Equal(1, await sut.GetFollowersCountAsync(userBId));
        }
    }

    [Fact]
    public async Task ToggleFollowUserAsync_WhenAlreadyFollowing_ShouldUnfollowAndReturnFalse()
    {
        var (db, sut, userAId, userBId, _) = await CreateScopeAsync();
        await using (db)
        {
            // フォロー実行
            var followResult = await sut.ToggleFollowUserAsync(userAId, userBId);
            Assert.True(followResult);

            // 再度実行でフォロー解除
            var unfollowResult = await sut.ToggleFollowUserAsync(userAId, userBId);
            Assert.False(unfollowResult);

            Assert.False(await sut.IsFollowingUserAsync(userAId, userBId));
            Assert.Equal(0, await sut.GetFollowingCountAsync(userAId));
            Assert.Equal(0, await sut.GetFollowersCountAsync(userBId));
        }
    }

    [Fact]
    public async Task ToggleFollowUserAsync_WhenFollowingSelf_ShouldReturnFalse()
    {
        var (db, sut, userAId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var result = await sut.ToggleFollowUserAsync(userAId, userAId);

            Assert.False(result);
            Assert.Equal(0, await sut.GetFollowingCountAsync(userAId));
        }
    }

    [Fact]
    public async Task ToggleFollowUserAsync_WhenTargetUserDoesNotExist_ShouldReturnFalse()
    {
        var (db, sut, userAId, _, _) = await CreateScopeAsync();
        await using (db)
        {
            var result = await sut.ToggleFollowUserAsync(userAId, "non_existent_user_id");

            Assert.False(result);
            Assert.Equal(0, await sut.GetFollowingCountAsync(userAId));
        }
    }

    [Fact]
    public async Task GetUserDetailAsync_ShouldReturnCorrectFollowStateAndLists()
    {
        var (db, sut, userAId, userBId, userCId) = await CreateScopeAsync();
        await using (db)
        {
            // UserA と UserC が UserB をフォロー
            _ = await sut.ToggleFollowUserAsync(userAId, userBId);
            _ = await sut.ToggleFollowUserAsync(userCId, userBId);

            // UserB が UserC をフォロー
            _ = await sut.ToggleFollowUserAsync(userBId, userCId);

            // UserA の視点で UserB の詳細を取得
            UserDetailPageData userBDetailForA = await sut.GetUserDetailAsync(userBId, userAId);
            Assert.NotNull(userBDetailForA.User);
            Assert.True(userBDetailForA.IsFollowing);
            Assert.Equal(2, userBDetailForA.FollowersCount);
            Assert.Equal(1, userBDetailForA.FollowingCount);
            Assert.NotNull(userBDetailForA.FollowerUsers);
            Assert.Equal(2, userBDetailForA.FollowerUsers.Count);
            Assert.Contains(userBDetailForA.FollowerUsers, u => u.Id == userAId);
            Assert.Contains(userBDetailForA.FollowerUsers, u => u.Id == userCId);
            Assert.NotNull(userBDetailForA.FollowingUsers);
            Assert.Single(userBDetailForA.FollowingUsers);
            Assert.Equal(userCId, userBDetailForA.FollowingUsers[0].Id);

            // 未ログイン（null）の視点で UserB の詳細を取得
            UserDetailPageData userBDetailAnonymous = await sut.GetUserDetailAsync(userBId, null);
            Assert.False(userBDetailAnonymous.IsFollowing);
            Assert.Equal(2, userBDetailAnonymous.FollowersCount);
            Assert.Equal(1, userBDetailAnonymous.FollowingCount);
        }
    }

    [Fact]
    public async Task GetFollowingUsersAsync_And_GetFollowerUsersAsync_ShouldReturnExpectedUsers()
    {
        var (db, sut, userAId, userBId, userCId) = await CreateScopeAsync();
        await using (db)
        {
            _ = await sut.ToggleFollowUserAsync(userAId, userBId);
            _ = await sut.ToggleFollowUserAsync(userAId, userCId);

            var followings = await sut.GetFollowingUsersAsync(userAId);
            Assert.Equal(2, followings.Count);
            Assert.Contains(followings, u => u.Id == userBId);
            Assert.Contains(followings, u => u.Id == userCId);

            var followersB = await sut.GetFollowerUsersAsync(userBId);
            Assert.Single(followersB);
            Assert.Equal(userAId, followersB[0].Id);
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