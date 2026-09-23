#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     RightAsset の状況閲覧・集計データを提供する DataProvider インターフェース。
///     Blazor コンポーネントからの直接的な DbContext 依存を分離する。
/// </summary>
public interface IRightAssetDataProvider
{
    /// <summary>
    ///     指定されたタグにおける RightAsset の保有状況（誰がどれだけ持っているか等）を取得する。
    /// </summary>
    /// <param name="tagId">対象タグID。</param>
    /// <param name="cancellationToken">キャンセラレーショントークン。</param>
    /// <returns>RightAsset の概要データ。タグが存在しない場合は null。</returns>
    Task<RightAssetOverviewData?> GetRightAssetOverviewByTagIdAsync(int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     RightAsset が発行されている代表的なタグ一覧を取得する（タグ未選択時のクイック選択用）。
    /// </summary>
    /// <param name="count">取得最大件数。</param>
    /// <param name="cancellationToken">キャンセラレーショントークン。</param>
    /// <returns>タグごとの RightAsset 概要一覧。</returns>
    Task<IReadOnlyList<TagRightAssetSummary>> GetTopTagsWithRightAssetsAsync(int count = 10, CancellationToken cancellationToken = default);
}

/// <summary>
///     RightAsset の保有状況データを集計・取得する DataProvider 実装。
/// </summary>
public class RightAssetDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IRightAssetDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <inheritdoc />
    public async Task<RightAssetOverviewData?> GetRightAssetOverviewByTagIdAsync(int tagId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Tag? tag = await dbContext.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);

        if (tag is null)
        {
            return null;
        }

        List<RightAsset> assets = await dbContext.RightAssets
            .AsNoTracking()
            .Include(a => a.Owner)
            .Where(a => a.TargetTagId == tagId)
            .OrderByDescending(a => a.CreatedDate)
            .ToListAsync(cancellationToken);

        // ユーザーごとの保有状況集計
        var holders = assets
            .GroupBy(a => a.OwnerId)
            .Select(group =>
            {
                var activeAssets = group.Where(a => !a.IsBurned && a.Amount > 0).ToList();
                var burnedAssets = group.Where(a => a.IsBurned).ToList();
                var owner = group.FirstOrDefault()?.Owner;
                var userName = owner?.UserName;
                var latestUpdate = group.Max(a => (DateTime?)a.UpdatedDate);

                return new RightAssetHolderSummary(
                    UserId: group.Key,
                    UserName: userName,
                    TotalAmount: activeAssets.Sum(a => a.Amount),
                    ActiveAssetCount: activeAssets.Count,
                    BurnedAmount: burnedAssets.Sum(a => a.Amount),
                    BurnedAssetCount: burnedAssets.Count,
                    LastUpdated: latestUpdate
                );
            })
            .OrderByDescending(h => h.TotalAmount)
            .ThenByDescending(h => h.ActiveAssetCount)
            .ToList();

        // 個別アセット明細 DTO
        var assetDtos = assets.Select(a =>
        {
            var status = a.IsBurned
                ? "燃焼済 (Burned)"
                : a.Amount > 0
                    ? "有効"
                    : "残高0";

            return new RightAssetDetailDto(
                Id: a.Id,
                OwnerId: a.OwnerId,
                OwnerName: a.Owner?.UserName,
                Amount: a.Amount,
                IsBurned: a.IsBurned,
                StatusSummary: status,
                CreatedDate: a.CreatedDate,
                UpdatedDate: a.UpdatedDate
            );
        }).ToList();

        var totalHoldersCount = holders.Count(h => h.TotalAmount > 0);
        var totalActiveAmount = assets.Where(a => !a.IsBurned && a.Amount > 0).Sum(a => a.Amount);
        var totalActiveAssetsCount = assets.Count(a => !a.IsBurned && a.Amount > 0);
        var totalBurnedAmount = assets.Where(a => a.IsBurned).Sum(a => a.Amount);

        return new RightAssetOverviewData(
            Tag: tag,
            TotalHoldersCount: totalHoldersCount,
            TotalActiveAmount: totalActiveAmount,
            TotalActiveAssetsCount: totalActiveAssetsCount,
            TotalBurnedAmount: totalBurnedAmount,
            Holders: holders,
            Assets: assetDtos
        );
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagRightAssetSummary>> GetTopTagsWithRightAssetsAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // 有効な RightAsset を持つタグをグループ化して集計
        var topTagIds = await dbContext.RightAssets
            .AsNoTracking()
            .Where(a => !a.IsBurned && a.Amount > 0)
            .GroupBy(a => a.TargetTagId)
            .Select(g => new
            {
                TagId = g.Key,
                TotalAmount = g.Sum(a => a.Amount),
                HolderCount = g.Select(a => a.OwnerId).Distinct().Count()
            })
            .OrderByDescending(x => x.TotalAmount)
            .Take(count)
            .ToListAsync(cancellationToken);

        if (topTagIds.Count == 0)
        {
            // 有効なアセットがない場合は、全 RightAssets から取得（燃焼済み含む）
            var fallbackTagIds = await dbContext.RightAssets
                .AsNoTracking()
                .GroupBy(a => a.TargetTagId)
                .Select(g => new
                {
                    TagId = g.Key,
                    TotalAmount = g.Sum(a => a.Amount),
                    HolderCount = g.Select(a => a.OwnerId).Distinct().Count()
                })
                .OrderByDescending(x => x.HolderCount)
                .Take(count)
                .ToListAsync(cancellationToken);

            if (fallbackTagIds.Count == 0)
            {
                return [];
            }

            var fallbackIds = fallbackTagIds.Select(x => x.TagId).ToList();
            var tagsMap = await dbContext.Tags
                .AsNoTracking()
                .Where(t => fallbackIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, cancellationToken);

            return fallbackTagIds
                .Where(x => tagsMap.ContainsKey(x.TagId))
                .Select(x => new TagRightAssetSummary(
                    TagId: x.TagId,
                    TagName: tagsMap[x.TagId].Name,
                    TagContent: tagsMap[x.TagId].Content,
                    TotalAmount: x.TotalAmount,
                    HolderCount: x.HolderCount
                ))
                .ToList();
        }

        var ids = topTagIds.Select(x => x.TagId).ToList();
        var tags = await dbContext.Tags
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        return topTagIds
            .Where(x => tags.ContainsKey(x.TagId))
            .Select(x => new TagRightAssetSummary(
                TagId: x.TagId,
                TagName: tags[x.TagId].Name,
                TagContent: tags[x.TagId].Content,
                TotalAmount: x.TotalAmount,
                HolderCount: x.HolderCount
            ))
            .ToList();
    }
}