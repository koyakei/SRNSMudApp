#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Components.Pages;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

#endregion

namespace SRNSMudApp.Services;

/// <summary>システムタグ (good/bad/真実/善/美) の取得または作成結果。</summary>
public sealed record SystemTagsResult(
    int? GoodTagId,
    int? BadTagId,
    int? ShinjiTagId = null,
    int? ZenTagId = null,
    int? BiTagId = null,
    bool Created = false);

/// <summary>タイムライン 1 ページ分。</summary>
public sealed record HomeTimelinePage(IReadOnlyList<TimelineFeedGroup> Groups, int TotalCount);

/// <summary>
///     Home コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IHomeDataProvider
{
    Task<List<int>> GetFollowedTagIdsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>全タグとタグ間リレーション (Tag / TargetTag 込み) を取得する。</summary>
    Task<(List<Tag> Tags, List<TagRelationToTag> Relations)> GetTagsAndRelationsAsync(CancellationToken cancellationToken = default);

    /// <summary>good / bad システムタグを取得し、無ければ作成する。</summary>
    Task<SystemTagsResult> EnsureSystemTagsAsync(string userId, CancellationToken cancellationToken = default);

    Task<HomeTimelinePage> LoadTimelineAsync(IReadOnlyList<int> followedTagIds, int startIndex, int count, CancellationToken cancellationToken = default);
}

public class HomeDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IHomeDataProvider
{
    public async Task<List<int>> GetFollowedTagIdsAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.UserTagFollows!
            .Where(f => f.OwnerId == userId)
            .Select(f => f.TagId)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<Tag> Tags, List<TagRelationToTag> Relations)> GetTagsAndRelationsAsync(CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync(cancellationToken);
        List<Tag> tags = await context.Tags
            .Include(t => t.Owner)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        List<TagRelationToTag> relations = await context.TagRelationToTags
            .Include(tr => tr.Tag)
            .Include(tr => tr.TargetTag)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (tags, relations);
    }

    public async Task<SystemTagsResult> EnsureSystemTagsAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync(cancellationToken);

        string[] systemTagNames = ["good", "bad", "真実", "善", "美"];
        Dictionary<string, Tag> existingTags = await context.Tags
            .Where(t => t.OwnerId == userId && t.IsSystem && systemTagNames.Contains(t.Name))
            .ToDictionaryAsync(t => t.Name, cancellationToken);

        var created = false;
        Tag goodTag = EnsureTag(context, existingTags.GetValueOrDefault("good"), "good", userId, ref created);
        Tag badTag = EnsureTag(context, existingTags.GetValueOrDefault("bad"), "bad", userId, ref created);
        Tag shinjiTag = EnsureTag(context, existingTags.GetValueOrDefault("真実"), "真実", userId, ref created);
        Tag zenTag = EnsureTag(context, existingTags.GetValueOrDefault("善"), "善", userId, ref created);
        Tag biTag = EnsureTag(context, existingTags.GetValueOrDefault("美"), "美", userId, ref created);

        if (created)
        {
            _ = await context.SaveChangesAsync(cancellationToken);
        }

        return new SystemTagsResult(goodTag.Id, badTag.Id, shinjiTag.Id, zenTag.Id, biTag.Id, created);
    }

    private static Tag EnsureTag(ApplicationDbContext context, Tag? existing, string name, string userId, ref bool created)
    {
        if (existing is not null)
        {
            return existing;
        }

        var newTag = new Tag
        {
            Name = name,
            IsSystem = true,
            OwnerId = userId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        _ = context.Tags.Add(newTag);
        created = true;
        return newTag;
    }

    public async Task<HomeTimelinePage> LoadTimelineAsync(
        IReadOnlyList<int> followedTagIds,
        int startIndex,
        int count,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<TimelineEvent> query = db.TimelineEvents!
            .Where(e => followedTagIds.Contains(e.FollowedTagId));

        var groupedQuery = query
            .GroupBy(e => e.TimelineTargetJson)
            .Select(g => new
            {
                TimelineTargetJson = g.Key,
                LatestEventDate = g.Max(e => e.CreatedDate)
            })
            .OrderByDescending(g => g.LatestEventDate);

        var totalGroups = await groupedQuery.CountAsync(cancellationToken);

        var pagedGroups = await groupedQuery
            .Skip(startIndex)
            .Take(count)
            .ToListAsync(cancellationToken);

        List<TimelineFeedGroup> feedGroups = [];
        foreach (var pg in pagedGroups)
        {
            var feedGroup = new TimelineFeedGroup
            {
                TimelineTargetJson = pg.TimelineTargetJson,
                LatestEventDate = pg.LatestEventDate,
                Events = await db.TimelineEvents!
                    .Where(e => followedTagIds.Contains(e.FollowedTagId) &&
                                e.TimelineTargetJson == pg.TimelineTargetJson)
                    .Include(e => e.FollowedTag)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken)
            };

            TimelineTarget? target = (feedGroup.Events.Count > 0 ? feedGroup.Events[0] : null)?.Target;
            switch (target)
            {
                case ItemTarget it:
                    feedGroup.Item = await db.Items!
                        .Include(i => i.Owner)
                        .Include(i => i.TagRelations)
                        .ThenInclude(tr => tr.Tag)
                        .ThenInclude(t => t.Owner)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(i => i.Id == it.TargetItemId, cancellationToken);
                    break;
                case TagTarget tt:
                    feedGroup.Tag = await db.Tags!
                        .Include(t => t.Owner)
                        .Include(t => t.TargetTagRelations!)
                        .ThenInclude(tr => tr.Tag)
                        .ThenInclude(t => t.Owner)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == tt.TargetTagId, cancellationToken);
                    break;
                default:
                    break;
            }

            if (feedGroup.Item != null || feedGroup.Tag != null)
            {
                feedGroups.Add(feedGroup);
            }
        }

        return new HomeTimelinePage(feedGroups, totalGroups);
    }
}