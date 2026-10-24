#region

using System.ComponentModel.DataAnnotations;

using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;

#endregion

namespace SRNSMudApp.Data;

/// <summary>
///     他ユーザーの Item の本文の一部を選択し、別アイテムへ分割（切り抜き）することを提案するリクエスト。
/// </summary>
public class ItemSplitRequest : BaseEntity
{
    /// <summary>分割対象の元アイテムID。</summary>
    public int OriginalItemId { get; set; }

    /// <summary>分割対象の元アイテム。</summary>
    public Item OriginalItem { get; set; } = null!;

    /// <summary>リクエストを送信したユーザーID。</summary>
    [MaxLength(450)]
    public string RequesterUserId { get; set; } = string.Empty;

    /// <summary>リクエスト送信者。</summary>
    public ApplicationUser RequesterUser { get; set; } = null!;

    /// <summary>元アイテムの所有者ユーザーID。</summary>
    [MaxLength(450)]
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>元アイテムの所有者。</summary>
    public ApplicationUser OwnerUser { get; set; } = null!;

    /// <summary>分割対象として選択されたテキスト。</summary>
    [Required]
    [StringLength(1000)]
    public string SelectedText { get; set; } = string.Empty;

    /// <summary>リクエストの現在のステータス。</summary>
    public TradeStatus Status { get; set; } = TradeStatus.Proposed;

    /// <summary>承認時に新しく作成されたアイテムのID（承認前または却下時は null）。</summary>
    public int? CreatedItemId { get; set; }

    /// <summary>承認時に新しく作成されたアイテム。</summary>
    public Item? CreatedItem { get; set; }

    /// <summary>却下時の理由（任意）。</summary>
    [StringLength(500)]
    public string? RejectReason { get; set; }

    /// <summary>
    ///     リクエスト送信者が分割リクエストと同時に投稿した引用アイテムの ID。
    ///     分割リクエストが承認された際、この引用アイテムのタグ・被引用情報を
    ///     分割によって新しく作成されたアイテムへ移行し、引用アイテム自体は削除される。
    /// </summary>
    public int? RequesterQuoteItemId { get; set; }

    /// <summary>リクエスト送信者が同時に投稿した引用アイテム。</summary>
    public Item? RequesterQuoteItem { get; set; }

    /// <summary>
    ///     指定されたユーザーがこのリクエストの承認権限を持っているか判定する。
    /// </summary>
    public bool CanBeApprovedBy(string? userId) =>
        !string.IsNullOrEmpty(userId) && OwnerUserId == userId && Status == TradeStatus.Proposed;

    /// <summary>
    ///     指定されたユーザーがこのリクエストの却下権限を持っているか判定する。
    /// </summary>
    public bool CanBeRejectedBy(string? userId) =>
        !string.IsNullOrEmpty(userId) && OwnerUserId == userId && Status == TradeStatus.Proposed;

    /// <summary>
    ///     指定されたユーザーがこのリクエストの取り下げ権限を持っているか判定する。
    /// </summary>
    public bool CanBeCanceledBy(string? userId) =>
        !string.IsNullOrEmpty(userId) && RequesterUserId == userId && Status == TradeStatus.Proposed;

    /// <summary>
    ///     リクエストを承認済みに遷移させ、生成されたアイテムIDを記録する。
    /// </summary>
    public Result<bool> Approve(int createdItemId)
    {
        if (Status == TradeStatus.Executed)
        {
            return Result.Ok();
        }

        if (Status != TradeStatus.Proposed)
        {
            return Result.Fail(ErrorMessages.FormatSplitRequestNotProposedForApprove(Status));
        }

        Status = TradeStatus.Executed;
        CreatedItemId = createdItemId;
        return Result.Ok();
    }

    /// <summary>
    ///     リクエストを却下済みに遷移させ、理由を記録する。
    /// </summary>
    public Result<bool> Reject(string? reason)
    {
        if (Status == TradeStatus.Rejected)
        {
            return Result.Ok();
        }

        if (Status != TradeStatus.Proposed)
        {
            return Result.Fail(ErrorMessages.FormatSplitRequestNotProposedForReject(Status));
        }

        Status = TradeStatus.Rejected;
        RejectReason = reason;
        return Result.Ok();
    }

    /// <summary>
    ///     リクエストを取り下げ済みに遷移させる。
    /// </summary>
    public Result<bool> Cancel()
    {
        if (Status == TradeStatus.Canceled)
        {
            return Result.Ok();
        }

        if (Status != TradeStatus.Proposed)
        {
            return Result.Fail(ErrorMessages.FormatSplitRequestNotProposedForCancel(Status));
        }

        Status = TradeStatus.Canceled;
        return Result.Ok();
    }
}