#region

using System.ComponentModel.DataAnnotations;

#endregion

namespace SRNSMudApp.Data;

/// <summary>
///     通報対象のコンテンツ種別。
/// </summary>
public enum ReportTargetType
{
    /// <summary>アイテム（投稿・リプライ・引用）</summary>
    Item = 0,

    /// <summary>タグ</summary>
    Tag = 1
}

/// <summary>
///     通報の審査・対応ステータス。
/// </summary>
public enum ReportStatus
{
    /// <summary>未対応（新規受付）</summary>
    Pending = 0,

    /// <summary>確認済み（問題なし、または調査完了）</summary>
    Reviewed = 1,

    /// <summary>処置済み（対象コンテンツの削除等の対応完了）</summary>
    ActionTaken = 2,

    /// <summary>却下（誤報、嫌がらせ通報など）</summary>
    Dismissed = 3
}

/// <summary>
///     不適切な投稿やタグに対するユーザーからの通報エンティティ。
///     通報時のコンテンツのスナップショット、通報理由、管理者による対応履歴を保持する。
/// </summary>
public class ContentReport : BaseEntity
{
    /// <summary>
    ///     通報対象の種別（Item / Tag）。
    /// </summary>
    public ReportTargetType TargetType { get; set; }

    /// <summary>
    ///     対象アイテムID（対象が Item の場合に設定。Item が削除された場合は null）。
    /// </summary>
    public int? ItemId { get; set; }

    /// <summary>
    ///     対象アイテムのナビゲーションプロパティ。
    /// </summary>
    public Item? Item { get; set; }

    /// <summary>
    ///     対象タグID（対象が Tag の場合に設定。Tag が削除された場合は null）。
    /// </summary>
    public int? TagId { get; set; }

    /// <summary>
    ///     対象タグのナビゲーションプロパティ。
    /// </summary>
    public Tag? Tag { get; set; }

    /// <summary>
    ///     通報時点の対象コンテンツ・名前のスナップショット。
    ///     後から対象が編集・削除されても、管理者が通報内容を客観的に確認できるように保存する。
    /// </summary>
    [MaxLength(2000)]
    public string TargetContentSnapshot { get; set; } = string.Empty;

    /// <summary>
    ///     通報理由のカテゴリ（例: スパム・宣伝、誹謗中傷・ハラスメント、不適切なコンテンツ、その他）。
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    ///     通報者からの詳細説明コメント。
    /// </summary>
    [MaxLength(1000)]
    public string Detail { get; set; } = string.Empty;

    /// <summary>
    ///     通報の処理ステータス。
    /// </summary>
    public ReportStatus Status { get; set; } = ReportStatus.Pending;

    /// <summary>
    ///     管理者による対応メモ・解決理由。
    /// </summary>
    [MaxLength(1000)]
    public string? ResolutionNote { get; set; }

    /// <summary>
    ///     対応を行った管理者のユーザーID。
    /// </summary>
    public string? HandledByAdminId { get; set; }

    /// <summary>
    ///     対応を行った管理者のナビゲーションプロパティ。
    /// </summary>
    public ApplicationUser? HandledByAdmin { get; set; }

    /// <summary>
    ///     対応が実施された日時（UTC）。
    /// </summary>
    public DateTime? HandledDate { get; set; }
}