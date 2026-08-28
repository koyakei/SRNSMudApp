using SRNSMudApp.Data;
using SRNSMudApp.Models;

// 親名前空間 Pages と同名型の衝突を避けるため、エイリアスを置く

namespace SRNSMudApp.Components.Pages;

/// <summary>
///     NotificationsPage コンポーネントに含まれる純粋なロジックを切り出した ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class NotificationsViewModel
{
    /// <summary>
    ///     通知に関連付けられたアイテム ID の一覧（重複なし、ID 0 は除外）を返す。
    /// </summary>
    public static IReadOnlyList<int> GetAssociatedItemIds(IEnumerable<NotificationDto> notifications)
    {
        return [.. notifications
            .Where(n => n.AssociatedItemId != 0)
            .Select(n => n.AssociatedItemId)
            .Distinct()];
    }

    /// <summary>
    ///     取得済みアイテムを通知へ紐付ける。ID が一致する通知のみ更新される。
    /// </summary>
    public static void MapAssociatedItems(
        IEnumerable<NotificationDto> notifications,
        IReadOnlyCollection<Data.Item> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var itemDict = items.ToDictionary(i => i.Id);
        foreach (NotificationDto notification in notifications)
        {
            if (notification.AssociatedItemId != 0 &&
                itemDict.TryGetValue(notification.AssociatedItemId, out Data.Item? item))
            {
                notification.AssociatedItem = item;
            }
        }
    }

    /// <summary>
    ///     通知のハイライト対象タグに対する TimelineEvent を生成する。対象がなければ空リスト。
    /// </summary>
    public static IReadOnlyList<TimelineEvent> CreateHighlightEvents(NotificationDto notification, string? userId)
    {
        List<TimelineEvent> highlightEvents = [];
        if (notification.HighlightTagId.HasValue)
        {
            highlightEvents.Add(new TimelineEvent
            {
                EventType = "Update",
                FollowedTagId = notification.HighlightTagId.Value,
                OwnerId = userId ?? ""
            });
        }

        return highlightEvents;
    }

    /// <summary>
    ///     現在時刻からの相対的な経過時間表現を返す。
    /// </summary>
    public static string GetRelativeTime(DateTimeOffset dateTime, DateTimeOffset? now = null)
    {
        DateTimeOffset current = now ?? DateTimeOffset.UtcNow;
        TimeSpan timeSpan = current - dateTime;
        return timeSpan switch
        {
            _ when timeSpan <= TimeSpan.FromSeconds(60) => $"{timeSpan.Seconds}秒前",
            _ when timeSpan <= TimeSpan.FromMinutes(60) => $"{timeSpan.Minutes}分前",
            _ when timeSpan <= TimeSpan.FromHours(24) => $"{timeSpan.Hours}時間前",
            _ => $"{timeSpan.Days}日前"
        };
    }
}