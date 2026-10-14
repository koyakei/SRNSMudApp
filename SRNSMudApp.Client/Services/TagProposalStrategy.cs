namespace SRNSMudApp.Client.Services;

using System;
using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Client.Models.Api;

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
    Task<bool> SubmitAsync(ITagProposalApiClient apiClient, int tagId, string value, string? reason, CancellationToken cancellationToken);
}

/// <summary>
/// タグ名変更提案の Strategy 実装。
/// </summary>
public sealed class TagNameProposalStrategy : ITagProposalStrategy
{
    public string Title => "タグ名の変更を提案";
    public string ValueLabel => "新しいタグ名 *";
    public int Lines => 1;
    public string SuccessMessage => "タグ名変更の提案を送信しました。";
    public string ValidationErrorMessage => "提案するタグ名を入力してください。";
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
    public string Title => "説明文の変更を提案";
    public string ValueLabel => "新しい説明文 *";
    public int Lines => 3;
    public string SuccessMessage => "説明文変更の提案を送信しました。";
    public string ValidationErrorMessage => "提案する説明文を入力してください。";
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