namespace SRNSMudApp.Components.Bounty;

using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

/// <summary>
///     バウンティ（タグ付け依頼）完了ダイアログコンポーネントのコードビハインド。
///     対象アイテム・付与タグ・報酬情報の表示、消費アセット選択および依頼完了実行を制御する。
/// </summary>
public partial class FulfillBountyDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    [Parameter]
    public TaggingRequestEntity Bounty { get; set; } = null!;

    [Inject]
    private FulfillBountyViewModel ViewModel { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    private MudForm? _form;
    private bool _isValid;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        var currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

        await ViewModel.InitializeAsync(Bounty, currentUserId);
    }

    private bool CanSubmit()
    {
        if (ViewModel.IsLoading || ViewModel.IsSubmitting)
        {
            return false;
        }

        return ViewModel.CanFulfillAsOwner || (ViewModel.MyValidAssets.Count > 0 && ViewModel.SelectedAsset is not null && _isValid);
    }

    private async Task Submit()
    {
        if (ViewModel.IsSubmitting)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        if (!CanSubmit())
        {
            return;
        }

        if (!ViewModel.CanFulfillAsOwner)
        {
            await _form!.ValidateAsync();
            if (!_isValid)
            {
                return;
            }
        }

        var result = await ViewModel.SubmitAsync();
        switch (result)
        {
            case Success<string>:
                Snackbar.Add("依頼を完了しました！", Severity.Success);
                MudDialog.Close(DialogResult.Ok(true));
                break;
            case Failure fail:
                if (fail.ErrorMessage == Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage)
                {
                    Snackbar.Add(fail.ErrorMessage, Severity.Info);
                    return;
                }

                Snackbar.Add($"エラーが発生しました: {fail.ErrorMessage}", Severity.Error);
                break;
            default:
                break;
        }
    }

    private void Cancel()
    {
        MudDialog.Cancel();
    }
}