using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Pages;

/// <summary>
///     NotificationsPage コンポーネントに含まれる表示ロジックおよび通知操作ロジックを集約する ViewModel。
///     UI (MudBlazor / Dialog) への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public class NotificationsViewModel
{
    private readonly INotificationService _notificationService;
    private readonly IHomeDataProvider _homeData;
    private readonly INotificationsDataProvider _notificationsData;
    private readonly ITaggingRequestActions _requestActions;
    private readonly ISystemTagEnsurer _systemTagEnsurer;
    private readonly ITaggingContractService _taggingContractService;
    private readonly IItemSplitService _itemSplitService;
    private readonly ITagContentProposalService _tagContentProposalService;
    private readonly ITagNameProposalService _tagNameProposalService;
    private readonly IRightAssetDataProvider _rightAssetDataProvider;

    public NotificationsViewModel(
        INotificationService notificationService,
        IHomeDataProvider homeData,
        INotificationsDataProvider notificationsData,
        ITaggingRequestActions requestActions,
        ISystemTagEnsurer systemTagEnsurer,
        ITaggingContractService taggingContractService,
        IItemSplitService itemSplitService,
        ITagContentProposalService tagContentProposalService,
        ITagNameProposalService tagNameProposalService,
        IRightAssetDataProvider rightAssetDataProvider)
    {
        _notificationService = notificationService;
        _homeData = homeData;
        _notificationsData = notificationsData;
        _requestActions = requestActions;
        _systemTagEnsurer = systemTagEnsurer;
        _taggingContractService = taggingContractService;
        _itemSplitService = itemSplitService;
        _tagContentProposalService = tagContentProposalService;
        _tagNameProposalService = tagNameProposalService;
        _rightAssetDataProvider = rightAssetDataProvider;
    }

    public string? CurrentUserId { get; private set; }
    public bool IsAdmin { get; private set; }
    public IReadOnlyList<NotificationDto> Notifications { get; private set; } = [];
    public bool IsLoading { get; private set; } = true;

    [SuppressMessage("Usage", "CA1002:Do not expose generic lists", Justification = "Blazor component parameter requirement")]
    public List<Data.Tag> AllTags { get; private set; } = [];

    [SuppressMessage("Usage", "CA1002:Do not expose generic lists", Justification = "Blazor component parameter requirement")]
    public List<TagRelationToTag> AllTagRelationsToTags { get; private set; } = [];

    public int? CurrentUserGoodTagId { get; private set; }
    public int? CurrentUserBadTagId { get; private set; }
    public int? CurrentUserShinjiTagId { get; private set; }
    public int? CurrentUserZenTagId { get; private set; }
    public int? CurrentUserBiTagId { get; private set; }

    public async Task InitializeAsync(string? userId, bool isAdmin = false)
    {
        CurrentUserId = userId;
        IsAdmin = isAdmin;
        if (!string.IsNullOrEmpty(userId))
        {
            Notifications = await _notificationService.GetUserNotificationsAsync(userId);

            await FetchTagsAsync();
            await FetchAssociatedItemsAsync();

            await _notificationService.MarkAllAsReadAsync(userId);
        }

        IsLoading = false;
    }

    public async Task RefreshNotificationsAsync()
    {
        if (!string.IsNullOrEmpty(CurrentUserId))
        {
            Notifications = await _notificationService.GetUserNotificationsAsync(CurrentUserId);
            await FetchAssociatedItemsAsync();
        }
    }

    public async Task FetchTagsAsync()
    {
        (List<Data.Tag> tags, List<TagRelationToTag> relations) = await _homeData.GetTagsAndRelationsAsync();
        AllTags = tags;
        AllTagRelationsToTags = relations;

        if (!string.IsNullOrEmpty(CurrentUserId))
        {
            SystemTagIds systemTags = ResourceListViewModel.FindSystemTags(AllTags, CurrentUserId);
            CurrentUserGoodTagId = systemTags.GoodTagId;
            CurrentUserBadTagId = systemTags.BadTagId;

            ReactionTagIds reactionTags = ResourceListViewModel.FindReactionTags(AllTags, CurrentUserId);
            CurrentUserShinjiTagId = reactionTags.ShinjiTagId;
            CurrentUserZenTagId = reactionTags.ZenTagId;
            CurrentUserBiTagId = reactionTags.BiTagId;
        }
    }

    public async Task FetchAssociatedItemsAsync()
    {
        IReadOnlyList<int> itemIds = GetAssociatedItemIds(Notifications);
        if (itemIds.Count == 0)
        {
            return;
        }

        List<Data.Item> items = await _notificationsData.GetAssociatedItemsAsync(itemIds, CurrentUserId, IsAdmin);
        MapAssociatedItems(Notifications, items);
    }

    public async Task EnsureSystemTagsExistAsync()
    {
        (SystemTagIds voteIds, ReactionTagIds reactionIds, var refetch) = await _systemTagEnsurer.EnsureAllAsync(
            CurrentUserId,
            new SystemTagIds(CurrentUserGoodTagId, CurrentUserBadTagId),
            new ReactionTagIds(CurrentUserShinjiTagId, CurrentUserZenTagId, CurrentUserBiTagId));

        CurrentUserGoodTagId = voteIds.GoodTagId;
        CurrentUserBadTagId = voteIds.BadTagId;
        CurrentUserShinjiTagId = reactionIds.ShinjiTagId;
        CurrentUserZenTagId = reactionIds.ZenTagId;
        CurrentUserBiTagId = reactionIds.BiTagId;

        if (refetch)
        {
            await FetchTagsAsync();
        }
    }

    public async Task MarkAsReadAsync(NotificationDto notification)
    {
        if (!notification.IsRead && CurrentUserId != null)
        {
            await _notificationService.MarkAsReadAsync(CurrentUserId, notification.SourceId, notification.Kind.SourceType);
        }
    }

    public async Task<TagCardActionResult> ApproveRequestAsync(NotificationDto notification)
    {
        if (CurrentUserId == null)
        {
            return TagCardActionResult.NoOp();
        }

        if (!await _requestActions.ApproveAsync(notification.SourceId, CurrentUserId))
        {
            return TagCardActionResult.NoOp();
        }

        UpdateNotificationStatus(notification, TradeStatus.Executed);
        await FetchAssociatedItemsAsync();
        return TagCardActionResult.Success(shouldNotifyChanged: true);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "例外をUI向けメッセージに変換するため")]
    public async Task<TagCardActionResult> RejectRequestAsync(NotificationDto notification, string? comment)
    {
        if (CurrentUserId == null)
        {
            return TagCardActionResult.NoOp();
        }

        try
        {
            _ = await _taggingContractService.RejectContractAsync(notification.SourceId, CurrentUserId);
            UpdateNotificationStatus(notification, TradeStatus.Rejected);
            await FetchAssociatedItemsAsync();
            return TagCardActionResult.Success("リクエストを却下しました。");
        }
        catch (Exception ex)
        {
            return TagCardActionResult.Error($"エラー: {ex.Message}");
        }
    }

    public async Task<TagCardActionResult> ApproveSplitRequestAsync(NotificationDto notification)
    {
        if (CurrentUserId == null)
        {
            return TagCardActionResult.NoOp();
        }

        Result<Data.Item> result = await _itemSplitService.ApproveSplitAsync(notification.SourceId, CurrentUserId);
        switch (result)
        {
            case Success<Data.Item>:
                UpdateNotificationStatus(notification, TradeStatus.Executed);
                await FetchAssociatedItemsAsync();
                return TagCardActionResult.Success("分割リクエストを承認しました。");
            case Failure fail:
                return TagCardActionResult.Error(fail.ErrorMessage);
            default:
                return TagCardActionResult.NoOp();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "例外をUI向けメッセージに変換するため")]
    public async Task<TagCardActionResult> RejectSplitRequestAsync(NotificationDto notification, string? comment)
    {
        if (CurrentUserId == null)
        {
            return TagCardActionResult.NoOp();
        }

        try
        {
            Result<bool> rejectResult = await _itemSplitService.RejectSplitAsync(notification.SourceId, CurrentUserId, comment);
            switch (rejectResult)
            {
                case Success<bool>:
                    UpdateNotificationStatus(notification, TradeStatus.Rejected);
                    await FetchAssociatedItemsAsync();
                    return TagCardActionResult.Success("分割リクエストを却下しました。");
                case Failure fail:
                    return TagCardActionResult.Error(fail.ErrorMessage);
                default:
                    return TagCardActionResult.NoOp();
            }
        }
        catch (Exception ex)
        {
            return TagCardActionResult.Error($"エラー: {ex.Message}");
        }
    }

    public async Task<TagCardActionResult> ApproveTagProposalAsync(NotificationDto notification)
    {
        if (CurrentUserId == null)
        {
            return TagCardActionResult.NoOp();
        }

        Result<Data.Tag> result = await _tagContentProposalService.ApproveProposalAsync(notification.SourceId, CurrentUserId);
        switch (result)
        {
            case Success<Data.Tag>:
                UpdateNotificationStatus(notification, TradeStatus.Executed);
                await FetchTagsAsync();
                return TagCardActionResult.Success("編集提案を承認しました。");
            case Failure fail:
                return TagCardActionResult.Error(fail.ErrorMessage);
            default:
                return TagCardActionResult.NoOp();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "例外をUI向けメッセージに変換するため")]
    public async Task<TagCardActionResult> RejectTagProposalAsync(NotificationDto notification, string? comment)
    {
        if (CurrentUserId == null)
        {
            return TagCardActionResult.NoOp();
        }

        try
        {
            Result<bool> rejectResult = await _tagContentProposalService.RejectProposalAsync(notification.SourceId, CurrentUserId, comment);
            switch (rejectResult)
            {
                case Success<bool>:
                    UpdateNotificationStatus(notification, TradeStatus.Rejected);
                    return TagCardActionResult.Success("編集提案を却下しました。", shouldNotifyChanged: true);
                case Failure fail:
                    return TagCardActionResult.Error(fail.ErrorMessage);
                default:
                    return TagCardActionResult.NoOp();
            }
        }
        catch (Exception ex)
        {
            return TagCardActionResult.Error($"エラー: {ex.Message}");
        }
    }

    public async Task<TagCardActionResult> ApproveTagNameProposalAsync(NotificationDto notification)
    {
        if (CurrentUserId == null)
        {
            return TagCardActionResult.NoOp();
        }

        Result<Data.Tag> result = await _tagNameProposalService.ApproveProposalAsync(notification.SourceId, CurrentUserId);
        switch (result)
        {
            case Success<Data.Tag>:
                UpdateNotificationStatus(notification, TradeStatus.Executed);
                await FetchTagsAsync();
                return TagCardActionResult.Success("名前変更提案を承認しました。");
            case Failure fail:
                return TagCardActionResult.Error(fail.ErrorMessage);
            default:
                return TagCardActionResult.NoOp();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "例外をUI向けメッセージに変換するため")]
    public async Task<TagCardActionResult> RejectTagNameProposalAsync(NotificationDto notification, string? comment)
    {
        if (CurrentUserId == null)
        {
            return TagCardActionResult.NoOp();
        }

        try
        {
            Result<bool> rejectResult = await _tagNameProposalService.RejectProposalAsync(notification.SourceId, CurrentUserId, comment);
            switch (rejectResult)
            {
                case Success<bool>:
                    UpdateNotificationStatus(notification, TradeStatus.Rejected);
                    return TagCardActionResult.Success("名前変更提案を却下しました。", shouldNotifyChanged: true);
                case Failure fail:
                    return TagCardActionResult.Error(fail.ErrorMessage);
                default:
                    return TagCardActionResult.NoOp();
            }
        }
        catch (Exception ex)
        {
            return TagCardActionResult.Error($"エラー: {ex.Message}");
        }
    }

    public async Task<TagCardActionResult> ApprovePermissionRequestAsync(NotificationDto notification)
    {
        if (CurrentUserId == null)
        {
            return TagCardActionResult.NoOp();
        }

        Result<bool> result = await _rightAssetDataProvider.ApprovePermissionRequestAsync(notification.SourceId, CurrentUserId);
        switch (result)
        {
            case Success<bool>:
                UpdateNotificationStatus(notification, TradeStatus.Executed);
                await FetchTagsAsync();
                return TagCardActionResult.Success("操作権限リクエストを承認しました。");
            case Failure fail:
                return TagCardActionResult.Error(fail.ErrorMessage);
            default:
                return TagCardActionResult.NoOp();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "例外をUI向けメッセージに変換するため")]
    public async Task<TagCardActionResult> RejectPermissionRequestAsync(NotificationDto notification, string? comment)
    {
        if (CurrentUserId == null)
        {
            return TagCardActionResult.NoOp();
        }

        try
        {
            Result<bool> rejectResult = await _rightAssetDataProvider.RejectPermissionRequestAsync(notification.SourceId, CurrentUserId, comment);
            switch (rejectResult)
            {
                case Success<bool>:
                    UpdateNotificationStatus(notification, TradeStatus.Rejected);
                    return TagCardActionResult.Success("操作権限リクエストを却下しました。", shouldNotifyChanged: true);
                case Failure fail:
                    return TagCardActionResult.Error(fail.ErrorMessage);
                default:
                    return TagCardActionResult.NoOp();
            }
        }
        catch (Exception ex)
        {
            return TagCardActionResult.Error($"エラー: {ex.Message}");
        }
    }

    [SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "NotificationKind pattern matching false positive")]
    private void UpdateNotificationStatus(NotificationDto notification, TradeStatus newStatus)
    {
        NotificationDto? updated = notification.Kind switch
        {
            TagRequestNotification reqNote => notification with { Kind = reqNote with { Status = newStatus }, IsRead = true },
            ItemSplitRequestNotification splitNote => notification with { Kind = splitNote with { Status = newStatus }, IsRead = true },
            TagContentProposalNotification proposalNote => notification with { Kind = proposalNote with { Status = newStatus }, IsRead = true },
            TagNameProposalNotification nameNote => notification with { Kind = nameNote with { Status = newStatus }, IsRead = true },
            TagPermissionRequestNotification permNote => notification with { Kind = permNote with { Status = newStatus }, IsRead = true },
            _ => null
        };

        if (updated != null)
        {
            Notifications = [.. Notifications.Select(n =>
                (n.SourceId == notification.SourceId && n.Kind.SourceType == notification.Kind.SourceType)
                    ? updated
                    : n)];
        }
    }

    // ============================================================
    // 純粋静的ヘルパーメソッド群
    // ============================================================

    /// <summary>
    ///     通知に対応するステータス（TradeStatus）を取得する。該当しない場合は null。
    /// </summary>
    public static TradeStatus? GetNotificationStatus(NotificationDto notification) => notification.Kind switch
    {
        TagRequestNotification r => r.Status,
        ItemSplitRequestNotification s => s.Status,
        TagContentProposalNotification p => p.Status,
        TagNameProposalNotification n => n.Status,
        TagPermissionRequestNotification perm => perm.Status,
        _ => null
    };

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
        IReadOnlyCollection<Data.Item>? items)
    {
        if (items == null || items.Count == 0)
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
                ParsedEventType = TimelineEventType.Update,
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