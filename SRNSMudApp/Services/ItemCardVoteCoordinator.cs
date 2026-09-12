using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Resources;

#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services;

/// <summary>
///     ItemCard における投票・リアクション操作および TagRelation コレクション更新の調整実装。
///     UI コンポーネントから投票オーケストレーションとエンティティ変更の責務を分離する（SRP 準拠）。
/// </summary>
public class ItemCardVoteCoordinator(
    IItemReactionService itemReactionService,
    ISnackbar snackbar) : IItemCardVoteCoordinator
{
    private readonly IItemReactionService _itemReactionService =
        itemReactionService ?? throw new ArgumentNullException(nameof(itemReactionService));
    private readonly ISnackbar _snackbar =
        snackbar ?? throw new ArgumentNullException(nameof(snackbar));

    /// <inheritdoc />
    public async Task<bool> ToggleVoteAsync(
        int itemId,
        string currentUserId,
        int? goodTagId,
        bool isUpvote,
        Func<Task>? ensureSystemTagsAsync = null)
    {
        if (string.IsNullOrEmpty(currentUserId))
        {
            _ = _snackbar.Add(ErrorMessages.LoginRequired, Severity.Warning);
            return false;
        }

        if (ensureSystemTagsAsync is not null)
        {
            await ensureSystemTagsAsync();
        }

        if (!goodTagId.HasValue)
        {
            _ = _snackbar.Add(ErrorMessages.SystemTagRetrievalFailed, Severity.Error);
            return false;
        }

        var targetWeight = isUpvote ? 1 : -1;
        var tagId = goodTagId.Value;

        return await ToggleTagVoteAsync(
            () => _itemReactionService.ToggleItemVoteAsync(itemId, currentUserId, tagId, targetWeight));
    }

    /// <inheritdoc />
    public async Task<bool> ToggleReactionAsync(
        int itemId,
        string currentUserId,
        string reactionTagName,
        int targetWeight,
        int? reactionTagId,
        IReadOnlyList<Tag> allTags,
        Func<Task>? ensureSystemTagsAsync = null)
    {
        if (string.IsNullOrEmpty(currentUserId))
        {
            _ = _snackbar.Add("ログインが必要です。", Severity.Warning);
            return false;
        }

        if (ensureSystemTagsAsync is not null)
        {
            await ensureSystemTagsAsync();
        }

        // タグ ID が未確定の場合は既存タグから検索、または自動確保する
        int tagId;
        if (!reactionTagId.HasValue)
        {
            tagId = (await _itemReactionService.EnsureReactionTagAsync(currentUserId, reactionTagName)).Id;
        }
        else
        {
            tagId = allTags?.FirstOrDefault(t => t.Id == reactionTagId.Value)?.Id
                ?? (await _itemReactionService.EnsureReactionTagAsync(currentUserId, reactionTagName)).Id;
        }

        return await ToggleTagVoteAsync(
            () => _itemReactionService.ToggleItemReactionAsync(itemId, currentUserId, tagId, targetWeight));
    }

    private static async Task<bool> ToggleTagVoteAsync(
        Func<Task<ItemVoteResult>> toggleAsync)
    {
        _ = await toggleAsync();
        return true;
    }
}