using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

// CA1508: union 型 (Option<T> / CheckAuth 結果など) の網羅的パターンマッチでは、先行アームの後の
// Some / エラー型アームが静的に「常に真」とみなされるが、網羅性確保のためアームは必須。
// 解析器の誤検知のため、ファイル単位で抑制する。
#pragma warning disable CA1508

namespace SRNSMudApp.Services;

public record TagExists;
public record TagMissing;

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union TagPresenceState(TagExists, TagMissing);

public record AuthorizedToReject;
public record UnauthorizedToReject;

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union RejectAuthorization(AuthorizedToReject, UnauthorizedToReject);

public class TaggingService(IDbContextFactory<ApplicationDbContext> dbFactory) : ITaggingService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task AddTagAsync<T>(int entityId, int tagId) where T : class, IDirectTaggable
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        T? entity = await context.Set<T>().Include(e => e.Tags).FirstOrDefaultAsync(e => e.Id == entityId);
        Tag? tag = await context.Tags.FindAsync(tagId);

        Option<(T entity, Tag tag)> tupleOption = (entity, tag) switch
        {
            (not null, not null) => new Some<(T entity, Tag tag)>((entity, tag)),
            _ => new None()
        };

        await (tupleOption switch
        {
            None => Task.CompletedTask,
            Some<(T entity, Tag tag)> some => some.Value.entity.Tags.Any(t => t.Id == tagId) switch
            {
                true => (TagPresenceState)new TagExists(),
                false => new TagMissing()
            } switch
            {
                TagExists => Task.CompletedTask,
                _ => AddTagAndSaveAsync(context, some.Value.entity, some.Value.tag)
            },
            _ => Task.CompletedTask
        });
    }

    private static async Task AddTagAndSaveAsync<T>(ApplicationDbContext context, T entity, Tag tag) where T : class, IDirectTaggable
    {
        entity.Tags.Add(tag);
        _ = await context.SaveChangesAsync();
    }

    public async Task RemoveTagAsync<T>(int entityId, int tagId) where T : class, IDirectTaggable
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();

        T? entity = await context.Set<T>().Include(e => e.Tags).FirstOrDefaultAsync(e => e.Id == entityId);

        var entityOption = Option<T>.Create(entity);
        await (entityOption switch
        {
            None => Task.CompletedTask,
            Some<T> someEntity => Option<Tag>.Create(someEntity.Value.Tags.FirstOrDefault(t => t.Id == tagId)) switch
            {
                None => Task.CompletedTask,
                Some<Tag> someTag => RemoveTagAndSaveAsync(context, someEntity.Value, someTag.Value),
                _ => Task.CompletedTask
            },
            _ => Task.CompletedTask
        });
    }

    private static async Task RemoveTagAndSaveAsync<T>(ApplicationDbContext context, T entity, Tag tag) where T : class, IDirectTaggable
    {
        _ = entity.Tags.Remove(tag);
        _ = await context.SaveChangesAsync();
    }

    public async Task RejectRequestAsync(int requestId, string rejectUserId, string? comment)
    {
        await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
        TaggingRequestEntity request = await context.TaggingRequestEntities!.FirstOrDefaultAsync(r => r.Id == requestId) switch
        {
            { } req => req,
            null => throw new InvalidOperationException("リクエストが見つかりません。")
        };

        _ = request.Status switch
        {
            TradeStatus.Proposed => request,
            TradeStatus.Executed => throw new InvalidOperationException("このリクエストは既に処理されています。"),
            TradeStatus.Canceled => throw new InvalidOperationException("このリクエストは既に処理されています。"),
            TradeStatus.Rejected => throw new InvalidOperationException("このリクエストは既に処理されています。"),
            _ => throw new InvalidOperationException("このリクエストは既に処理されています。")
        };

        RejectAuthorization authState = (request.TagOwnerUserId == rejectUserId || request.RequesterUserId == rejectUserId || request.ContractType == "Trigger" || request.ContractType == "Bounty") switch
        {
            true => new AuthorizedToReject(),
            false => new UnauthorizedToReject()
        };

        _ = authState switch
        {
            AuthorizedToReject => true,
            _ => throw new UnauthorizedAccessException("このリクエストを却下する権限がありません。")
        };

        request.Status = TradeStatus.Rejected;
        request.Rejection = new RejectionReason(comment ?? "");

        _ = await context.SaveChangesAsync();
    }
}