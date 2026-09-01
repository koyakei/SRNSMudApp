using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Commands;
using SRNSMudApp.Services.Dialogs;

// CA1508: union 型の網羅的パターンマッチにおける解析器の誤検知のため抑制する。
// IDE0072: enum の網羅的 switch に対する「Populate switch」は解析器の誤検知のため抑制する。
#pragma warning disable CA1508, IDE0072

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
///     タグ付けリクエストの承認/却下フローの共通実装 (Facade)。
///     コマンドハンドラー（<see cref="ICommandHandler{TCommand, TResult}" />）へ処理を委譲し、UI 通知を行う。
/// </summary>
public class TaggingRequestActions(
    ICommandHandler<ApproveTaggingRequestCommand, Result<string>> approveHandler,
    ICommandHandler<RejectTaggingRequestCommand, Result<bool>> rejectHandler,
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
            var command = new ApproveTaggingRequestCommand(requestId, currentUserId);
            Result<string> result = await approveHandler.HandleAsync(command);

            return result switch
            {
                Success<string> => NotifySuccess(ContractMessages.ContractApproved),
                Failure f => NotifyError($"{ContractMessages.ContractApprovalFailedPrefix}{f.ErrorMessage}")
            };
        }
        catch (Exception ex)
        {
            return NotifyError($"{ContractMessages.ContractApprovalFailedPrefix}{ex.Message}");
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
            var command = new RejectTaggingRequestCommand(requestId, currentUserId, comment);
            Result<bool> rejectResult = await rejectHandler.HandleAsync(command);

            return rejectResult switch
            {
                Success<bool> => NotifySuccess(ContractMessages.ContractRejected),
                Failure f => NotifyError($"{ContractMessages.ContractRejectionFailedPrefix}{f.ErrorMessage}")
            };
        }
        catch (Exception ex)
        {
            return NotifyError($"{ContractMessages.ContractRejectionFailedPrefix}{ex.Message}");
        }
    }

    private bool NotifySuccess(string message)
    {
        _ = snackbar.Add(message, Severity.Success);
        return true;
    }

    private bool NotifyError(string message)
    {
        _ = snackbar.Add(message, Severity.Error);
        return false;
    }
}