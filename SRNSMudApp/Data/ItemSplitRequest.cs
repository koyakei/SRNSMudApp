using System.ComponentModel.DataAnnotations;

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
}