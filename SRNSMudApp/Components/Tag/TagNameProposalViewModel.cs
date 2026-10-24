#region

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     タグ名（Name）の編集提案ダイアログ用 ViewModel。
///     タグ名の形式バリデーション、提案リクエスト送信および状態管理をカプセル化する。
/// </summary>
public sealed class TagNameProposalViewModel
{
    private readonly ITagNameProposalService _proposalService;

    public TagNameProposalViewModel(ITagNameProposalService proposalService)
    {
        _proposalService = proposalService ?? throw new ArgumentNullException(nameof(proposalService));
    }

    public TagEntity? TargetTag { get; private set; }
    public string CurrentUserId { get; set; } = string.Empty;
    public string ProposedName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool IsSubmitting { get; private set; }

    public bool CanSubmit =>
        !IsSubmitting &&
        !string.IsNullOrWhiteSpace(ProposedName) &&
        ValidateTagName(ProposedName) == null &&
        !string.IsNullOrWhiteSpace(CurrentUserId) &&
        TargetTag != null;

    /// <summary>
    ///     対象タグおよび現在のユーザーIDを設定して初期化する。
    /// </summary>
    public void Initialize(TagEntity tag, string currentUserId)
    {
        TargetTag = tag ?? throw new ArgumentNullException(nameof(tag));
        CurrentUserId = currentUserId;
        ProposedName = tag.Name;
        Reason = null;
        IsSubmitting = false;
    }

    /// <summary>
    ///     タグ名の入力値検証を行う。
    /// </summary>
    public static string? ValidateTagName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "タグ名は必須です";
        }

        if (value.Length > 100)
        {
            return "タグ名は100文字以内で入力してください";
        }

        return !TagNameProposalService.TagNameRegex().IsMatch(value) ? "タグ名に使用できない文字が含まれています" : null;
    }

    /// <summary>
    ///     タグ名編集提案リクエストを送信する。
    /// </summary>
    public async Task<Result<TagNameProposal>> SubmitAsync(CancellationToken cancellationToken = default)
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

        string? validationError = ValidateTagName(ProposedName);
        if (validationError != null)
        {
            return new Failure(validationError);
        }

        IsSubmitting = true;
        try
        {
            return await _proposalService.ProposeNameAsync(
                TargetTag.Id,
                ProposedName.Trim(),
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