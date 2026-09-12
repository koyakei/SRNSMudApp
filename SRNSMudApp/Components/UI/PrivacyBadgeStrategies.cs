using MudBlazor;

namespace SRNSMudApp.Components.UI;

public sealed record PrivacyBadgeContext(bool IsPrivate, string? TargetUserGroupName);

public sealed record PrivacyBadgeModel(string Text, string Icon, Color Color);

public interface IPrivacyBadgeStrategy
{
    bool CanApply(PrivacyBadgeContext context);

    PrivacyBadgeModel CreateBadge(PrivacyBadgeContext context);
}

public sealed class FollowersOnlyPrivacyBadgeStrategy : IPrivacyBadgeStrategy
{
    public bool CanApply(PrivacyBadgeContext context) =>
        context.IsPrivate && string.IsNullOrEmpty(context.TargetUserGroupName);

    public PrivacyBadgeModel CreateBadge(PrivacyBadgeContext context) =>
        new("フォロワー限定", Icons.Material.Filled.Lock, Color.Warning);
}

public sealed class GroupPrivacyBadgeStrategy : IPrivacyBadgeStrategy
{
    public bool CanApply(PrivacyBadgeContext context) =>
        context.IsPrivate && !string.IsNullOrEmpty(context.TargetUserGroupName);

    public PrivacyBadgeModel CreateBadge(PrivacyBadgeContext context) =>
        new($"グループ: {context.TargetUserGroupName}", Icons.Material.Filled.Group, Color.Info);
}

public static class PrivacyBadgeStrategyResolver
{
    private static readonly IReadOnlyList<IPrivacyBadgeStrategy> Strategies =
    [
        new FollowersOnlyPrivacyBadgeStrategy(),
        new GroupPrivacyBadgeStrategy()
    ];

    public static PrivacyBadgeModel? Resolve(bool isPrivate, string? targetUserGroupName)
    {
        var context = new PrivacyBadgeContext(isPrivate, targetUserGroupName);
        IPrivacyBadgeStrategy? strategy = Strategies.FirstOrDefault(candidate => candidate.CanApply(context));
        return strategy?.CreateBadge(context);
    }
}