using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Components.Pages;

/// <summary>
///     NotificationsPage のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、ダイアログ起動や通知クリックなどの
///     UI オーケストレーションはこちらに集約する。
///     データアクセスおよびビジネスロジックは <see cref="NotificationsViewModel" /> へ委譲する。
/// </summary>
public partial class NotificationsPage
{
    [CascadingParameter] private Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    [Inject] private NotificationsViewModel ViewModel { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;

#pragma warning disable IDE1006 // Naming Styles for Blazor markup bindings
    private bool _isLoading => ViewModel.IsLoading;
    private IReadOnlyList<NotificationDto> _notifications => ViewModel.Notifications;
    private string? _userId => ViewModel.CurrentUserId;
    private List<Data.Tag> _allTags => ViewModel.AllTags;
    private List<TagRelationToTag> _allTagRelationsToTags => ViewModel.AllTagRelationsToTags;
    private int? _currentUserGoodTagId => ViewModel.CurrentUserGoodTagId;
    private int? _currentUserBadTagId => ViewModel.CurrentUserBadTagId;
    private int? _currentUserShinjiTagId => ViewModel.CurrentUserShinjiTagId;
    private int? _currentUserZenTagId => ViewModel.CurrentUserZenTagId;
    private int? _currentUserBiTagId => ViewModel.CurrentUserBiTagId;
#pragma warning restore IDE1006

    protected override async Task OnInitializedAsync()
    {
        AuthenticationState authState = await AuthenticationStateTask;
        ClaimsPrincipal user = authState.User;
        string? userId = user.Identity?.IsAuthenticated == true
            ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;

        await ViewModel.InitializeAsync(userId);
    }

    public Task EnsureSystemTagsExistAsync() => ViewModel.EnsureSystemTagsExistAsync();

    private async Task HandleNotificationClick(NotificationDto notification)
    {
        await ViewModel.MarkAsReadAsync(notification);
        NavigationManager.NavigateTo(notification.TargetUrl.ToHref());
    }

    private static string GetRelativeTime(DateTimeOffset dateTime) => NotificationsViewModel.GetRelativeTime(dateTime);

    private void ApplyResult(TagCardActionResult result)
    {
        switch (result.Type)
        {
            case TagCardActionResultType.Warning:
                if (result.Message != null)
                {
                    _ = Snackbar.Add(result.Message, Severity.Warning);
                }
                break;
            case TagCardActionResultType.Error:
                if (result.Message != null)
                {
                    _ = Snackbar.Add(result.Message, Severity.Error);
                }
                break;
            case TagCardActionResultType.Success:
                if (result.Message != null)
                {
                    _ = Snackbar.Add(result.Message, Severity.Success);
                }
                break;
            case TagCardActionResultType.NoOp:
            default:
                break;
        }

        if (result.ShouldNotifyChanged)
        {
            StateHasChanged();
        }
    }

    private async Task ApproveRequestAsync(NotificationDto notification)
    {
        TagCardActionResult result = await ViewModel.ApproveRequestAsync(notification);
        ApplyResult(result);
    }

    private async Task RejectRequestAsync(NotificationDto notification)
    {
        if (_userId == null)
        {
            return;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<RejectRequestDialog>("リクエストを却下", options);
        DialogResult? dialogResult = await dialog.Result;

        if (dialogResult is { Canceled: false })
        {
            var comment = dialogResult.Data as string;
            TagCardActionResult result = await ViewModel.RejectRequestAsync(notification, comment);
            ApplyResult(result);
        }
    }

    private async Task ApproveSplitRequestAsync(NotificationDto notification)
    {
        TagCardActionResult result = await ViewModel.ApproveSplitRequestAsync(notification);
        ApplyResult(result);
    }

    private async Task RejectSplitRequestAsync(NotificationDto notification)
    {
        if (_userId == null)
        {
            return;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<RejectRequestDialog>("分割リクエストを却下", options);
        DialogResult? dialogResult = await dialog.Result;

        if (dialogResult is { Canceled: false })
        {
            var comment = dialogResult.Data as string;
            TagCardActionResult result = await ViewModel.RejectSplitRequestAsync(notification, comment);
            ApplyResult(result);
        }
    }

    private async Task ApproveTagProposalAsync(NotificationDto notification)
    {
        TagCardActionResult result = await ViewModel.ApproveTagProposalAsync(notification);
        ApplyResult(result);
    }

    private async Task RejectTagProposalAsync(NotificationDto notification)
    {
        if (_userId == null)
        {
            return;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<RejectRequestDialog>("編集提案を却下", options);
        DialogResult? dialogResult = await dialog.Result;

        if (dialogResult is { Canceled: false })
        {
            var comment = dialogResult.Data as string;
            TagCardActionResult result = await ViewModel.RejectTagProposalAsync(notification, comment);
            ApplyResult(result);
        }
    }

    private async Task ApproveTagNameProposalAsync(NotificationDto notification)
    {
        TagCardActionResult result = await ViewModel.ApproveTagNameProposalAsync(notification);
        ApplyResult(result);
    }

    private async Task RejectTagNameProposalAsync(NotificationDto notification)
    {
        if (_userId == null)
        {
            return;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<RejectRequestDialog>("名前変更提案を却下", options);
        DialogResult? dialogResult = await dialog.Result;

        if (dialogResult is { Canceled: false })
        {
            var comment = dialogResult.Data as string;
            TagCardActionResult result = await ViewModel.RejectTagNameProposalAsync(notification, comment);
            ApplyResult(result);
        }
    }

    private async Task ApprovePermissionRequestAsync(NotificationDto notification)
    {
        TagCardActionResult result = await ViewModel.ApprovePermissionRequestAsync(notification);
        ApplyResult(result);
    }

    private async Task RejectPermissionRequestAsync(NotificationDto notification)
    {
        if (_userId == null)
        {
            return;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<RejectRequestDialog>("権限リクエストを却下", options);
        DialogResult? dialogResult = await dialog.Result;

        if (dialogResult is { Canceled: false })
        {
            var comment = dialogResult.Data as string;
            TagCardActionResult result = await ViewModel.RejectPermissionRequestAsync(notification, comment);
            ApplyResult(result);
        }
    }

    private async Task OnItemCardDataChangedAsync()
    {
        if (_userId != null)
        {
            await ViewModel.RefreshNotificationsAsync();
        }
    }
}