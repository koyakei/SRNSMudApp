using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Data;

/// <summary>
///     <see cref="ApplicationDbContext" /> に対するタグ関連操作の拡張メソッド。
///     ビジネスロジックを DbContext 本体から分離するため、拡張メソッドとして実装する。
/// </summary>
public static class ApplicationDbContextTagExtensions
{
    /// <summary>
    ///     指定ユーザーがタグを直接（コントラクト提案を経ずに）付与可能かどうかを判定する。
    ///     - システムタグ・リアクションタグ
    ///     - タグのオーナー本人
    ///     - タグの自動承認（全体またはユーザーが所属するグループ）が有効な場合
    /// </summary>
    public static async Task<bool> CanUserAttachTagDirectlyAsync(
        this ApplicationDbContext context,
        Tag tag,
        string currentUserId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tag);

        if (string.IsNullOrEmpty(currentUserId))
        {
            return false;
        }

#pragma warning disable CA1508, IDE0072
        var isSpecialTag = tag.GetKind() switch
        {
            SystemClassificationTag or ReactionTag => true,
            _ => false
        };
#pragma warning restore CA1508, IDE0072

        if (isSpecialTag)
        {
            return true;
        }

        if (tag.GetKind() is VoteTag)
        {
            return false;
        }

        if (tag.OwnerId == currentUserId)
        {
            return true;
        }

        if (tag.AutoAcceptIncomingTaggingRequests)
        {
            return true;
        }

        var allowedGroupIds = await context.TagAutoApproveGroups
            .AsNoTracking()
            .Where(g => g.TagId == tag.Id)
            .Select(g => g.UserGroupId)
            .ToListAsync();

        if (allowedGroupIds.Count > 0)
        {
            return await context.UserGroupMembers
                .AsNoTracking()
                .AnyAsync(m => allowedGroupIds.Contains(m.UserGroupId) && m.UserId == currentUserId);
        }

        return false;
    }

    /// <summary>
    ///     タグのオーナー（または自動承認が有効なタグを付与するユーザー）が RightAsset を自動発行して消費し、タグを付与するシナリオ
    /// </summary>
    /// <param name="context">操作対象の <see cref="ApplicationDbContext" />。</param>
    /// <param name="itemId">タグを付与する対象アイテムの ID。</param>
    /// <param name="tagId">付与するタグの ID。</param>
    /// <param name="currentUserId">操作を実行しているユーザーの ID。</param>
    public static async Task CreateFreeTagRelationAsync(
        this ApplicationDbContext context,
        int itemId,
        int tagId,
        string currentUserId)
    {
        ArgumentNullException.ThrowIfNull(context);

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        try
        {
            // 1. Tag の権限検証 (SystemClassificationTag は誰でも無償付与可能、UserCustomTag は本人または自動承認有効時)
            Tag tag = await context.Tags.FindAsync(tagId) ?? throw new InvalidOperationException("指定されたタグが見つかりません。");

            var isAuthorized = await context.CanUserAttachTagDirectlyAsync(tag, currentUserId);

            if (!isAuthorized)
            {
                throw new UnauthorizedAccessException("このタグを無償で付与する権限がありません（タグのオーナーではありません）。");
            }

            // 2. RightAsset の発行と即時消費 (Burn)
            var rightAsset = new RightAsset
            {
                OwnerId = (tag.IsSystem || tag.OwnerId == "system" || string.IsNullOrEmpty(tag.OwnerId)) ? currentUserId : tag.OwnerId,
                TargetTagId = tagId,
                IsBurned = true,
                BurnStatusJson = JsonSerializer.Serialize<BurnStatus>(new Burned(DateTime.UtcNow))
            };
            _ = context.RightAssets.Add(rightAsset);
            _ = await context.SaveChangesAsync(); // IDを発行するためにSave

            // 3. TagRelation の作成
            var relation = new TagRelation
            {
                ItemId = itemId,
                TagId = tagId,
                OwnerId = currentUserId,
                Weight = 1 // 基本値
            };
            _ = context.TagRelations.Add(relation);

            _ = await context.SaveChangesAsync();

            // 4. Tag.CachedWeight の更新と以前の値の取得
            var previousWeight = tag.CachedWeight;
            tag.CachedWeight++;
            var newWeight = tag.CachedWeight;

            // 5. 元帳 (Ledger) への記帳
            var isSystemTag = tag.GetKind() is SystemClassificationTag;
            var isOwner = tag.OwnerId == currentUserId;
            var ledger = new TagWeightLedger
            {
                TagId = tagId,
                TagNameSnapshot = tag.Name,
                ItemId = itemId,
                SourceType = "TagRelation",
                SourceId = relation.Id,
                ConsumedRightAssetId = rightAsset.Id, // 必ずセットされる
                Delta = 1,
                PreviousWeight = previousWeight,
                NewWeight = newWeight,
                IsOwnerAction = isOwner || isSystemTag,
                Reason = isSystemTag ? "System Classification Tagging"
                    : isOwner ? "Owner Self-Tagging"
                    : "Auto-Approved Tagging",
                OwnerId = currentUserId
            };
            _ = context.TagWeightLedgers.Add(ledger);

            _ = context.TimelineEvents.Add(new TimelineEvent
            {
                OwnerId = currentUserId,
                TimelineTargetJson = JsonSerializer.Serialize<TimelineTarget>(new ItemTarget(itemId)),
                FollowedTagId = tagId,
                EventType = "Insert",
                NewWeight = 1
            });

            _ = await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}