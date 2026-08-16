#region

using System.Text.Json;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

#endregion

namespace SRNSMudApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    // DbSet properties for all custom entities
    public DbSet<Item>? Items { get; set; }
    public DbSet<Tag>? Tags { get; set; }
    public DbSet<TagRelation>? TagRelations { get; set; }
    public DbSet<UserTagFollow>? UserTagFollows { get; set; }
    public DbSet<TagRelationToTag>? TagRelationToTags { get; set; }
    public DbSet<RightAsset>? RightAssets { get; set; }
    public DbSet<TagWeightLedger>? TagWeightLedgers { get; set; }
    public DbSet<TaggingRequestEntity>? TaggingRequestEntities { get; set; }
    public DbSet<PublicTradeOffer>? PublicTradeOffers { get; set; }
    public DbSet<TimelineEvent>? TimelineEvents { get; set; }
    public DbSet<Invitation>? Invitations { get; set; }
    public DbSet<TaggingRequestReply>? TaggingRequestReplies { get; set; }


    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        // ApplicationUserに対する多数の外部キーによる「複数カスケードパス」エラーを防ぐため、
        // ApplicationUserを参照する関連に対して OnDelete(DeleteBehavior.Restrict) を設定します。

        // 1. BaseEntityを継承するエンティティと Owner(ApplicationUser) のリレーション
        _ = builder.Entity<Item>()
            .HasOne(i => i.Owner)
            .WithMany()
            .HasForeignKey(i => i.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // アイテムのリプライ用自己参照リレーション
        _ = builder.Entity<Item>()
            .HasOne(i => i.ParentItem)
            .WithMany(i => i.Replies)
            .HasForeignKey(i => i.ParentItemId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<Tag>()
            .HasOne(t => t.Owner)
            .WithMany()
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<Tag>()
            .HasIndex(t => new { t.OwnerId, t.Name })
            .IsUnique();

        var embeddingComparer = new ValueComparer<float[]>(
            (c1, c2) => c1 != null && c2 != null ? Enumerable.SequenceEqual(c1, c2) : c1 == c2,
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToArray()
        );

        builder.Entity<Tag>()
            .Property(t => t.Embedding)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null)
            )
            .Metadata.SetValueComparer(embeddingComparer);

        _ = builder.Entity<TagRelation>()
            .HasOne(tr => tr.Owner)
            .WithMany()
            .HasForeignKey(tr => tr.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagRelationToTag>()
            .HasOne(tr => tr.Owner)
            .WithMany()
            .HasForeignKey(tr => tr.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagRelationToTag>()
            .HasOne(tr => tr.TargetTag)
            .WithMany(t => t.TargetTagRelations)
            .HasForeignKey(tr => tr.TargetTagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagRelationToTag>()
            .HasOne(tr => tr.Tag)
            .WithMany(t => t.SourceTagRelations)
            .HasForeignKey(tr => tr.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<UserTagFollow>()
            .HasOne(utf => utf.Owner)
            .WithMany()
            .HasForeignKey(utf => utf.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<UserTagFollow>()
            .HasOne(utf => utf.Tag)
            .WithMany()
            .HasForeignKey(utf => utf.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        // 4. TagRelationの中間テーブル設定
        // Item または Tag が削除された場合は中間テーブルのレコードも削除して問題ないため Cascade とします
        _ = builder.Entity<TagRelation>()
            .HasOne(tr => tr.Item)
            .WithMany(i => i.TagRelations)
            .HasForeignKey(tr => tr.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Entity<TagRelation>()
            .HasOne(tr => tr.Tag)
            .WithMany(t => t.TagRelations)
            .HasForeignKey(tr => tr.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        // -- BaseEntity の Owner に対する複数カスケードパス回避 --
        _ = builder.Entity<RightAsset>()
            .HasOne(r => r.Owner)
            .WithMany()
            .HasForeignKey(r => r.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagWeightLedger>()
            .HasOne(l => l.Owner)
            .WithMany()
            .HasForeignKey(l => l.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // -- TagWeightLedger のリレーション設定 --
        _ = builder.Entity<TagWeightLedger>()
            .HasOne(l => l.Tag)
            .WithMany(t => t.TagWeightLedgers)
            .HasForeignKey(l => l.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagWeightLedger>()
            .HasOne(l => l.ConsumedRightAsset)
            .WithMany() // ConsumedRightAssetId is optional now, and could potentially be null. Using WithMany() instead of WithOne() if an asset could theoretically be consumed multiple times? No, the rule is 1 asset -> 1 ledger, but EF might complain about optional 1:1 without a collection if not configured properly. Let's keep it simple: WithMany() on the principal side is easier since RightAsset doesn't have a navigation property back.
            .HasForeignKey(l => l.ConsumedRightAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagWeightLedger>()
            .HasOne(l => l.TagRelation)
            .WithMany()
            .HasForeignKey(l => l.SourceId)
            .OnDelete(DeleteBehavior.SetNull);

        // -- TimelineEvent のリレーション設定 --
        _ = builder.Entity<TimelineEvent>()
            .HasOne(e => e.Owner)
            .WithMany()
            .HasForeignKey(e => e.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);


        _ = builder.Entity<TimelineEvent>()
            .HasOne(e => e.FollowedTag)
            .WithMany()
            .HasForeignKey(e => e.FollowedTagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TimelineEvent>()
            .HasOne(e => e.TargetItem)
            .WithMany()
            .HasForeignKey(e => e.TargetItemId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Entity<TimelineEvent>()
            .HasOne(e => e.TargetTag)
            .WithMany()
            .HasForeignKey(e => e.TargetTagId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- TaggingRequestEntity TPH Configuration ---
        _ = builder.Entity<TaggingRequestEntity>()
            .ToTable("TaggingRequestContracts")
            .HasDiscriminator<string>("ContractType")
            .HasValue<GratisTaggingContract>("Gratis")
            .HasValue<MutualTaggingContract>("Mutual")
            .HasValue<PublicOfferTriggerContract>("Trigger")
            .HasValue<BountyTaggingContract>("Bounty");

        // Restrict BaseEntity.OwnerId to prevent multiple cascade paths
        _ = builder.Entity<TaggingRequestEntity>()
            .HasOne(e => e.Owner)
            .WithMany()
            .HasForeignKey(e => e.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict other relationships to avoid multiple cascade paths with Tags/Items
        _ = builder.Entity<TaggingRequestEntity>()
            .HasOne(e => e.TargetItem)
            .WithMany()
            .HasForeignKey(e => e.TargetItemId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TaggingRequestEntity>()
            .HasOne(e => e.RequestedTag)
            .WithMany()
            .HasForeignKey(e => e.RequestedTagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TaggingRequestEntity>()
            .HasOne(e => e.ConsumedRightAsset)
            .WithMany()
            .HasForeignKey(e => e.ConsumedRightAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // ITaggable mappings
        _ = builder.Entity<TaggingRequestEntity>()
            .HasMany(e => e.Tags)
            .WithMany();

        _ = builder.Entity<TaggingRequestReply>()
            .HasMany(e => e.Tags)
            .WithMany();

        _ = builder.Entity<BountyTaggingContract>()
            .HasOne(e => e.OfferedRewardAsset)
            .WithMany()
            .HasForeignKey(e => e.OfferedRewardAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<MutualTaggingContract>()
            .HasOne(e => e.OfferedTargetItem)
            .WithMany()
            .HasForeignKey(e => e.OfferedTargetItemId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<MutualTaggingContract>()
            .HasOne(e => e.OfferedTag)
            .WithMany()
            .HasForeignKey(e => e.OfferedTagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<RightAsset>()
            .HasOne(e => e.TargetTag)
            .WithMany()
            .HasForeignKey(e => e.TargetTagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<PublicTradeOffer>()
            .HasOne(e => e.Owner)
            .WithMany()
            .HasForeignKey(e => e.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<PublicTradeOffer>()
            .HasOne(e => e.OfferedTag)
            .WithMany()
            .HasForeignKey(e => e.OfferedTagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<PublicOfferTriggerContract>()
            .HasOne(e => e.TargetPublicTradeOffer)
            .WithMany()
            .HasForeignKey(e => e.TargetPublicTradeOfferId)
            .OnDelete(DeleteBehavior.Restrict);

        // リプライとリクエストのリレーション（カスケード削除）
        builder.Entity<TaggingRequestReply>()
            .HasOne(r => r.TaggingRequest)
            .WithMany(tr => tr.Replies)
            .HasForeignKey(r => r.TaggingRequestEntityId)
            .OnDelete(DeleteBehavior.Cascade);

        // リプライとユーザーのリレーション (BaseEntity の Owner プロパティに対する設定)
        // Note: OwnerId is inherited from BaseEntity.
        builder.Entity<TaggingRequestReply>()
            .HasOne(r => r.Owner)
            .WithMany()
            .HasForeignKey(r => r.OwnerId)
            .OnDelete(DeleteBehavior.Restrict); // BaseEntity.OwnerId is generally Restrict to prevent cascade issues.
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        EnforceTagWeightLimits();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        await EnforceTagWeightLimitsAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void EnforceTagWeightLimits()
    {
        var itemRelations = ChangeTracker.Entries<TagRelation>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified && e.Entity.Weight > 1)
            .ToList();

        var tagRelations = ChangeTracker.Entries<TagRelationToTag>()
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

        var restrictedTagIds = Tags
            .Where(t => tagIds.Contains(t.Id) && t.IsSystem && (t.Name == "good" || t.Name == "bad"))
            .Select(t => t.Id)
            .ToList();

        foreach (EntityEntry<TagRelation> entry in itemRelations.Where(entry => restrictedTagIds.Contains(entry.Entity.TagId)))
        {
            entry.Entity.Weight = 1;
        }

        foreach (EntityEntry<TagRelationToTag> entry in tagRelations.Where(entry => restrictedTagIds.Contains(entry.Entity.TagId)))
        {
            entry.Entity.Weight = 1;
        }
    }

    private async Task EnforceTagWeightLimitsAsync(CancellationToken cancellationToken = default)
    {
        var itemRelations = ChangeTracker.Entries<TagRelation>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified && e.Entity.Weight > 1)
            .ToList();

        var tagRelations = ChangeTracker.Entries<TagRelationToTag>()
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

        List<int> restrictedTagIds = await Tags
            .Where(t => tagIds.Contains(t.Id) && t.IsSystem && (t.Name == "good" || t.Name == "bad"))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (EntityEntry<TagRelation> entry in itemRelations.Where(entry => restrictedTagIds.Contains(entry.Entity.TagId)))
        {
            entry.Entity.Weight = 1;
        }

        foreach (EntityEntry<TagRelationToTag> entry in tagRelations.Where(entry => restrictedTagIds.Contains(entry.Entity.TagId)))
        {
            entry.Entity.Weight = 1;
        }
    }

    private void UpdateTimestamps()
    {
        // Entity Framework の変更トラッカーから追加・更新されたエンティティを取得
        IEnumerable<EntityEntry> entries = ChangeTracker
            .Entries()
            .Where(e => e.Entity is BaseEntity &&
                        e.State is EntityState.Added or EntityState.Modified);

        foreach (EntityEntry entityEntry in entries)
        {
            if (entityEntry.Entity is not BaseEntity baseEntity)
            {
                continue;
            }
            // 更新日時は常時現在時刻をセット
            baseEntity.UpdatedDate = DateTime.UtcNow;

            // 新規追加時のみ作成日時をセット
            if (entityEntry.State == EntityState.Added)
            {
                baseEntity.CreatedDate = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// RightAsset を消費して TagRelation を作成し、Ledger に記帳の上、CachedWeight を更新するアトミックトランザクション
    /// </summary>
    public async Task CreateTagRelationWithAtomicSwapAsync(
        int itemId, int tagId, int rightAssetId, int weightDelta, string currentUserId)
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await Database.BeginTransactionAsync();

        try
        {
            // 1. RightAsset の取得と Burn 検証
            RightAsset asset = await RightAssets.FindAsync(rightAssetId) ?? throw new InvalidOperationException("指定されたアセットが見つかりません。");
            if (asset.IsBurned)
            {
                throw new InvalidOperationException("このアセットはすでに消費（Burn）されています。");
            }

            if (asset.OwnerId != currentUserId)
            {
                throw new UnauthorizedAccessException("このアセットを消費する権限がありません。");
            }

            // RightAsset を Burn (論理削除)
            asset.IsBurned = true;
            asset.BurnedAt = DateTime.UtcNow;

            // 2. TagRelation の作成
            var relation = new TagRelation
            {
                ItemId = itemId,
                TagId = tagId,
                OwnerId = currentUserId,
                Weight = weightDelta
            };
            _ = TagRelations.Add(relation);

            _ = await SaveChangesAsync();

            // 3. Tag.CachedWeight の加算と以前の値の取得
            Tag tag = await Tags.FindAsync(tagId) ?? throw new InvalidOperationException("指定されたタグが見つかりません。");
            var previousWeight = tag.CachedWeight;
            tag.CachedWeight += weightDelta;
            var newWeight = tag.CachedWeight;

            // 4. 元帳 (Ledger) への記帳
            var ledger = new TagWeightLedger
            {
                TagId = tagId,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelation",
                SourceId = relation.Id,
                ConsumedRightAssetId = asset.Id,
                Delta = weightDelta,
                PreviousWeight = previousWeight,
                NewWeight = newWeight,
                IsOwnerAction = tag.OwnerId == currentUserId,
                Reason = "Atomic Swap via Contract",
                OwnerId = currentUserId
            };
            _ = TagWeightLedgers.Add(ledger);

            _ = TimelineEvents.Add(new TimelineEvent
            {
                OwnerId = currentUserId,
                TargetType = "Item",
                TargetItemId = itemId,
                FollowedTagId = tagId,
                EventType = "Insert",
                NewWeight = weightDelta
            });

            _ = await SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// タグのオーナーが RightAsset を自動発行して消費し、自身のタグを付与するシナリオ
    /// </summary>
    public async Task CreateFreeTagRelationAsync(
        int itemId, int tagId, string currentUserId)
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await Database.BeginTransactionAsync();

        try
        {
            // 1. Tag のオーナー権限検証
            Tag tag = await Tags.FindAsync(tagId) ?? throw new InvalidOperationException("指定されたタグが見つかりません。");
            if (tag.OwnerId != currentUserId)
            {
                throw new UnauthorizedAccessException("このタグを無償で付与する権限がありません（タグのオーナーではありません）。");
            }

            // 2. RightAsset の発行と即時消費 (Burn)
            var rightAsset = new RightAsset
            {
                OwnerId = currentUserId,
                TargetTagId = tagId,
                IsBurned = true,
                BurnedAt = DateTime.UtcNow
            };
            _ = RightAssets.Add(rightAsset);
            _ = await SaveChangesAsync(); // IDを発行するためにSave

            // 3. TagRelation の作成
            var relation = new TagRelation
            {
                ItemId = itemId,
                TagId = tagId,
                OwnerId = currentUserId,
                Weight = 1 // 基本値
            };
            _ = TagRelations.Add(relation);

            _ = await SaveChangesAsync();

            // 4. Tag.CachedWeight の更新と以前の値の取得
            var previousWeight = tag.CachedWeight;
            tag.CachedWeight += 1;
            var newWeight = tag.CachedWeight;

            // 5. 元帳 (Ledger) への記帳
            var ledger = new TagWeightLedger
            {
                TagId = tagId,
                TagNameSnapshot = tag.Name,
                SourceType = "TagRelation",
                SourceId = relation.Id,
                ConsumedRightAssetId = rightAsset.Id, // 必ずセットされる
                Delta = 1,
                PreviousWeight = previousWeight,
                NewWeight = newWeight,
                IsOwnerAction = true,
                Reason = "Owner Self-Tagging",
                OwnerId = currentUserId
            };
            _ = TagWeightLedgers.Add(ledger);

            _ = TimelineEvents.Add(new TimelineEvent
            {
                OwnerId = currentUserId,
                TargetType = "Item",
                TargetItemId = itemId,
                FollowedTagId = tagId,
                EventType = "Insert",
                NewWeight = 1
            });

            _ = await SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}