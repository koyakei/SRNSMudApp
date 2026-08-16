namespace SRNSMudApp.Services;

using SRNSMudApp.Data;

public interface ITaggingService
{
    Task AddTagAsync<T>(int entityId, int tagId) where T : class, ITaggable;
    Task RemoveTagAsync<T>(int entityId, int tagId) where T : class, ITaggable;
}
