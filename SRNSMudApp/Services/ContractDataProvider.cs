#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

using Tag = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Services;

/// <summary>コントラクト管理ページの表示データ。</summary>
public sealed record ContractManagementPageData(
    IReadOnlyList<TaggingRequestEntity> IncomingContracts,
    IReadOnlyList<TaggingRequestEntity> OutgoingContracts);

/// <summary>バウンティボードの表示データ。</summary>
public sealed record BountyBoardData(
    IReadOnlyList<TaggingRequestEntity> Bounties,
    Dictionary<int, RightAsset> RewardAssets);

/// <summary>
///     コントラクト系コンポーネント (ContractManagement / ProposeContractDialog) 用の
///     データアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface IContractDataProvider
{
    /// <summary>ユーザーの受信 / 送信コントラクト (Proposed) を取得する。</summary>
    Task<ContractManagementPageData> GetContractsAsync(string userId);

    /// <summary>未消費の RightAsset (TargetTag 込み) を取得する。</summary>
    Task<List<RightAsset>> GetAvailableRightAssetsAsync(string userId);

    /// <summary>アイテムを内容部分一致で検索する。</summary>
    Task<List<Item>> SearchItemsAsync(string? value, CancellationToken token = default);

    /// <summary>タグを名前部分一致で検索する。</summary>
    Task<List<Tag>> SearchTagsByNameAsync(string? value, CancellationToken token = default);

    /// <summary>アクティブなバウンティと報酬アセットを取得する。</summary>
    Task<BountyBoardData> GetActiveBountiesAsync();

    /// <summary>アクティブな公開オファーを取得する。</summary>
    Task<List<PublicTradeOffer>> GetActivePublicOffersAsync();

    /// <summary>自分の公開オファーを取り下げる。所有者一致時のみ無効化される。</summary>
    Task<bool> DeactivatePublicOfferAsync(int offerId, string userId);

    /// <summary>指定ユーザー所有のタグを名前部分一致で検索する (最大 10 件)。</summary>
    Task<List<Tag>> SearchMyTagsAsync(string userId, string? value, CancellationToken token = default);

    Task CreatePublicOfferAsync(PublicTradeOffer offer);

    Task CreateBountyAsync(TaggingRequestEntity bounty);

    Task CreateTriggerContractAsync(TaggingRequestEntity triggerContract);

    /// <summary>条件に合う未消費 RightAsset (TargetTag 込み) を取得する。</summary>
    Task<List<RightAsset>> GetValidRightAssetsAsync(string userId, int? targetTagId = null, int? minAmount = null);

    Task<RightAsset?> GetRightAssetByIdAsync(int assetId);
}

public class ContractDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IContractDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    public async Task<ContractManagementPageData> GetContractsAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();

        // Incoming: User is the Tag Owner, and contract is Proposed
        List<TaggingRequestEntity> incomingContracts = await dbContext.TaggingRequestEntities
            .Where(c => c.TagOwnerUserId == userId && c.Status == TradeStatus.Proposed &&
                        (c.ContractType == "Gratis" || c.ContractType == "Mutual"))
            .Include(c => c.Target).ThenInclude(t => t.Item)
            .Include(c => c.RequestedTag)
            .OrderByDescending(c => c.CreatedDate)
            .ToListAsync();

        // Outgoing: User is the Requester
        List<TaggingRequestEntity> outgoingContracts = await dbContext.TaggingRequestEntities
            .Where(c => c.RequesterUserId == userId &&
                        (c.ContractType == "Gratis" || c.ContractType == "Mutual"))
            .Include(c => c.Target).ThenInclude(t => t.Item)
            .Include(c => c.RequestedTag)
            .OrderByDescending(c => c.CreatedDate)
            .ToListAsync();

        return new ContractManagementPageData(incomingContracts, outgoingContracts);
    }

    public async Task<List<RightAsset>> GetAvailableRightAssetsAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.RightAssets
            .Include(a => a.TargetTag)
            .Where(a => a.OwnerId == userId && !a.IsBurned)
            .ToListAsync();
    }

    public async Task<List<Item>> SearchItemsAsync(string? value, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(token);
        return string.IsNullOrEmpty(value) switch
        {
            true => await dbContext.Items.OrderByDescending(i => i.Id).Take(10).ToListAsync(token),
            _ => await dbContext.Items
                                .Where(i => i.Content.Contains(value))
                                .Take(10)
                                .ToListAsync(token),
        };
    }

    public async Task<List<Tag>> SearchTagsByNameAsync(string? value, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(token);
        return string.IsNullOrEmpty(value) switch
        {
            true => await dbContext.Tags.Take(10).ToListAsync(token),
            _ => await dbContext.Tags
                                .Where(t => t.Name.Contains(value))
                                .Take(10)
                                .ToListAsync(token),
        };
    }

    public async Task<BountyBoardData> GetActiveBountiesAsync()
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();

        List<TaggingRequestEntity> bounties = await dbContext.TaggingRequestEntities
            .Where(b => b.ContractType == "Bounty")
            .Include(b => b.Target).ThenInclude(t => t.Item)
            .Include(b => b.RequestedTag)
            .Where(b => b.Status == TradeStatus.Proposed)
            .OrderByDescending(b => b.CreatedDate)
            .AsNoTracking()
            .ToListAsync();

        var assetIds = bounties
            .Select(b => b.Payload is Models.Unions.BountyPayload bp ? bp.OfferedRewardAssetId : 0)
            .Where(id => id != 0)
            .Distinct()
            .ToList();

        List<RightAsset> assets = await dbContext.RightAssets!
            .Include(a => a.TargetTag)
            .Where(a => assetIds.Contains(a.Id))
            .AsNoTracking()
            .ToListAsync();

        return new BountyBoardData(bounties, assets.ToDictionary(a => a.Id));
    }

    public async Task<List<PublicTradeOffer>> GetActivePublicOffersAsync()
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.PublicTradeOffers!
            .Include(o => o.OfferedTag)
            .Include(o => o.Owner)
            .Where(o => o.IsActive)
            .OrderByDescending(o => o.CreatedDate)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<bool> DeactivatePublicOfferAsync(int offerId, string userId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        PublicTradeOffer? entity = await dbContext.PublicTradeOffers!.FindAsync(offerId);
        switch (entity != null && entity.OwnerId == userId)
        {
            case true:
                entity.IsActive = false;
                _ = await dbContext.SaveChangesAsync();
                return true;
            default:
                return false;
        }
    }

    public async Task<List<Tag>> SearchMyTagsAsync(
        string userId,
        string? value,
        CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(token);
        IQueryable<Tag> query = dbContext.Tags.Where(t => t.OwnerId == userId);

        if (!string.IsNullOrEmpty(value))
        {
            query = query.Where(t => t.Name.Contains(value));
        }

        return await query.Take(10).ToListAsync(token);
    }

    public async Task CreatePublicOfferAsync(PublicTradeOffer offer)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        _ = dbContext.PublicTradeOffers!.Add(offer);
        _ = await dbContext.SaveChangesAsync();
    }

    public async Task CreateBountyAsync(TaggingRequestEntity bounty)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        _ = dbContext.TaggingRequestEntities.Add(bounty);
        _ = await dbContext.SaveChangesAsync();
    }

    public async Task CreateTriggerContractAsync(TaggingRequestEntity triggerContract)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        _ = dbContext.TaggingRequestEntities.Add(triggerContract);
        _ = await dbContext.SaveChangesAsync();
    }

    public async Task<List<RightAsset>> GetValidRightAssetsAsync(
        string userId,
        int? targetTagId = null,
        int? minAmount = null)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        IQueryable<RightAsset> query = dbContext.RightAssets
            .Include(a => a.TargetTag)
            .Where(a => a.OwnerId == userId && !a.IsBurned);

        if (targetTagId.HasValue)
        {
            query = query.Where(a => a.TargetTagId == targetTagId.Value);
        }

        if (minAmount.HasValue)
        {
            query = query.Where(a => a.Amount >= minAmount.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<RightAsset?> GetRightAssetByIdAsync(int assetId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.RightAssets.Include(a => a.TargetTag).FirstOrDefaultAsync(a => a.Id == assetId);
    }
}