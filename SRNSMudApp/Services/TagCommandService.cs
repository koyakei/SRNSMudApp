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

/// <summary>
///     タグ削除操作の結果を表す。
/// </summary>
public enum TagDeleteOperationResult
{
    /// <summary>削除成功。</summary>
    Success,

    /// <summary>対象タグが見つからない。</summary>
    NotFound,

    /// <summary>タグまたはその兄弟がロックされているため削除不可。</summary>
    Locked,

    /// <summary>システムタグのため削除不可。</summary>
    SystemTag,

    /// <summary>タグの作成者ではないため削除権限がない。</summary>
    Unauthorized
}

/// <summary>タグの作成・更新・削除を担う CQS の Command 契約。</summary>
public interface ITagCommandService
{
    /// <summary>タグを作成する。</summary>
    Task CreateTagAsync(Tag newTag, bool isAdmin = false);

    /// <summary>埋め込みを生成せずにタグを作成する。</summary>
    Task CreateTagWithoutEmbeddingAsync(Tag newTag, bool isAdmin = false);

    /// <summary>タグを更新する。</summary>
    Task<bool> UpdateTagAsync(int tagId, string name, string? content, bool autoAcceptIncomingTaggingRequests = false, IEnumerable<int>? allowedUserGroupIds = null, bool isAdmin = false);

    /// <summary>タグを削除する。</summary>
    /// <param name="tagId">削除対象のタグID。</param>
    /// <param name="currentUserId">現在のユーザーID。</param>
    /// <param name="isAdmin">管理者フラグ（管理者の場合はロックや所有権制限をバイパス）。</param>
    /// <returns>削除操作の結果。</returns>
    Task<TagDeleteOperationResult> DeleteTagAsync(int tagId, string? currentUserId, bool isAdmin = false);
}

/// <summary>
///     タグの作成・更新・削除コマンドを実行するドメインサービス実装。
/// </summary>
public class TagCommandService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITagEmbeddingService tagEmbeddingService,
    ITagLockService? tagLockService = null,
    ILogger<TagCommandService>? logger = null) : ITagCommandService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITagEmbeddingService _tagEmbeddingService =
        tagEmbeddingService ?? throw new ArgumentNullException(nameof(tagEmbeddingService));
    private readonly ITagLockService? _tagLockService = tagLockService;
    private readonly ILogger<TagCommandService> _logger =
        logger ?? NullLogger<TagCommandService>.Instance;

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "ユーザー入力由来の任意の例外を UI 向けメッセージに変換するため広く捕捉する")]
    public async Task CreateTagAsync(Tag newTag, bool isAdmin = false)
    {
        ArgumentNullException.ThrowIfNull(newTag);

        if (_tagLockService != null && !isAdmin)
        {
            var isRestricted = await _tagLockService.IsChildCreationRestrictedAsync(newTag.ParentTagId);
            if (isRestricted)
            {
                throw new InvalidOperationException("ロックされている階層またはロックされたタグの兄弟は新規作成できません。");
            }
        }

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
    public async Task<bool> UpdateTagAsync(int tagId, string name, string? content, bool autoAcceptIncomingTaggingRequests = false, IEnumerable<int>? allowedUserGroupIds = null, bool isAdmin = false)
    {
        if (_tagLockService != null && !isAdmin)
        {
            var isLocked = await _tagLockService.IsTagOrSiblingLockedAsync(tagId);
            if (isLocked)
            {
                _logger.LogWarning("タグ ID {TagId} またはその兄弟がロックされているため更新できません。", tagId);
                return false;
            }
        }

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

    /// <inheritdoc />
    public async Task CreateTagWithoutEmbeddingAsync(Tag newTag, bool isAdmin = false)
    {
        ArgumentNullException.ThrowIfNull(newTag);

        if (_tagLockService != null && !isAdmin)
        {
            var isRestricted = await _tagLockService.IsChildCreationRestrictedAsync(newTag.ParentTagId);
            if (isRestricted)
            {
                throw new InvalidOperationException("ロックされている階層またはロックされたタグの兄弟は新規作成できません。");
            }
        }

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

    /// <inheritdoc />
    public async Task<TagDeleteOperationResult> DeleteTagAsync(int tagId, string? currentUserId, bool isAdmin = false)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Tag? tagToDelete = await context.Tags.FindAsync(tagId);
        if (tagToDelete is null)
        {
            return TagDeleteOperationResult.NotFound;
        }

        if (tagToDelete.IsSystem || tagToDelete.Name == Tag.RootTagName)
        {
            return TagDeleteOperationResult.SystemTag;
        }

        if (!isAdmin && tagToDelete.OwnerId != currentUserId)
        {
            return TagDeleteOperationResult.Unauthorized;
        }

        if (_tagLockService != null && !isAdmin)
        {
            var isLocked = await _tagLockService.IsTagOrSiblingLockedAsync(tagId);
            if (isLocked)
            {
                _logger.LogWarning("タグ ID {TagId} またはその兄弟がロックされているため削除できません。", tagId);
                return TagDeleteOperationResult.Locked;
            }
        }

        // 外部キー制約 (DeleteBehavior.Restrict) により手動削除が必要な関連エンティティ（TagWeightLedger, TagRelationToTag 等）を削除
        await context.RemoveTagRestrictedDependenciesAsync([tagId]);

        // 削除対象のタグを親に持つ子タグを取得し、ルートタグ（"全て∀"）配下に変更する
        List<Tag> orphanedChildren = await context.Tags
            .Where(t => t.ParentTagId == tagId)
            .ToListAsync();

        if (orphanedChildren.Count > 0)
        {
            Tag? rootTag = await context.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName);
            HierarchyId? lastChildNode = rootTag != null
                ? await context.Tags
                    .Where(t => t.ParentTagId == rootTag.Id)
                    .OrderByDescending(t => t.Node)
                    .Select(t => (HierarchyId?)t.Node)
                    .FirstOrDefaultAsync()
                : null;

            foreach (Tag child in orphanedChildren)
            {
                child.ParentTagId = rootTag?.Id;
                if (rootTag != null)
                {
                    child.Node = rootTag.Node.GetDescendant(lastChildNode, null);
                    lastChildNode = child.Node;
                }
            }
        }

        tagToDelete.ParentTagId = null;
        _ = await context.SaveChangesAsync();

        _ = context.Tags.Remove(tagToDelete);
        _ = await context.SaveChangesAsync();
        return TagDeleteOperationResult.Success;
    }
}