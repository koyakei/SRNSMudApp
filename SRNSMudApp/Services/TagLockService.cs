#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     タグのロック管理（n階層目までのロック、祖先ロック、兄弟ロック制約）を提供するサービス実装。
/// </summary>
public class TagLockService(IDbContextFactory<ApplicationDbContext> dbFactory) : ITagLockService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <inheritdoc />
    public async Task<int> GetLockedHierarchyLevelAsync(CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        TagLockSetting? setting = await context.TagLockSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return setting?.LockedHierarchyLevel ?? 0;
    }

    /// <inheritdoc />
    public async Task SetLockedHierarchyLevelAsync(int level, CancellationToken cancellationToken = default)
    {
        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "ロック階層レベルは0以上である必要があります。");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        TagLockSetting? setting = await context.TagLockSettings.FirstOrDefaultAsync(cancellationToken);

        if (setting is null)
        {
            setting = new TagLockSetting
            {
                LockedHierarchyLevel = level,
                UpdatedDate = DateTime.UtcNow
            };
            _ = context.TagLockSettings.Add(setting);
        }
        else
        {
            setting.LockedHierarchyLevel = level;
            setting.UpdatedDate = DateTime.UtcNow;
        }

        _ = await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsTagLockedAsync(int tagId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        Tag? tag = await context.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag is null)
        {
            return false;
        }

        if (tag.Name == Tag.RootTagName)
        {
            return true;
        }

        if (tag.IsLocked)
        {
            return true;
        }

        var lockedLevel = await GetLockedHierarchyLevelAsync(cancellationToken);
        return lockedLevel > 0 && tag.Node is not null && tag.Node.GetLevel() <= lockedLevel;
    }

    /// <inheritdoc />
    public async Task<bool> IsTagOrSiblingLockedAsync(int tagId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        Tag? tag = await context.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag is null)
        {
            return false;
        }

        if (tag.Name == Tag.RootTagName)
        {
            return true;
        }

        // 1. タグ自身が個別ロックされているか
        if (tag.IsLocked)
        {
            return true;
        }

        // 2. 階層レベルによるロック
        var lockedLevel = await GetLockedHierarchyLevelAsync(cancellationToken);
        if (lockedLevel > 0 && tag.Node is not null && tag.Node.GetLevel() <= lockedLevel)
        {
            return true;
        }

        // 3. 同じ親を持つ兄弟タグのいずれかが個別ロックされているか
        if (tag.Node is not null)
        {
            HierarchyId? parentNode = tag.Node.GetAncestor(1);
            if (parentNode != null)
            {
                var hasLockedSibling = await context.Tags
                    .AsNoTracking()
                    .AnyAsync(t => t.Id != tagId &&
                                   t.IsLocked &&
                                   (t.ParentTagId == tag.ParentTagId || (t.Node != null && t.Node.GetAncestor(1) == parentNode)),
                              cancellationToken);

                if (hasLockedSibling)
                {
                    return true;
                }
            }
        }
        else if (tag.ParentTagId.HasValue)
        {
            var hasLockedSibling = await context.Tags
                .AsNoTracking()
                .AnyAsync(t => t.Id != tagId &&
                               t.IsLocked &&
                               t.ParentTagId == tag.ParentTagId.Value,
                          cancellationToken);

            if (hasLockedSibling)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> IsChildCreationRestrictedAsync(int? parentTagId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        Tag? parentTag = null;
        if (parentTagId.HasValue)
        {
            parentTag = await context.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == parentTagId.Value, cancellationToken);
        }

        if (parentTag is null)
        {
            parentTag = await context.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Name == Tag.RootTagName, cancellationToken);
        }

        if (parentTag is null)
        {
            return false;
        }

        var lockedLevel = await GetLockedHierarchyLevelAsync(cancellationToken);

        // 新規子タグのレベル
        var newChildLevel = parentTag.Node is not null ? parentTag.Node.GetLevel() + 1 : 1;
        if (lockedLevel > 0 && newChildLevel <= lockedLevel)
        {
            return true;
        }

        // 親タグ配下に既に個別ロックされた子タグが存在するか（存在する場合、新規子タグはその兄弟となるため作成不能）
        if (parentTag.Node is not null)
        {
            HierarchyId parentNode = parentTag.Node;
            var hasLockedExistingChild = await context.Tags
                .AsNoTracking()
                .AnyAsync(t => t.IsLocked &&
                               (t.ParentTagId == parentTag.Id || (t.Node != null && t.Node.GetAncestor(1) == parentNode)),
                          cancellationToken);

            if (hasLockedExistingChild)
            {
                return true;
            }
        }
        else
        {
            var hasLockedExistingChild = await context.Tags
                .AsNoTracking()
                .AnyAsync(t => t.IsLocked && t.ParentTagId == parentTag.Id, cancellationToken);

            if (hasLockedExistingChild)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task LockAncestorsAsync(int tagId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        Tag? tag = await context.Tags.FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag?.Node is null)
        {
            return;
        }

        HierarchyId node = tag.Node;
        List<Tag> ancestors = await context.Tags
            .Where(t => t.Id != tagId &&
                        t.Name != Tag.RootTagName &&
                        node.IsDescendantOf(t.Node))
            .ToListAsync(cancellationToken);

        foreach (Tag ancestor in ancestors)
        {
            ancestor.IsLocked = true;
            ancestor.UpdatedDate = DateTime.UtcNow;
        }

        _ = await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UnlockAncestorsAsync(int tagId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        Tag? tag = await context.Tags.FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag?.Node is null)
        {
            return;
        }

        HierarchyId node = tag.Node;
        List<Tag> ancestors = await context.Tags
            .Where(t => t.Id != tagId &&
                        t.Name != Tag.RootTagName &&
                        node.IsDescendantOf(t.Node))
            .ToListAsync(cancellationToken);

        foreach (Tag ancestor in ancestors)
        {
            ancestor.IsLocked = false;
            ancestor.UpdatedDate = DateTime.UtcNow;
        }

        _ = await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> AreAncestorsLockedAsync(int tagId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        Tag? tag = await context.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag?.Node is null)
        {
            return false;
        }

        HierarchyId node = tag.Node;
        List<Tag> ancestors = await context.Tags
            .AsNoTracking()
            .Where(t => t.Id != tagId &&
                        t.Name != Tag.RootTagName &&
                        node.IsDescendantOf(t.Node))
            .ToListAsync(cancellationToken);

        return ancestors.Count > 0 && ancestors.All(a => a.IsLocked);
    }

    /// <inheritdoc />
    public async Task<bool> ToggleTagLockAsync(int tagId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        Tag? tag = await context.Tags.FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag is null || tag.Name == Tag.RootTagName)
        {
            return false;
        }

        tag.IsLocked = !tag.IsLocked;
        tag.UpdatedDate = DateTime.UtcNow;
        _ = await context.SaveChangesAsync(cancellationToken);

        return tag.IsLocked;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagLockItemDto>> GetAllTagsWithLockStatusAsync(CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        List<Tag> tags = await context.Tags
            .Include(t => t.Owner)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var lockedLevel = await GetLockedHierarchyLevelAsync(cancellationToken);

        var tagsById = tags.ToDictionary(t => t.Id);

        // 各親配下で個別ロックされているタグが存在するかマップを作成
        HashSet<int?> parentsWithLockedChildren = [.. tags
            .Where(t => t.IsLocked && t.ParentTagId.HasValue)
            .Select(t => t.ParentTagId)];

        HashSet<HierarchyId> parentNodesWithLockedChildren = [.. tags
            .Where(t => t.IsLocked && t.Node is not null && t.Node.GetLevel() > 0)
            .Select(t => t.Node!.GetAncestor(1))];

        List<TagLockItemDto> results = [];
        foreach (Tag t in tags.OrderBy(t => t.Node is not null ? t.Node.GetLevel() : 0).ThenBy(t => t.Name))
        {
            var level = t.Node?.GetLevel() ?? (t.ParentTagId is null ? 0 : 1);
            var isLevelLocked = lockedLevel > 0 && level > 0 && level <= lockedLevel;
            var isDirectlyLocked = t.IsLocked || t.Name == Tag.RootTagName;

            var isSiblingLocked = false;
            if (t.Node is not null && t.Node.GetLevel() > 0)
            {
                HierarchyId? parentNode = t.Node.GetAncestor(1);
                isSiblingLocked = parentNode is not null &&
                                  parentNodesWithLockedChildren.Contains(parentNode) &&
                                  tags.Any(sibling => sibling.Id != t.Id && sibling.IsLocked && sibling.Node is not null && sibling.Node.GetAncestor(1) == parentNode);
            }
            else if (t.ParentTagId.HasValue)
            {
                isSiblingLocked = parentsWithLockedChildren.Contains(t.ParentTagId.Value) &&
                                  tags.Any(sibling => sibling.Id != t.Id && sibling.IsLocked && sibling.ParentTagId == t.ParentTagId.Value);
            }

            var isEffective = isDirectlyLocked || isLevelLocked || isSiblingLocked;

            string? parentName = null;
            if (t.ParentTagId.HasValue && tagsById.TryGetValue(t.ParentTagId.Value, out Tag? parentTag))
            {
                parentName = parentTag.Name;
            }

            results.Add(new TagLockItemDto(
                t.Id,
                t.Name,
                level,
                t.ParentTagId,
                parentName,
                t.Owner?.UserName,
                isDirectlyLocked,
                isLevelLocked,
                isSiblingLocked,
                isEffective));
        }

        return results;
    }
}