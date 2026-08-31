namespace SRNSMudApp.Models;

/// <summary>
///     コントラクト種別を表す文字列定数。
///     EF Core のディスクリミネーター列に保存される値と一致させる必要がある。
/// </summary>
public static class ContractTypes
{
    /// <summary>無償タグ付け（タグオーナーが RightAsset を発行・消費して無償でタグを付与する）。</summary>
    public const string Gratis = "Gratis";

    /// <summary>相互タグ付け（依頼者と承認者が互いに RightAsset を消費する）。</summary>
    public const string Mutual = "Mutual";

    /// <summary>トリガー/公開オファー（依頼者自身が RightAsset を消費して実行する）。</summary>
    public const string Trigger = "Trigger";

    /// <summary>バウンティ（タグオーナーが報酬を設定し、実行者が RightAsset を提供する）。</summary>
    public const string Bounty = "Bounty";
}