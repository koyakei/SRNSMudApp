using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

public class ItemReactionService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagWeightLedgerService? ledgerService = null,
    TimeProvider? timeProvider = null) : IItemReactionService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITagWeightLedgerService _ledgerService = ledgerService ?? new TagWeightLedgerService();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ItemVoteResult> ToggleItemVoteAsync(
        int itemId,
        string userId,
        int goodTagId,
        int targetWeight)
        => await ApplyReactionChangeAsync(itemId, userId, goodTagId, targetWeight, false, "Vote");

    public async Task<ItemVoteResult> ToggleItemReactionAsync(
        int itemId,
        string userId,
        int reactionTagId,
        int targetWeight)
        => await ApplyReactionChangeAsync(itemId, userId, reactionTagId, targetWeight, true, "Reaction");

    private async Task<ItemVoteResult> ApplyReactionChangeAsync(
        int itemId,
        string userId,
        int tagId,
        int targetWeight,
        bool accumulate,
        string reasonPrefix)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        TagRelation? existingRelation = await context.TagRelations
            .FirstOrDefaultAsync(tr => tr.ItemId == itemId && tr.OwnerId == userId && tr.TagId == tagId);
        Tag? tag = await context.Tags.FindAsync(tagId);

        if (existingRelation is null)
        {
            var newRelation = new TagRelation
            {
                ItemId = itemId,
                TagId = tagId,
                OwnerId = userId,
                Weight = targetWeight,
                CreatedDate = now,
                UpdatedDate = now
            };
            _ = context.TagRelations.Add(newRelation);
            AddTimelineEvent(context, itemId, userId, tagId, "Insert", now, newWeight: targetWeight);
            RecordWeightChange(context, tag, itemId, "TagRelationInsert", null, targetWeight, $"{reasonPrefix}付与", userId, newRelation);

            _ = await context.SaveChangesAsync();
            return new ItemVoteResult(ItemVoteAction.Added, newRelation.Id, targetWeight);
        }

        int previousWeight = existingRelation.Weight;
        int newWeight = accumulate ? previousWeight + targetWeight : targetWeight;
        if (newWeight == 0 || (!accumulate && newWeight == previousWeight))
        {
            int delta = -previousWeight;
            RecordWeightChange(context, tag, itemId, "TagRelationDelete", null, delta, $"{reasonPrefix}取り消し", userId);
            AddTimelineEvent(context, itemId, userId, tagId, "Delete", now, previousWeight: previousWeight);
            _ = context.TagRelations.Remove(existingRelation);

            _ = await context.SaveChangesAsync();
            return new ItemVoteResult(ItemVoteAction.Removed, existingRelation.Id, accumulate ? 0 : previousWeight);
        }

        int weightDelta = newWeight - previousWeight;
        existingRelation.Weight = newWeight;
        existingRelation.UpdatedDate = now;
        RecordWeightChange(context, tag, itemId, "TagRelationUpdate", existingRelation.Id, weightDelta, $"{reasonPrefix}変更", userId);
        AddTimelineEvent(context, itemId, userId, tagId, "Update", now, previousWeight, newWeight);

        _ = await context.SaveChangesAsync();
        return new ItemVoteResult(ItemVoteAction.Updated, existingRelation.Id, newWeight);
    }

    private void RecordWeightChange(
        ApplicationDbContext context,
        Tag? tag,
        int itemId,
        string sourceType,
        int? sourceId,
        int delta,
        string reason,
        string userId,
        TagRelation? sourceRelation = null)
    {
        if (tag is null)
        {
            return;
        }

        _ledgerService.RecordItemTagWeightChange(context, tag, itemId, sourceType, sourceId, delta, reason, userId, sourceRelation);
    }

    private static void AddTimelineEvent(
        ApplicationDbContext context,
        int itemId,
        string userId,
        int tagId,
        string eventType,
        DateTime timestamp,
        int previousWeight = 0,
        int newWeight = 0)
    {
        _ = context.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = userId,
            Target = new ItemTarget(itemId),
            FollowedTagId = tagId,
            EventType = eventType,
            PreviousWeight = previousWeight,
            NewWeight = newWeight,
            CreatedDate = timestamp,
            UpdatedDate = timestamp
        });
    }

    public async Task<Tag> EnsureReactionTagAsync(string userId, string reactionTagName)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Tag? tag = await context.Tags.FirstOrDefaultAsync(t =>
            t.OwnerId == userId && t.Name == reactionTagName && t.IsSystem);

        if (tag is not null)
        {
            return tag;
        }

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        tag = new Tag
        {
            Name = reactionTagName,
            IsSystem = true,
            OwnerId = userId,
            CreatedDate = now,
            UpdatedDate = now
        };
        _ = context.Tags.Add(tag);
        _ = await context.SaveChangesAsync();
        return tag;
    }
}