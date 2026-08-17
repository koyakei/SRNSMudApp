using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

public class TaggingService(IDbContextFactory<ApplicationDbContext> dbFactory) : ITaggingService
{
    public async Task AddTagAsync<T>(int entityId, int tagId) where T : class, ITaggable
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        T? entity = await context.Set<T>()
            .Include(e => e.Tags)
            .FirstOrDefaultAsync(e => e.Id == entityId);

        if (entity == null)
        {
            return;
        }

        Tag? tag = await context.Tags.FindAsync(tagId);
        if (tag == null)
        {
            return;
        }

        if (!entity.Tags.Any(t => t.Id == tagId))
        {
            entity.Tags.Add(tag);
            await context.SaveChangesAsync();
        }
    }

    public async Task RemoveTagAsync<T>(int entityId, int tagId) where T : class, ITaggable
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();

        T? entity = await context.Set<T>()
            .Include(e => e.Tags)
            .FirstOrDefaultAsync(e => e.Id == entityId);

        if (entity == null)
        {
            return;
        }

        Tag? tag = entity.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tag != null)
        {
            entity.Tags.Remove(tag);
            await context.SaveChangesAsync();
        }
    }

    public async Task RejectRequestAsync(int requestId, string rejectUserId, string? comment)
    {
        await using ApplicationDbContext context = await dbFactory.CreateDbContextAsync();
        TaggingRequestEntity? request =
            await context.TaggingRequestEntities!.FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            throw new InvalidOperationException("リクエストが見つかりません。");
        }

        if (request.Status != TradeStatus.Proposed)
        {
            throw new InvalidOperationException("このリクエストは既に処理されています。");
        }

        // Only the tag owner or the requester can reject/cancel (similar to CancelContractAsync)
        if (request.TagOwnerUserId != rejectUserId && request.RequesterUserId != rejectUserId)
        {
            // For public offer and bounty, we might need different checks, but for now we follow general rule
            if (request is not PublicOfferTriggerContract && request is not BountyTaggingContract)
            {
                throw new UnauthorizedAccessException("このリクエストを却下する権限がありません。");
            }
        }

        request.Status = TradeStatus.Rejected;
        request.RejectedAt = DateTimeOffset.UtcNow;
        request.RejectComment = comment;

        await context.SaveChangesAsync();
    }
}