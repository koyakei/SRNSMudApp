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
/// </summary>
public class TaggingContractService(
    ApplicationDbContext dbContext,
    IEnumerable<IContractExecutor>? executors = null)
{
    public TaggingContractService(ApplicationDbContext dbContext)
        : this(dbContext, null)
    {
    }

    private readonly IReadOnlyList<IContractExecutor> _executors =
        executors?.ToList() is { Count: > 0 } list
            ? list
            : [
                new GratisContractExecutor(dbContext),
                new MutualContractExecutor(dbContext),
                new TriggerContractExecutor(dbContext),
                new BountyContractExecutor(dbContext)
            ];

    /// <summary>
    ///     Gratis コントラクトを提案する。タグの <c>AutoAcceptIncomingTaggingRequests</c> が有効な場合は即時承認される。
    /// </summary>
    /// <param name="requesterUserId">依頼者のユーザー ID。</param>
    /// <param name="tagOwnerUserId">タグオーナーのユーザー ID。</param>
    /// <param name="targetItemId">タグ付けの対象アイテム ID。</param>
    /// <param name="requestedTagId">付与・解除を要求するタグの ID。</param>
    /// <param name="requestType">付与 (<see cref="TaggingRequestType.Add" />) または解除要求の種別。</param>
    /// <param name="proposedWeight">提案する Weight 値（省略時は 1）。</param>
    /// <param name="message">依頼時のメッセージ（省略時はデフォルトメッセージを使用）。</param>
    /// <returns>
    ///     成功した場合は <see cref="Success{T}" />（作成された契約エンティティ）、
    ///     失敗した場合は <see cref="Failure" />（エラーメッセージ）。
    /// </returns>
    public async Task<Result<TaggingRequestEntity>> ProposeGratisContractAsync(
        string requesterUserId,
        string tagOwnerUserId,
        int targetItemId,
        int requestedTagId,
        TaggingRequestType requestType = TaggingRequestType.Add,
        int proposedWeight = 1,
        string? message = null)
    {
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

    /// <summary>
    ///     Mutual コントラクトを提案する。タグの <c>AutoAcceptIncomingTaggingRequests</c> が有効な場合は即時承認される。
    /// </summary>
    /// <param name="requesterUserId">依頼者のユーザー ID。</param>
    /// <param name="tagOwnerUserId">タグオーナーのユーザー ID。</param>
    /// <param name="targetItemId">タグ付けの対象アイテム ID。</param>
    /// <param name="requestedTagId">付与・解除を要求するタグの ID。</param>
    /// <param name="offeredTargetItemId">依頼者が提供するアイテムの ID。</param>
    /// <param name="offeredTagId">依頼者が提供するタグの ID。</param>
    /// <param name="consumedRightAssetId">依頼者が消費する RightAsset の ID。</param>
    /// <param name="requestType">付与 (<see cref="TaggingRequestType.Add" />) または解除要求の種別。</param>
    /// <param name="proposedWeight">提案する Weight 値（省略時は 1）。</param>
    /// <returns>
    ///     成功した場合は <see cref="Success{T}" />（作成された契約エンティティ）、
    ///     失敗した場合は <see cref="Failure" />（エラーメッセージ）。
    /// </returns>
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

    /// <summary>
    ///     指定アイテムに紐づくコントラクト一覧を取得する。
    /// </summary>
    /// <param name="itemId">対象アイテムの ID。</param>
    /// <returns>コントラクトエンティティの読み取り専用リスト。</returns>
    public virtual async Task<IReadOnlyList<TaggingRequestEntity>> GetRequestsByItemIdAsync(int itemId)
    {
        return await dbContext.TaggingRequestEntities
            .Include(r => r.TargetItem)
            .Include(r => r.Owner)
            .Include(r => r.RequestedTag)
            .Where(r => r.TargetItemId == itemId)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync();
    }

    /// <summary>
    ///     指定コントラクトを承認・実行する。実行処理は <see cref="IContractExecutor" /> に委譲する。
    /// </summary>
    /// <param name="contractId">承認するコントラクトの ID。</param>
    /// <param name="currentUserId">承認操作を実行しているユーザーの ID。</param>
    /// <param name="fulfillerAssetId">バウンティ契約で使用する実行者 RightAsset の ID（省略可能）。</param>
    /// <returns>
    ///     成功した場合は <see cref="Success{T}" />（成功メッセージ）、
    ///     失敗した場合は <see cref="Failure" />（エラーメッセージ）。
    /// </returns>
    public virtual async Task<Result<string>> AcceptContractAsync(int contractId, string currentUserId, int? fulfillerAssetId = null)
    {
        TaggingRequestEntity? entity = await dbContext.TaggingRequestEntities
                                          .Include(c => c.RequestedTag)
                                          .Include(c => c.ConsumedRightAsset)
                                          .FirstOrDefaultAsync(c => c.Id == contractId);

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
            Success<TaggingRequestEntity> s => ProcessAcceptContractAtomicAsync(s.Value, currentUserId, fulfillerAssetId)
        });
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "コントラクト処理の任意の例外を結果ユニオンへ変換するため広く捕捉する")]
    private async Task<Result<string>> ProcessAcceptContractAtomicAsync(TaggingRequestEntity entity, string currentUserId, int? fulfillerAssetId)
    {
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            IContractExecutor? executor = _executors.FirstOrDefault(e => e.ContractType == entity.ContractType);
            Result<string> executeResult = executor is not null
                ? await executor.ExecuteAsync(entity, currentUserId, fulfillerAssetId)
                : new Failure("DBに未知の契約型が存在します。");

            return await (executeResult switch
            {
                Failure f => RollbackAndReturnAsync(transaction, f),
                Success<string> s => CommitAndReturnAsync(transaction, entity, s)
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

    private async Task<Result<string>> CommitAndReturnAsync(IDbContextTransaction transaction, TaggingRequestEntity entity, Success<string> s)
    {
        entity.Status = TradeStatus.Executed;
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return s;
    }

    /// <summary>
    ///     指定コントラクトをキャンセル（または拒否）する。
    /// </summary>
    /// <param name="contractId">キャンセルするコントラクトの ID。</param>
    /// <param name="currentUserId">キャンセル操作を実行しているユーザーの ID。</param>
    /// <returns>
    ///     成功した場合は <see cref="Success{T}" />（成功メッセージ）、
    ///     失敗した場合は <see cref="Failure" />（エラーメッセージ）。
    /// </returns>
    public virtual async Task<Result<string>> CancelContractAsync(int contractId, string currentUserId)
    {
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
            Success<TaggingRequestEntity> s => ProcessCancelAsync(s.Value)
        });
    }

    private async Task<Result<string>> ProcessCancelAsync(TaggingRequestEntity entity)
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