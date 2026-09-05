#region

using System.Text.Json;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

using SRNSMudApp.Data.Interceptors;
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
    public DbSet<TagEdge> TagEdges { get; set; } = null!;
    public DbSet<TagEdgeTagAttachment> TagEdgeTagAttachments { get; set; } = null!;
    public DbSet<TaggableTarget> TaggableTargets { get; set; } = null!;

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

        // --- TagEdge / TagEdgeTagAttachment Configuration ---
        _ = builder.Entity<TagEdge>()
            .HasOne(e => e.Owner)
            .WithMany()
            .HasForeignKey(e => e.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagEdge>()
            .HasOne(e => e.SourceTag)
            .WithMany()
            .HasForeignKey(e => e.SourceTagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagEdge>()
            .HasOne(e => e.TargetTag)
            .WithMany()
            .HasForeignKey(e => e.TargetTagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagEdge>()
            .HasIndex(e => new { e.OwnerId, e.SourceTagId, e.TargetTagId })
            .IsUnique();

        _ = builder.Entity<TagEdgeTagAttachment>()
            .HasOne(a => a.Owner)
            .WithMany()
            .HasForeignKey(a => a.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagEdgeTagAttachment>()
            .HasOne(a => a.TagEdge)
            .WithMany(e => e.TagAttachments)
            .HasForeignKey(a => a.TagEdgeId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Entity<TagEdgeTagAttachment>()
            .HasOne(a => a.Tag)
            .WithMany()
            .HasForeignKey(a => a.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagEdgeTagAttachment>()
            .HasOne(a => a.ConsumedRightAsset)
            .WithMany()
            .HasForeignKey(a => a.ConsumedRightAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagEdgeTagAttachment>()
            .HasIndex(a => new { a.TagEdgeId, a.TagId })
            .IsUnique();

        // --- TaggableTarget Configuration ---
        _ = builder.Entity<TaggableTarget>()
            .HasOne(t => t.Owner)
            .WithMany()
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<Item>()
            .HasOne(i => i.TagTarget)
            .WithOne(t => t.Item)
            .HasForeignKey<Item>(i => i.TagTargetId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Entity<Item>()
            .HasIndex(i => i.TagTargetId)
            .IsUnique();

        _ = builder.Entity<TagEdge>()
            .HasOne(e => e.TagTarget)
            .WithOne(t => t.TagEdge)
            .HasForeignKey<TagEdge>(e => e.TagTargetId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Entity<TagEdge>()
            .HasIndex(e => e.TagTargetId)
            .IsUnique();

        // --- TaggingRequestEntity Configuration ---
        _ = builder.Entity<TaggingRequestEntity>()
            .ToTable("TaggingRequestContracts");

        // Restrict BaseEntity.OwnerId to prevent multiple cascade paths
        _ = builder.Entity<TaggingRequestEntity>()
            .HasOne(e => e.Owner)
            .WithMany()
            .HasForeignKey(e => e.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Target (TaggableTarget) relationship with Restrict to avoid multiple cascade paths in SQL Server
        _ = builder.Entity<TaggingRequestEntity>()
            .HasOne(e => e.Target)
            .WithMany(t => t.TaggingRequests)
            .HasForeignKey(e => e.TargetId)
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

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var coreOptions = optionsBuilder.Options.FindExtension<CoreOptionsExtension>();
        if (coreOptions?.Interceptors?.Any(i => i is ApplicationDbSaveChangesInterceptor) != true)
        {
            optionsBuilder.AddInterceptors(new ApplicationDbSaveChangesInterceptor());
        }
    }
}