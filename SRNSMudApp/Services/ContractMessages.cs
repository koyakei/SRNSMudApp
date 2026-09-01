namespace SRNSMudApp.Services;

/// <summary>
///     契約およびタグリクエスト関連の定型システムメッセージ文言。
/// </summary>
public static class ContractMessages
{
    public const string TagAddRequestSent = "タグ追加リクエストを送信しました。";
    public const string TagDeleteRequestSent = "タグ削除リクエストを送信しました。";
    public const string MutualTagAddRequestSent = "タグ追加リクエスト(Mutual)を送信しました。";
    public const string MutualTagDeleteRequestSent = "タグ削除リクエスト(Mutual)を送信しました。";
    public const string AutoAcceptFailedFormatPrefix = "自動承認に失敗しました: ";

    // エラーメッセージ
    public const string TagNotFound = "Tag not found";
    public const string InvalidRequestType = "無効なリクエストタイプです。";
    public const string TagEdgeNotFound = "対象の TagEdge が見つかりません。";
    public const string TagEdgeAttachmentNotFound = "対象のタグ紐付けが見つかりません。";
    public const string TagRelationNotFound = "対象のタグ付けが見つかりません。";
    public const string UnknownContractType = "DBに未知の契約型が存在します。";

    // 成功・操作メッセージ
    public const string TagEdgeTagAttached = "TagEdge にタグを紐付けました。";
    public const string TagEdgeTagDetachedOrDecreased = "TagEdge からタグを解除または減量しました。";
    public const string ContractApproved = "リクエストを承認しました。";
    public const string ContractApprovalFailedPrefix = "承認に失敗しました: ";
    public const string ContractRejected = "リクエストを却下しました。";
    public const string ContractRejectionFailedPrefix = "却下に失敗しました: ";
}