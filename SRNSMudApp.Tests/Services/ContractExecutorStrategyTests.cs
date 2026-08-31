using Moq;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Contracts;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Services;

public class ContractExecutorStrategyTests : TaggingContractTestBase
{
    [Fact]
    public void ContractTypes_Constants_ShouldMatchExpectedStrings()
    {
        Assert.Equal("Gratis", ContractTypes.Gratis);
        Assert.Equal("Mutual", ContractTypes.Mutual);
        Assert.Equal("Trigger", ContractTypes.Trigger);
        Assert.Equal("Bounty", ContractTypes.Bounty);
    }

    [Fact]
    public void Executors_ShouldExposeExpectedContractTypes()
    {
        using var context = new ApplicationDbContext(SharedDb.Options);

        var gratis = new GratisContractExecutor(context);
        var mutual = new MutualContractExecutor(context);
        var trigger = new TriggerContractExecutor(context);
        var bounty = new BountyContractExecutor(context);

        Assert.Equal(ContractTypes.Gratis, gratis.ContractType);
        Assert.Equal(ContractTypes.Mutual, mutual.ContractType);
        Assert.Equal(ContractTypes.Trigger, trigger.ContractType);
        Assert.Equal(ContractTypes.Bounty, bounty.ContractType);
    }

    [Fact]
    public async Task TaggingContractService_WithCustomExecutor_DispatchesCorrectly()
    {
        var (context, _, tid) = CreateTestScope();

        // テストデータ作成
        var user = new ApplicationUser { Id = $"u_{tid}", UserName = $"u_{tid}" };
        var tag = new Tag { Name = $"t_{tid}", OwnerId = user.Id, IsSystem = false };
        var item = new Item { Content = $"item_{tid}", OwnerId = user.Id };

        context.Users.Add(user);
        context.Tags.Add(tag);
        context.Items.Add(item);
        await context.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "CustomStrategy",
            OwnerId = user.Id,
            RequesterUserId = user.Id,
            TagOwnerUserId = user.Id,
            TargetItemId = item.Id,
            RequestedTagId = tag.Id,
            Status = TradeStatus.Proposed,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1
        };
        context.TaggingRequestEntities.Add(contract);
        await context.SaveChangesAsync();

        var mockExecutor = new Mock<IContractExecutor>();
        mockExecutor.Setup(e => e.ContractType).Returns("CustomStrategy");
        mockExecutor
            .Setup(e => e.ExecuteAsync(It.IsAny<TaggingRequestEntity>(), user.Id, null))
            .ReturnsAsync(new Success<string>("Custom strategy executed successfully"));

        var service = new TaggingContractService(context, [mockExecutor.Object]);

        var result = await service.AcceptContractAsync(contract.Id, user.Id);

        Assert.True(result is Success<string> success && success.Value == "Custom strategy executed successfully");
        mockExecutor.Verify(e => e.ExecuteAsync(It.Is<TaggingRequestEntity>(c => c.Id == contract.Id), user.Id, null), Times.Once);
    }

    [Fact]
    public async Task TaggingContractService_WithUnknownContractType_ReturnsFailure()
    {
        var (context, _, tid) = CreateTestScope();

        var user = new ApplicationUser { Id = $"u_{tid}", UserName = $"u_{tid}" };
        var tag = new Tag { Name = $"t_{tid}", OwnerId = user.Id, IsSystem = false };
        var item = new Item { Content = $"item_{tid}", OwnerId = user.Id };

        context.Users.Add(user);
        context.Tags.Add(tag);
        context.Items.Add(item);
        await context.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = "NonExistentType",
            OwnerId = user.Id,
            RequesterUserId = user.Id,
            TagOwnerUserId = user.Id,
            TargetItemId = item.Id,
            RequestedTagId = tag.Id,
            Status = TradeStatus.Proposed,
            RequestType = TaggingRequestType.Add,
            ProposedWeight = 1
        };
        context.TaggingRequestEntities.Add(contract);
        await context.SaveChangesAsync();

        var service = new TaggingContractService(context, []);

        var result = await service.AcceptContractAsync(contract.Id, user.Id);

        Assert.True(result is Failure failure && failure.ErrorMessage.Contains("未知の契約型"));
    }
}