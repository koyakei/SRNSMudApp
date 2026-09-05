#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     NotificationsPage コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface INotificationsDataProvider
{
    /// <summary>通知に関連付けられたアイテムを関連データ込みで取得する。</summary>
    Task<List<Item>> GetAssociatedItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default);

    /// <summary>ユーザー向けの各種通知生成に必要な未加工エンティティ群を取得する。</summary>
    Task<NotificationRawData> GetNotificationRawDataAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>通知を既読として記録する。</summary>
    Task MarkAsReadAsync(string userId, int sourceId, string sourceType, CancellationToken cancellationToken = default);
}

/// <summary>
///     NotificationsPage コンポーネント用データアクセスプロバイダーの実装。
/// </summary>
/// <param name="dbFactory">DbContext ファクトリ。</param>
public class NotificationsDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory)
    : INotificationsDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <inheritdoc />
    public async Task<List<Item>> GetAssociatedItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default)
    {
        switch (itemIds.Count)
        {
            case 0:
                return [];
            default:
                break;
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await context.Items
            .AsNoTracking()
            .Include(i => i.Owner)
            .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
            .ThenInclude(t => t.Owner)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.Target)
            .ThenInclude(t => t.Item)
            .Include(i => i.AsRequestOf)
            .ThenInclude(r => r.RequestedTag)
            .Where(i => itemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NotificationRawData> GetNotificationRawDataAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // 1. Tag Requests targeting the user
        List<TaggingRequestEntity> tagRequests = await context.TaggingRequestEntities!
            .AsNoTracking()
            .Include(r => r.Target).ThenInclude(t => t.Item)
            .Include(r => r.RequestedTag)
            .Where(r => r.RequesterUserId != userId &&
                        (r.Target.OwnerId == userId || r.RequestedTag.OwnerId == userId))
            .ToListAsync(cancellationToken);

        // 2. Item Replies targeting the user
        List<Item> itemReplies = await context.Items!
            .AsNoTracking()
            .Include(i => i.ParentItem)
            .Include(i => i.Owner)
            .Where(i => i.ParentItemId != 0 && i.ParentItem!.OwnerId == userId && i.OwnerId != userId)
            .ToListAsync(cancellationToken);

        // 3. Rejected requests for the user
        List<TaggingRequestEntity> rejectedRequests = await context.TaggingRequestEntities!
            .AsNoTracking()
            .Include(r => r.Target).ThenInclude(t => t.Item)
            .Include(r => r.RequestedTag)
            .Where(r => r.RequesterUserId == userId && r.Status == TradeStatus.Rejected)
            .ToListAsync(cancellationToken);

        // 4. Approved requests for the user
        List<TaggingRequestEntity> approvedRequests = await context.TaggingRequestEntities!
            .AsNoTracking()
            .Include(r => r.Target).ThenInclude(t => t.Item)
            .Include(r => r.RequestedTag)
            .Where(r => r.RequesterUserId == userId && r.Status == TradeStatus.Executed)
            .ToListAsync(cancellationToken);

        // 5. Replies to the user's requests
        List<Item> requestReplies = await context.Items!
            .AsNoTracking()
            .Include(i => i.TaggingRequest)
            .Include(i => i.Owner)
            .Where(i => i.TaggingRequestEntityId != 0 &&
                        i.TaggingRequest!.RequesterUserId == userId &&
                        i.OwnerId != userId)
            .ToListAsync(cancellationToken);

        List<NotificationReadState> readStates = await context.NotificationReadStates!
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .ToListAsync(cancellationToken);

        return new NotificationRawData(
            tagRequests,
            itemReplies,
            rejectedRequests,
            approvedRequests,
            requestReplies,
            readStates);
    }

    /// <inheritdoc />
    public async Task MarkAsReadAsync(string userId, int sourceId, string sourceType, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        NotificationReadState? existing = await context.NotificationReadStates
            .FirstOrDefaultAsync(n => n.UserId == userId && n.SourceId == sourceId && n.SourceType == sourceType, cancellationToken);

        if (existing is null)
        {
            _ = context.NotificationReadStates.Add(new NotificationReadState
            {
                UserId = userId,
                SourceId = sourceId,
                SourceType = sourceType,
                ReadAt = DateTimeOffset.UtcNow
            });
            _ = await context.SaveChangesAsync(cancellationToken);
        }
    }
}