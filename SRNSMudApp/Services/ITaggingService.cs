using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

public interface ITaggingService
{
    Task AddTagAsync<T>(int entityId, int tagId) where T : class, ITaggable;
    Task RemoveTagAsync<T>(int entityId, int tagId) where T : class, ITaggable;
    Task RejectRequestAsync(int requestId, string rejectUserId, string? comment);
}