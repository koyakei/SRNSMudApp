using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services.Contracts;

/// <summary>
///     タグ位置変更（Move）コントラクトの承認・実行処理を担当する <see cref="IContractExecutor" /> 実装。
///     タグオーナーが位置変更リクエストを承認した際に、階層ツリー上の親ノード・HierarchyId を更新する。
/// </summary>
public class MoveContractExecutor : IContractExecutor
{
    public string ContractType => ContractTypes.Move;

    public async Task<Result<string>> ExecuteAsync(
        ApplicationDbContext dbContext,
        TaggingRequestEntity contract,
        string currentUserId,
        int? fulfillerAssetId = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(contract);

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

        tagToUpdate.Node = parentNode.GetDescendant(lastChild, null);
        tagToUpdate.ParentTagId = newParentTagId;
        tagToUpdate.UpdatedDate = DateTime.UtcNow;

        contract.Status = TradeStatus.Executed;

        _ = await dbContext.SaveChangesAsync();

        return new Success<string>("タグの配置変更リクエストを承認しました。");
    }
}