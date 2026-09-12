using MudBlazor;

namespace SRNSMudApp.Components.UI;

/// <summary>
///     公開範囲バッジの判定に必要なコンテキスト情報。
/// </summary>
/// <param name="IsPrivate">非公開設定であるかどうか。</param>
/// <param name="TargetUserGroupName">公開対象のユーザーグループ名（グループ指定がない場合は null）。</param>
public sealed record PrivacyBadgeContext(bool IsPrivate, string? TargetUserGroupName);

/// <summary>
///     公開範囲バッジの UI 表示モデル。
/// </summary>
/// <param name="Text">バッジに表示するテキスト。</param>
/// <param name="Icon">バッジに表示する MudBlazor アイコン。</param>
/// <param name="Color">バッジの表示色。</param>
public sealed record PrivacyBadgeModel(string Text, string Icon, Color Color);

/// <summary>
///     公開範囲バッジ生成のための Strategy インターフェース。
/// </summary>
public interface IPrivacyBadgeStrategy
{
    /// <summary>
    ///     指定されたコンテキストに対して本 Strategy が適用可能かどうかを判定する。
    /// </summary>
    /// <param name="context">判定コンテキスト。</param>
    /// <returns>適用可能な場合は true、それ以外は false。</returns>
    bool CanApply(PrivacyBadgeContext context);

    /// <summary>
    ///     コンテキストに応じたバッジ表示モデルを生成する。
    /// </summary>
    /// <param name="context">判定コンテキスト。</param>
    /// <returns>生成されたバッジモデル。</returns>
    PrivacyBadgeModel CreateBadge(PrivacyBadgeContext context);
}

/// <summary>
///     フォロワー限定公開用のバッジ Strategy。
/// </summary>
public sealed class FollowersOnlyPrivacyBadgeStrategy : IPrivacyBadgeStrategy
{
    /// <inheritdoc />
    public bool CanApply(PrivacyBadgeContext context) =>
        context.IsPrivate && string.IsNullOrEmpty(context.TargetUserGroupName);

    /// <inheritdoc />
    public PrivacyBadgeModel CreateBadge(PrivacyBadgeContext context) =>
        new("フォロワー限定", Icons.Material.Filled.Lock, Color.Warning);
}

/// <summary>
///     特定グループ限定公開用のバッジ Strategy。
/// </summary>
public sealed class GroupPrivacyBadgeStrategy : IPrivacyBadgeStrategy
{
    /// <inheritdoc />
    public bool CanApply(PrivacyBadgeContext context) =>
        context.IsPrivate && !string.IsNullOrEmpty(context.TargetUserGroupName);

    /// <inheritdoc />
    public PrivacyBadgeModel CreateBadge(PrivacyBadgeContext context) =>
        new($"グループ: {context.TargetUserGroupName}", Icons.Material.Filled.Group, Color.Info);
}

/// <summary>
///     公開範囲バッジ Strategy の解決リゾルバー。
/// </summary>
public static class PrivacyBadgeStrategyResolver
{
    private static readonly IReadOnlyList<IPrivacyBadgeStrategy> Strategies =
    [
        new FollowersOnlyPrivacyBadgeStrategy(),
        new GroupPrivacyBadgeStrategy()
    ];

    /// <summary>
    ///     公開範囲設定に応じたバッジモデルを解決する。
    /// </summary>
    /// <param name="isPrivate">非公開設定であるかどうか。</param>
    /// <param name="targetUserGroupName">対象ユーザーグループ名。</param>
    /// <returns>該当するバッジモデル。公開設定の場合は null。</returns>
    public static PrivacyBadgeModel? Resolve(bool isPrivate, string? targetUserGroupName)
    {
        var context = new PrivacyBadgeContext(isPrivate, targetUserGroupName);
        IPrivacyBadgeStrategy? strategy = Strategies.FirstOrDefault(candidate => candidate.CanApply(context));
        return strategy?.CreateBadge(context);
    }
}