namespace SRNSMudApp.Components.Tag;

using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

/// <summary>
///     タグ内容の編集提案ダイアログコンポーネントのコードビハインド。
///     提案内容・理由の入力と提案作成処理を制御する。
/// </summary>
public partial class TagContentProposalDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    [Parameter]
    public Tag Tag { get; set; } = null!;

    [Inject]
    private TagContentProposalViewModel ViewModel { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var userId = string.Empty;
        if (AuthState is not null)
        {
            var auth = await AuthState;
            userId = auth.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        }

        ViewModel.Initialize(Tag, userId);
    }

    private void Cancel()
    {
        MudDialog.Cancel();
    }

    private async Task SubmitAsync()
    {
        if (ViewModel.IsSubmitting)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        if (AuthState is null)
        {
            Snackbar.Add("ログインが必要です。", Severity.Warning);
            return;
        }

        var result = await ViewModel.SubmitAsync();

        switch (result)
        {
            case Success<TagContentProposal>:
                Snackbar.Add("編集提案を送信しました。", Severity.Success);
                MudDialog.Close(DialogResult.Ok(true));
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