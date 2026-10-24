namespace SRNSMudApp.Components.UI;

using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Data;

/// <summary>
///     タグ付けリクエストのスレッド表示および承認・却下・返信入力ダイアログコンポーネントのコードビハインド。
/// </summary>
public partial class TaggingRequestThreadDialog : ComponentBase
{
    [CascadingParameter]
    public IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public TaggingRequestEntity TaggingRequest { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    [Inject]
    private TaggingRequestThreadViewModel ViewModel { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthenticationStateTask;
        var user = authState.User;
        string? currentUserId = null;
        if (user.Identity?.IsAuthenticated == true)
        {
            currentUserId = user.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        }

        await ViewModel.InitializeAsync(TaggingRequest, currentUserId);
    }

    private async Task SubmitReplyAsync()
    {
        if (ViewModel.IsSubmitting)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        var reply = await ViewModel.SubmitReplyAsync();
        if (reply is { IsDuplicate: true })
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
        }
    }

    private async Task ApproveRequestAsync()
    {
        if (ViewModel.IsSubmitting)
        {
            return;
        }

        if (await ViewModel.ApproveRequestAsync())
        {
            MudDialog.Close(DialogResult.Ok(true));
        }
    }

    private async Task RejectRequestAsync()
    {
        if (ViewModel.IsSubmitting)
        {
            return;
        }

        if (await ViewModel.RejectRequestAsync())
        {
            MudDialog.Close(DialogResult.Ok(true));
        }
    }
}