namespace SRNSMudApp.Components.UserGroup;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

/// <summary>
///     ユーザーグループのメンバー一覧・追加・削除ダイアログコンポーネントのコードビハインド。
/// </summary>
public partial class UserGroupMembersDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Inject]
    private UserGroupMembersViewModel ViewModel { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Parameter]
    public UserGroup Group { get; set; } = null!;

    [Parameter]
    public string CurrentUserId { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.InitializeAsync(Group, CurrentUserId);
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

    private void Close()
    {
        MudDialog.Close(DialogResult.Ok(true));
    }
}