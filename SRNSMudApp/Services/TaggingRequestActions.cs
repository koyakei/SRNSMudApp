using System.Diagnostics.CodeAnalysis;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Services.Dialogs;

// IDE0072: enum の網羅的 switch に対する「Populate switch」は解析器の誤検知のため抑制する。
#pragma warning disable IDE0072

namespace SRNSMudApp.Services;

public interface ITaggingRequestActions
{
    /// <summary>
    ///     指定ユーザーがリクエストを承認できるかどうかを返す。
    /// </summary>
    bool CanApprove(TaggingRequestEntity request, string? currentUserId);

    /// <summary>
    ///     リクエストを承認する。成功時 true を返す。
    /// </summary>
    Task<bool> ApproveAsync(int requestId, string currentUserId);

    /// <summary>
    ///     却下理由ダイアログを表示し、確定時にリクエストを却下する。実行された場合 true を返す。
    /// </summary>
    Task<bool> RejectViaDialogAsync(int requestId, string currentUserId);
}

/// <summary>
///     タグ付けリクエストの承認/却下フローの共通実装。
///     TaggingRequestList / TaggingRequestThreadDialog / NotificationsPage で重複していた処理を集約する (Facade)。
/// </summary>
public class TaggingRequestActions(
    TaggingContractService taggingContractService,
    ITaggingService taggingService,
    IDialogLauncher dialogLauncher,
    ISnackbar snackbar) : ITaggingRequestActions
{
    public bool CanApprove(TaggingRequestEntity request, string? currentUserId)
    {
        return request.Status switch
        {
            TradeStatus.Proposed => request.ContractType switch
            {
                "Trigger" => request.RequesterUserId == currentUserId,
                "Bounty" => true,
                _ => request.TagOwnerUserId == currentUserId
            },
            _ => false
        };
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    public async Task<bool> ApproveAsync(int requestId, string currentUserId)
    {
        try
        {
            _ = await taggingContractService.AcceptContractAsync(requestId, currentUserId);
            _ = snackbar.Add("リクエストを承認しました。", Severity.Success);
            return true;
        }
        catch (Exception ex)
        {
            _ = snackbar.Add($"承認に失敗しました: {ex.Message}", Severity.Error);
            return false;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    public async Task<bool> RejectViaDialogAsync(int requestId, string currentUserId)
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        IDialogReference dialog = await dialogLauncher.ShowAsync<Components.UI.RejectRequestDialog>("リクエストを却下", options);
        DialogResult? result = await dialog.Result;

        if (result is not { Canceled: false })
        {
            return false;
        }

        try
        {
            var comment = result.Data as string;
            await taggingService.RejectRequestAsync(requestId, currentUserId, comment);
            _ = snackbar.Add("リクエストを却下しました。", Severity.Success);
            return true;
        }
        catch (Exception ex)
        {
            _ = snackbar.Add($"却下に失敗しました: {ex.Message}", Severity.Error);
            return false;
        }
    }
}