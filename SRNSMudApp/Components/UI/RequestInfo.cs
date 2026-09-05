using SRNSMudApp.Data;

namespace SRNSMudApp.Components.UI;

/// <summary>
///     ItemCard および RequestInfoAlert で表示するためのリクエスト情報 DTO。
///     イミュータブルな record として定義し、UI 描画時のデータ整合性を保証する。
/// </summary>
public record RequestInfo
{
    public bool IsTaggingRequest { get; init; }
    public TaggingRequestType? RequestType { get; init; }
    public int? ProposedWeight { get; init; }
    public int? TargetItemId { get; init; }
    public string? TargetItemContent { get; init; }
    public int? TargetTagId { get; init; }
    public string? TargetTagName { get; init; }
    public TradeStatus? Status { get; init; }
}