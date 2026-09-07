using SRNSMudApp.Models;

namespace SRNSMudApp.Services;

public interface INotificationService
{
    /// <summary>通知の未読状態や件数が変化した際に発火するイベント。</summary>
    event EventHandler? NotificationsChanged;

    Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(string userId);
    Task<int> GetUnreadCountAsync(string userId);
    Task MarkAsReadAsync(string userId, int sourceId, string sourceType);

    /// <summary>指定されたユーザーの未読通知をすべて既読として記録する。</summary>
    Task MarkAllAsReadAsync(string userId);
}