using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Data;

/// <summary>
///     他ユーザーの Tag の Content（説明文・内容）に対する編集提案リクエスト。
/// </summary>
public class TagContentProposal : BaseEntity
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

    /// <summary>提案された新しいタグの Content。</summary>
    [Required]
    public string ProposedContent { get; set; } = string.Empty;

    /// <summary>編集提案の理由や補足説明（任意）。</summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>リクエストの現在のステータス。</summary>
    public TradeStatus Status { get; set; } = TradeStatus.Proposed;

    /// <summary>却下時の理由（任意）。</summary>
    [MaxLength(500)]
    public string? RejectReason { get; set; }
}