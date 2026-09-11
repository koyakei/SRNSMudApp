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
    public DbSet<UserFollow> UserFollows { get; set; } = null!;
    public DbSet<TagRelationToTag> TagRelationToTags { get; set; } = null!;
    public DbSet<RightAsset> RightAssets { get; set; } = null!;
    public DbSet<TagWeightLedger> TagWeightLedgers { get; set; } = null!;
    public DbSet<TaggingRequestEntity> TaggingRequestEntities { get; set; } = null!;
    public DbSet<PublicTradeOffer> PublicTradeOffers { get; set; } = null!;
    public DbSet<TimelineEvent> TimelineEvents { get; set; } = null!;
    public DbSet<Invitation> Invitations { get; set; } = null!;
    public DbSet<NotificationReadState> NotificationReadStates { get; set; } = null!;
    public DbSet<ItemReplyNotificationRecipient> ItemReplyNotificationRecipients { get; set; } = null!;
    public DbSet<TagEdge> TagEdges { get; set; } = null!;
    public DbSet<TagEdgeTagAttachment> TagEdgeTagAttachments { get; set; } = null!;
    public DbSet<TaggableTarget> TaggableTargets { get; set; } = null!;
    public DbSet<UserGroup> UserGroups { get; set; } = null!;
    public DbSet<UserGroupMember> UserGroupMembers { get; set; } = null!;
    public DbSet<TagAutoApproveUserGroup> TagAutoApproveGroups { get; set; } = null!;
    public DbSet<ContentReport> ContentReports { get; set; } = null!;
    public DbSet<ItemSplitRequest> ItemSplitRequests { get; set; } = null!;
    public DbSet<TagContentProposal> TagContentProposals { get; set; } = null!;
    public DbSet<TagNameProposal> TagNameProposals { get; set; } = null!;

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

        // アイテムの引用リツイート用自己参照リレーション
        _ = builder.Entity<Item>()
            .HasOne(i => i.QuotedItem)
            .WithMany(i => i.QuotedByItems)
            .HasForeignKey(i => i.QuotedItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // アイテムの公開対象ユーザーグループ（プライベートモード用）
        _ = builder.Entity<Item>()
            .HasOne(i => i.TargetUserGroup)
            .WithMany()
            .HasForeignKey(i => i.TargetUserGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        _ = builder.Entity<Item>()
            .HasIndex(i => i.IsPrivate);

        _ = builder.Entity<Item>()
            .HasIndex(i => i.TargetUserGroupId);

        // ユーザーのデフォルトプライベートユーザーグループ
        _ = builder.Entity<ApplicationUser>()
            .HasOne(u => u.DefaultPrivateUserGroup)
            .WithMany()
            .HasForeignKey(u => u.DefaultPrivateUserGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        // リプライ通知対象ユーザー
        _ = builder.Entity<ItemReplyNotificationRecipient>()
            .HasOne(r => r.ReplyItem)
            .WithMany(i => i.NotificationRecipients)
            .HasForeignKey(r => r.ReplyItemId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Entity<ItemReplyNotificationRecipient>()
            .HasOne(r => r.RecipientUser)
            .WithMany()
            .HasForeignKey(r => r.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<ItemReplyNotificationRecipient>()
            .HasIndex(r => new { r.ReplyItemId, r.RecipientUserId });

        _ = builder.Entity<ItemReplyNotificationRecipient>()
            .HasIndex(r => r.RecipientUserId);

        _ = builder.Entity<Tag>()
            .HasOne(t => t.Owner)
            .WithMany()
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<Tag>()
            .HasOne(t => t.AutoApproveUserGroup)
            .WithMany()
            .HasForeignKey(t => t.AutoApproveUserGroupId)
            .OnDelete(DeleteBehavior.SetNull);

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

        _ = builder.Entity<UserFollow>()
            .HasOne(uf => uf.Owner)
            .WithMany()
            .HasForeignKey(uf => uf.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<UserFollow>()
            .HasOne(uf => uf.FollowedUser)
            .WithMany()
            .HasForeignKey(uf => uf.FollowedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<UserFollow>()
            .HasIndex(uf => new { uf.OwnerId, uf.FollowedUserId })
            .IsUnique();

        _ = builder.Entity<UserFollow>()
            .HasIndex(uf => uf.FollowedUserId);

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

        _ = builder.Entity<UserGroup>()
            .HasOne(g => g.Owner)
            .WithMany()
            .HasForeignKey(g => g.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<UserGroupMember>()
            .HasOne(m => m.UserGroup)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.UserGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Entity<UserGroupMember>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<UserGroupMember>()
            .HasOne(m => m.Owner)
            .WithMany()
            .HasForeignKey(m => m.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<UserGroupMember>()
            .HasIndex(m => new { m.UserGroupId, m.UserId })
            .IsUnique();

        _ = builder.Entity<TagAutoApproveUserGroup>()
            .HasOne(g => g.Tag)
            .WithMany(t => t.AutoApproveUserGroups)
            .HasForeignKey(g => g.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Entity<TagAutoApproveUserGroup>()
            .HasOne(g => g.UserGroup)
            .WithMany()
            .HasForeignKey(g => g.UserGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Entity<TagAutoApproveUserGroup>()
            .HasOne(g => g.Owner)
            .WithMany()
            .HasForeignKey(g => g.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagAutoApproveUserGroup>()
            .HasIndex(g => new { g.TagId, g.UserGroupId })
            .IsUnique();

        // ContentReport (不適切な投稿・タグの通報) のリレーション・インデックス設定
        _ = builder.Entity<ContentReport>()
            .HasOne(r => r.Owner)
            .WithMany()
            .HasForeignKey(r => r.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<ContentReport>()
            .HasOne(r => r.HandledByAdmin)
            .WithMany()
            .HasForeignKey(r => r.HandledByAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<ContentReport>()
            .HasOne(r => r.Item)
            .WithMany()
            .HasForeignKey(r => r.ItemId)
            .OnDelete(DeleteBehavior.SetNull);

        _ = builder.Entity<ContentReport>()
            .HasOne(r => r.Tag)
            .WithMany()
            .HasForeignKey(r => r.TagId)
            .OnDelete(DeleteBehavior.SetNull);

        _ = builder.Entity<ContentReport>()
            .HasIndex(r => r.Status);

        _ = builder.Entity<ContentReport>()
            .HasIndex(r => r.CreatedDate);

        // --- ItemSplitRequest Configuration ---
        _ = builder.Entity<ItemSplitRequest>()
            .HasOne(r => r.Owner)
            .WithMany()
            .HasForeignKey(r => r.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<ItemSplitRequest>()
            .HasOne(r => r.RequesterUser)
            .WithMany()
            .HasForeignKey(r => r.RequesterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<ItemSplitRequest>()
            .HasOne(r => r.OwnerUser)
            .WithMany()
            .HasForeignKey(r => r.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<ItemSplitRequest>()
            .HasOne(r => r.OriginalItem)
            .WithMany()
            .HasForeignKey(r => r.OriginalItemId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<ItemSplitRequest>()
            .HasOne(r => r.CreatedItem)
            .WithMany()
            .HasForeignKey(r => r.CreatedItemId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<ItemSplitRequest>()
            .HasIndex(r => r.OriginalItemId);

        _ = builder.Entity<ItemSplitRequest>()
            .HasIndex(r => r.RequesterUserId);

        _ = builder.Entity<ItemSplitRequest>()
            .HasIndex(r => r.OwnerUserId);

        _ = builder.Entity<ItemSplitRequest>()
            .HasIndex(r => r.Status);

        // --- TagContentProposal Configuration ---
        _ = builder.Entity<TagContentProposal>()
            .HasOne(p => p.Owner)
            .WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagContentProposal>()
            .HasOne(p => p.RequesterUser)
            .WithMany()
            .HasForeignKey(p => p.RequesterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagContentProposal>()
            .HasOne(p => p.OwnerUser)
            .WithMany()
            .HasForeignKey(p => p.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagContentProposal>()
            .HasOne(p => p.Tag)
            .WithMany()
            .HasForeignKey(p => p.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagContentProposal>()
            .HasIndex(p => p.TagId);

        _ = builder.Entity<TagContentProposal>()
            .HasIndex(p => p.RequesterUserId);

        _ = builder.Entity<TagContentProposal>()
            .HasIndex(p => p.OwnerUserId);

        _ = builder.Entity<TagContentProposal>()
            .HasIndex(p => p.Status);

        // --- TagNameProposal Configuration ---
        _ = builder.Entity<TagNameProposal>()
            .HasOne(p => p.Owner)
            .WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagNameProposal>()
            .HasOne(p => p.RequesterUser)
            .WithMany()
            .HasForeignKey(p => p.RequesterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagNameProposal>()
            .HasOne(p => p.OwnerUser)
            .WithMany()
            .HasForeignKey(p => p.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagNameProposal>()
            .HasOne(p => p.Tag)
            .WithMany()
            .HasForeignKey(p => p.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        _ = builder.Entity<TagNameProposal>()
            .HasIndex(p => p.TagId);

        _ = builder.Entity<TagNameProposal>()
            .HasIndex(p => p.RequesterUserId);

        _ = builder.Entity<TagNameProposal>()
            .HasIndex(p => p.OwnerUserId);

        _ = builder.Entity<TagNameProposal>()
            .HasIndex(p => p.Status);
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