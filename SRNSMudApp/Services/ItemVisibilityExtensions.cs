// Services/ItemVisibilityExtensions.cs
#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     プライベートモード（非公開アイテム）の可視性制御をクエリおよびエンティティに適用する拡張メソッド。
/// </summary>
public static class ItemVisibilityExtensions
{
    /// <summary>
    ///     指定されたユーザーが閲覧可能なアイテムのみにフィルタリングする。
    ///     1. 閲覧者が未ログインの場合: パブリックアイテム（!IsPrivate）のみ閲覧可能。
    ///     2. 閲覧者がログイン済みの場合:
    ///        - 自身が投稿したアイテム（OwnerId == currentUserId）
    ///        - パブリックアイテム（!IsPrivate）
    ///        - プライベート（フォロワー限定、TargetUserGroupId == null）で、閲覧者が投稿者をフォローしている場合
    ///        - プライベート（グループ限定、TargetUserGroupId != null）で、閲覧者がそのグループのメンバーまたはオーナーである場合
    /// </summary>
    public static IQueryable<Item> WhereVisibleToUser(
        this IQueryable<Item> query,
        ApplicationDbContext context,
        string? currentUserId)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(currentUserId))
        {
            return query.Where(i => !i.IsPrivate);
        }

        return query.Where(i =>
            i.OwnerId == currentUserId
            || !i.IsPrivate
            || (i.TargetUserGroupId == null && context.UserFollows.Any(f => f.OwnerId == currentUserId && f.FollowedUserId == i.OwnerId))
            || (i.TargetUserGroupId != null && (
                context.UserGroupMembers.Any(m => m.UserGroupId == i.TargetUserGroupId && m.UserId == currentUserId)
                || context.UserGroups.Any(g => g.Id == i.TargetUserGroupId && g.OwnerId == currentUserId)
            ))
        );
    }

    /// <summary>
    ///     メモリ上の単一アイテムに対して、指定ユーザーが閲覧可能かを非同期で判定する。
    /// </summary>
    public static async Task<bool> IsItemVisibleToUserAsync(
        this Item item,
        ApplicationDbContext context,
        string? currentUserId)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        if (!item.IsPrivate)
        {
            return true;
        }

        if (string.IsNullOrEmpty(currentUserId))
        {
            return false;
        }

        if (item.OwnerId == currentUserId)
        {
            return true;
        }

        if (item.TargetUserGroupId is null)
        {
            return await context.UserFollows.AnyAsync(f => f.OwnerId == currentUserId && f.FollowedUserId == item.OwnerId);
        }

        return await context.UserGroupMembers.AnyAsync(m => m.UserGroupId == item.TargetUserGroupId && m.UserId == currentUserId)
               || await context.UserGroups.AnyAsync(g => g.Id == item.TargetUserGroupId && g.OwnerId == currentUserId);
    }
}