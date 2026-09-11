using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
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
    protected override async Task<Result<bool>> ExecuteAsync(
        CreatePublicOfferCommand command,
        CancellationToken cancellationToken)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        _ = dbContext.PublicTradeOffers!.Add(command.Offer);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}

/// <summary>バウンティを永続化するコマンドハンドラー。</summary>
public sealed class CreateBountyCommandHandler(IDbContextFactory<ApplicationDbContext> dbFactory)
    : CommandHandlerBase<CreateBountyCommand, Result<bool>>
{
    protected override async Task<Result<bool>> ExecuteAsync(
        CreateBountyCommand command,
        CancellationToken cancellationToken)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        _ = dbContext.TaggingRequestEntities.Add(command.Bounty);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}

/// <summary>トリガー契約を永続化するコマンドハンドラー。</summary>
public sealed class CreateTriggerContractCommandHandler(IDbContextFactory<ApplicationDbContext> dbFactory)
    : CommandHandlerBase<CreateTriggerContractCommand, Result<bool>>
{
    protected override async Task<Result<bool>> ExecuteAsync(
        CreateTriggerContractCommand command,
        CancellationToken cancellationToken)
    {
        await using ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        _ = dbContext.TaggingRequestEntities.Add(command.TriggerContract);
        _ = await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}