namespace SRNSMudApp.Components.Bounty;

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
///     バウンティ（タグ付け依頼）作成ダイアログコンポーネントのコードビハインド。
///     依頼対象アイテム、希望タグ、報酬アセットの入力・検索および作成処理を制御する。
/// </summary>
public partial class BountyCreateDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    [Parameter]
    public Item? TargetItem { get; set; }

    [Inject]
    private BountyCreateViewModel ViewModel { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    private MudForm? _form;
    private bool _isValid;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        var currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

        await ViewModel.InitializeAsync(TargetItem, currentUserId);
    }

    private Task<IEnumerable<Item>> SearchItems(string value, CancellationToken token)
    {
        return ViewModel.SearchItemsAsync(value, token);
    }

    private Task<IEnumerable<Tag>> SearchTags(string value, CancellationToken token)
    {
        return ViewModel.SearchTagsAsync(value, token);
    }

    private async Task Submit()
    {
        if (ViewModel.IsSubmitting)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        if (_form != null)
        {
            await _form.ValidateAsync();
        }

        if (!_isValid)
        {
            return;
        }

        var result = await ViewModel.SubmitAsync();
        switch (result)
        {
            case Success<bool>:
                Snackbar.Add("タグ付けの依頼（バウンティ）を作成しました。", Severity.Success);
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

    private void Cancel()
    {
        MudDialog.Cancel();
    }
}