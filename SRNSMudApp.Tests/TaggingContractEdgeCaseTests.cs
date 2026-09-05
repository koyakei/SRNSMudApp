using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Tests;

public class TaggingContractEdgeCaseTests : TaggingContractTestBase
{
    [Fact]
    public async Task AcceptContractAsync_ShouldThrow_WhenUserIsNotOwner()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var system = $"sys_{tid}";
        var wrongUser = $"wrong_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, system, wrongUser);

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            TagOwnerUserId = userB,
            Status = TradeStatus.Proposed,
            OwnerId = userA,
            RequestedTag = new Tag { Name = $"Tag_{tid}", OwnerId = userB },
            TargetItem = new Item { Content = $"Item_{tid}", OwnerId = system }
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await service.AcceptContractAsync(contract.Id, wrongUser);
        Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("承認できない契約です。", ex.ErrorMessage);
    }

    [Fact]
    public async Task AcceptContractAsync_ShouldThrow_WhenStatusIsNotProposed()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var system = $"sys_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, system);

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            TagOwnerUserId = userB,
            Status = TradeStatus.Canceled,
            OwnerId = userA,
            RequestedTag = new Tag { Name = $"Tag_{tid}", OwnerId = userB },
            TargetItem = new Item { Content = $"Item_{tid}", OwnerId = system }
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await service.AcceptContractAsync(contract.Id, userB);
        Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("実行・承認できない状態の契約です。", ex.ErrorMessage);
    }

    [Fact]
    public async Task CancelContractAsync_ByRequester_ShouldChangeStatusToCanceled()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var tagOwnerId = $"owner_{tid}";
        var system = $"sys_{tid}";
        await dbContext.SeedUsersAsync(requesterId, tagOwnerId, system);

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            RequesterUserId = requesterId,
            TagOwnerUserId = tagOwnerId,
            Status = TradeStatus.Proposed,
            OwnerId = requesterId,
            RequestedTag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId },
            TargetItem = new Item { Content = $"Item_{tid}", OwnerId = system }
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var cancelResult = await service.CancelContractAsync(contract.Id, requesterId);
        Assert.True(cancelResult is Success<string>, cancelResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        dbContext.ChangeTracker.Clear();
        TaggingRequestEntity? updatedContract = await dbContext.TaggingRequestEntities.FindAsync(contract.Id);
        Assert.Equal(TradeStatus.Canceled, updatedContract!.Status);
    }

    [Fact]
    public async Task CancelContractAsync_ByTagOwner_ShouldChangeStatusToCanceled()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var tagOwnerId = $"owner_{tid}";
        var system = $"sys_{tid}";
        await dbContext.SeedUsersAsync(requesterId, tagOwnerId, system);

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            RequesterUserId = requesterId,
            TagOwnerUserId = tagOwnerId,
            Status = TradeStatus.Proposed,
            OwnerId = requesterId,
            RequestedTag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId },
            TargetItem = new Item { Content = $"Item_{tid}", OwnerId = system }
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var cancelResult = await service.CancelContractAsync(contract.Id, tagOwnerId);
        Assert.True(cancelResult is Success<string>, cancelResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        // Assert
        dbContext.ChangeTracker.Clear();
        TaggingRequestEntity? updatedContract = await dbContext.TaggingRequestEntities.FindAsync(contract.Id);
        Assert.Equal(TradeStatus.Canceled, updatedContract!.Status);
    }

    [Fact]
    public async Task CancelContractAsync_ShouldThrow_WhenUserIsNeitherRequesterNorTagOwner()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var userC = $"uc_{tid}";
        var system = $"sys_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, userC, system);

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            Status = TradeStatus.Proposed,
            OwnerId = userA,
            RequestedTag = new Tag { Name = $"Tag_{tid}", OwnerId = userB },
            TargetItem = new Item { Content = $"Item_{tid}", OwnerId = system }
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await service.CancelContractAsync(contract.Id, userC);
        Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("この契約をキャンセル・拒否する権限がありません。", ex.ErrorMessage);
    }

    [Fact]
    public async Task CancelContractAsync_ShouldThrow_WhenStatusIsNotProposed()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var userA = $"ua_{tid}";
        var userB = $"ub_{tid}";
        var system = $"sys_{tid}";
        await dbContext.SeedUsersAsync(userA, userB, system);

        var contract = new TaggingRequestEntity
        {
            ContractType = "Gratis",
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            Status = TradeStatus.Executed,
            OwnerId = userA,
            RequestedTag = new Tag { Name = $"Tag_{tid}", OwnerId = userB },
            TargetItem = new Item { Content = $"Item_{tid}", OwnerId = system }
        };
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await service.CancelContractAsync(contract.Id, userA);
        Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("この状態の契約はキャンセルできません。", ex.ErrorMessage);
    }
}