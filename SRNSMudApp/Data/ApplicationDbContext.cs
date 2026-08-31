#region

using System.Text.Json;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

using SRNSMudApp.Models.Unions;

#endregion

namespace SRNSMudApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    // DbSet properties for all custom entities
    public DbSet<Item> Items { get; set; } = null!;
    public DbSet<Tag> Tags { get; set; } = null!;
    public DbSet<TagRelation> TagRelations { get; set; } = null!;
    public DbSet<UserTagFollow> UserTagFollows { get; set; } = null!;
    public DbSet<TagRelationToTag> TagRelationToTags { get; set; } = null!;
    public DbSet<RightAsset> RightAssets { get; set; } = null!;
    public DbSet<TagWeightLedger> TagWeightLedgers { get; set; } = null!;
    public DbSet<TaggingRequestEntity> TaggingRequestEntities { get; set; } = null!;
    public DbSet<PublicTradeOffer> PublicTradeOffers { get; set; } = null!;
    public DbSet<TimelineEvent> TimelineEvents { get; set; } = null!;
    public DbSet<Invitation> Invitations { get; set; } = null!;
    public DbSet<NotificationReadState> NotificationReadStates { get; set; } = null!;

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

        // Node (hierarchyid) のインデックス設定
        _ = builder.Entity<Tag>()
            .HasIndex(t => t.Node)
            .HasDatabaseName("IX_Tags_Node");

        _ = builder.Entity<Tag>()
            .HasIndex(t => new { t.OwnerId, t.Name })
            .IsUnique();

        _ = builder.Entity<Tag>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_Tags_RootOnlyForUniversalTag",
                "[Name] = N'全て∀' OR [Node] <> hierarchyid::GetRoot()"));

        var embeddingComparer = new ValueComparer<float[]>(
            (c1, c2) => c1 != null && c2 != null ? Enumerable.SequenceEqual(c1, c2) : c1 == c2,
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToArray()
        );

        builder.Entity<Tag>()
            .Property(t => t.Embedding)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null! : JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null)!
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
            .IsRequired(false)
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

        // --- TaggingRequestEntity Configuration ---
        _ = builder.Entity<TaggingRequestEntity>()
            .ToTable("TaggingRequestContracts");

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
            .HasOne(e => e.RequestItem)
            .WithOne(i => i.AsRequestOf)
            .HasForeignKey<TaggingRequestEntity>(e => e.RequestItemId)
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

        // Removed merged subclass navigation properties as they are now in ContractPayloadJson

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

        // リクエストへのリプライとリクエストのリレーション（カスケード削除）
        _ = builder.Entity<Item>()
            .HasOne(i => i.TaggingRequest)
            .WithMany(tr => tr.Replies)
            .HasForeignKey(i => i.TaggingRequestEntityId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Entity<Invitation>()
            .HasOne(i => i.InvitedByAdmin)
            .WithMany()
            .HasForeignKey(i => i.InvitedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);
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
        ValidateRootTagConstraint();
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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateRootTagConstraint();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateRootTagConstraint();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateRootTagConstraint()
    {
        Tag? rootTag = null;
        HierarchyId? rootNode = null;
        var rootTagId = 0;

        foreach (EntityEntry<Tag> entry in ChangeTracker.Entries<Tag>())
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
                        rootTag = Tags.Local.FirstOrDefault(t => t.Name == Tag.RootTagName)
                                  ?? Tags.FirstOrDefault(t => t.Name == Tag.RootTagName);
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

                        HierarchyId? lastChildNode = Tags.Local
                            .Where(t => t.ParentTagId == rootTagId || (t.Node != null && t.Node.GetAncestor(1) == rootNode))
                            .Select(t => t.Node)
                            .Concat(Tags.Where(t => t.ParentTagId == rootTagId || t.Node.GetAncestor(1) == rootNode).Select(t => t.Node))
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
}