#region

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

#endregion

namespace SRNSMudApp.Tests.Data;

/// <summary>
///     <see cref="ItemSplitRequest" /> の状態遷移および権限ガードに対する純粋単体テスト。
///     データベース接続を一切介さず、メモリ上で状態遷移ルールを網羅的に検証する。
/// </summary>
public sealed class ItemSplitRequestUnitTests
{
    private static ItemSplitRequest CreateRequest(TradeStatus status = TradeStatus.Proposed) =>
        new()
        {
            Id = 1,
            OwnerId = "owner_user",
            OriginalItemId = 10,
            OwnerUserId = "owner_user",
            RequesterUserId = "requester_user",
            SelectedText = "Sample text to split",
            Status = status
        };

    [Fact]
    public void Approve_WhenProposed_TransitionsToExecutedAndSetsCreatedItemId()
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(TradeStatus.Proposed);

        // Act
        Result<bool> result = request.Approve(createdItemId: 99);

        // Assert
        Assert.True(result is Success<bool>);
        Assert.Equal(TradeStatus.Executed, request.Status);
        Assert.Equal(99, request.CreatedItemId);
    }

    [Fact]
    public void Approve_WhenAlreadyExecuted_IsIdempotent()
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(TradeStatus.Executed);

        // Act
        Result<bool> result = request.Approve(createdItemId: 99);

        // Assert
        Assert.True(result is Success<bool>);
        Assert.Equal(TradeStatus.Executed, request.Status);
    }

    [Theory]
    [InlineData(TradeStatus.Rejected)]
    [InlineData(TradeStatus.Canceled)]
    public void Approve_WhenNotProposed_ReturnsFailure(TradeStatus invalidStatus)
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(invalidStatus);

        // Act
        Result<bool> result = request.Approve(createdItemId: 99);

        // Assert
        Assert.True(result is Failure);
        Assert.Equal(invalidStatus, request.Status);
    }

    [Fact]
    public void Reject_WhenProposed_TransitionsToRejectedAndSetsReason()
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(TradeStatus.Proposed);

        // Act
        Result<bool> result = request.Reject("不適切な分割です。");

        // Assert
        Assert.True(result is Success<bool>);
        Assert.Equal(TradeStatus.Rejected, request.Status);
        Assert.Equal("不適切な分割です。", request.RejectReason);
    }

    [Fact]
    public void Reject_WhenAlreadyRejected_IsIdempotent()
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(TradeStatus.Rejected);

        // Act
        Result<bool> result = request.Reject("再却下");

        // Assert
        Assert.True(result is Success<bool>);
        Assert.Equal(TradeStatus.Rejected, request.Status);
    }

    [Theory]
    [InlineData(TradeStatus.Executed)]
    [InlineData(TradeStatus.Canceled)]
    public void Reject_WhenNotProposed_ReturnsFailure(TradeStatus invalidStatus)
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(invalidStatus);

        // Act
        Result<bool> result = request.Reject("却下不可");

        // Assert
        Assert.True(result is Failure);
        Assert.Equal(invalidStatus, request.Status);
    }

    [Fact]
    public void Cancel_WhenProposed_TransitionsToCanceled()
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(TradeStatus.Proposed);

        // Act
        Result<bool> result = request.Cancel();

        // Assert
        Assert.True(result is Success<bool>);
        Assert.Equal(TradeStatus.Canceled, request.Status);
    }

    [Fact]
    public void Cancel_WhenAlreadyCanceled_IsIdempotent()
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(TradeStatus.Canceled);

        // Act
        Result<bool> result = request.Cancel();

        // Assert
        Assert.True(result is Success<bool>);
        Assert.Equal(TradeStatus.Canceled, request.Status);
    }

    [Theory]
    [InlineData(TradeStatus.Executed)]
    [InlineData(TradeStatus.Rejected)]
    public void Cancel_WhenNotProposed_ReturnsFailure(TradeStatus invalidStatus)
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(invalidStatus);

        // Act
        Result<bool> result = request.Cancel();

        // Assert
        Assert.True(result is Failure);
        Assert.Equal(invalidStatus, request.Status);
    }

    [Fact]
    public void CanBeApprovedBy_OnlyOwnerCanApproveWhenProposed()
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(TradeStatus.Proposed);

        // Assert
        Assert.True(request.CanBeApprovedBy("owner_user"));
        Assert.False(request.CanBeApprovedBy("other_user"));
        Assert.False(request.CanBeApprovedBy(null));
        Assert.False(request.CanBeApprovedBy(""));

        // Status が Proposed 以外ではオーナーでも false
        request.Status = TradeStatus.Executed;
        Assert.False(request.CanBeApprovedBy("owner_user"));
    }

    [Fact]
    public void CanBeRejectedBy_OnlyOwnerCanRejectWhenProposed()
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(TradeStatus.Proposed);

        // Assert
        Assert.True(request.CanBeRejectedBy("owner_user"));
        Assert.False(request.CanBeRejectedBy("other_user"));
        Assert.False(request.CanBeRejectedBy(null));

        request.Status = TradeStatus.Canceled;
        Assert.False(request.CanBeRejectedBy("owner_user"));
    }

    [Fact]
    public void CanBeCanceledBy_OnlyRequesterCanCancelWhenProposed()
    {
        // Arrange
        ItemSplitRequest request = CreateRequest(TradeStatus.Proposed);

        // Assert
        Assert.True(request.CanBeCanceledBy("requester_user"));
        Assert.False(request.CanBeCanceledBy("owner_user"));
        Assert.False(request.CanBeCanceledBy(null));

        request.Status = TradeStatus.Executed;
        Assert.False(request.CanBeCanceledBy("requester_user"));
    }
}