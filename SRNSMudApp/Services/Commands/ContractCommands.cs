using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services.Commands;

/// <summary>公開オファー作成コマンド。</summary>
public sealed record CreatePublicOfferCommand(PublicTradeOffer Offer);

/// <summary>バウンティ作成コマンド。</summary>
public sealed record CreateBountyCommand(TaggingRequestEntity Bounty);

/// <summary>公開オファー実行によるトリガー契約作成コマンド。</summary>
public sealed record CreateTriggerContractCommand(TaggingRequestEntity TriggerContract);

/// <summary>公開オファーを永続化するコマンドハンドラー。</summary>
public sealed class CreatePublicOfferCommandHandler(IDbContextFactory<ApplicationDbContext> dbFactory)
    : CommandHandlerBase<CreatePublicOfferCommand, Result<bool>>
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    protected override async Task<Result<bool>> ExecuteAsync(
        CreatePublicOfferCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Offer.RequiredAssetAmount < 0)
        {
            return new Failure("要求するアセット量は0以上を指定してください。");
        }

        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        Tag? tag = await dbContext.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == command.Offer.OfferedTagId, cancellationToken);

        if (tag is null)
        {
            return new Failure("指定されたタグが存在しません。");
        }

        // 所有権チェック: 自身が所有するタグのみオファー作成可能 (Issue 5-4)
        if (tag.OwnerId != command.Offer.OwnerId)
        {
            return new Failure("自身が所有していないタグのオファーは作成できません。");
        }

        _ = dbContext.PublicTradeOffers!.Add(command.Offer);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}

/// <summary>バウンティを永続化するコマンドハンドラー。</summary>
public sealed class CreateBountyCommandHandler(IDbContextFactory<ApplicationDbContext> dbFactory)
    : CommandHandlerBase<CreateBountyCommand, Result<bool>>
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    protected override async Task<Result<bool>> ExecuteAsync(
        CreateBountyCommand command,
        CancellationToken cancellationToken)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // バウンティ報酬アセットのバリデーション (Issue 2-4)
        if (command.Bounty.Payload is BountyPayload { OfferedRewardAssetId: not 0 } bp)
        {
            RightAsset? rewardAsset = await dbContext.RightAssets
                .FirstOrDefaultAsync(a => a.Id == bp.OfferedRewardAssetId, cancellationToken);

            if (rewardAsset is null)
            {
                return new Failure("指定された報酬アセットが存在しません。");
            }

            if (rewardAsset.OwnerId != command.Bounty.RequesterUserId)
            {
                return new Failure("自身が所有していないアセットを報酬に設定することはできません。");
            }

            if (rewardAsset.IsBurned || rewardAsset.Amount <= 0)
            {
                return new Failure("指定された報酬アセットは燃焼済みまたは残高が不足しています。");
            }

            // 同一アセットが既に別のアクティブなバウンティに設定されていないか検証
            var activeBounties = await dbContext.TaggingRequestEntities
                .Where(b => b.ContractType == ContractTypes.Bounty && b.Status == TradeStatus.Proposed)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (activeBounties.Any(b => b.Payload is BountyPayload p && p.OfferedRewardAssetId == bp.OfferedRewardAssetId))
            {
                return new Failure("指定された報酬アセットは既に別のアクティブなバウンティに設定されています。");
            }
        }

        _ = dbContext.TaggingRequestEntities.Add(command.Bounty);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}

/// <summary>トリガー契約を永続化するコマンドハンドラー。</summary>
public sealed class CreateTriggerContractCommandHandler(IDbContextFactory<ApplicationDbContext> dbFactory)
    : CommandHandlerBase<CreateTriggerContractCommand, Result<bool>>
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    protected override async Task<Result<bool>> ExecuteAsync(
        CreateTriggerContractCommand command,
        CancellationToken cancellationToken)
    {
        await using ApplicationDbContext dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);
        _ = dbContext.TaggingRequestEntities.Add(command.TriggerContract);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}