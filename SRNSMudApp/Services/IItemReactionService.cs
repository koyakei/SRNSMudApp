using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions; // if needed, but likely we just need Tag and ItemVoteResult

namespace SRNSMudApp.Services;

/// <summary>投票操作の結果。</summary>
public enum ItemVoteAction
{
    /// <summary>新しい投票を追加した。</summary>
    Added,

    /// <summary>既存の投票の Weight を変更した。</summary>
    Updated,

    /// <summary>既存の投票を取り消した。</summary>
    Removed
}

/// <summary>投票操作の結果とローカル状態更新に必要な情報。</summary>
public sealed record ItemVoteResult(ItemVoteAction Action, int RelationId, int Weight);

/// <summary>
///     アイテムに対する投票（Vote）やリアクション（Reaction）に関するドメインロジックを提供するサービス。
/// </summary>
public interface IItemReactionService
{
    /// <summary>good タグへの投票を追加 / 変更 / 取り消しする。</summary>
    Task<ItemVoteResult> ToggleItemVoteAsync(int itemId, string userId, int goodTagId, int targetWeight);

    /// <summary>リアクションタグ（真実・善・美）への投票（Upvote=+1 / Downvote=-1）を追加 / 変更 / 取り消しする。</summary>
    Task<ItemVoteResult> ToggleItemReactionAsync(int itemId, string userId, int reactionTagId, int targetWeight);

    /// <summary>指定した名前のシステムリアクションタグを確実に取得または作成する。</summary>
    Task<Tag> EnsureReactionTagAsync(string userId, string reactionTagName);
}

