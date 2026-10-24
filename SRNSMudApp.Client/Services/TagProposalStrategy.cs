namespace SRNSMudApp.Client.Services;

using System;
using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Resources;

/// <summary>
/// タグ変更提案の種別ごとの挙動を抽象化する Strategy インターフェース。
/// </summary>
public interface ITagProposalStrategy
{
    /// <summary>
    /// 提案フォームのタイトル。
    /// </summary>
    string Title { get; }

    /// <summary>
    /// 入力フィールドのラベル。
    /// </summary>
    string ValueLabel { get; }

    /// <summary>
    /// 入力フィールドの行数（1行 or 複数行）。
    /// </summary>
    int Lines { get; }

    /// <summary>
    /// 送信成功時の通知メッセージ。
    /// </summary>
    string SuccessMessage { get; }

    /// <summary>
    /// 未入力時の警告メッセージ。
    /// </summary>
    string ValidationErrorMessage { get; }

    /// <summary>
    /// data-testid 識別子。
    /// </summary>
    string TestId { get; }

    /// <summary>
    /// 提案を API クライアント経由で送信します。
    /// </summary>
    /// <param name="apiClient">API クライアント。</param>
    /// <param name="tagId">タグ ID。</param>
    /// <param name="value">提案値。</param>
    /// <param name="reason">提案理由。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>送信成否。</returns>
    Task<bool> SubmitAsync(ITagProposalApiClient apiClient, int tagId, string value, string? reason, CancellationToken cancellationToken);
}

/// <summary>
/// タグ名変更提案の Strategy 実装。
/// </summary>
public sealed class TagNameProposalStrategy : ITagProposalStrategy
{
    public string Title => ClientMessages.ProposalNameTitle;
    public string ValueLabel => ClientMessages.ProposalNameLabel;
    public int Lines => 1;
    public string SuccessMessage => ClientMessages.ProposalNameSuccess;
    public string ValidationErrorMessage => ClientMessages.ProposalNameValidation;
    public string TestId => "proposal-name-input";

    public async Task<bool> SubmitAsync(ITagProposalApiClient apiClient, int tagId, string value, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var req = new ProposeTagNameRequest(tagId, value, trimmedReason);
        var res = await apiClient.ProposeTagNameAsync(req, cancellationToken);
        return res != null;
    }
}

/// <summary>
/// タグ説明文変更提案の Strategy 実装。
/// </summary>
public sealed class TagContentProposalStrategy : ITagProposalStrategy
{
    public string Title => ClientMessages.ProposalContentTitle;
    public string ValueLabel => ClientMessages.ProposalContentLabel;
    public int Lines => 3;
    public string SuccessMessage => ClientMessages.ProposalContentSuccess;
    public string ValidationErrorMessage => ClientMessages.ProposalContentValidation;
    public string TestId => "proposal-content-input";

    public async Task<bool> SubmitAsync(ITagProposalApiClient apiClient, int tagId, string value, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var req = new ProposeTagContentRequest(tagId, value, trimmedReason);
        var res = await apiClient.ProposeTagContentAsync(req, cancellationToken);
        return res != null;
    }
}