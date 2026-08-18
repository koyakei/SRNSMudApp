using SRNSMudApp.Models;

namespace SRNSMudApp.Services;

public interface INotificationService
{
    Task<List<NotificationDto>> GetUserNotificationsAsync(string userId);
    Task<int> GetUnreadCountAsync(string userId);
    Task MarkAsReadAsync(string userId, int sourceId, string sourceType);
}