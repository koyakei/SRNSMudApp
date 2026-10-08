using System.Text.Json;

using Microsoft.EntityFrameworkCore;

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

        await context.Database.ExecuteWithStrategyAsync(async () =>
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
        });
    }

    /// <summary>
    ///     タグ削除時に、外部キー制約 (DeleteBehavior.Restrict) により手動削除が必要な関連エンティティを一括削除する。
    ///     SQL Server の多重カスケード制限を回避するため Restrict 設定となっているテーブル群（TagWeightLedger 等）が対象。
    /// </summary>
    public static async Task RemoveTagRestrictedDependenciesAsync(
        this ApplicationDbContext context,
        IReadOnlyCollection<int> tagIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tagIds);

        if (tagIds.Count == 0)
        {
            return;
        }

        // 1. TagRelationToTag (タグ間の直接関係)
        List<TagRelationToTag> relationsToDelete = await context.TagRelationToTags
            .Where(tr => tagIds.Contains(tr.TagId) || tagIds.Contains(tr.TargetTagId))
            .ToListAsync(cancellationToken);
        if (relationsToDelete.Count > 0)
        {
            context.TagRelationToTags.RemoveRange(relationsToDelete);
        }

        // 2. TagWeightLedger (タグ重み台帳履歴)
        List<TagWeightLedger> ledgersToDelete = await context.TagWeightLedgers
            .Where(l => tagIds.Contains(l.TagId))
            .ToListAsync(cancellationToken);
        if (ledgersToDelete.Count > 0)
        {
            context.TagWeightLedgers.RemoveRange(ledgersToDelete);
        }

        // 3. TagEdgeTagAttachment (タグエッジへのアタッチメント)
        List<TagEdgeTagAttachment> attachmentsToDelete = await context.TagEdgeTagAttachments
            .Where(a => tagIds.Contains(a.TagId))
            .ToListAsync(cancellationToken);
        if (attachmentsToDelete.Count > 0)
        {
            context.TagEdgeTagAttachments.RemoveRange(attachmentsToDelete);
        }

        // 4. TagEdge (タグ間のエッジ)
        List<TagEdge> edgesToDelete = await context.TagEdges
            .Where(e => tagIds.Contains(e.SourceTagId) || tagIds.Contains(e.TargetTagId))
            .ToListAsync(cancellationToken);
        if (edgesToDelete.Count > 0)
        {
            context.TagEdges.RemoveRange(edgesToDelete);
        }

        // 5. TimelineEvent (タイムラインイベント)
        List<TimelineEvent> timelineEventsToDelete = await context.TimelineEvents
            .Where(e => tagIds.Contains(e.FollowedTagId))
            .ToListAsync(cancellationToken);
        if (timelineEventsToDelete.Count > 0)
        {
            context.TimelineEvents.RemoveRange(timelineEventsToDelete);
        }

        // 6. TagContentProposal (タグ説明変更提案)
        List<TagContentProposal> contentProposalsToDelete = await context.TagContentProposals
            .Where(p => tagIds.Contains(p.TagId))
            .ToListAsync(cancellationToken);
        if (contentProposalsToDelete.Count > 0)
        {
            context.TagContentProposals.RemoveRange(contentProposalsToDelete);
        }

        // 7. TagNameProposal (タグ名変更提案)
        List<TagNameProposal> nameProposalsToDelete = await context.TagNameProposals
            .Where(p => tagIds.Contains(p.TagId))
            .ToListAsync(cancellationToken);
        if (nameProposalsToDelete.Count > 0)
        {
            context.TagNameProposals.RemoveRange(nameProposalsToDelete);
        }

        // 8. TaggingRequestEntity (タグ付けリクエスト)
        List<TaggingRequestEntity> requestsToDelete = await context.TaggingRequestEntities
            .Where(r => tagIds.Contains(r.RequestedTagId))
            .ToListAsync(cancellationToken);
        if (requestsToDelete.Count > 0)
        {
            context.TaggingRequestEntities.RemoveRange(requestsToDelete);
        }

        // 9. PublicTradeOffer (パブリックトレードオファー)
        List<PublicTradeOffer> offersToDelete = await context.PublicTradeOffers
            .Where(o => tagIds.Contains(o.OfferedTagId))
            .ToListAsync(cancellationToken);
        if (offersToDelete.Count > 0)
        {
            context.PublicTradeOffers.RemoveRange(offersToDelete);
        }

        // 10. RightAsset (対象タグの権利アセット)
        List<RightAsset> assetsToDelete = await context.RightAssets
            .Where(a => tagIds.Contains(a.TargetTagId))
            .ToListAsync(cancellationToken);
        if (assetsToDelete.Count > 0)
        {
            context.RightAssets.RemoveRange(assetsToDelete);
        }
    }
}