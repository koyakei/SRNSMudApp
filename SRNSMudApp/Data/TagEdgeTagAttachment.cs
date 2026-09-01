namespace SRNSMudApp.Data;

/// <summary>
///     TagEdge に対して「意味」を定義する Tag の紐付け 1 件分。
///     作成には既存の RightAsset（TargetTagId が紐付け対象タグと一致し、未消費のもの）を
///     1 件消費することが必須（無償付与の自動発行は行わない）。
/// </summary>
public class TagEdgeTagAttachment : BaseEntity
{
    public int TagEdgeId { get; set; }
    public TagEdge TagEdge { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public int Weight { get; set; } = 1;

    // 紐付けの権利として消費された RightAsset（必須・null 不可）
    public int ConsumedRightAssetId { get; set; }
    public RightAsset ConsumedRightAsset { get; set; } = null!;
}