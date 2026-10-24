#pragma warning disable CA1848

using System.Diagnostics.CodeAnalysis;
using System.Numerics.Tensors;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     Item・Tag・TagRelation・TagEdge インポート用のデータアクセスプロバイダー実装。
/// </summary>
public class TaggingImportDataProvider(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagHierarchyService tagHierarchyService,
    ITagEmbeddingService tagEmbeddingService,
    TimeProvider? timeProvider = null,
    ILogger<TaggingImportDataProvider>? logger = null) : ITaggingImportDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITagHierarchyService _tagHierarchyService =
        tagHierarchyService ?? throw new ArgumentNullException(nameof(tagHierarchyService));
    private readonly ITagEmbeddingService _tagEmbeddingService =
        tagEmbeddingService ?? throw new ArgumentNullException(nameof(tagEmbeddingService));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ILogger<TaggingImportDataProvider> _logger =
        logger ?? NullLogger<TaggingImportDataProvider>.Instance;

    /// <inheritdoc />
    public async Task<Tag?> FindExistingTagAsync(
        string name,
        string? ownerId,
        bool isSystem,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        if (isSystem)
        {
            return await db.Tags
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Name == name && (t.IsSystem || t.OwnerId == "system"), cancellationToken);
        }

        Tag? userTag = null;
        if (!string.IsNullOrEmpty(ownerId))
        {
            userTag = await db.Tags
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Name == name && t.OwnerId == ownerId, cancellationToken);
        }

        return userTag ?? await db.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Name == name && (t.IsSystem || t.OwnerId == "system"), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tag>> SearchTagsAsync(string? query, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Tag> q = db.Tags
            .Where(t => !Tag.VoteTagNames.Contains(t.Name) && !Tag.ReactionTagNames.Contains(t.Name))
            .AsNoTracking();

        return string.IsNullOrWhiteSpace(query)
            ? await q.OrderBy(t => t.Name).Take(30).ToListAsync(cancellationToken)
            : await q.Where(t => t.Name.Contains(query)).OrderBy(t => t.Name).Take(30).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tag>> SearchParentCandidateTagsAsync(string? query, CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // 1階層目以降 (Level >= 1) のタグを対象にする（ルートタグ「全て∀」およびVote/Reactionタグは除外）
        List<Tag> levelTags = await db.Tags
            .Where(t => !Tag.VoteTagNames.Contains(t.Name) &&
                        !Tag.ReactionTagNames.Contains(t.Name) &&
                        t.Name != Tag.RootTagName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var filtered = levelTags.Where(t => t.Node != null && t.Node.GetLevel() >= 1).ToList();
        if (filtered.Count == 0)
        {
            filtered = levelTags;
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return filtered.Take(30).ToList();
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Embedding generation failure should not prevent tag creation")]
    public async Task<Tag> CreateTagAsync(
        string name,
        string ownerId,
        bool isSystem,
        int? parentTagId,
        CancellationToken cancellationToken = default)
    {
        await using ApplicationDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        HierarchyId node;
        if (parentTagId.HasValue)
        {
            node = await _tagHierarchyService.DetermineNewNodeAsync(parentTagId.Value, cancellationToken);
        }
        else
        {
            Tag? root = await db.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName, cancellationToken);
            if (root != null)
            {
                node = await _tagHierarchyService.DetermineNewNodeAsync(root.Id, cancellationToken);
                parentTagId = root.Id;
            }
            else
            {
                node = HierarchyId.GetRoot();
            }
        }

        var tag = new Tag
        {
            Name = name,
            OwnerId = isSystem ? "system" : ownerId,
            IsSystem = isSystem,
            ParentTagId = parentTagId,
            Node = node,
            CreatedDate = _timeProvider.GetUtcNow().UtcDateTime,
            UpdatedDate = _timeProvider.GetUtcNow().UtcDateTime
        };

        try
        {
            ReadOnlyMemory<float> embedding = await _tagEmbeddingService.GenerateEmbeddingAsync(name);
            tag.Embedding = embedding.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate embedding for new tag {TagName}: {Message}", name, ex.Message);
        }

        _ = db.Tags.Add(tag);
        _ = await db.SaveChangesAsync(cancellationToken);

        return tag;
    }

    /// <inheritdoc />
    public async Task<TaggingImportResult> ExecuteImportAsync(
        string currentUserId,
        TaggingImportPayload payload,
        IReadOnlyDictionary<string, int> relationIdToTagIdMap,
        IReadOnlyList<string> processedItemContents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(relationIdToTagIdMap);
        ArgumentNullException.ThrowIfNull(processedItemContents);

        await using ApplicationDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Database.ExecuteWithStrategyAsync(async () =>
        {
            var createdItemsCount = 0;
            var createdRelationsCount = 0;
            var createdEdgesCount = 0;
            var createdAttachmentsCount = 0;
            List<string> messages = [];

            // 1. Items の作成
            var itemMap = new Dictionary<string, Item>();
            foreach (var (i, itemDto) in payload.Item.Index())
            {
                var content = i < processedItemContents.Count ? processedItemContents[i] : itemDto.Content;

                var item = new Item
                {
                    OwnerId = currentUserId,
                    Content = content,
                    CreatedDate = _timeProvider.GetUtcNow().UtcDateTime,
                    UpdatedDate = _timeProvider.GetUtcNow().UtcDateTime
                };

                _ = db.Items.Add(item);
                _ = await db.SaveChangesAsync(cancellationToken);

                itemMap[itemDto.ItemId] = item;
                createdItemsCount++;
            }

            // 2. TagRelations の作成
            foreach (ImportTagRelationDto relDto in payload.TagRelations)
            {
                if (!itemMap.TryGetValue(relDto.SourceItemId, out Item? targetItem))
                {
                    continue;
                }

                if (!relationIdToTagIdMap.TryGetValue(relDto.RelationId, out int tagId))
                {
                    continue;
                }

                // 重複登録防止
                bool exists = await db.TagRelations.AnyAsync(
                    tr => tr.ItemId == targetItem.Id && tr.TagId == tagId, cancellationToken);
                if (exists)
                {
                    continue;
                }

                var relation = new TagRelation
                {
                    ItemId = targetItem.Id,
                    TagId = tagId,
                    OwnerId = currentUserId,
                    Weight = 1,
                    CreatedDate = _timeProvider.GetUtcNow().UtcDateTime,
                    UpdatedDate = _timeProvider.GetUtcNow().UtcDateTime
                };

                _ = db.TagRelations.Add(relation);
                createdRelationsCount++;
            }
            _ = await db.SaveChangesAsync(cancellationToken);

            // 3. TagEdges の作成
            foreach (ImportTagEdgeDto edgeDto in payload.TagEdges)
            {
                if (!relationIdToTagIdMap.TryGetValue(edgeDto.SourceTagId, out int sourceTagId) ||
                    !relationIdToTagIdMap.TryGetValue(edgeDto.TargetTagId, out int targetTagId))
                {
                    continue;
                }

                if (sourceTagId == targetTagId)
                {
                    continue;
                }

                // 既存 Edge を探すか新規作成
                TagEdge? edge = await db.TagEdges
                    .FirstOrDefaultAsync(e => e.SourceTagId == sourceTagId && e.TargetTagId == targetTagId && e.OwnerId == currentUserId, cancellationToken);

                if (edge is null)
                {
                    edge = new TagEdge
                    {
                        SourceTagId = sourceTagId,
                        TargetTagId = targetTagId,
                        OwnerId = currentUserId,
                        CreatedDate = _timeProvider.GetUtcNow().UtcDateTime,
                        UpdatedDate = _timeProvider.GetUtcNow().UtcDateTime
                    };
                    _ = db.TagEdges.Add(edge);
                    _ = await db.SaveChangesAsync(cancellationToken);
                    createdEdgesCount++;
                }

                // 4. AppliedTags (TagEdgeTagAttachment) の作成
                foreach (string appliedTagName in edgeDto.AppliedTags)
                {
                    if (string.IsNullOrWhiteSpace(appliedTagName))
                    {
                        continue;
                    }

                    Tag? appliedTag = await db.Tags.FirstOrDefaultAsync(
                        t => t.Name == appliedTagName && (t.OwnerId == currentUserId || t.IsSystem || t.OwnerId == "system"), cancellationToken);

                    if (appliedTag is null)
                    {
                        // 無ければ自動作成
                        appliedTag = await CreateTagAsync(appliedTagName, currentUserId, false, null, cancellationToken);
                    }

                    bool alreadyAttached = await db.TagEdgeTagAttachments.AnyAsync(
                        a => a.TagEdgeId == edge.Id && a.TagId == appliedTag.Id, cancellationToken);

                    if (alreadyAttached)
                    {
                        continue;
                    }

                    // 消費用 RightAsset の取得または新規発行
                    RightAsset? asset = await db.RightAssets.FirstOrDefaultAsync(
                        r => r.OwnerId == currentUserId && r.TargetTagId == appliedTag.Id && !r.IsBurned && r.Amount > 0, cancellationToken);

                    if (asset is null)
                    {
                        asset = new RightAsset
                        {
                            OwnerId = currentUserId,
                            TargetTagId = appliedTag.Id,
                            Amount = 0,
                            IsBurned = true,
                            Status = new Burned(_timeProvider.GetUtcNow().UtcDateTime),
                            CreatedDate = _timeProvider.GetUtcNow().UtcDateTime,
                            UpdatedDate = _timeProvider.GetUtcNow().UtcDateTime
                        };
                        _ = db.RightAssets.Add(asset);
                        _ = await db.SaveChangesAsync(cancellationToken);
                    }
                    else
                    {
                        asset.Amount -= 1;
                        if (asset.Amount <= 0)
                        {
                            asset.IsBurned = true;
                            asset.Status = new Burned(_timeProvider.GetUtcNow().UtcDateTime);
                        }
                    }

                    var attachment = new TagEdgeTagAttachment
                    {
                        TagEdgeId = edge.Id,
                        TagId = appliedTag.Id,
                        Weight = 1,
                        ConsumedRightAssetId = asset.Id,
                        OwnerId = currentUserId,
                        CreatedDate = _timeProvider.GetUtcNow().UtcDateTime,
                        UpdatedDate = _timeProvider.GetUtcNow().UtcDateTime
                    };
                    _ = db.TagEdgeTagAttachments.Add(attachment);

                    var prevWeight = appliedTag.CachedWeight;
                    appliedTag.CachedWeight += 1;

                    _ = db.TagWeightLedgers.Add(new TagWeightLedger
                    {
                        TagId = appliedTag.Id,
                        TagNameSnapshot = appliedTag.Name,
                        SourceType = "TagEdgeTagAttachmentInsert",
                        SourceId = null,
                        ConsumedRightAssetId = asset.Id,
                        Delta = 1,
                        PreviousWeight = prevWeight,
                        NewWeight = appliedTag.CachedWeight,
                        IsOwnerAction = appliedTag.OwnerId == currentUserId,
                        Reason = "Import: Edgeへのタグ付け",
                        OwnerId = currentUserId,
                        CreatedDate = _timeProvider.GetUtcNow().UtcDateTime,
                        UpdatedDate = _timeProvider.GetUtcNow().UtcDateTime
                    });

                    createdAttachmentsCount++;
                }
            }

            _ = await db.SaveChangesAsync(cancellationToken);

            messages.Add($"Item: {createdItemsCount}件, TagRelation: {createdRelationsCount}件, TagEdge: {createdEdgesCount}件, EdgeTagAttachment: {createdAttachmentsCount}件 を正常にインポートしました。");

            return new TaggingImportResult(
                createdItemsCount,
                0, // 外部で作成されたタグ数は呼び出し元でカウント
                createdRelationsCount,
                createdEdgesCount,
                createdAttachmentsCount,
                messages);
        });
    }
}