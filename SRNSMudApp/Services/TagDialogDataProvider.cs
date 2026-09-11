#pragma warning disable CA1848

#region

using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SRNSMudApp.Data;

using Tag = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Services;

/// <summary>タグ検索・一覧取得を担う CQS の Query 契約。</summary>
public interface ITagSearchQueryService
{
    Task<List<Tag>> GetAllTagsAsync();
    Task<List<Tag>> SearchTagsAsync(string searchText);
    Task<Tag?> FindTagByNameAsync(string tagName);
    Task<List<Tag>> SearchTagsWithFallbackAsync(string? value, CancellationToken token = default);
    Task<List<Tag>> GetTagsWithDetailsAsync();
}

/// <summary>タグの作成・更新を担う CQS の Command 契約。</summary>
public interface ITagCommandService
{
    Task CreateTagAsync(Tag newTag);
    Task CreateTagWithoutEmbeddingAsync(Tag newTag);
    Task<bool> UpdateTagAsync(int tagId, string name, string? content, bool autoAcceptIncomingTaggingRequests = false, IEnumerable<int>? allowedUserGroupIds = null);
}

public class TagCommandService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagEmbeddingService tagEmbeddingService,
    ILogger<TagCommandService>? logger = null) : ITagCommandService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITagEmbeddingService _tagEmbeddingService =
        tagEmbeddingService ?? throw new ArgumentNullException(nameof(tagEmbeddingService));
    private readonly ILogger<TagCommandService> _logger =
        logger ?? NullLogger<TagCommandService>.Instance;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ユーザー入力由来の任意の例外を UI 向けメッセージに変換するため広く捕捉する")]
    public async Task CreateTagAsync(Tag newTag)
    {
        try
        {
            ReadOnlyMemory<float> embedding =
                await _tagEmbeddingService.GenerateEmbeddingAsync($"{newTag.Name} {newTag.Content}");
            newTag.Embedding = embedding.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding generation failed: {Message}", ex.Message);
        }

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        await EnsureParentNodeAsync(dbContext, newTag);
        _ = dbContext.Tags.Add(newTag);
        _ = await dbContext.SaveChangesAsync();
    }
    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ユーザー入力由来の任意の例外を UI 向けメッセージに変換するため広く捕捉する")]
    public async Task<bool> UpdateTagAsync(int tagId, string name, string? content, bool autoAcceptIncomingTaggingRequests = false, IEnumerable<int>? allowedUserGroupIds = null)
    {
        bool nameChanged;
        await using (ApplicationDbContext context = await _dbFactory.CreateDbContextAsync())
        {
            Tag? tagToUpdate = await context.Tags
                .Include(t => t.AutoApproveUserGroups)
                .FirstOrDefaultAsync(t => t.Id == tagId);
            if (tagToUpdate is null)
            {
                return false;
            }

            nameChanged = !string.Equals(tagToUpdate.Name, name, StringComparison.Ordinal);
            tagToUpdate.Name = name;
            tagToUpdate.Content = content ?? "";
            tagToUpdate.AutoAcceptIncomingTaggingRequests = autoAcceptIncomingTaggingRequests;

            if (allowedUserGroupIds is not null)
            {
                var targetGroupIds = allowedUserGroupIds.ToHashSet();
                List<TagAutoApproveUserGroup> toRemove =
                [
                    .. tagToUpdate.AutoApproveUserGroups.Where(g => !targetGroupIds.Contains(g.UserGroupId))
                ];
                foreach (TagAutoApproveUserGroup rel in toRemove)
                {
                    _ = tagToUpdate.AutoApproveUserGroups.Remove(rel);
                    _ = context.TagAutoApproveGroups.Remove(rel);
                }

                var existingGroupIds = tagToUpdate.AutoApproveUserGroups
                    .Select(g => g.UserGroupId)
                    .ToHashSet();

                foreach (var groupId in targetGroupIds)
                {
                    if (!existingGroupIds.Contains(groupId))
                    {
                        tagToUpdate.AutoApproveUserGroups.Add(new TagAutoApproveUserGroup
                        {
                            TagId = tagId,
                            UserGroupId = groupId,
                            OwnerId = tagToUpdate.OwnerId,
                            CreatedDate = DateTime.UtcNow,
                            UpdatedDate = DateTime.UtcNow
                        });
                    }
                }

            }

            _ = await context.SaveChangesAsync();
        }

        if (nameChanged)
        {
            await UpdateEmbeddingAfterNameChangeAsync(tagId, name);
        }

        return true;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "埋め込み生成の失敗でタグ名変更の保存結果を取り消さないため広く捕捉する")]
    private async Task UpdateEmbeddingAfterNameChangeAsync(int tagId, string name)
    {
        try
        {
            // 埋め込み生成中は DB コンテキストを保持せず、接続プールを占有しない。
            ReadOnlyMemory<float> embedding = await _tagEmbeddingService.GenerateEmbeddingAsync(name);

            await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
            Tag? tag = await context.Tags.FirstOrDefaultAsync(t => t.Id == tagId && t.Name == name);
            if (tag is null)
            {
                return;
            }

            tag.Embedding = embedding.ToArray();
            _ = await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate embedding on edit: {Message}", ex.Message);
        }
    }

    public async Task CreateTagWithoutEmbeddingAsync(Tag newTag)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        await EnsureParentNodeAsync(dbContext, newTag);
        _ = dbContext.Tags.Add(newTag);
        _ = await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureParentNodeAsync(ApplicationDbContext dbContext, Tag newTag)
    {
        if (newTag.Name == Tag.RootTagName)
        {
            newTag.Node = HierarchyId.GetRoot();
            newTag.ParentTagId = null;
            return;
        }

        if (newTag.Node is null || newTag.Node == HierarchyId.GetRoot())
        {
            Tag? parentTag = null;
            if (newTag.ParentTagId.HasValue)
            {
                parentTag = await dbContext.Tags.FindAsync(newTag.ParentTagId.Value);
            }

            if (parentTag is null)
            {
                parentTag = await dbContext.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName);
                if (parentTag is not null)
                {
                    newTag.ParentTagId = parentTag.Id;
                }
            }

            if (parentTag is not null)
            {
                HierarchyId? lastChildNode = await dbContext.Tags
                    .Where(t => t.ParentTagId == parentTag.Id || t.Node.GetAncestor(1) == parentTag.Node)
                    .OrderByDescending(t => t.Node)
                    .Select(t => (HierarchyId?)t.Node)
                    .FirstOrDefaultAsync();

                newTag.Node = parentTag.Node.GetDescendant(lastChildNode, null);
            }
        }
    }
}