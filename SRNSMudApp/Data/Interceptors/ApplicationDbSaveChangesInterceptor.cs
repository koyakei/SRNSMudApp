using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SRNSMudApp.Data.Interceptors;

/// <summary>
///     ApplicationDbContext の SaveChanges / SaveChangesAsync 実行時に介入し、
///     TaggableTarget の整合性維持、タイムスタンプ更新、ルートタグ制約、タグウェイト制限などの
///     ドメイン不変条件を自動適用する EF Core インターセプター。
///     ApplicationDbContext の単一責任の原則 (SRP) を守り、DbContext をピュアな Unit of Work に保つ。
/// </summary>
public class ApplicationDbSaveChangesInterceptor(TimeProvider? timeProvider = null) : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is ApplicationDbContext context)
        {
            EnsureTaggableTargets(context);
            CleanupTaggableTargets(context);
            UpdateTimestamps(context);
            ValidateRootTagConstraint(context);
            EnforceTagWeightLimits(context);
        }

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is ApplicationDbContext context)
        {
            EnsureTaggableTargets(context);
            CleanupTaggableTargets(context);
            UpdateTimestamps(context);
            ValidateRootTagConstraint(context);
            await EnforceTagWeightLimitsAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private static void CleanupTaggableTargets(ApplicationDbContext context)
    {
        var deletedTaggables = context.ChangeTracker.Entries<ITaggable>()
            .Where(e => e.State == EntityState.Deleted)
            .ToList();

        foreach (var entry in deletedTaggables)
        {
            if (entry.Entity.TagTargetId > 0)
            {
                var target = context.TaggableTargets.Local.FirstOrDefault(t => t.Id == entry.Entity.TagTargetId)
                    ?? context.TaggableTargets.FirstOrDefault(t => t.Id == entry.Entity.TagTargetId);
                if (target != null)
                {
                    _ = context.TaggableTargets.Remove(target);
                }
            }
            else if (entry.Entity.TagTarget != null)
            {
                _ = context.TaggableTargets.Remove(entry.Entity.TagTarget);
            }
        }
    }

    private static void EnsureTaggableTargets(ApplicationDbContext context)
    {
        var itemEntries = context.ChangeTracker.Entries<Item>()
            .Where(e => e.State == EntityState.Added)
            .ToList();

        foreach (var entry in itemEntries)
        {
            if (entry.Entity.TagTarget == null && entry.Entity.TagTargetId == 0)
            {
                entry.Entity.TagTarget = new TaggableTarget
                {
                    OwnerId = entry.Entity.OwnerId,
                    TargetType = "Item"
                };
            }
        }

        var edgeEntries = context.ChangeTracker.Entries<TagEdge>()
            .Where(e => e.State == EntityState.Added)
            .ToList();

        foreach (var entry in edgeEntries)
        {
            if (entry.Entity.TagTarget == null && entry.Entity.TagTargetId == 0)
            {
                entry.Entity.TagTarget = new TaggableTarget
                {
                    OwnerId = entry.Entity.OwnerId,
                    TargetType = "TagEdge"
                };
            }
        }

        var requestEntries = context.ChangeTracker.Entries<TaggingRequestEntity>()
            .Where(e => e.State == EntityState.Added)
            .ToList();

        foreach (var entry in requestEntries)
        {
            if (entry.Entity.Target == null && entry.Entity.TargetId == 0)
            {
                var targetItem = entry.Entity.TargetItem;
                if (targetItem != null)
                {
                    if (targetItem.TagTarget == null && targetItem.TagTargetId == 0)
                    {
                        targetItem.TagTarget = new TaggableTarget
                        {
                            OwnerId = targetItem.OwnerId,
                            TargetType = "Item"
                        };
                    }

                    if (targetItem.TagTarget != null)
                    {
                        entry.Entity.Target = targetItem.TagTarget;
                    }
                    else if (targetItem.TagTargetId > 0)
                    {
                        entry.Entity.TargetId = targetItem.TagTargetId;
                    }
                }
                else if (entry.Entity.TargetItemId > 0)
                {
                    var item = context.Items.Local.FirstOrDefault(i => i.Id == entry.Entity.TargetItemId)
                        ?? context.Items.FirstOrDefault(i => i.Id == entry.Entity.TargetItemId);

                    if (item != null)
                    {
                        if (item.TagTarget == null && item.TagTargetId == 0)
                        {
                            item.TagTarget = new TaggableTarget
                            {
                                OwnerId = item.OwnerId,
                                TargetType = "Item"
                            };
                        }

                        if (item.TagTarget != null)
                        {
                            entry.Entity.Target = item.TagTarget;
                        }
                        else if (item.TagTargetId > 0)
                        {
                            entry.Entity.TargetId = item.TagTargetId;
                        }
                    }
                }
            }
        }
    }

    private void UpdateTimestamps(ApplicationDbContext context)
    {
        var entries = context.ChangeTracker
            .Entries()
            .Where(e => e.Entity is BaseEntity &&
                        e.State is EntityState.Added or EntityState.Modified);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (EntityEntry entityEntry in entries)
        {
            if (entityEntry.Entity is not BaseEntity baseEntity)
            {
                continue;
            }

            baseEntity.UpdatedDate = now;

            if (entityEntry.State == EntityState.Added)
            {
                baseEntity.CreatedDate = now;
            }
        }
    }

    private static void ValidateRootTagConstraint(ApplicationDbContext context)
    {
        Tag? rootTag = null;
        HierarchyId? rootNode = null;
        var rootTagId = 0;

        foreach (EntityEntry<Tag> entry in context.ChangeTracker.Entries<Tag>())
        {
            if (entry.State is EntityState.Added)
            {
                if (entry.Entity.Name == Tag.RootTagName)
                {
                    entry.Entity.Node = HierarchyId.GetRoot();
                    entry.Entity.ParentTagId = null;
                    continue;
                }

                if (entry.Entity.Node is null)
                {
                    if (rootTag is null && rootNode is null)
                    {
                        rootTag = context.Tags.Local.FirstOrDefault(t => t.Name == Tag.RootTagName)
                                  ?? context.Tags.FirstOrDefault(t => t.Name == Tag.RootTagName);
                        if (rootTag != null)
                        {
                            rootTagId = rootTag.Id;
                            rootNode = rootTag.Node;
                        }
                    }

                    if (rootNode != null)
                    {
                        if (!entry.Entity.ParentTagId.HasValue && rootTagId != 0)
                        {
                            entry.Entity.ParentTagId = rootTagId;
                        }

                        HierarchyId? lastChildNode = context.Tags.Local
                            .Where(t => t.ParentTagId == rootTagId || (t.Node != null && t.Node.GetAncestor(1) == rootNode))
                            .Select(t => t.Node)
                            .Concat(context.Tags.Where(t => t.ParentTagId == rootTagId || t.Node.GetAncestor(1) == rootNode).Select(t => t.Node))
                            .OrderByDescending(n => n)
                            .FirstOrDefault();

                        entry.Entity.Node = rootNode.GetDescendant(lastChildNode, null);
                    }
                    else
                    {
                        throw new InvalidOperationException($"親ノードを持たないルートタグは '{Tag.RootTagName}' 以外作成・更新できません。");
                    }
                }
                else if (entry.Entity.Node == HierarchyId.GetRoot())
                {
                    throw new InvalidOperationException($"親ノードを持たないルートタグは '{Tag.RootTagName}' 以外作成・更新できません。");
                }
            }
            else if (entry.State is EntityState.Modified)
            {
                if (entry.Entity.Name != Tag.RootTagName && entry.Entity.Node == HierarchyId.GetRoot())
                {
                    throw new InvalidOperationException($"親ノードを持たないルートタグは '{Tag.RootTagName}' 以外作成・更新できません。");
                }
            }
        }
    }

    private static void EnforceTagWeightLimits(ApplicationDbContext context)
    {
        var itemRelations = context.ChangeTracker.Entries<TagRelation>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified && e.Entity.Weight > 1)
            .ToList();

        var tagRelations = context.ChangeTracker.Entries<TagRelationToTag>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified && e.Entity.Weight > 1)
            .ToList();

        if (itemRelations.Count == 0 && tagRelations.Count == 0)
        {
            return;
        }

        var tagIds = itemRelations.Select(e => e.Entity.TagId)
            .Concat(tagRelations.Select(e => e.Entity.TagId))
            .Distinct()
            .ToList();

        var restrictedTagIds = context.Tags
            .Where(t => tagIds.Contains(t.Id) && t.IsSystem && (t.Name == "good" || t.Name == "bad"))
            .Select(t => t.Id)
            .ToList();

        foreach (EntityEntry<TagRelation> entry in itemRelations.Where(entry =>
                     restrictedTagIds.Contains(entry.Entity.TagId)))
        {
            entry.Entity.Weight = 1;
        }

        foreach (EntityEntry<TagRelationToTag> entry in tagRelations.Where(entry =>
                     restrictedTagIds.Contains(entry.Entity.TagId)))
        {
            entry.Entity.Weight = 1;
        }
    }

    private static async Task EnforceTagWeightLimitsAsync(ApplicationDbContext context, CancellationToken cancellationToken = default)
    {
        var itemRelations = context.ChangeTracker.Entries<TagRelation>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified && e.Entity.Weight > 1)
            .ToList();

        var tagRelations = context.ChangeTracker.Entries<TagRelationToTag>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified && e.Entity.Weight > 1)
            .ToList();

        if (itemRelations.Count == 0 && tagRelations.Count == 0)
        {
            return;
        }

        var tagIds = itemRelations.Select(e => e.Entity.TagId)
            .Concat(tagRelations.Select(e => e.Entity.TagId))
            .Distinct()
            .ToList();

        List<int> restrictedTagIds = await context.Tags
            .Where(t => tagIds.Contains(t.Id) && t.IsSystem && (t.Name == "good" || t.Name == "bad"))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (EntityEntry<TagRelation> entry in itemRelations.Where(entry =>
                     restrictedTagIds.Contains(entry.Entity.TagId)))
        {
            entry.Entity.Weight = 1;
        }

        foreach (EntityEntry<TagRelationToTag> entry in tagRelations.Where(entry =>
                     restrictedTagIds.Contains(entry.Entity.TagId)))
        {
            entry.Entity.Weight = 1;
        }
    }
}