namespace SRNSMudApp.Components.UserGroup;

using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Models.Unions;

using UserGroupEntity = SRNSMudApp.Data.UserGroup;

/// <summary>
///     ユーザーグループ作成・編集ダイアログコンポーネントのコードビハインド。
///     グループ名、説明の入力と保存処理を制御する。
/// </summary>
public partial class UserGroupCreateEditDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public UserGroupEntity? Group { get; set; }

    [Parameter]
    public string CurrentUserId { get; set; } = null!;

    [Inject]
    private UserGroupCreateEditViewModel ViewModel { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    protected override void OnInitialized()
    {
        ViewModel.Initialize(Group, CurrentUserId);
    }

    private void Cancel()
    {
        MudDialog.Cancel();
    }

    private async Task SaveAsync()
    {
        if (ViewModel.IsSaving)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        var result = await ViewModel.SaveAsync();

        switch (result)
        {
            case Success<UserGroupEntity> success:
                if (ViewModel.IsEditMode)
                {
                    Snackbar.Add($"グループ「{ViewModel.Name}」を更新しました。", Severity.Success);
                    MudDialog.Close(DialogResult.Ok(true));
                }
                else
                {
                    Snackbar.Add($"グループ「{success.Value.Name}」を作成しました。", Severity.Success);
                    MudDialog.Close(DialogResult.Ok(success.Value));
                }
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
}