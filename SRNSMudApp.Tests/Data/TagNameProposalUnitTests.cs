#region

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

#endregion

namespace SRNSMudApp.Tests.Data;

/// <summary>
///     <see cref="TagNameProposal" /> の状態遷移および権限判定ドメインメソッドに対する純粋単体テスト。
///     データベース接続を介さず、インメモリで状態遷移ルールと権限ガードを高速に検証する。
/// </summary>
public sealed class TagNameProposalUnitTests
{
    private static TagNameProposal CreateProposal(TradeStatus status = TradeStatus.Proposed) =>
        new()
        {
            Id = 1,
            OwnerId = "requester_1",
            TagId = 10,
            RequesterUserId = "requester_1",
            OwnerUserId = "owner_1",
            ProposedName = "NewTagName",
            Status = status
        };

    [Fact]
    public void Approve_WhenProposed_TransitionsToExecuted()
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(TradeStatus.Proposed);

        // Act
        Result<bool> result = proposal.Approve();

        // Assert
        Assert.True(result is Success<bool>);
        Assert.Equal(TradeStatus.Executed, proposal.Status);
    }

    [Theory]
    [InlineData(TradeStatus.Executed)]
    [InlineData(TradeStatus.Rejected)]
    [InlineData(TradeStatus.Canceled)]
    public void Approve_WhenNotProposed_ReturnsFailureAndPreservesStatus(TradeStatus currentStatus)
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(currentStatus);

        // Act
        Result<bool> result = proposal.Approve();

        // Assert
        Assert.True(result is Failure);
        Assert.Equal(currentStatus, proposal.Status);
    }

    [Fact]
    public void Reject_WhenProposed_TransitionsToRejectedAndSetsReason()
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(TradeStatus.Proposed);

        // Act
        Result<bool> result = proposal.Reject("不適切なタグ名のため却下");

        // Assert
        Assert.True(result is Success<bool>);
        Assert.Equal(TradeStatus.Rejected, proposal.Status);
        Assert.Equal("不適切なタグ名のため却下", proposal.RejectReason);
    }

    [Theory]
    [InlineData(TradeStatus.Executed)]
    [InlineData(TradeStatus.Rejected)]
    [InlineData(TradeStatus.Canceled)]
    public void Reject_WhenNotProposed_ReturnsFailureAndPreservesStatus(TradeStatus currentStatus)
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(currentStatus);

        // Act
        Result<bool> result = proposal.Reject("不適切なタグ名のため却下");

        // Assert
        Assert.True(result is Failure);
        Assert.Equal(currentStatus, proposal.Status);
    }

    [Fact]
    public void Cancel_WhenProposed_TransitionsToCanceled()
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(TradeStatus.Proposed);

        // Act
        Result<bool> result = proposal.Cancel();

        // Assert
        Assert.True(result is Success<bool>);
        Assert.Equal(TradeStatus.Canceled, proposal.Status);
    }

    [Theory]
    [InlineData(TradeStatus.Executed)]
    [InlineData(TradeStatus.Rejected)]
    [InlineData(TradeStatus.Canceled)]
    public void Cancel_WhenNotProposed_ReturnsFailureAndPreservesStatus(TradeStatus currentStatus)
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(currentStatus);

        // Act
        Result<bool> result = proposal.Cancel();

        // Assert
        Assert.True(result is Failure);
        Assert.Equal(currentStatus, proposal.Status);
    }

    [Fact]
    public void CanBeApprovedBy_OwnerAndProposed_ReturnsTrue()
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(TradeStatus.Proposed);

        // Act & Assert
        Assert.True(proposal.CanBeApprovedBy("owner_1"));
    }

    [Fact]
    public void CanBeApprovedBy_WrongUser_ReturnsFalse()
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(TradeStatus.Proposed);

        // Act & Assert
        Assert.False(proposal.CanBeApprovedBy("other_user"));
        Assert.False(proposal.CanBeApprovedBy("requester_1"));
    }

    [Fact]
    public void CanBeApprovedBy_NotProposed_ReturnsFalse()
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(TradeStatus.Executed);

        // Act & Assert
        Assert.False(proposal.CanBeApprovedBy("owner_1"));
    }

    [Fact]
    public void CanBeRejectedBy_OwnerAndProposed_ReturnsTrue()
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(TradeStatus.Proposed);

        // Act & Assert
        Assert.True(proposal.CanBeRejectedBy("owner_1"));
    }

    [Fact]
    public void CanBeRejectedBy_WrongUser_ReturnsFalse()
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(TradeStatus.Proposed);

        // Act & Assert
        Assert.False(proposal.CanBeRejectedBy("other_user"));
    }

    [Fact]
    public void CanBeCanceledBy_RequesterAndProposed_ReturnsTrue()
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(TradeStatus.Proposed);

        // Act & Assert
        Assert.True(proposal.CanBeCanceledBy("requester_1"));
    }

    [Fact]
    public void CanBeCanceledBy_WrongUser_ReturnsFalse()
    {
        // Arrange
        TagNameProposal proposal = CreateProposal(TradeStatus.Proposed);

        // Act & Assert
        Assert.False(proposal.CanBeCanceledBy("owner_1"));
        Assert.False(proposal.CanBeCanceledBy("other_user"));
    }
}