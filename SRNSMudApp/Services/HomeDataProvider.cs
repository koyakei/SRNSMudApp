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
    Task<List<int>> GetFollowedTagIdsAsync(string userId);

    /// <summary>全タグとタグ間リレーション (Tag / TargetTag 込み) を取得する。</summary>
    Task<(List<Tag> Tags, List<TagRelationToTag> Relations)> GetTagsAndRelationsAsync();

    /// <summary>good / bad システムタグを取得し、無ければ作成する。</summary>
    Task<SystemTagsResult> EnsureSystemTagsAsync(string userId);

    Task<HomeTimelinePage> LoadTimelineAsync(IReadOnlyList<int> followedTagIds, int startIndex, int count);
}

public class HomeDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IHomeDataProvider
{
    public async Task<List<int>> GetFollowedTagIdsAsync(string userId)
    {
        await using ApplicationDbContext db = await dbFactory.CreateDbContextAsync();
        return await db.UserTagFollows!
            .Where(f => f.OwnerId == userId)
            .Select(f => f.TagId)
            .ToListAsync();
    }

    public async Task<(List<Tag> Tags, List<TagRelationToTag> Relations)> GetTagsAndRelationsAsync()
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        List<Tag> tags = await context.Tags
            .Include(t => t.Owner)
            .AsNoTracking()
            .ToListAsync();

        List<TagRelationToTag> relations = await context.TagRelationToTags
            .Include(tr => tr.Tag)
            .Include(tr => tr.TargetTag)
            .AsNoTracking()
            .ToListAsync();

        return (tags, relations);
    }

    public async Task<SystemTagsResult> EnsureSystemTagsAsync(string userId)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        Tag? goodTag =
            await context.Tags.FirstOrDefaultAsync(t => t.OwnerId == userId && t.Name == "good" && t.IsSystem);
        Tag? badTag =
            await context.Tags.FirstOrDefaultAsync(t => t.OwnerId == userId && t.Name == "bad" && t.IsSystem);
        Tag? shinjiTag =
            await context.Tags.FirstOrDefaultAsync(t => t.OwnerId == userId && t.Name == "真実" && t.IsSystem);
        Tag? zenTag =
            await context.Tags.FirstOrDefaultAsync(t => t.OwnerId == userId && t.Name == "善" && t.IsSystem);
        Tag? biTag =
            await context.Tags.FirstOrDefaultAsync(t => t.OwnerId == userId && t.Name == "美" && t.IsSystem);

        var created = false;
        if (goodTag is null)
        {
            goodTag = new Tag
            {
                Name = "good",
                IsSystem = true,
                OwnerId = userId,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            _ = context.Tags.Add(goodTag);
            created = true;
        }

        if (badTag is null)
        {
            badTag = new Tag
            {
                Name = "bad",
                IsSystem = true,
                OwnerId = userId,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            _ = context.Tags.Add(badTag);
            created = true;
        }

        if (shinjiTag is null)
        {
            shinjiTag = new Tag
            {
                Name = "真実",
                IsSystem = true,
                OwnerId = userId,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            _ = context.Tags.Add(shinjiTag);
            created = true;
        }

        if (zenTag is null)
        {
            zenTag = new Tag
            {
                Name = "善",
                IsSystem = true,
                OwnerId = userId,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            _ = context.Tags.Add(zenTag);
            created = true;
        }

        if (biTag is null)
        {
            biTag = new Tag
            {
                Name = "美",
                IsSystem = true,
                OwnerId = userId,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };
            _ = context.Tags.Add(biTag);
            created = true;
        }

        switch (created)
        {
            case true:
                _ = await context.SaveChangesAsync();
                return new SystemTagsResult(goodTag.Id, badTag.Id, shinjiTag.Id, zenTag.Id, biTag.Id, true);
            default:
                return new SystemTagsResult(goodTag!.Id, badTag!.Id, shinjiTag!.Id, zenTag!.Id, biTag!.Id, false);
        }
    }

    public async Task<HomeTimelinePage> LoadTimelineAsync(
        IReadOnlyList<int> followedTagIds,
        int startIndex,
        int count)
    {
        await using ApplicationDbContext db = await dbFactory.CreateDbContextAsync();

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

        var totalGroups = await groupedQuery.CountAsync();

        var pagedGroups = await groupedQuery
            .Skip(startIndex)
            .Take(count)
            .ToListAsync();

        var feedGroups = new List<TimelineFeedGroup>();
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
                    .ToListAsync()
            };

            TimelineTarget? target = (feedGroup.Events.Count > 0 ? feedGroup.Events[0] : null)?.Target;
            switch (target)
            {
                case Models.Unions.ItemTarget it:
                    feedGroup.Item = await db.Items!
                        .Include(i => i.Owner)
                        .Include(i => i.TagRelations)
                        .ThenInclude(tr => tr.Tag)
                        .ThenInclude(t => t.Owner)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(i => i.Id == it.TargetItemId);
                    break;
                case Models.Unions.TagTarget tt:
                    feedGroup.Tag = await db.Tags!
                        .Include(t => t.Owner)
                        .Include(t => t.TargetTagRelations!)
                        .ThenInclude(tr => tr.Tag)
                        .ThenInclude(t => t.Owner)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == tt.TargetTagId);
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