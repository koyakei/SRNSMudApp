using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

public class ItemReactionService(IDbContextFactory<ApplicationDbContext> dbFactory) : IItemReactionService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<ItemVoteResult> ToggleItemVoteAsync(
        int itemId,
        string userId,
        int goodTagId,
        int targetWeight)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        List<TagRelation> relations = await context.TagRelations
            .Where(tr => tr.ItemId == itemId && tr.OwnerId == userId)
            .ToListAsync();

        TagRelation? existingRelation = relations.Find(tr => tr.TagId == goodTagId);
        Tag? tag = await context.Tags.FindAsync(goodTagId);

        switch (existingRelation)
        {
            case null:
                {
                    var newRelation = new TagRelation
                    {
                        ItemId = itemId,
                        TagId = goodTagId,
                        OwnerId = userId,
                        Weight = targetWeight,
                        CreatedDate = DateTime.UtcNow,
                        UpdatedDate = DateTime.UtcNow
                    };
                    _ = context.TagRelations.Add(newRelation);

                    _ = context.TimelineEvents!.Add(new TimelineEvent
                    {
                        OwnerId = userId,
                        Target = new ItemTarget(itemId),
                        FollowedTagId = goodTagId,
                        EventType = "Insert",
                        NewWeight = targetWeight
                    });

                    _ = await context.SaveChangesAsync();

                    if (tag is not null)
                    {
                        var prevWeightAdd = tag.CachedWeight;
                        tag.CachedWeight += targetWeight;
                        _ = context.TagWeightLedgers!.Add(new TagWeightLedger
                        {
                            TagId = tag.Id,
                            TagNameSnapshot = tag.Name,
                            ItemId = itemId,
                            SourceType = "TagRelationInsert",
                            SourceId = newRelation.Id,
                            PreviousWeight = prevWeightAdd,
                            NewWeight = tag.CachedWeight,
                            Delta = targetWeight,
                            IsOwnerAction = true,
                            Reason = "Vote付与",
                            OwnerId = userId
                        });

                        _ = await context.SaveChangesAsync();
                    }

                    return new ItemVoteResult(ItemVoteAction.Added, newRelation.Id, targetWeight);
                }
            default:
                switch (existingRelation.Weight == targetWeight)
                {
                    // 同じ Weight なら投票取り消し
                    case true:
                        {
                            var deltaCancel = -existingRelation.Weight;
                            if (tag is not null)
                            {
                                var prevWeightCancel = tag.CachedWeight;
                                tag.CachedWeight += deltaCancel;
                                _ = context.TagWeightLedgers!.Add(new TagWeightLedger
                                {
                                    TagId = tag.Id,
                                    TagNameSnapshot = tag.Name,
                                    ItemId = itemId,
                                    SourceType = "TagRelationDelete",
                                    SourceId = null,
                                    PreviousWeight = prevWeightCancel,
                                    NewWeight = tag.CachedWeight,
                                    Delta = deltaCancel,
                                    IsOwnerAction = true,
                                    Reason = "Vote取り消し",
                                    OwnerId = userId
                                });
                            }

                            _ = context.TimelineEvents!.Add(new TimelineEvent
                            {
                                OwnerId = userId,
                                Target = new ItemTarget(itemId),
                                FollowedTagId = goodTagId,
                                EventType = "Delete",
                                PreviousWeight = existingRelation.Weight
                            });

                            _ = context.TagRelations.Remove(existingRelation);
                            _ = await context.SaveChangesAsync();
                            return new ItemVoteResult(ItemVoteAction.Removed, existingRelation.Id, existingRelation.Weight);
                        }
                    default:
                        {
                            var deltaUpdate = targetWeight - existingRelation.Weight;
                            existingRelation.Weight = targetWeight;
                            existingRelation.UpdatedDate = DateTime.UtcNow;

                            if (tag is not null)
                            {
                                var prevWeightUpdate = tag.CachedWeight;
                                tag.CachedWeight += deltaUpdate;
                                _ = context.TagWeightLedgers!.Add(new TagWeightLedger
                                {
                                    TagId = tag.Id,
                                    TagNameSnapshot = tag.Name,
                                    ItemId = itemId,
                                    SourceType = "TagRelationUpdate",
                                    SourceId = existingRelation.Id,
                                    PreviousWeight = prevWeightUpdate,
                                    NewWeight = tag.CachedWeight,
                                    Delta = deltaUpdate,
                                    IsOwnerAction = true,
                                    Reason = "Vote変更",
                                    OwnerId = userId
                                });
                            }

                            _ = context.TimelineEvents!.Add(new TimelineEvent
                            {
                                OwnerId = userId,
                                Target = new ItemTarget(itemId),
                                FollowedTagId = goodTagId,
                                EventType = "Update",
                                PreviousWeight = existingRelation.Weight - deltaUpdate,
                                NewWeight = existingRelation.Weight
                            });

                            _ = await context.SaveChangesAsync();
                            return new ItemVoteResult(ItemVoteAction.Updated, existingRelation.Id, targetWeight);
                        }
                }
        }
    }

    public async Task<ItemVoteResult> ToggleItemReactionAsync(
        int itemId,
        string userId,
        int reactionTagId,
        int targetWeight)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        TagRelation? existingRelation = await context.TagRelations
            .FirstOrDefaultAsync(tr => tr.ItemId == itemId && tr.OwnerId == userId && tr.TagId == reactionTagId);
        Tag? tag = await context.Tags.FindAsync(reactionTagId);

        switch (existingRelation)
        {
            case null:
                {
                    var newRelation = new TagRelation
                    {
                        ItemId = itemId,
                        TagId = reactionTagId,
                        OwnerId = userId,
                        Weight = targetWeight,
                        CreatedDate = DateTime.UtcNow,
                        UpdatedDate = DateTime.UtcNow
                    };
                    _ = context.TagRelations.Add(newRelation);

                    _ = context.TimelineEvents!.Add(new TimelineEvent
                    {
                        OwnerId = userId,
                        Target = new ItemTarget(itemId),
                        FollowedTagId = reactionTagId,
                        EventType = "Insert",
                        NewWeight = targetWeight
                    });

                    _ = await context.SaveChangesAsync();

                    if (tag is not null)
                    {
                        var prevWeightAdd = tag.CachedWeight;
                        tag.CachedWeight += targetWeight;
                        _ = context.TagWeightLedgers!.Add(new TagWeightLedger
                        {
                            TagId = tag.Id,
                            TagNameSnapshot = tag.Name,
                            ItemId = itemId,
                            SourceType = "TagRelationInsert",
                            SourceId = newRelation.Id,
                            PreviousWeight = prevWeightAdd,
                            NewWeight = tag.CachedWeight,
                            Delta = targetWeight,
                            IsOwnerAction = true,
                            Reason = "Reaction付与",
                            OwnerId = userId
                        });

                        _ = await context.SaveChangesAsync();
                    }
                    return new ItemVoteResult(ItemVoteAction.Added, newRelation.Id, targetWeight);
                }
            default:
                var newWeight = existingRelation.Weight + targetWeight;
                switch (newWeight)
                {
                    // 逆操作によって Weight が 0 に達した場合はリレーションを削除 (Removed)
                    case 0:
                        {
                            var deltaCancel = -existingRelation.Weight;
                            if (tag is not null)
                            {
                                var prevWeightCancel = tag.CachedWeight;
                                tag.CachedWeight += deltaCancel;
                                _ = context.TagWeightLedgers!.Add(new TagWeightLedger
                                {
                                    TagId = tag.Id,
                                    TagNameSnapshot = tag.Name,
                                    ItemId = itemId,
                                    SourceType = "TagRelationDelete",
                                    SourceId = null,
                                    PreviousWeight = prevWeightCancel,
                                    NewWeight = tag.CachedWeight,
                                    Delta = deltaCancel,
                                    IsOwnerAction = true,
                                    Reason = "Reaction取り消し",
                                    OwnerId = userId
                                });
                            }

                            _ = context.TimelineEvents!.Add(new TimelineEvent
                            {
                                OwnerId = userId,
                                Target = new ItemTarget(itemId),
                                FollowedTagId = reactionTagId,
                                EventType = "Delete",
                                PreviousWeight = existingRelation.Weight
                            });

                            _ = context.TagRelations.Remove(existingRelation);
                            _ = await context.SaveChangesAsync();
                            return new ItemVoteResult(ItemVoteAction.Removed, existingRelation.Id, 0);
                        }
                    // 同方向なら加算、逆方向なら減算して Weight を更新 (Updated)
                    default:
                        {
                            var deltaUpdate = targetWeight;
                            existingRelation.Weight = newWeight;
                            existingRelation.UpdatedDate = DateTime.UtcNow;

                            if (tag is not null)
                            {
                                var prevWeightUpdate = tag.CachedWeight;
                                tag.CachedWeight += deltaUpdate;
                                _ = context.TagWeightLedgers!.Add(new TagWeightLedger
                                {
                                    TagId = tag.Id,
                                    TagNameSnapshot = tag.Name,
                                    ItemId = itemId,
                                    SourceType = "TagRelationUpdate",
                                    SourceId = existingRelation.Id,
                                    PreviousWeight = prevWeightUpdate,
                                    NewWeight = tag.CachedWeight,
                                    Delta = deltaUpdate,
                                    IsOwnerAction = true,
                                    Reason = "Reaction変更",
                                    OwnerId = userId
                                });
                            }

                            _ = context.TimelineEvents!.Add(new TimelineEvent
                            {
                                OwnerId = userId,
                                Target = new ItemTarget(itemId),
                                FollowedTagId = reactionTagId,
                                EventType = "Update",
                                PreviousWeight = existingRelation.Weight - deltaUpdate,
                                NewWeight = existingRelation.Weight
                            });

                            _ = await context.SaveChangesAsync();
                            return new ItemVoteResult(ItemVoteAction.Updated, existingRelation.Id, newWeight);
                        }
                }
        }
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

        tag = new Tag
        {
            Name = reactionTagName,
            IsSystem = true,
            OwnerId = userId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        _ = context.Tags.Add(tag);
        _ = await context.SaveChangesAsync();
        return tag;
    }
}

