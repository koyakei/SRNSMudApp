using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

// 兄弟名前空間との衝突を避けるためのエイリアス
using Tag = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Services;

/// <summary>タグ一括削除の結果。</summary>
public sealed record TagTreeDeleteResult(
    bool HasDeleted,
    int DeletedCount,
    IReadOnlyList<string> UnauthorizedNames,
    IReadOnlyList<string> SystemNames);

/// <summary>
///     TagTree コンポーネント用のデータアクセスを分離するインターフェース。
///     コンポーネントから DbContext への直接依存を断ち、単体テストでモック可能にする。
/// </summary>
public interface ITagTreeDataProvider
{
    /// <summary>VoteTag / ReactionTag 以外を読み込む。</summary>
    Task<List<Tag>> LoadTagsAsync();

    Task<TagTreeDeleteResult> DeleteTagsAsync(string userId, IReadOnlyList<int> selectedIds);

    /// <summary>子タグを追加する。一意制約違反などは例外として伝搬する。</summary>
    Task AddTagAsync(Tag tag);

    /// <summary>タグの親を変更する。対象が存在しない場合は false。</summary>
    Task<bool> UpdateParentAsync(int tagId, int? parentTagId);

    /// <summary>他人のタグの位置変更リクエストを送信する。</summary>
    Task<Result<TaggingRequestEntity>> RequestTagMoveAsync(string requesterUserId, int tagId, int? newParentTagId);

    /// <summary>承認待ち（Proposed）のタグ移動リクエスト一覧を取得する。</summary>
    Task<List<PendingTagMoveDto>> LoadPendingTagMovesAsync();

    /// <summary>移動リクエストをキャンセルする。</summary>
    Task<Result<string>> CancelTagMoveAsync(int requestId, string currentUserId);
}

public class TagTreeDataProvider(IDbContextFactory<ApplicationDbContext> dbFactory) : ITagTreeDataProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    public async Task<List<Tag>> LoadTagsAsync()
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        return await context.Tags
            .Where(t => !Tag.VoteTagNames.Contains(t.Name) && !Tag.ReactionTagNames.Contains(t.Name))
            .OrderBy(t => t.Node)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<TagTreeDeleteResult> DeleteTagsAsync(string userId, IReadOnlyList<int> selectedIds)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync();

        try
        {
            List<Tag> selectedTagsFromDb = await context.Tags.Where(t => selectedIds.Contains(t.Id)).ToListAsync();

            var unauthorizedNames = selectedTagsFromDb
                .Where(t => t.OwnerId != userId)
                .Select(t => t.Name)
                .ToList();
            var systemNames = selectedTagsFromDb
                .Where(t => t.IsSystem)
                .Select(t => t.Name)
                .ToList();
            var authorizedIds = selectedTagsFromDb
                .Where(t => t.OwnerId == userId && !t.IsSystem)
                .Select(t => t.Id)
                .ToList();

            var hasDeleted = false;
            var deletedCount = 0;

            if (authorizedIds.Count > 0)
            {
                List<Tag> tagsToDelete = await context.Tags
                    .Where(t => authorizedIds.Contains(t.Id) && t.OwnerId == userId)
                    .ToListAsync();

                if (tagsToDelete.Count > 0)
                {
                    // 関連する TagRelationToTag (DeleteBehavior.Restrict) を手動で削除
                    List<TagRelationToTag> relationsToDelete = await context.TagRelationToTags
                        .Where(tr =>
                            authorizedIds.Contains(tr.TagId) || authorizedIds.Contains(tr.TargetTagId))
                        .ToListAsync();

                    if (relationsToDelete.Count > 0)
                    {
                        context.TagRelationToTags.RemoveRange(relationsToDelete);
                    }

                    // 削除対象のタグを親に持つ子タグを取得し、ルートタグ（"全て∀"）配下に変更する
                    List<Tag> orphanedChildren = await context.Tags
                        .Where(t => t.ParentTagId != null &&
                                    authorizedIds.Contains(t.ParentTagId.Value) &&
                                    !authorizedIds.Contains(t.Id))
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

                    // 自己参照外部キー制約エラーを避けるため、一旦親タグ参照を解除して保存
                    foreach (Tag tag in tagsToDelete)
                    {
                        tag.ParentTagId = null;
                    }

                    _ = await context.SaveChangesAsync();

                    // タグ本体の削除
                    context.Tags.RemoveRange(tagsToDelete);
                    _ = await context.SaveChangesAsync();

                    hasDeleted = true;
                    deletedCount = tagsToDelete.Count;
                }

                await transaction.CommitAsync();
            }

            return new TagTreeDeleteResult(hasDeleted, deletedCount, unauthorizedNames, systemNames);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task AddTagAsync(Tag tag)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        if (tag.Name != Tag.RootTagName && !tag.ParentTagId.HasValue)
        {
            Tag? rootTag = await context.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName);
            if (rootTag != null)
            {
                tag.ParentTagId = rootTag.Id;
            }
        }

        if (tag.ParentTagId.HasValue)
        {
            HierarchyId? parentNode = await context.Tags
                .Where(t => t.Id == tag.ParentTagId.Value)
                .Select(t => (HierarchyId?)t.Node)
                .FirstOrDefaultAsync();

            if (parentNode != null)
            {
                HierarchyId? lastChild = await context.Tags
                    .Where(t => t.Node.GetAncestor(1) == parentNode)
                    .OrderByDescending(t => t.Node)
                    .Select(t => (HierarchyId?)t.Node)
                    .FirstOrDefaultAsync();

                tag.Node = parentNode.GetDescendant(lastChild, null);
            }
        }
        else if (tag.Name == Tag.RootTagName)
        {
            tag.Node = HierarchyId.GetRoot();
        }

        _ = context.Tags.Add(tag);
        _ = await context.SaveChangesAsync();
    }

    public async Task<bool> UpdateParentAsync(int tagId, int? parentTagId)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Tag? tagToUpdate = await context.Tags.FindAsync(tagId);
        if (tagToUpdate is null)
        {
            return false;
        }

        if (!parentTagId.HasValue && tagToUpdate.Name != Tag.RootTagName)
        {
            Tag? rootTag = await context.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName);
            if (rootTag != null)
            {
                parentTagId = rootTag.Id;
            }
        }

        var parentNode = HierarchyId.GetRoot();
        if (parentTagId.HasValue)
        {
            HierarchyId? foundParentNode = await context.Tags
                .Where(t => t.Id == parentTagId.Value)
                .Select(t => (HierarchyId?)t.Node)
                .FirstOrDefaultAsync();

            if (foundParentNode != null)
            {
                parentNode = foundParentNode;
            }
        }

        HierarchyId? lastChild = await context.Tags
            .Where(t => t.Node.GetAncestor(1) == parentNode)
            .OrderByDescending(t => t.Node)
            .Select(t => (HierarchyId?)t.Node)
            .FirstOrDefaultAsync();

        tagToUpdate.Node = parentNode.GetDescendant(lastChild, null);
        tagToUpdate.ParentTagId = parentTagId;
        tagToUpdate.UpdatedDate = DateTime.UtcNow;

        _ = await context.SaveChangesAsync();
        return true;
    }

    public async Task<Result<TaggingRequestEntity>> RequestTagMoveAsync(string requesterUserId, int tagId, int? newParentTagId)
    {
        if (string.IsNullOrEmpty(requesterUserId))
        {
            return new Failure("ログインが必要です。");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        Tag? tag = await context.Tags.FindAsync(tagId);
        if (tag is null)
        {
            return new Failure(ContractMessages.TagNotFound);
        }

        if (tag.OwnerId == requesterUserId)
        {
            return new Failure("自分のタグの位置変更依頼は作成できません。");
        }

        Tag? parentTag = null;
        if (newParentTagId.HasValue)
        {
            parentTag = await context.Tags.FindAsync(newParentTagId.Value);
            if (parentTag is null)
            {
                return new Failure("移動先の親タグが見つかりません。");
            }
        }

        var parentName = parentTag?.Name ?? Tag.RootTagName;
        var content = $"タグ「{tag.Name}」を「{parentName}」配下に移動する変更依頼";

        var requestItem = new Item
        {
            OwnerId = requesterUserId,
            Content = content
        };
        _ = context.Items.Add(requestItem);
        _ = await context.SaveChangesAsync();

        var contract = new TaggingRequestEntity
        {
            ContractType = ContractTypes.Move,
            OwnerId = requesterUserId,
            RequesterUserId = requesterUserId,
            TagOwnerUserId = tag.OwnerId,
            RequestedTagId = tag.Id,
            TargetId = requestItem.TagTargetId > 0 ? requestItem.TagTargetId : requestItem.TagTarget.Id,
            Target = requestItem.TagTarget,
            TargetItem = requestItem,
            RequestItem = requestItem,
            Status = TradeStatus.Proposed,
            RequestType = TaggingRequestType.Move,
            ProposedWeight = 1,
            Payload = new TagMovePayload(newParentTagId)
        };

        _ = context.TaggingRequestEntities.Add(contract);
        _ = await context.SaveChangesAsync();

        return new Success<TaggingRequestEntity>(contract);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code",
        Justification = "Union pattern matching false positive")]
    public async Task<List<PendingTagMoveDto>> LoadPendingTagMovesAsync()
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        List<TaggingRequestEntity> requests = await context.TaggingRequestEntities
            .Include(r => r.RequestedTag)
            .Where(r => r.ContractType == ContractTypes.Move && r.Status == TradeStatus.Proposed)
            .AsNoTracking()
            .ToListAsync();

        List<PendingTagMoveDto> dtos = [];
        foreach (TaggingRequestEntity req in requests)
        {
            if (req.Payload is TagMovePayload payload)
            {
                dtos.Add(new PendingTagMoveDto(
                    req.Id,
                    req.RequestedTagId,
                    req.RequestedTag?.Name ?? "不明なタグ",
                    payload.NewParentTagId,
                    req.RequesterUserId,
                    req.TagOwnerUserId));
            }
        }

        return dtos;
    }

    public async Task<Result<string>> CancelTagMoveAsync(int requestId, string currentUserId)
    {
        if (string.IsNullOrEmpty(currentUserId))
        {
            return new Failure("ログインが必要です。");
        }

        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        TaggingRequestEntity? request = await context.TaggingRequestEntities
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request is null)
        {
            return new Failure("対象のリクエストが見つかりません。");
        }

        if (request.Status != TradeStatus.Proposed)
        {
            return new Failure("承認待ち状態のリクエストのみキャンセルできます。");
        }

        if (request.RequesterUserId != currentUserId && request.TagOwnerUserId != currentUserId)
        {
            return new Failure("リクエストをキャンセルする権限がありません。");
        }

        request.Status = TradeStatus.Canceled;
        request.UpdatedDate = DateTime.UtcNow;
        _ = await context.SaveChangesAsync();

        return new Success<string>("移動リクエストをキャンセルしました。");
    }
}