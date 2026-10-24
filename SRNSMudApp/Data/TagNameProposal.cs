using System.ComponentModel.DataAnnotations;

using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Data;

/// <summary>
///     他ユーザーの Tag の Name（タグ名）に対する編集提案リクエスト。
/// </summary>
public class TagNameProposal : BaseEntity
{
    /// <summary>提案対象のタグID。</summary>
    public int TagId { get; set; }

    /// <summary>提案対象のタグ。</summary>
    public Tag Tag { get; set; } = null!;

    /// <summary>編集提案を送信したユーザーID。</summary>
    [MaxLength(450)]
    public string RequesterUserId { get; set; } = string.Empty;

    /// <summary>編集提案送信者。</summary>
    public ApplicationUser RequesterUser { get; set; } = null!;

    /// <summary>タグ所有者のユーザーID。</summary>
    [MaxLength(450)]
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>タグ所有者。</summary>
    public ApplicationUser OwnerUser { get; set; } = null!;

    /// <summary>提案された新しいタグ名。</summary>
    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^[\x20-\x7E\u3000-\u30FF\u4E00-\u9FFF\uFF01-\uFF9F\u2200-\u22FF]+$",
        ErrorMessage = "タグ名には漢字、ひらがな、カタカナ（半角/全角）、英数字（半角/全角）、アンダーバー、数学記号のみ使用できます。")]
    public string ProposedName { get; set; } = string.Empty;

    /// <summary>編集提案の理由や補足説明（任意）。</summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>リクエストの現在のステータス。</summary>
    public TradeStatus Status { get; set; } = TradeStatus.Proposed;

    /// <summary>却下時の理由（任意）。</summary>
    [MaxLength(500)]
    public string? RejectReason { get; set; }

    /// <summary>
    ///     指定されたユーザーがこの提案を承認できる権限および状態であるかを判定する。
    /// </summary>
    /// <param name="userId">操作を試みるユーザーID。</param>
    /// <returns>承認可能な場合は true。</returns>
    public bool CanBeApprovedBy(string userId) =>
        OwnerUserId == userId && Status == TradeStatus.Proposed;

    /// <summary>
    ///     指定されたユーザーがこの提案を却下できる権限および状態であるかを判定する。
    /// </summary>
    /// <param name="userId">操作を試みるユーザーID。</param>
    /// <returns>却下可能な場合は true。</returns>
    public bool CanBeRejectedBy(string userId) =>
        OwnerUserId == userId && Status == TradeStatus.Proposed;

    /// <summary>
    ///     指定されたユーザーがこの提案を取り下げできる権限および状態であるかを判定する。
    /// </summary>
    /// <param name="userId">操作を試みるユーザーID。</param>
    /// <returns>取り下げ可能な場合は true。</returns>
    public bool CanBeCanceledBy(string userId) =>
        RequesterUserId == userId && Status == TradeStatus.Proposed;

    /// <summary>
    ///     提案を承認し、ステータスを Executed に遷移させる。
    /// </summary>
    /// <returns>遷移結果。</returns>
    public Result<bool> Approve()
    {
        if (Status != TradeStatus.Proposed)
        {
            return Result.Fail<bool>($"申請中ではない提案は承認できません（現在の状態: {Status}）。");
        }

        Status = TradeStatus.Executed;
        UpdatedDate = DateTime.UtcNow;
        return Result.Ok(true);
    }

    /// <summary>
    ///     提案を却下し、ステータスを Rejected に遷移させる。
    /// </summary>
    /// <param name="rejectReason">却下理由。</param>
    /// <returns>遷移結果。</returns>
    public Result<bool> Reject(string? rejectReason = null)
    {
        if (Status != TradeStatus.Proposed)
        {
            return Result.Fail<bool>($"申請中ではない提案は却下できません（現在の状態: {Status}）。");
        }

        Status = TradeStatus.Rejected;
        RejectReason = rejectReason;
        UpdatedDate = DateTime.UtcNow;
        return Result.Ok(true);
    }

    /// <summary>
    ///     提案を取り下げ、ステータスを Canceled に遷移させる。
    /// </summary>
    /// <returns>遷移結果。</returns>
    public Result<bool> Cancel()
    {
        if (Status != TradeStatus.Proposed)
        {
            return Result.Fail<bool>($"申請中ではない提案は取り下げできません（現在の状態: {Status}）。");
        }

        Status = TradeStatus.Canceled;
        UpdatedDate = DateTime.UtcNow;
        return Result.Ok(true);
    }
}