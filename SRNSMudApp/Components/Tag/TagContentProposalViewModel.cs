#region

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     タグ説明文（Content）の編集提案ダイアログ用 ViewModel。
///     入力値のバリデーション、提案リクエスト送信および状態管理をカプセル化する。
/// </summary>
public sealed class TagContentProposalViewModel
{
    private readonly ITagContentProposalService _proposalService;

    public TagContentProposalViewModel(ITagContentProposalService proposalService)
    {
        _proposalService = proposalService ?? throw new ArgumentNullException(nameof(proposalService));
    }

    public TagEntity? TargetTag { get; private set; }
    public string CurrentUserId { get; set; } = string.Empty;
    public string ProposedContent { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool IsSubmitting { get; private set; }

    public bool CanSubmit =>
        !IsSubmitting &&
        !string.IsNullOrWhiteSpace(ProposedContent) &&
        !string.IsNullOrWhiteSpace(CurrentUserId) &&
        TargetTag != null;

    /// <summary>
    ///     対象タグおよび現在のユーザーIDを設定して初期化する。
    /// </summary>
    public void Initialize(TagEntity tag, string currentUserId)
    {
        TargetTag = tag ?? throw new ArgumentNullException(nameof(tag));
        CurrentUserId = currentUserId;
        ProposedContent = tag.Content ?? string.Empty;
        Reason = null;
        IsSubmitting = false;
    }

    /// <summary>
    ///     編集提案リクエストを送信する。
    /// </summary>
    public async Task<Result<TagContentProposal>> SubmitAsync(CancellationToken cancellationToken = default)
    {
        if (IsSubmitting)
        {
            return new Failure(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage);
        }

        if (TargetTag == null)
        {
            return new Failure("対象タグが指定されていません。");
        }

        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return new Failure("ログインユーザー情報が取得できませんでした。");
        }

        if (string.IsNullOrWhiteSpace(ProposedContent))
        {
            return new Failure("提案内容は必須です。");
        }

        IsSubmitting = true;
        try
        {
            return await _proposalService.ProposeContentAsync(
                TargetTag.Id,
                ProposedContent.Trim(),
                string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim(),
                CurrentUserId,
                cancellationToken);
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}