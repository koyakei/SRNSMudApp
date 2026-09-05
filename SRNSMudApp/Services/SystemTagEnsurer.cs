using SRNSMudApp.Components.UI;

namespace SRNSMudApp.Services;

public interface ISystemTagEnsurer
{
    /// <summary>
    ///     必要なら投票用・リアクション用システムタグ (good / bad / 真実 / 善 / 美) を作成し、確定したタグ ID を返す。
    ///     <c>Refetch</c> が true の場合、呼び出し元はタグ一覧を再取得して ID を再計算すること。
    /// </summary>
    Task<(SystemTagIds VoteIds, ReactionTagIds ReactionIds, bool Refetch)> EnsureAllAsync(
        string? userId,
        SystemTagIds currentVote,
        ReactionTagIds currentReaction);

    /// <summary>
    ///     必要なら投票用システムタグ (good / bad) を作成し、確定したタグ ID を返す。
    ///     <c>Refetch</c> が true の場合、呼び出し元はタグ一覧を再取得して ID を再計算すること。
    /// </summary>
    Task<(SystemTagIds Ids, bool Refetch)> EnsureAsync(string? userId, SystemTagIds current);
}

/// <summary>
///     システムタグの存在保証ロジックの共通実装。
///     Home / ResourceList / NotificationsPage で重複していたガードと分岐を集約する (Facade)。
/// </summary>
public class SystemTagEnsurer(IHomeDataProvider homeData) : ISystemTagEnsurer
{
    private readonly IHomeDataProvider _homeData =
        homeData ?? throw new ArgumentNullException(nameof(homeData));

    public async Task<(SystemTagIds VoteIds, ReactionTagIds ReactionIds, bool Refetch)> EnsureAllAsync(
        string? userId,
        SystemTagIds currentVote,
        ReactionTagIds currentReaction)
    {
        if (string.IsNullOrEmpty(userId) || (currentVote.IsComplete && currentReaction.IsComplete))
        {
            return (currentVote, currentReaction, false);
        }

        SystemTagsResult result = await _homeData.EnsureSystemTagsAsync(userId);

        return result.Created
            ? (currentVote, currentReaction, true)
            : (new SystemTagIds(result.GoodTagId, result.BadTagId),
                new ReactionTagIds(result.ShinjiTagId, result.ZenTagId, result.BiTagId),
                false);
    }

    public async Task<(SystemTagIds Ids, bool Refetch)> EnsureAsync(string? userId, SystemTagIds current)
    {
        if (string.IsNullOrEmpty(userId) || current.IsComplete)
        {
            return (current, false);
        }

        SystemTagsResult result = await _homeData.EnsureSystemTagsAsync(userId);

        return result.Created
            ? (current, true)
            : (new SystemTagIds(result.GoodTagId, result.BadTagId), false);
    }
}