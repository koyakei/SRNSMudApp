namespace SRNSMudApp.Client.Resources;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Resources;

/// <summary>
/// ClientMessages.resx に対応する型安全なリソースアクセサークラス。
/// Resource Pattern に従い、ハードコード文字列の排除とローカライズをサポートします。
/// </summary>
[SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "Resource strings can change dynamically with culture")]
public static class ClientMessages
{
    private static readonly ResourceManager ResourceManager =
        new("SRNSMudApp.Client.Resources.ClientMessages", typeof(ClientMessages).Assembly);

    /// <summary>
    /// カルチャに対応したリソース文字列を取得します。
    /// </summary>
    /// <param name="name">取得対象のリソースキー名。</param>
    /// <param name="culture">カルチャ情報。null の場合は現在の UI カルチャを使用。</param>
    /// <returns>解決されたリソース文字列。見つからない場合はキー名。</returns>
    public static string GetString(string name, CultureInfo? culture = null) =>
        ResourceManager.GetString(name, culture ?? CultureInfo.CurrentUICulture) ?? name;

    /// <summary>タグが見つかりませんでした。</summary>
    public static string TagNotFound => GetString(nameof(TagNotFound));

    /// <summary>このタグはロックされています。新規作成・編集・削除は制限されています（管理者のみ操作可能）。</summary>
    public static string TagLockedAlert => GetString(nameof(TagLockedAlert));

    /// <summary>タグ名を入力してください。</summary>
    public static string TagNameRequired => GetString(nameof(TagNameRequired));

    /// <summary>タグ情報を更新しました。</summary>
    public static string TagUpdateSuccess => GetString(nameof(TagUpdateSuccess));

    /// <summary>タグの更新に失敗しました。</summary>
    public static string TagUpdateFailed => GetString(nameof(TagUpdateFailed));

    /// <summary>タグを削除しました。</summary>
    public static string TagDeleteSuccess => GetString(nameof(TagDeleteSuccess));

    /// <summary>タグの削除に失敗しました。</summary>
    public static string TagDeleteFailed => GetString(nameof(TagDeleteFailed));

    /// <summary>タグ削除確認メッセージ書式。</summary>
    public static string TagDeleteConfirm => GetString(nameof(TagDeleteConfirm));

    /// <summary>タグ削除確認メッセージを生成します。</summary>
    public static string FormatTagDeleteConfirm(string tagName) =>
        string.Format(CultureInfo.CurrentCulture, TagDeleteConfirm, tagName);

    /// <summary>タグをフォローしました。</summary>
    public static string TagFollowSuccess => GetString(nameof(TagFollowSuccess));

    /// <summary>フォローを解除しました。</summary>
    public static string TagUnfollowSuccess => GetString(nameof(TagUnfollowSuccess));

    /// <summary>フォロー状態の変更エラーメッセージ書式。</summary>
    public static string TagFollowFailed => GetString(nameof(TagFollowFailed));

    /// <summary>フォロー状態変更エラーメッセージを生成します。</summary>
    public static string FormatTagFollowFailed(string error) =>
        string.Format(CultureInfo.CurrentCulture, TagFollowFailed, error);

    /// <summary>投票に失敗しました。</summary>
    public static string TagVoteFailed => GetString(nameof(TagVoteFailed));

    /// <summary>タグのロック状態を切り替えました。</summary>
    public static string TagLockToggleSuccess => GetString(nameof(TagLockToggleSuccess));

    /// <summary>ロック状態の変更に失敗しました。</summary>
    public static string TagLockToggleFailed => GetString(nameof(TagLockToggleFailed));

    /// <summary>タグ情報取得エラーメッセージ書式。</summary>
    public static string TagLoadFailed => GetString(nameof(TagLoadFailed));

    /// <summary>タグ情報取得エラーメッセージを生成します。</summary>
    public static string FormatTagLoadFailed(string error) =>
        string.Format(CultureInfo.CurrentCulture, TagLoadFailed, error);

    /// <summary>タグ名の変更を提案</summary>
    public static string ProposalNameTitle => GetString(nameof(ProposalNameTitle));

    /// <summary>新しいタグ名 *</summary>
    public static string ProposalNameLabel => GetString(nameof(ProposalNameLabel));

    /// <summary>タグ名変更の提案を送信しました。</summary>
    public static string ProposalNameSuccess => GetString(nameof(ProposalNameSuccess));

    /// <summary>提案するタグ名を入力してください。</summary>
    public static string ProposalNameValidation => GetString(nameof(ProposalNameValidation));

    /// <summary>説明文の変更を提案</summary>
    public static string ProposalContentTitle => GetString(nameof(ProposalContentTitle));

    /// <summary>新しい説明文 *</summary>
    public static string ProposalContentLabel => GetString(nameof(ProposalContentLabel));

    /// <summary>説明文変更の提案を送信しました。</summary>
    public static string ProposalContentSuccess => GetString(nameof(ProposalContentSuccess));

    /// <summary>提案する説明文を入力してください。</summary>
    public static string ProposalContentValidation => GetString(nameof(ProposalContentValidation));

    /// <summary>提案の送信に失敗しました。</summary>
    public static string ProposalSubmitFailed => GetString(nameof(ProposalSubmitFailed));

    /// <summary>タグ名変更提案を承認しました。</summary>
    public static string ProposalApproveNameSuccess => GetString(nameof(ProposalApproveNameSuccess));

    /// <summary>タグ名変更提案を却下しました。</summary>
    public static string ProposalRejectNameSuccess => GetString(nameof(ProposalRejectNameSuccess));

    /// <summary>提案を取り下げました。</summary>
    public static string ProposalCancelNameSuccess => GetString(nameof(ProposalCancelNameSuccess));

    /// <summary>説明文変更提案を承認しました。</summary>
    public static string ProposalApproveContentSuccess => GetString(nameof(ProposalApproveContentSuccess));

    /// <summary>説明文変更提案を却下しました。</summary>
    public static string ProposalRejectContentSuccess => GetString(nameof(ProposalRejectContentSuccess));

    /// <summary>提案を取り下げました。</summary>
    public static string ProposalCancelContentSuccess => GetString(nameof(ProposalCancelContentSuccess));

    /// <summary>承認に失敗しました。</summary>
    public static string ProposalApproveFailed => GetString(nameof(ProposalApproveFailed));

    /// <summary>却下に失敗しました。</summary>
    public static string ProposalRejectFailed => GetString(nameof(ProposalRejectFailed));

    /// <summary>リクエストを承認しました。</summary>
    public static string TaggingRequestApproveSuccess => GetString(nameof(TaggingRequestApproveSuccess));

    /// <summary>リクエストを却下しました。</summary>
    public static string TaggingRequestRejectSuccess => GetString(nameof(TaggingRequestRejectSuccess));

    /// <summary>リクエストの承認に失敗しました。</summary>
    public static string TaggingRequestApproveFailed => GetString(nameof(TaggingRequestApproveFailed));

    /// <summary>リクエストの却下に失敗しました。</summary>
    public static string TaggingRequestRejectFailed => GetString(nameof(TaggingRequestRejectFailed));

    /// <summary>このタグに関連付けられたアイテムはありません。</summary>
    public static string NoRelatedItems => GetString(nameof(NoRelatedItems));

    /// <summary>関連するタグはありません。</summary>
    public static string NoRelatedTags => GetString(nameof(NoRelatedTags));

    /// <summary>保留中のタグ付け提案はありません。</summary>
    public static string NoPendingTaggingRequests => GetString(nameof(NoPendingTaggingRequests));

    /// <summary>保留中の変更提案はありません。</summary>
    public static string NoPendingProposals => GetString(nameof(NoPendingProposals));

    /// <summary>重み変動履歴はありません。</summary>
    public static string NoWeightLedgers => GetString(nameof(NoWeightLedgers));
}