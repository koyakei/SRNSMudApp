namespace SRNSMudApp.Components.UserGroup;

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

/// <summary>
///     ユーザーグループメンバー管理画面（URL ルーティング対応）のコードビハインド。
///     URL パラメータの GroupId からグループ情報を読み込み、メンバーの閲覧・追加・削除を行う。
/// </summary>
public partial class UserGroupMembers : ComponentBase
{
    [Inject]
    private UserGroupMembersViewModel ViewModel { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    [Parameter]
    public int GroupId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        string currentUserId = string.Empty;
        if (AuthState is not null)
        {
            var auth = await AuthState;
            currentUserId = auth.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        }

        await ViewModel.InitializeAsync(GroupId, currentUserId);
    }

    private Task<IEnumerable<ApplicationUser>> SearchUsersAsync(string? value, CancellationToken ct)
    {
        return ViewModel.SearchUsersAsync(value, ct);
    }

    private async Task AddMemberAsync()
    {
        if (ViewModel.IsAdding)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        string? addedUserName = ViewModel.SelectedUser?.UserName;
        Result<bool> result = await ViewModel.AddMemberAsync();

        switch (result)
        {
            case Success<bool>:
                Snackbar.Add($"ユーザー「{addedUserName}」を追加しました。", Severity.Success);
                break;
            case Failure fail:
                if (fail.ErrorMessage == Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage)
                {
                    Snackbar.Add(fail.ErrorMessage, Severity.Info);
                    return;
                }

                Snackbar.Add(fail.ErrorMessage, Severity.Error);
                break;
            default:
                break;
        }
    }

    private async Task RemoveMemberAsync(string userId)
    {
        Result<bool> result = await ViewModel.RemoveMemberAsync(userId);

        switch (result)
        {
            case Success<bool>:
                Snackbar.Add("メンバーを削除しました。", Severity.Success);
                break;
            case Failure fail:
                Snackbar.Add(fail.ErrorMessage, Severity.Error);
                break;
            default:
                break;
        }
    }
}