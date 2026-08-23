#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

// 名前空間の内側でエイリアスして Data.Tag を確実に解決させる
using SRNSMudApp.Data;

using Tag = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Services;

/// <summary>コントラクト管理ページの表示データ。</summary>
public sealed record ContractManagementPageData(
    List<TaggingRequestEntity> IncomingContracts,
    List<TaggingRequestEntity> OutgoingContracts);

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
}

public class ContractDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : IContractDataProvider
{
    public async Task<ContractManagementPageData> GetContractsAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();

        // Incoming: User is the Tag Owner, and contract is Proposed
        List<TaggingRequestEntity> incomingContracts = await dbContext.TaggingRequestEntities
            .Where(c => c.TagOwnerUserId == userId && c.Status == TradeStatus.Proposed &&
                        (c.ContractType == "Gratis" || c.ContractType == "Mutual"))
            .Include(c => c.TargetItem)
            .Include(c => c.RequestedTag)
            .OrderByDescending(c => c.CreatedDate)
            .ToListAsync();

        // Outgoing: User is the Requester
        List<TaggingRequestEntity> outgoingContracts = await dbContext.TaggingRequestEntities
            .Where(c => c.RequesterUserId == userId &&
                        (c.ContractType == "Gratis" || c.ContractType == "Mutual"))
            .Include(c => c.TargetItem)
            .Include(c => c.RequestedTag)
            .OrderByDescending(c => c.CreatedDate)
            .ToListAsync();

        return new ContractManagementPageData(incomingContracts, outgoingContracts);
    }

    public async Task<List<RightAsset>> GetAvailableRightAssetsAsync(string userId)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync();
        return await dbContext.RightAssets
            .Include(a => a.TargetTag)
            .Where(a => a.OwnerId == userId && !a.IsBurned)
            .ToListAsync();
    }

    public async Task<List<Item>> SearchItemsAsync(string? value, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(token);
        switch (string.IsNullOrEmpty(value))
        {
            case true:
                return await dbContext.Items.OrderByDescending(i => i.Id).Take(10).ToListAsync(token);
            default:
                return await dbContext.Items
                    .Where(i => i.Content.Contains(value))
                    .Take(10)
                    .ToListAsync(token);
        }
    }

    public async Task<List<Tag>> SearchTagsByNameAsync(string? value, CancellationToken token = default)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(token);
        switch (string.IsNullOrEmpty(value))
        {
            case true:
                return await dbContext.Tags.Take(10).ToListAsync(token);
            default:
                return await dbContext.Tags
                    .Where(t => t.Name.Contains(value))
                    .Take(10)
                    .ToListAsync(token);
        }
    }
}
