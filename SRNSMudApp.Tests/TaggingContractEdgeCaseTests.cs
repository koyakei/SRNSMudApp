using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
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
            OwnerId = userA,
            RequestedTag = new Tag { Name = $"Tag_{tid}", OwnerId = userB },
            TargetItem = new Item { Content = $"Item_{tid}", OwnerId = system }
        };
        contract.Cancel();
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
            OwnerId = userA,
            RequestedTag = new Tag { Name = $"Tag_{tid}", OwnerId = userB },
            TargetItem = new Item { Content = $"Item_{tid}", OwnerId = system }
        };
        contract.Execute();
        dbContext.TaggingRequestEntities!.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await service.CancelContractAsync(contract.Id, userA);
        Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("この状態の契約はキャンセルできません。", ex.ErrorMessage);
    }

    [Fact]
    public async Task AcceptContractAsync_WhenContractNotFound_ReturnsFailure()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (_, service, _) = scope;

        // Act
        var res = await service.AcceptContractAsync(999999, "any_user");

        // Assert
        Assert.True(res is Failure);
        var fail = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("契約が見つかりません。", fail.ErrorMessage);
    }

    [Fact]
    public async Task AcceptContractAsync_WhenTriggerContractAndUserIsNotRequester_ReturnsFailure()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var otherUserId = $"other_{tid}";
        await dbContext.SeedUsersAsync(requesterId, otherUserId);

        var item = new Item { Content = $"Item_{tid}", OwnerId = requesterId };
        var tag = new Tag { Name = $"Tag_{tid}", OwnerId = requesterId };
        dbContext.Items.Add(item);
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = ContractTypes.Trigger,
            RequesterUserId = requesterId,
            TagOwnerUserId = requesterId,
            OwnerId = requesterId,
            TargetItemId = item.Id,
            TargetId = item.TagTargetId,
            RequestedTagId = tag.Id,
            Payload = new PublicOfferPayload(1)
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act: 他のユーザーが Trigger 契約を承認しようとする
        var res = await service.AcceptContractAsync(contract.Id, otherUserId);

        // Assert
        Assert.True(res is Failure);
        var fail = res switch { Failure f => f, _ => throw new InvalidOperationException("Expected Failure") };
        Assert.Equal("実行できない契約です。", fail.ErrorMessage);
    }

    [Fact]
    public async Task AcceptContractAsync_WhenTargetItemIdIsZero_ShouldResolveItemIdFromTagTarget()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var tagOwnerId = $"owner_{tid}";
        await dbContext.SeedUsersAsync(requesterId, tagOwnerId);

        var item = new Item { Content = $"TargetItem_{tid}", OwnerId = requesterId };
        var tag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId, CachedWeight = 10 };
        dbContext.Items.Add(item);
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();

        // TargetItemId をあえて 0 にし、TargetId に item.TagTargetId をセット
        var contract = new TaggingRequestEntity
        {
            ContractType = ContractTypes.Gratis,
            RequesterUserId = requesterId,
            TagOwnerUserId = tagOwnerId,
            OwnerId = requesterId,
            TargetItemId = 0,
            TargetId = item.TagTargetId,
            RequestedTagId = tag.Id,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1,
            Payload = new GratisPayload("auto resolve item id test")
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act
        var res = await service.AcceptContractAsync(contract.Id, tagOwnerId);

        // Assert: TargetItemId が自動解決され正常に TagRelation が作成されること
        Assert.True(res is Success<string>, res switch { Failure f => f.ErrorMessage, _ => "Expected Success" });

        TagRelation? rel = await dbContext.TagRelations
            .FirstOrDefaultAsync(tr => tr.ItemId == item.Id && tr.TagId == tag.Id);
        Assert.NotNull(rel);
        Assert.Equal(1, rel.Weight);
    }

    [Fact]
    public async Task AcceptContractAsync_ConcurrentAccepts_OnlyOneSucceeds()
    {
        // Arrange
        await using var scope = CreateTestScope();
        var (dbContext, service, tid) = scope;

        var requesterId = $"req_{tid}";
        var tagOwnerId = $"owner_{tid}";
        await dbContext.SeedUsersAsync(requesterId, tagOwnerId);

        var item = new Item { Content = $"Item_{tid}", OwnerId = requesterId };
        var tag = new Tag { Name = $"Tag_{tid}", OwnerId = tagOwnerId, CachedWeight = 10 };
        dbContext.Items.Add(item);
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = ContractTypes.Gratis,
            RequesterUserId = requesterId,
            TagOwnerUserId = tagOwnerId,
            OwnerId = requesterId,
            TargetItemId = item.Id,
            TargetId = item.TagTargetId,
            RequestedTagId = tag.Id,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1,
            Payload = new GratisPayload("concurrent test")
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        // Act: 2つの AcceptContractAsync を並行実行
        var task1 = service.AcceptContractAsync(contract.Id, tagOwnerId);
        var task2 = service.AcceptContractAsync(contract.Id, tagOwnerId);
        var results = await Task.WhenAll(task1, task2);

        // Assert: 1つは成功、もう1つは失敗すること
        var successCount = results.Count(r => r is Success<string>);
        var failureCount = results.Count(r => r is Failure);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        dbContext.ChangeTracker.Clear();
        TaggingRequestEntity? finalContract = await dbContext.TaggingRequestEntities.FindAsync(contract.Id);
        Assert.Equal(TradeStatus.Executed, finalContract!.Status);

        // TagRelation は重複せず 1 件のみ存在すること
        var relations = await dbContext.TagRelations
            .Where(tr => tr.ItemId == item.Id && tr.TagId == tag.Id)
            .ToListAsync();
        Assert.Single(relations);
    }
}