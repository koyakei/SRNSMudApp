using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     タグの CachedWeight 更新およびウェイト台帳（TagWeightLedger）の記録を担当するドメインサービス実装。
/// </summary>
public class TagWeightLedgerService(IDbContextFactory<ApplicationDbContext>? dbFactory = null) : ITagWeightLedgerService
{
    private readonly IDbContextFactory<ApplicationDbContext>? _dbFactory = dbFactory;

    /// <summary>
    ///     依存関係注入なしの既定コンストラクタ（単体テスト向け）。
    /// </summary>
    public TagWeightLedgerService() : this(null)
    {
    }

    /// <inheritdoc />
    public void RecordItemTagWeightChange(
        ApplicationDbContext context,
        Tag tag,
        int itemId,
        string sourceType,
        int? sourceId,
        int delta,
        string reason,
        string userId,
        TagRelation? sourceRelation = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tag);

        var (prevWeight, newWeight) = tag.ApplyWeightDelta(delta);

        _ = context.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = tag.Id,
            TagNameSnapshot = tag.Name,
            ItemId = itemId,
            SourceType = sourceType,
            SourceId = sourceId,
            TagRelation = sourceRelation,
            CommentItemId = sourceRelation?.CommentItemId,
            CommentItem = sourceRelation?.CommentItem,
            PreviousWeight = prevWeight,
            NewWeight = newWeight,
            Delta = delta,
            IsOwnerAction = true,
            Reason = reason,
            OwnerId = userId
        });
    }

    /// <inheritdoc />
    public void RecordTagToTagWeightChange(
        ApplicationDbContext context,
        Tag tag,
        int targetTagId,
        string sourceType,
        int? sourceId,
        int delta,
        string reason,
        string userId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tag);

        var (prevWeight, newWeight) = tag.ApplyWeightDelta(delta);

        _ = context.TagWeightLedgers.Add(new TagWeightLedger
        {
            TagId = tag.Id,
            TagNameSnapshot = tag.Name,
            TargetTagId = targetTagId,
            SourceType = sourceType,
            SourceId = sourceId,
            PreviousWeight = prevWeight,
            NewWeight = newWeight,
            Delta = delta,
            IsOwnerAction = true,
            Reason = reason,
            OwnerId = userId
        });
    }

    /// <inheritdoc />
    public async Task<Item> AddCommentAsync(int ledgerId, string content, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (_dbFactory == null)
        {
            throw new InvalidOperationException("DbContextFactory is not configured.");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        TagWeightLedger? ledger = await context.TagWeightLedgers
            .Include(l => l.CommentItem)
            .Include(l => l.TagRelation)
                .ThenInclude(tr => tr!.CommentItem)
            .FirstOrDefaultAsync(l => l.Id == ledgerId);

        if (ledger is null)
        {
            throw new InvalidOperationException($"TagWeightLedger #{ledgerId} not found.");
        }

        DateTime now = DateTime.UtcNow;
        Item? baseCommentItem = ledger.EffectiveCommentItem;
        string ledgerOwnerId = !string.IsNullOrEmpty(ledger.OwnerId) ? ledger.OwnerId : userId;

        if (baseCommentItem is null)
        {
            if (userId == ledgerOwnerId)
            {
                // タグ履歴の Owner 本人がコメントする場合、直接そのアイテムとして作成する
                var newItem = new Item
                {
                    Content = content,
                    OwnerId = userId,
                    CreatedDate = now,
                    UpdatedDate = now,
                    ItemKindJson = JsonSerializer.Serialize(new TagCommentItem(ledger.ItemId ?? 0, ledger.TagId))
                };
                _ = context.Items.Add(newItem);
                await context.SaveChangesAsync();

                ledger.CommentItemId = newItem.Id;
                ledger.CommentItem = newItem;
                await context.SaveChangesAsync();
                return newItem;
            }
            else
            {
                // Owner 以外のユーザーがコメントする場合、Owner のベースアイテムを作成し、そのリプライとしてコメントを作成する
                var baseItem = new Item
                {
                    Content = string.Empty,
                    OwnerId = ledgerOwnerId,
                    CreatedDate = now,
                    UpdatedDate = now,
                    ItemKindJson = JsonSerializer.Serialize(new TagCommentItem(ledger.ItemId ?? 0, ledger.TagId))
                };
                _ = context.Items.Add(baseItem);
                await context.SaveChangesAsync();

                ledger.CommentItemId = baseItem.Id;
                ledger.CommentItem = baseItem;

                var replyItem = new Item
                {
                    Content = content,
                    OwnerId = userId,
                    ParentItemId = baseItem.Id,
                    CreatedDate = now,
                    UpdatedDate = now,
                    ItemKindJson = JsonSerializer.Serialize(new ReplyItem(baseItem.Id))
                };
                _ = context.Items.Add(replyItem);
                await context.SaveChangesAsync();
                return replyItem;
            }
        }

        // すでにベースアイテムが存在する場合
        // もしベースアイテムの Owner 本人で、かつ Content が空の場合は、そのアイテムの Content を設定する
        if (baseCommentItem.OwnerId == userId && string.IsNullOrEmpty(baseCommentItem.Content))
        {
            baseCommentItem.Content = content;
            baseCommentItem.UpdatedDate = now;
            await context.SaveChangesAsync();
            return baseCommentItem;
        }

        // それ以外（Owner 以外のユーザー、またはすでに Content がある場合の追加コメント）は
        // baseCommentItem に対するリプライとしてコメントを作成する
        var reply = new Item
        {
            Content = content,
            OwnerId = userId,
            ParentItemId = baseCommentItem.Id,
            CreatedDate = now,
            UpdatedDate = now,
            ItemKindJson = JsonSerializer.Serialize(new ReplyItem(baseCommentItem.Id))
        };
        _ = context.Items.Add(reply);
        await context.SaveChangesAsync();
        return reply;
    }

    /// <inheritdoc />
    public async Task<Item> EnsureCommentItemAsync(int ledgerId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (_dbFactory == null)
        {
            throw new InvalidOperationException("DbContextFactory is not configured.");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        TagWeightLedger? ledger = await context.TagWeightLedgers
            .Include(l => l.CommentItem)
            .Include(l => l.TagRelation)
                .ThenInclude(tr => tr!.CommentItem)
            .FirstOrDefaultAsync(l => l.Id == ledgerId);

        if (ledger is null)
        {
            throw new InvalidOperationException($"TagWeightLedger #{ledgerId} not found.");
        }

        Item? targetItem = ledger.EffectiveCommentItem;
        if (targetItem is not null)
        {
            return targetItem;
        }

        DateTime now = DateTime.UtcNow;
        string ownerId = !string.IsNullOrEmpty(ledger.OwnerId) ? ledger.OwnerId : userId;
        targetItem = new Item
        {
            Content = string.Empty,
            OwnerId = ownerId,
            CreatedDate = now,
            UpdatedDate = now,
            ItemKindJson = JsonSerializer.Serialize(new TagCommentItem(ledger.ItemId ?? 0, ledger.TagId))
        };
        _ = context.Items.Add(targetItem);
        await context.SaveChangesAsync();

        ledger.CommentItemId = targetItem.Id;
        ledger.CommentItem = targetItem;
        await context.SaveChangesAsync();

        return targetItem;
    }

    /// <inheritdoc />
    public async Task<TagRelation> AddTagAsync(int ledgerId, int tagId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (_dbFactory == null)
        {
            throw new InvalidOperationException("DbContextFactory is not configured.");
        }

        Item targetItem = await EnsureCommentItemAsync(ledgerId, userId);

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        DateTime now = DateTime.UtcNow;

        Tag? tag = await context.Tags.FindAsync(tagId);
        if (tag is null)
        {
            throw new InvalidOperationException($"Tag #{tagId} not found.");
        }

        TagRelation? existing = await context.TagRelations
            .FirstOrDefaultAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tagId && tr.OwnerId == userId);

        if (existing is not null)
        {
            existing.Weight += 1;
            existing.UpdatedDate = now;
            _ = tag.ApplyWeightDelta(1);
            await context.SaveChangesAsync();
            return existing;
        }

        var newRelation = new TagRelation
        {
            ItemId = targetItem.Id,
            TagId = tagId,
            Weight = 1,
            OwnerId = userId,
            CreatedDate = now,
            UpdatedDate = now
        };
        _ = context.TagRelations.Add(newRelation);
        _ = tag.ApplyWeightDelta(1);
        await context.SaveChangesAsync();
        return newRelation;
    }

    /// <inheritdoc />
    public async Task<TagWeightLedger> EnsureLedgerRecordAsync(TagWeightLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        if (_dbFactory == null)
        {
            throw new InvalidOperationException("DbContextFactory is not configured.");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        if (ledger.Id > 0)
        {
            TagWeightLedger? existing = await context.TagWeightLedgers
                .Include(l => l.CommentItem)
                .Include(l => l.TagRelation)
                    .ThenInclude(tr => tr!.CommentItem)
                .FirstOrDefaultAsync(l => l.Id == ledger.Id);
            if (existing is not null)
            {
                return existing;
            }
        }

        // ledger.Id == 0 の場合、もし SourceId (TagRelationId) があれば同一の台帳が既にないか確認
        if (ledger.SourceId.HasValue)
        {
            TagWeightLedger? bySource = await context.TagWeightLedgers
                .Include(l => l.CommentItem)
                .Include(l => l.TagRelation)
                    .ThenInclude(tr => tr!.CommentItem)
                .FirstOrDefaultAsync(l => l.SourceId == ledger.SourceId.Value && l.SourceType == "TagRelationInsert");
            if (bySource is not null)
            {
                return bySource;
            }
        }

        // DB に台帳レコードを新規永続化する
        DateTime now = DateTime.UtcNow;
        var newLedger = new TagWeightLedger
        {
            TagId = ledger.TagId,
            TagNameSnapshot = !string.IsNullOrEmpty(ledger.TagNameSnapshot)
                ? ledger.TagNameSnapshot
                : (ledger.Tag?.Name ?? "タグ"),
            ItemId = ledger.ItemId,
            SourceType = !string.IsNullOrEmpty(ledger.SourceType) ? ledger.SourceType : "TagRelationInsert",
            SourceId = ledger.SourceId,
            Delta = ledger.Delta != 0 ? ledger.Delta : 1,
            PreviousWeight = ledger.PreviousWeight,
            NewWeight = ledger.NewWeight,
            IsOwnerAction = ledger.IsOwnerAction,
            Reason = !string.IsNullOrEmpty(ledger.Reason) ? ledger.Reason : "タグの新規追加",
            OwnerId = ledger.OwnerId,
            CommentItemId = ledger.CommentItemId,
            CreatedDate = ledger.CreatedDate != default ? ledger.CreatedDate : now,
            UpdatedDate = now
        };

        _ = context.TagWeightLedgers.Add(newLedger);
        await context.SaveChangesAsync();

        return newLedger;
    }

    /// <inheritdoc />
    public async Task<Item> AddCommentAsync(TagWeightLedger ledger, string content, string userId)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        TagWeightLedger persistedLedger = await EnsureLedgerRecordAsync(ledger);
        return await AddCommentAsync(persistedLedger.Id, content, userId);
    }

    /// <inheritdoc />
    public async Task<TagRelation> AddTagAsync(TagWeightLedger ledger, int tagId, string userId)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        TagWeightLedger persistedLedger = await EnsureLedgerRecordAsync(ledger);
        return await AddTagAsync(persistedLedger.Id, tagId, userId);
    }

    /// <inheritdoc />
    public async Task<Item> EnsureCommentItemAsync(TagWeightLedger ledger, string userId)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        TagWeightLedger persistedLedger = await EnsureLedgerRecordAsync(ledger);
        return await EnsureCommentItemAsync(persistedLedger.Id, userId);
    }
}