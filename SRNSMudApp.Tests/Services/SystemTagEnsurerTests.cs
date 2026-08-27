using Moq;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     SystemTagEnsurer の単体テスト。
///     ガード分岐（未認証・既存完了）とシステムタグ作成結果の反映を検証する。
/// </summary>
public class SystemTagEnsurerTests
{
    private const string UserId = "user-1";

    private readonly Mock<IHomeDataProvider> _homeDataMock = new();
    private readonly SystemTagEnsurer _ensurer;

    public SystemTagEnsurerTests()
    {
        _ensurer = new SystemTagEnsurer(_homeDataMock.Object);
    }

    [Fact]
    public async Task EnsureAsync_WithEmptyUserId_DoesNotCallService()
    {
        var (ids, refetch) = await _ensurer.EnsureAsync("", new SystemTagIds(null, null));

        Assert.Equal(default, ids);
        Assert.False(refetch);
        _homeDataMock.Verify(h => h.EnsureSystemTagsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureAsync_WhenBothTagsAlreadyKnown_DoesNotCallService()
    {
        var current = new SystemTagIds(10, 20);

        var (ids, refetch) = await _ensurer.EnsureAsync(UserId, current);

        Assert.Equal(current, ids);
        Assert.False(refetch);
        _homeDataMock.Verify(h => h.EnsureSystemTagsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureAsync_WhenTagsCreated_RequestsRefetch()
    {
        _ = _homeDataMock
            .Setup(h => h.EnsureSystemTagsAsync(UserId))
            .ReturnsAsync(new SystemTagsResult(GoodTagId: null, BadTagId: null, Created: true));

        var (ids, refetch) = await _ensurer.EnsureAsync(UserId, new SystemTagIds(null, null));

        // 作成された場合は呼び出し元がタグ一覧を再取得して ID を解決する
        Assert.Equal(default, ids);
        Assert.True(refetch);
    }

    [Fact]
    public async Task EnsureAsync_WhenAlreadyExisted_ReturnsResultIdsWithoutRefetch()
    {
        _ = _homeDataMock
            .Setup(h => h.EnsureSystemTagsAsync(UserId))
            .ReturnsAsync(new SystemTagsResult(GoodTagId: 11, BadTagId: 22, Created: false));

        var (ids, refetch) = await _ensurer.EnsureAsync(UserId, new SystemTagIds(null, null));

        Assert.Equal(new SystemTagIds(11, 22), ids);
        Assert.False(refetch);
    }

    [Fact]
    public async Task EnsureAllAsync_WhenReactionTagsMissing_CallsService()
    {
        _ = _homeDataMock
            .Setup(h => h.EnsureSystemTagsAsync(UserId))
            .ReturnsAsync(new SystemTagsResult(
                GoodTagId: 1,
                BadTagId: 2,
                ShinjiTagId: 3,
                ZenTagId: 4,
                BiTagId: 5,
                Created: false));

        var currentVote = new SystemTagIds(1, 2);
        var currentReaction = new ReactionTagIds(null, null, null);

        var (voteIds, reactionIds, refetch) = await _ensurer.EnsureAllAsync(UserId, currentVote, currentReaction);

        Assert.Equal(new SystemTagIds(1, 2), voteIds);
        Assert.Equal(new ReactionTagIds(3, 4, 5), reactionIds);
        Assert.False(refetch);
        _homeDataMock.Verify(h => h.EnsureSystemTagsAsync(UserId), Times.Once);
    }
}