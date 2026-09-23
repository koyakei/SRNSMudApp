#region

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Models;

/// <summary>
///     特定のタグに対するユーザー単位の RightAsset 保有状況サマリー。
///     「誰がどれだけ持っているか」を一覧表示するための集計DTO。
/// </summary>
public sealed record RightAssetHolderSummary(
    string UserId,
    string? UserName,
    int TotalAmount,
    int ActiveAssetCount,
    int BurnedAmount,
    int BurnedAssetCount,
    DateTime? LastUpdated);

/// <summary>
///     個別 RightAsset レコードの詳細DTO。
/// </summary>
public sealed record RightAssetDetailDto(
    int Id,
    string OwnerId,
    string? OwnerName,
    int Amount,
    bool IsBurned,
    string StatusSummary,
    DateTime CreatedDate,
    DateTime UpdatedDate);

/// <summary>
///     特定タグにおける RightAsset 全体状況の集約データ。
/// </summary>
public sealed record RightAssetOverviewData(
    Tag Tag,
    int TotalHoldersCount,
    int TotalActiveAmount,
    int TotalActiveAssetsCount,
    int TotalBurnedAmount,
    IReadOnlyList<RightAssetHolderSummary> Holders,
    IReadOnlyList<RightAssetDetailDto> Assets);

/// <summary>
///     タグごとの RightAsset 発行・保有状況の概要（タグ未選択時のクイック選択用）。
/// </summary>
public sealed record TagRightAssetSummary(
    int TagId,
    string TagName,
    string? TagContent,
    int TotalAmount,
    int HolderCount);