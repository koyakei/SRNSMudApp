using Moq;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Resources;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     ItemCardVoteCoordinator の単体テスト。
///     Good 投票（アップ/ダウン）および三相リアクション投票とサービス呼び出し・通知を検証する。
/// </summary>
public class ItemCardVoteCoordinatorTests
{
    private const string UserId = "user-123";
    private readonly Mock<IItemReactionService> _itemReactionServiceMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();
    private readonly ItemCardVoteCoordinator _coordinator;

    public ItemCardVoteCoordinatorTests()
    {
        _coordinator = new ItemCardVoteCoordinator(_itemReactionServiceMock.Object, _snackbarMock.Object);
    }

    [Fact]
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ItemCardVoteCoordinator(null!, _snackbarMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ItemCardVoteCoordinator(_itemReactionServiceMock.Object, null!));
    }

    [Fact]
    public async Task ToggleVoteAsync_WhenUserIdIsNullOrEmpty_ShowsLoginRequired()
    {
        var result = await _coordinator.ToggleVoteAsync(1, "", 10, isUpvote: true);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add(ErrorMessages.LoginRequired, Severity.Warning, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ToggleVoteAsync_WhenGoodTagIdIsNull_ShowsErrorSnackbar()
    {
        var result = await _coordinator.ToggleVoteAsync(1, UserId, null, isUpvote: true);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add(ErrorMessages.SystemTagRetrievalFailed, Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ToggleVoteAsync_WhenEnsureSystemTagsProvided_ExecutesCallback()
    {
        var callbackCalled = false;
        Func<Task> ensureCallback = () => { callbackCalled = true; return Task.CompletedTask; };

        _ = _itemReactionServiceMock
            .Setup(s => s.ToggleItemVoteAsync(1, UserId, 10, 1))
            .ReturnsAsync(new ItemVoteResult(ItemVoteAction.Added, 1, 1));

        var result = await _coordinator.ToggleVoteAsync(1, UserId, 10, isUpvote: true, ensureCallback);

        Assert.True(result);
        Assert.True(callbackCalled);
    }

    [Fact]
    public async Task ToggleVoteAsync_WhenUpvoted_CallsToggleItemVoteWithPositiveWeight()
    {
        _ = _itemReactionServiceMock
            .Setup(s => s.ToggleItemVoteAsync(1, UserId, 10, 1))
            .ReturnsAsync(new ItemVoteResult(ItemVoteAction.Added, 99, 1));

        var result = await _coordinator.ToggleVoteAsync(1, UserId, 10, isUpvote: true);

        Assert.True(result);
        _itemReactionServiceMock.Verify(s => s.ToggleItemVoteAsync(1, UserId, 10, 1), Times.Once);
    }

    [Fact]
    public async Task ToggleVoteAsync_WhenDownvoted_CallsToggleItemVoteWithNegativeWeight()
    {
        _ = _itemReactionServiceMock
            .Setup(s => s.ToggleItemVoteAsync(1, UserId, 10, -1))
            .ReturnsAsync(new ItemVoteResult(ItemVoteAction.Removed, 99, 0));

        var result = await _coordinator.ToggleVoteAsync(1, UserId, 10, isUpvote: false);

        Assert.True(result);
        _itemReactionServiceMock.Verify(s => s.ToggleItemVoteAsync(1, UserId, 10, -1), Times.Once);
    }

    [Fact]
    public async Task ToggleReactionAsync_WhenTagIdNotSpecified_EnsuresTagAndCallsToggleReaction()
    {
        var reactionTag = new Tag { Id = 20, Name = ReactionTagNames.Shinji, IsSystem = true, OwnerId = UserId };

        _ = _itemReactionServiceMock
            .Setup(s => s.EnsureReactionTagAsync(UserId, ReactionTagNames.Shinji))
            .ReturnsAsync(reactionTag);

        _ = _itemReactionServiceMock
            .Setup(s => s.ToggleItemReactionAsync(2, UserId, 20, 1))
            .ReturnsAsync(new ItemVoteResult(ItemVoteAction.Added, 101, 1));

        var result = await _coordinator.ToggleReactionAsync(
            2, UserId, ReactionTagNames.Shinji, targetWeight: 1, reactionTagId: null, allTags: []);

        Assert.True(result);
        _itemReactionServiceMock.Verify(s => s.EnsureReactionTagAsync(UserId, ReactionTagNames.Shinji), Times.Once);
        _itemReactionServiceMock.Verify(s => s.ToggleItemReactionAsync(2, UserId, 20, 1), Times.Once);
    }
}