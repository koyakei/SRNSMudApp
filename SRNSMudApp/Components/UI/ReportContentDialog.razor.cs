namespace SRNSMudApp.Components.UI;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Data;

/// <summary>
///     不適切なコンテンツ（アイテムまたはタグ）の通報ダイアログコンポーネントのコードビハインド。
/// </summary>
public partial class ReportContentDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    [Inject]
    private ReportContentViewModel ViewModel { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Parameter]
    public ReportTargetType TargetType { get; set; }

    [Parameter]
    public int? ItemId { get; set; }

    [Parameter]
    public int? TagId { get; set; }

    [Parameter]
    public string TargetContent { get; set; } = string.Empty;

    [Parameter]
    public string? TargetOwnerName { get; set; }

    protected override async Task OnInitializedAsync()
    {
        string userId = string.Empty;
        if (AuthStateTask is not null)
        {
            var authState = await AuthStateTask;
            userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        }

        ViewModel.Initialize(TargetType, ItemId, TagId, TargetContent, TargetOwnerName, userId);
    }

    private void Cancel()
    {
        MudDialog.Cancel();
    }

    [SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Display error notification to user")]
    private async Task SubmitAsync()
    {
        if (ViewModel.IsSubmitting)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        if (string.IsNullOrEmpty(ViewModel.CurrentUserId))
        {
            Snackbar.Add("通報するにはログインが必要です。", Severity.Warning);
            MudDialog.Cancel();
            return;
        }

        try
        {
            _ = await ViewModel.SubmitAsync();
            Snackbar.Add("通報を受け付けました。ご協力ありがとうございます。", Severity.Success);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (Exception ex)
        {
            Snackbar.Add($"通報の送信中にエラーが発生しました: {ex.Message}", Severity.Error);
        }
    }
}