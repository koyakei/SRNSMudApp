namespace SRNSMudApp.Components.Tag;

using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

/// <summary>
///     新規タグ作成ページコンポーネントのコードビハインド。
///     タグ名・内容の入力検証およびタグ作成後の画面遷移を制御する。
/// </summary>
public partial class AddTag : ComponentBase
{
    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    [Inject]
    private AddTagPageViewModel ViewModel { get; set; } = null!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    private async Task AddTagAsync()
    {
        if (ViewModel.IsSubmitting)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        var authState = await AuthStateTask;
        var result = await ViewModel.CreateTagAsync(authState.User);

        switch (result)
        {
            case Success<Tag>:
                NavigationManager.NavigateTo("/Tag/TagList");
                break;
            case Failure fail:
                if (fail.ErrorMessage == Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage || fail.ErrorMessage == "送信処理中です。")
                {
                    Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
                    return;
                }
                Snackbar.Add(fail.ErrorMessage, Severity.Error);
                break;
            default:
                break;
        }
    }
}