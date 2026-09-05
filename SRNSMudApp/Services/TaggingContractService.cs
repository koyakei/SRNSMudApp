using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Contracts;

// CA1508: union 型 (Option<T> / CheckAuth 結果など) の網羅的パターンマッチでは、先行アームの後の
// Some / エラー型アームが静的に「常に真」とみなされるが、網羅性確保のためアームは必須。
// 解析器の誤検知のため、ファイル単位で抑制する。
#pragma warning disable CA1508

// IDE0010 / IDE0072: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services;

/// <summary>
///     タグ付けコントラクト（Gratis / Mutual / Trigger / Bounty）の提案・承認・キャンセルを調整するサービス。
///     実際の承認ロジックは <see cref="IContractExecutor" /> 実装へ委譲する（Strategy パターン）。
///     各操作において <see cref="IDbContextFactory{TContext}" /> より短寿命なコンテキストを生成し、
///     Blazor Server での並行アクセス競合を防ぎつつ適切なトランザクション境界を管理する。
/// </summary>
public class TaggingContractService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IContractExecutorFactory executorFactory) : ITaggingContractService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IContractExecutorFactory _executorFactory =
        executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));

    public TaggingContractService(IDbContextFactory<ApplicationDbContext> dbFactory)
        : this(dbFactory, ContractExecutorFactory.CreateDefault())
    {
    }

    /// <inheritdoc />
    public async Task<Result<TaggingRequestEntity>> ProposeGratisContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int targetItemId,
        int requestedTagId,
        TaggingRequestType requestType = TaggingRequestType.Add,
        int proposedWeight = 1,
        string? message = null)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        Item? targetItem = await dbContext.Items.Include(i => i.TagTarget).FirstOrDefaultAsync(i => i.Id == targetItemId);
        if (targetItem == null)
        {
            return new Failure("対象アイテムが見つかりません。");
        }

        var content = message ?? (requestType switch
        {
            TaggingRequestType.Add => ContractMessages.TagAddRequestSent,
            _ => ContractMessages.TagDeleteRequestSent
        });

        var requestItem = new Item
        {
            OwnerId = requesterUserId,
            Content = content
        };

        var contract = new TaggingRequestEntity
        {
            ContractType = ContractTypes.Gratis,
            OwnerId = requesterUserId,
            RequesterUserId = requesterUserId,
            TagOwnerUserId = tagOwnerUserId,
            TargetId = targetItem.TagTargetId > 0 ? targetItem.TagTargetId : targetItem.TagTarget.Id,
            Target = targetItem.TagTarget,
            TargetItemId = targetItemId,
            RequestedTagId = requestedTagId,
            Status = TradeStatus.Proposed,
            RequestType = requestType,
            ProposedWeight = proposedWeight,
            Payload = new GratisPayload(message ?? ""),
            RequestItem = requestItem
        };

        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        return await TryAutoAcceptAsync(contract, requestedTagId, tagOwnerUserId);
    }

    /// <inheritdoc />
    public async Task<Result<TaggingRequestEntity>> ProposeGratisEdgeContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int tagEdgeId,
        int requestedTagId,
        TaggingRequestType requestType = TaggingRequestType.Add,
        int proposedWeight = 1,
        string? message = null)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        TagEdge? targetEdge = await dbContext.TagEdges.Include(e => e.TagTarget).FirstOrDefaultAsync(e => e.Id == tagEdgeId);
        if (targetEdge == null)
        {
            return new Failure(ContractMessages.TagEdgeNotFound);
        }

        var content = message ?? (requestType switch
        {
            TaggingRequestType.Add => ContractMessages.TagAddRequestSent,
            _ => ContractMessages.TagDeleteRequestSent
        });

        var requestItem = new Item
        {
            OwnerId = requesterUserId,
            Content = content
        };

        var contract = new TaggingRequestEntity
        {
            ContractType = ContractTypes.Gratis,
            OwnerId = requesterUserId,
            RequesterUserId = requesterUserId,
            TagOwnerUserId = tagOwnerUserId,
            TargetId = targetEdge.TagTargetId > 0 ? targetEdge.TagTargetId : targetEdge.TagTarget.Id,
            Target = targetEdge.TagTarget,
            RequestedTagId = requestedTagId,
            Status = TradeStatus.Proposed,
            RequestType = requestType,
            ProposedWeight = proposedWeight,
            Payload = new GratisPayload(message ?? ""),
            RequestItem = requestItem
        };

        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        return await TryAutoAcceptAsync(contract, requestedTagId, tagOwnerUserId);
    }

    /// <inheritdoc />
    public async Task<Result<TaggingRequestEntity>> ProposeMutualContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int targetItemId,
        int requestedTagId,
        int offeredTargetItemId,
        int offeredTagId,
        int consumedRightAssetId,
        TaggingRequestType requestType = TaggingRequestType.Add,
        int proposedWeight = 1)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        Item? targetItem = await dbContext.Items.Include(i => i.TagTarget).FirstOrDefaultAsync(i => i.Id == targetItemId);
        if (targetItem == null)
        {
            return new Failure("対象アイテムが見つかりません。");
        }

        var content = requestType switch
        {
            TaggingRequestType.Add => ContractMessages.MutualTagAddRequestSent,
            _ => ContractMessages.MutualTagDeleteRequestSent
        };

        var requestItem = new Item
        {
            OwnerId = requesterUserId,
            Content = content
        };

        var contract = new TaggingRequestEntity
        {
            ContractType = ContractTypes.Mutual,
            OwnerId = requesterUserId,
            RequesterUserId = requesterUserId,
            TagOwnerUserId = tagOwnerUserId,
            TargetId = targetItem.TagTargetId > 0 ? targetItem.TagTargetId : targetItem.TagTarget.Id,
            Target = targetItem.TagTarget,
            TargetItemId = targetItemId,
            RequestedTagId = requestedTagId,
            ConsumedRightAssetId = consumedRightAssetId,
            Status = TradeStatus.Proposed,
            RequestType = requestType,
            ProposedWeight = proposedWeight,
            Payload = new MutualPayload(offeredTargetItemId, offeredTagId),
            RequestItem = requestItem
        };
        dbContext.TaggingRequestEntities.Add(contract);
        await dbContext.SaveChangesAsync();

        return await TryAutoAcceptAsync(contract, requestedTagId, tagOwnerUserId);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TaggingRequestEntity>> GetRequestsByItemIdAsync(int itemId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        Item? item = await dbContext.Items.FindAsync(itemId);
        if (item == null)
        {
            return [];
        }

        return await dbContext.TaggingRequestEntities
            .Include(r => r.Target).ThenInclude(t => t.Item)
            .Include(r => r.Owner)
            .Include(r => r.RequestedTag)
            .Where(r => r.TargetId == item.TagTargetId)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync();
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TaggingRequestEntity>> GetRequestsByEdgeIdAsync(int edgeId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        TagEdge? edge = await dbContext.TagEdges.FindAsync(edgeId);
        if (edge == null)
        {
            return [];
        }

        return await dbContext.TaggingRequestEntities
            .Include(r => r.Target).ThenInclude(t => t.TagEdge)
            .Include(r => r.Owner)
            .Include(r => r.RequestedTag)
            .Where(r => r.TargetId == edge.TagTargetId)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync();
    }

    /// <inheritdoc />
    public virtual async Task<Result<string>> AcceptContractAsync(int contractId, string currentUserId, int? fulfillerAssetId = null)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        TaggingRequestEntity? entity = await dbContext.TaggingRequestEntities
                                          .Include(c => c.Target).ThenInclude(t => t.Item)
                                          .Include(c => c.Target).ThenInclude(t => t.TagEdge)
                                          .Include(c => c.RequestedTag)
                                          .Include(c => c.ConsumedRightAsset)
                                          .FirstOrDefaultAsync(c => c.Id == contractId);

        if (entity != null && entity.TargetItemId == 0 && entity.TargetId > 0)
        {
            var itemId = await dbContext.Items
                .Where(i => i.TagTargetId == entity.TargetId)
                .Select(i => i.Id)
                .FirstOrDefaultAsync();
            if (itemId > 0)
            {
                entity.TargetItemId = itemId;
            }
        }

        Result<TaggingRequestEntity> preCheckResult = entity switch
        {
            null => new Failure("契約が見つかりません。"),
            { Status: not TradeStatus.Proposed } => new Failure("実行・承認できない状態の契約です。"),
            { ContractType: ContractTypes.Trigger, RequesterUserId: var reqId } when reqId != currentUserId => new Failure("実行できない契約です。"),
            { ContractType: ContractTypes.Gratis or ContractTypes.Mutual, TagOwnerUserId: var ownerId } when ownerId != currentUserId => new Failure("承認できない契約です。"),
            _ => new Success<TaggingRequestEntity>(entity)
        };

        return await (preCheckResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<TaggingRequestEntity> s => ProcessAcceptContractAtomicAsync(dbContext, s.Value, currentUserId, fulfillerAssetId)
        });
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "コントラクト処理の任意の例外を結果ユニオンへ変換するため広く捕捉する")]
    private async Task<Result<string>> ProcessAcceptContractAtomicAsync(
        ApplicationDbContext dbContext,
        TaggingRequestEntity entity,
        string currentUserId,
        int? fulfillerAssetId)
    {
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            IContractExecutor? executor = _executorFactory.GetExecutor(entity.ContractType);
            Result<string> executeResult = executor is not null
                ? await executor.ExecuteAsync(dbContext, entity, currentUserId, fulfillerAssetId)
                : new Failure(ContractMessages.UnknownContractType);

            return await (executeResult switch
            {
                Failure f => RollbackAndReturnAsync(transaction, f),
                Success<string> s => CommitAndReturnAsync(dbContext, transaction, entity, s)
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return new Failure($"契約の承認中に予期せぬエラーが発生しました: {ex.Message}");
        }
    }

    private static async Task<Result<string>> RollbackAndReturnAsync(IDbContextTransaction transaction, Failure f)
    {
        await transaction.RollbackAsync();
        return f;
    }

    private static async Task<Result<string>> CommitAndReturnAsync(
        ApplicationDbContext dbContext,
        IDbContextTransaction transaction,
        TaggingRequestEntity entity,
        Success<string> s)
    {
        entity.Status = TradeStatus.Executed;
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return s;
    }

    /// <inheritdoc />
    public virtual async Task<Result<string>> CancelContractAsync(int contractId, string currentUserId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        TaggingRequestEntity? entity = await dbContext.TaggingRequestEntities
                                          .FirstOrDefaultAsync(c => c.Id == contractId);

        Result<TaggingRequestEntity> fetchResult = entity switch
        {
            null => new Failure("契約が見つかりません。"),
            { Status: not TradeStatus.Proposed } => new Failure("この状態の契約はキャンセルできません。"),
            var e when e.RequesterUserId != currentUserId && e.TagOwnerUserId != currentUserId => new Failure("この契約をキャンセル・拒否する権限がありません。"),
            _ => new Success<TaggingRequestEntity>(entity)
        };

        return await (fetchResult switch
        {
            Failure f => Task.FromResult<Result<string>>(f),
            Success<TaggingRequestEntity> s => ProcessCancelAsync(dbContext, s.Value)
        });
    }

    private static async Task<Result<string>> ProcessCancelAsync(ApplicationDbContext dbContext, TaggingRequestEntity entity)
    {
        entity.Status = TradeStatus.Canceled;
        await dbContext.SaveChangesAsync();
        return new Success<string>("契約をキャンセルしました。");
    }

    /// <summary>
    ///     タグの <c>AutoAcceptIncomingTaggingRequests</c> フラグを確認し、有効であれば即時承認する。
    ///     <see cref="ProposeGratisContractAsync" /> および <see cref="ProposeMutualContractAsync" /> から共有して使用する。
    /// </summary>
    private async Task<Result<TaggingRequestEntity>> TryAutoAcceptAsync(
        TaggingRequestEntity contract,
        int requestedTagId,
        string tagOwnerUserId)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync();
        bool autoAccept = await dbContext.Tags
            .AsNoTracking()
            .Where(t => t.Id == requestedTagId)
            .Select(t => t.AutoAcceptIncomingTaggingRequests)
            .FirstOrDefaultAsync();

        if (!autoAccept)
        {
            return new Success<TaggingRequestEntity>(contract);
        }

        Result<string> autoAcceptResult = await AcceptContractAsync(contract.Id, tagOwnerUserId);
        return autoAcceptResult switch
        {
            Failure f => new Failure($"{ContractMessages.AutoAcceptFailedFormatPrefix}{f.ErrorMessage}"),
            Success<string> => new Success<TaggingRequestEntity>(contract)
        };
    }
}