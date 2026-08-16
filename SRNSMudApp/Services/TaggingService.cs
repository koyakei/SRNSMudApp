namespace SRNSMudApp.Services;

using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;

public class TaggingService(IDbContextFactory<ApplicationDbContext> dbFactory) : ITaggingService
{
    public async Task AddTagAsync<T>(int entityId, int tagId) where T : class, ITaggable
    {
        await using var context = await dbFactory.CreateDbContextAsync();
        
        var entity = await context.Set<T>()
            .Include(e => e.Tags)
            .FirstOrDefaultAsync(e => e.Id == entityId);

        if (entity == null) return;

        var tag = await context.Tags.FindAsync(tagId);
        if (tag == null) return;

        if (!entity.Tags.Any(t => t.Id == tagId))
        {
            entity.Tags.Add(tag);
            await context.SaveChangesAsync();
        }
    }

    public async Task RemoveTagAsync<T>(int entityId, int tagId) where T : class, ITaggable
    {
        await using var context = await dbFactory.CreateDbContextAsync();
        
        var entity = await context.Set<T>()
            .Include(e => e.Tags)
            .FirstOrDefaultAsync(e => e.Id == entityId);

        if (entity == null) return;

        var tag = entity.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tag != null)
        {
            entity.Tags.Remove(tag);
            await context.SaveChangesAsync();
        }
    }
}
