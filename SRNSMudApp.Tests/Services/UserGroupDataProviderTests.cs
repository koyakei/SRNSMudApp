using Microsoft.EntityFrameworkCore;

using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Services;

public class UserGroupDataProviderTests : IAsyncLifetime
{
    private MsSqlTestDatabase _sharedDb = null!;

    public async Task InitializeAsync()
    {
        _sharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ApplicationDbContext dbContext, UserGroupDataProvider provider, string tid) CreateScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var dbContext = new ApplicationDbContext(_sharedDb.Options);
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(_sharedDb.Options));

        var provider = new UserGroupDataProvider(mockDbFactory.Object);
        return (dbContext, provider, tid);
    }

    [Fact]
    public async Task CreateUserGroupAsync_ShouldCreateGroupAndAddOwnerAsFirstMember()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var ownerId = $"owner_{tid}";
            await dbContext.SeedUsersAsync(ownerId);

            var group = await provider.CreateUserGroupAsync($"Team_{tid}", "Description", ownerId);

            Assert.NotNull(group);
            Assert.True(group.Id > 0);
            Assert.Equal($"Team_{tid}", group.Name);
            Assert.Equal("Description", group.Description);
            Assert.Equal(ownerId, group.OwnerId);

            // メンバーにオーナーが登録されているか確認
            var savedGroup = await provider.GetUserGroupByIdAsync(group.Id);
            Assert.NotNull(savedGroup);
            Assert.Single(savedGroup.Members);
            Assert.Equal(ownerId, savedGroup.Members.First().UserId);
        }
    }

    [Fact]
    public async Task GetManagedGroupsAsync_ShouldReturnOnlyGroupsOwnedByUser()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner1 = $"owner1_{tid}";
            var owner2 = $"owner2_{tid}";
            await dbContext.SeedUsersAsync(owner1, owner2);

            await provider.CreateUserGroupAsync($"Group1_{tid}", null, owner1);
            await provider.CreateUserGroupAsync($"Group2_{tid}", null, owner1);
            await provider.CreateUserGroupAsync($"Group3_{tid}", null, owner2);

            var managedByOwner1 = await provider.GetManagedGroupsAsync(owner1);

            Assert.Equal(2, managedByOwner1.Count);
            Assert.All(managedByOwner1, g => Assert.Equal(owner1, g.OwnerId));
        }
    }

    [Fact]
    public async Task GetUserGroupsForUserAsync_ShouldReturnGroupsWhereUserIsOwnerOrMember()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var userA = $"userA_{tid}";
            var userB = $"userB_{tid}";
            var userC = $"userC_{tid}";
            await dbContext.SeedUsersAsync(userA, userB, userC);

            var groupA = await provider.CreateUserGroupAsync($"GroupA_{tid}", null, userA);
            var groupB = await provider.CreateUserGroupAsync($"GroupB_{tid}", null, userB);
            await provider.CreateUserGroupAsync($"GroupC_{tid}", null, userC);

            // UserA を GroupB に追加
            await provider.AddMemberAsync(groupB.Id, userA, userB);

            var userAGroups = await provider.GetUserGroupsForUserAsync(userA);

            Assert.Equal(2, userAGroups.Count);
            Assert.Contains(userAGroups, g => g.Id == groupA.Id);
            Assert.Contains(userAGroups, g => g.Id == groupB.Id);
        }
    }

    [Fact]
    public async Task UpdateUserGroupAsync_WhenUserIsOwner_ShouldUpdateNameAndDescription()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner = $"owner_{tid}";
            await dbContext.SeedUsersAsync(owner);

            var group = await provider.CreateUserGroupAsync($"InitName_{tid}", "InitDesc", owner);

            bool result = await provider.UpdateUserGroupAsync(group.Id, $"NewName_{tid}", "NewDesc", owner);
            Assert.True(result);

            var updated = await provider.GetUserGroupByIdAsync(group.Id);
            Assert.NotNull(updated);
            Assert.Equal($"NewName_{tid}", updated.Name);
            Assert.Equal("NewDesc", updated.Description);
        }
    }

    [Fact]
    public async Task UpdateUserGroupAsync_WhenUserIsNotOwner_ShouldReturnFalse()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner = $"owner_{tid}";
            var other = $"other_{tid}";
            await dbContext.SeedUsersAsync(owner, other);

            var group = await provider.CreateUserGroupAsync($"Group_{tid}", null, owner);

            bool result = await provider.UpdateUserGroupAsync(group.Id, "HackedName", null, other);
            Assert.False(result);
        }
    }

    [Fact]
    public async Task AddMemberAsync_WhenUserIsOwner_ShouldAddMember()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner = $"owner_{tid}";
            var member = $"member_{tid}";
            await dbContext.SeedUsersAsync(owner, member);

            var group = await provider.CreateUserGroupAsync($"Group_{tid}", null, owner);

            bool added = await provider.AddMemberAsync(group.Id, member, owner);
            Assert.True(added);

            var saved = await provider.GetUserGroupByIdAsync(group.Id);
            Assert.NotNull(saved);
            Assert.Equal(2, saved.Members.Count);
            Assert.Contains(saved.Members, m => m.UserId == member);
        }
    }

    [Fact]
    public async Task AddMemberAsync_WhenAlreadyMember_ShouldReturnTrueWithoutDuplicating()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner = $"owner_{tid}";
            var member = $"member_{tid}";
            await dbContext.SeedUsersAsync(owner, member);

            var group = await provider.CreateUserGroupAsync($"Group_{tid}", null, owner);
            await provider.AddMemberAsync(group.Id, member, owner);

            // もう一度追加
            bool addedAgain = await provider.AddMemberAsync(group.Id, member, owner);
            Assert.True(addedAgain);

            var saved = await provider.GetUserGroupByIdAsync(group.Id);
            Assert.NotNull(saved);
            Assert.Equal(2, saved.Members.Count);
        }
    }

    [Fact]
    public async Task AddMemberAsync_WhenUserIsNotOwner_ShouldReturnFalse()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner = $"owner_{tid}";
            var other = $"other_{tid}";
            var member = $"member_{tid}";
            await dbContext.SeedUsersAsync(owner, other, member);

            var group = await provider.CreateUserGroupAsync($"Group_{tid}", null, owner);

            bool added = await provider.AddMemberAsync(group.Id, member, other);
            Assert.False(added);
        }
    }

    [Fact]
    public async Task RemoveMemberAsync_WhenOwnerRemovesMember_ShouldRemove()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner = $"owner_{tid}";
            var member = $"member_{tid}";
            await dbContext.SeedUsersAsync(owner, member);

            var group = await provider.CreateUserGroupAsync($"Group_{tid}", null, owner);
            await provider.AddMemberAsync(group.Id, member, owner);

            bool removed = await provider.RemoveMemberAsync(group.Id, member, owner);
            Assert.True(removed);

            var saved = await provider.GetUserGroupByIdAsync(group.Id);
            Assert.NotNull(saved);
            Assert.Single(saved.Members);
            Assert.DoesNotContain(saved.Members, m => m.UserId == member);
        }
    }

    [Fact]
    public async Task RemoveMemberAsync_WhenMemberRemovesSelf_ShouldRemove()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner = $"owner_{tid}";
            var member = $"member_{tid}";
            await dbContext.SeedUsersAsync(owner, member);

            var group = await provider.CreateUserGroupAsync($"Group_{tid}", null, owner);
            await provider.AddMemberAsync(group.Id, member, owner);

            // メンバー本人が脱退
            bool removed = await provider.RemoveMemberAsync(group.Id, member, member);
            Assert.True(removed);

            var saved = await provider.GetUserGroupByIdAsync(group.Id);
            Assert.NotNull(saved);
            Assert.Single(saved.Members);
        }
    }

    [Fact]
    public async Task RemoveMemberAsync_WhenTryingToRemoveOwner_ShouldReturnFalse()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner = $"owner_{tid}";
            await dbContext.SeedUsersAsync(owner);

            var group = await provider.CreateUserGroupAsync($"Group_{tid}", null, owner);

            // オーナー本人の脱退・削除は不可
            bool removed = await provider.RemoveMemberAsync(group.Id, owner, owner);
            Assert.False(removed);
        }
    }

    [Fact]
    public async Task DeleteUserGroupAsync_WhenUserIsOwner_ShouldDeleteGroup()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner = $"owner_{tid}";
            await dbContext.SeedUsersAsync(owner);

            var group = await provider.CreateUserGroupAsync($"Group_{tid}", null, owner);

            bool deleted = await provider.DeleteUserGroupAsync(group.Id, owner);
            Assert.True(deleted);

            var saved = await provider.GetUserGroupByIdAsync(group.Id);
            Assert.Null(saved);
        }
    }

    [Fact]
    public async Task DeleteUserGroupAsync_WhenUserIsNotOwner_ShouldReturnFalse()
    {
        var (dbContext, provider, tid) = CreateScope();
        await using (dbContext)
        {
            var owner = $"owner_{tid}";
            var other = $"other_{tid}";
            await dbContext.SeedUsersAsync(owner, other);

            var group = await provider.CreateUserGroupAsync($"Group_{tid}", null, owner);

            bool deleted = await provider.DeleteUserGroupAsync(group.Id, other);
            Assert.False(deleted);
        }
    }
}