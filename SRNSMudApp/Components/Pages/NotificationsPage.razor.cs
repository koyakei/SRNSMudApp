using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

// 兄弟名前空間 SRNSMudApp.Components.Tag / .Item が同名型と解決されるため、
// エイリアスを名前空間の内側に置く

// CA1508: union 型パターンマッチにおける解析器の誤検知のため抑制する。
// IDE0051: ITaggingService は元の .razor の @inject を機械的に移したものであり、DI 登録を維持するため残す。
#pragma warning disable CA1508, IDE0051

namespace SRNSMudApp.Components.Pages;

using Tag = SRNSMudApp.Data.Tag;
using Item = SRNSMudApp.Data.Item;
/// <summary>
///     NotificationsPage のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、通知取得・既読処理・リクエスト承認/却下などの
///     UI オーケストレーションはこちらに集約する。
/// </summary>
public partial class NotificationsPage
{
    [CascadingParameter] private Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    [Inject] private INotificationService NotificationService { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private IHomeDataProvider HomeData { get; set; } = null!;
    [Inject] private INotificationsDataProvider NotificationsData { get; set; } = null!;
    [Inject] private ITaggingRequestActions RequestActions { get; set; } = null!;
    [Inject] private ISystemTagEnsurer SystemTagEnsurer { get; set; } = null!;
    [Inject] private TaggingContractService TaggingContractService { get; set; } = null!;
    [Inject] private ITaggingService TaggingService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;

    private string? _userId;
    private List<NotificationDto> _notifications = [];
    private bool _isLoading = true;

    private List<Tag> _allTags = [];
    private List<TagRelationToTag> _allTagRelationsToTags = [];
    private int? _currentUserGoodTagId;
    private int? _currentUserBadTagId;

    protected override async Task OnInitializedAsync()
    {
        AuthenticationState authState = await AuthenticationStateTask;
        ClaimsPrincipal user = authState.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            _userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (_userId != null)
            {
                _notifications = await NotificationService.GetUserNotificationsAsync(_userId);

                await FetchTagsAsync();
                await FetchAssociatedItemsAsync();
            }
        }
        _isLoading = false;
    }

    private async Task FetchTagsAsync()
    {
        (List<Tag> tags, List<TagRelationToTag> relations) = await HomeData.GetTagsAndRelationsAsync();
        _allTags = tags;
        _allTagRelationsToTags = relations;

        if (!string.IsNullOrEmpty(_userId))
        {
            SystemTagIds systemTags = ResourceListViewModel.FindSystemTags(_allTags, _userId);
            _currentUserGoodTagId = systemTags.GoodTagId;
            _currentUserBadTagId = systemTags.BadTagId;
        }
    }

    private async Task FetchAssociatedItemsAsync()
    {
        IReadOnlyList<int> itemIds = NotificationsViewModel.GetAssociatedItemIds(_notifications);
        if (itemIds.Count == 0)
        {
            return;
        }

        List<Item> items = await NotificationsData.GetAssociatedItemsAsync(itemIds);
        NotificationsViewModel.MapAssociatedItems(_notifications, items);
    }

    public async Task EnsureSystemTagsExistAsync()
    {
        (SystemTagIds ids, var refetch) = await SystemTagEnsurer.EnsureAsync(
            _userId, new SystemTagIds(_currentUserGoodTagId, _currentUserBadTagId));
        _currentUserGoodTagId = ids.GoodTagId;
        _currentUserBadTagId = ids.BadTagId;

        if (refetch)
        {
            await FetchTagsAsync();
        }
    }

    private async Task HandleNotificationClick(NotificationDto notification)
    {
        if (!notification.IsRead && _userId != null)
        {
            await NotificationService.MarkAsReadAsync(_userId, notification.SourceId, notification.Kind.SourceType);
        }
        NavigationManager.NavigateTo(notification.TargetUrl.ToHref());
    }

    private static string GetRelativeTime(DateTimeOffset dateTime) => NotificationsViewModel.GetRelativeTime(dateTime);

    private async Task ApproveRequestAsync(NotificationDto notification)
    {
        if (_userId == null)
        {
            return;
        }

        if (!await RequestActions.ApproveAsync(notification.SourceId, _userId))
        {
            return;
        }

        // UIを更新
        if (notification.Kind is TagRequestNotification reqNote)
        {
            var index = _notifications.IndexOf(notification);
            if (index != -1)
            {
                _notifications[index] = notification with
                {
                    Kind = reqNote with { Status = TradeStatus.Executed },
                    IsRead = true
                };
            }
        }
        StateHasChanged();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task RejectRequestAsync(NotificationDto notification)
    {
        if (_userId == null)
        {
            return;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<RejectRequestDialog>("リクエストを却下", options);
        DialogResult? result = await dialog.Result;

        if (result is { Canceled: false })
        {
            try
            {
                var comment = result.Data as string;
                _ = await TaggingContractService.CancelContractAsync(notification.SourceId, _userId);
                _ = Snackbar.Add("リクエストを却下しました。", Severity.Success);

                // UIを更新
                if (notification.Kind is TagRequestNotification reqNote)
                {
                    var index = _notifications.IndexOf(notification);
                    if (index != -1)
                    {
                        _notifications[index] = notification with
                        {
                            Kind = reqNote with { Status = TradeStatus.Rejected },
                            IsRead = true
                        };
                    }
                }
                StateHasChanged();
            }
            catch (Exception ex)
            {
                _ = Snackbar.Add($"エラー: {ex.Message}", Severity.Error);
            }
        }
    }
}