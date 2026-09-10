using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Contracts;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Services;

public class MoveContractExecutorTests : TaggingContractTestBase
{
    [Fact]
    public async Task ExecuteAsync_WhenPayloadIsInvalid_ReturnsFailure()
    {
        var (context, _, tid) = CreateTestScope();
        var executor = new MoveContractExecutor();
        var contract = new TaggingRequestEntity
        {
            OwnerId = "user1",
            ContractType = ContractTypes.Move,
            Payload = new EmptyPayload()
        };

        var result = await executor.ExecuteAsync(context, contract, "user1");

        Assert.True(result is Failure failure && failure.ErrorMessage.Contains("無効なコントラクトペイロード"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTagNotFound_ReturnsFailure()
    {
        var (context, _, tid) = CreateTestScope();
        var executor = new MoveContractExecutor();
        var contract = new TaggingRequestEntity
        {
            OwnerId = "user1",
            ContractType = ContractTypes.Move,
            RequestedTagId = 99999,
            Payload = new TagMovePayload(null)
        };

        var result = await executor.ExecuteAsync(context, contract, "user1");

        Assert.True(result is Failure failure && failure.ErrorMessage == ContractMessages.TagNotFound);
    }

    [Fact]
    public async Task ExecuteAsync_WhenValid_UpdatesParentAndNodeAndSetsStatusExecuted()
    {
        var (context, _, tid) = CreateTestScope();
        var userOwner = new ApplicationUser { Id = $"owner_{tid}", UserName = $"owner_{tid}" };
        var userRequester = new ApplicationUser { Id = $"req_{tid}", UserName = $"req_{tid}" };
        context.Users.AddRange(userOwner, userRequester);

        var parentTag1 = new Tag { Name = $"Parent1_{tid}", OwnerId = userOwner.Id, ParentTagId = null };
        var parentTag2 = new Tag { Name = $"Parent2_{tid}", OwnerId = userOwner.Id, ParentTagId = null };
        var movingTag = new Tag { Name = $"Moving_{tid}", OwnerId = userOwner.Id, ParentTagId = null };

        context.Tags.AddRange(parentTag1, parentTag2, movingTag);
        await context.SaveChangesAsync();

        movingTag.ParentTagId = parentTag1.Id;
        await context.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            OwnerId = userRequester.Id,
            ContractType = ContractTypes.Move,
            RequesterUserId = userRequester.Id,
            TagOwnerUserId = userOwner.Id,
            RequestedTagId = movingTag.Id,
            RequestType = TaggingRequestType.Move,
            Payload = new TagMovePayload(parentTag2.Id)
        };

        var executor = new MoveContractExecutor();
        var result = await executor.ExecuteAsync(context, contract, userOwner.Id);

        Assert.True(result is Success<string>);
        Assert.Equal(TradeStatus.Executed, contract.Status);

        var updatedTag = await context.Tags.FindAsync(movingTag.Id);
        Assert.NotNull(updatedTag);
        Assert.Equal(parentTag2.Id, updatedTag.ParentTagId);
        Assert.NotNull(updatedTag.Node);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNewParentTagIdIsNull_MovesUnderRootTag()
    {
        var (context, _, tid) = CreateTestScope();
        var userOwner = new ApplicationUser { Id = $"owner_{tid}", UserName = $"owner_{tid}" };
        context.Users.Add(userOwner);

        var rootTag = await context.Tags.FirstAsync(t => t.Name == Tag.RootTagName);
        var parentTag = new Tag { Name = $"Parent_{tid}", OwnerId = userOwner.Id };
        var movingTag = new Tag { Name = $"Moving_{tid}", OwnerId = userOwner.Id };

        context.Tags.AddRange(parentTag, movingTag);
        await context.SaveChangesAsync();

        movingTag.ParentTagId = parentTag.Id;
        await context.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            OwnerId = "someone",
            ContractType = ContractTypes.Move,
            RequesterUserId = "someone",
            TagOwnerUserId = userOwner.Id,
            RequestedTagId = movingTag.Id,
            RequestType = TaggingRequestType.Move,
            Payload = new TagMovePayload(null)
        };

        var executor = new MoveContractExecutor();
        var result = await executor.ExecuteAsync(context, contract, userOwner.Id);

        Assert.True(result is Success<string>);
        var updatedTag = await context.Tags.FindAsync(movingTag.Id);
        Assert.NotNull(updatedTag);
        Assert.Equal(rootTag.Id, updatedTag.ParentTagId);
    }
}