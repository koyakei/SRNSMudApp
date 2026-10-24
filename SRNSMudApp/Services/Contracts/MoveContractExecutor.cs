using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     タグ位置変更（Move）コントラクトの承認・実行処理を担当する <see cref="IContractExecutor" /> 実装。
///     タグオーナーが位置変更リクエストを承認した際に、階層ツリー上の親ノード・HierarchyId を更新する。
/// </summary>
/// <param name="timeProvider">日時プロバイダー（省略時はシステム日時）。</param>
public class MoveContractExecutor(TimeProvider? timeProvider = null) : ContractExecutorBase(timeProvider)
{
    public override string ContractType => ContractTypes.Move;

    protected override async Task<Result<string>> ExecuteCoreAsync(
        ApplicationDbContext dbContext,
        TaggingRequestEntity contract,
        string currentUserId,
        int? fulfillerAssetId = null)
    {
        if (contract.Payload is not TagMovePayload payload)
        {
            return new Failure("無効なコントラクトペイロードです。");
        }

        Tag? tagToUpdate = await dbContext.Tags.FindAsync(contract.RequestedTagId);
        if (tagToUpdate is null)
        {
            return new Failure(ContractMessages.TagNotFound);
        }

        int? newParentTagId = payload.NewParentTagId;

        if (!newParentTagId.HasValue && tagToUpdate.Name != Tag.RootTagName)
        {
            Tag? rootTag = await dbContext.Tags.FirstOrDefaultAsync(t => t.Name == Tag.RootTagName);
            if (rootTag != null)
            {
                newParentTagId = rootTag.Id;
            }
        }

        var parentNode = HierarchyId.GetRoot();
        if (newParentTagId.HasValue)
        {
            HierarchyId? foundParentNode = await dbContext.Tags
                .Where(t => t.Id == newParentTagId.Value)
                .Select(t => (HierarchyId?)t.Node)
                .FirstOrDefaultAsync();

            if (foundParentNode != null)
            {
                parentNode = foundParentNode;
            }
        }

        HierarchyId? lastChild = await dbContext.Tags
            .Where(t => t.Node.GetAncestor(1) == parentNode)
            .OrderByDescending(t => t.Node)
            .Select(t => (HierarchyId?)t.Node)
            .FirstOrDefaultAsync();

        HierarchyId oldNode = tagToUpdate.Node;
        HierarchyId newNode = parentNode.GetDescendant(lastChild, null);

        var now = TimeProvider.GetUtcNow().UtcDateTime;
        tagToUpdate.Node = newNode;
        tagToUpdate.ParentTagId = newParentTagId;
        tagToUpdate.UpdatedDate = now;

        // 子孫タグの HierarchyId ノードを再計算してツリー整合性を維持する
        if (oldNode != null)
        {
            List<Tag> descendants = await dbContext.Tags
                .Where(t => t.Id != tagToUpdate.Id && t.Node.IsDescendantOf(oldNode))
                .ToListAsync();

            foreach (Tag descendant in descendants)
            {
                if (descendant.Node.GetReparentedValue(oldNode, newNode) is { } reparented)
                {
                    descendant.Node = reparented;
                    descendant.UpdatedDate = now;
                }
            }
        }

        contract.Execute();

        _ = await dbContext.SaveChangesAsync();

        return new Success<string>("タグの配置変更リクエストを承認しました。");
    }
}