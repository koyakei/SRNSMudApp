namespace SRNSMudApp.Components.Tag;

using System;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

/// <summary>
///     タグ編集ダイアログコンポーネントのコードビハインド。
///     タグ名、内容、および自動承認を許可するユーザーグループの編集と保存処理を制御する。
/// </summary>
public partial class TagEditDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    [Parameter]
    public Tag Tag { get; set; } = null!;

    [Inject]
    private TagEditViewModel ViewModel { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var auth = await AuthStateTask;
        var currentUserId = auth.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = auth.User.IsInRole("Admin");

        await ViewModel.InitializeAsync(Tag, currentUserId, isAdmin);
    }

    private void Cancel()
    {
        MudDialog.Cancel();
    }

    private async Task SaveAsync()
    {
        if (ViewModel.IsSubmitting)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        var result = await ViewModel.SaveAsync();
        switch (result)
        {
            case Success<bool>:
                MudDialog.Close(DialogResult.Ok(true));
                break;
            case Failure failure:
                if (failure.ErrorMessage == Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage)
                {
                    Snackbar.Add(failure.ErrorMessage, Severity.Info);
                    return;
                }

                var severity = failure.ErrorMessage.Contains("ロック", StringComparison.Ordinal)
                    ? Severity.Warning
                    : Severity.Error;
                Snackbar.Add(failure.ErrorMessage, severity);

                if (severity == Severity.Warning)
                {
                    MudDialog.Cancel();
                }
                break;
            default:
                break;
        }
    }
}