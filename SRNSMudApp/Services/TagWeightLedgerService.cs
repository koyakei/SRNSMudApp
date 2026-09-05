using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     タグの CachedWeight 更新およびウェイト台帳（TagWeightLedger）の記録を担当するドメインサービス実装。
/// </summary>
public class TagWeightLedgerService : ITagWeightLedgerService
{
    /// <inheritdoc />
    public void RecordItemTagWeightChange(
        ApplicationDbContext context,
        Tag tag,
        int itemId,
        string sourceType,
        int? sourceId,
        int delta,
        string reason,
        string userId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tag);

        var prevWeight = tag.CachedWeight;
        tag.CachedWeight += delta;

        _ = context.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = tag.Id,
            TagNameSnapshot = tag.Name,
            ItemId = itemId,
            SourceType = sourceType,
            SourceId = sourceId,
            PreviousWeight = prevWeight,
            NewWeight = tag.CachedWeight,
            Delta = delta,
            IsOwnerAction = true,
            Reason = reason,
            OwnerId = userId
        });
    }

    /// <inheritdoc />
    public void RecordTagToTagWeightChange(
        ApplicationDbContext context,
        Tag tag,
        int targetTagId,
        string sourceType,
        int? sourceId,
        int delta,
        string reason,
        string userId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tag);

        var prevWeight = tag.CachedWeight;
        tag.CachedWeight += delta;

        _ = context.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = tag.Id,
            TagNameSnapshot = tag.Name,
            TargetTagId = targetTagId,
            SourceType = sourceType,
            SourceId = sourceId,
            PreviousWeight = prevWeight,
            NewWeight = tag.CachedWeight,
            Delta = delta,
            IsOwnerAction = true,
            Reason = reason,
            OwnerId = userId
        });
    }
}