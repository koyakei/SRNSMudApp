namespace SRNSMudApp.Services;

/// <summary>
///     契約およびタグリクエスト関連の定型システムメッセージ文言。
/// </summary>
public static class ContractMessages
{
    /// <summary>タグ追加リクエスト送信メッセージ。</summary>
    public const string TagAddRequestSent = "タグ追加リクエストを送信しました。";

    /// <summary>タグ削除リクエスト送信メッセージ。</summary>
    public const string TagDeleteRequestSent = "タグ削除リクエストを送信しました。";

    /// <summary>相互タグ追加リクエスト送信メッセージ。</summary>
    public const string MutualTagAddRequestSent = "タグ追加リクエスト(Mutual)を送信しました。";

    /// <summary>相互タグ削除リクエスト送信メッセージ。</summary>
    public const string MutualTagDeleteRequestSent = "タグ削除リクエスト(Mutual)を送信しました。";

    /// <summary>自動承認失敗時のプレフィックス。</summary>
    public const string AutoAcceptFailedFormatPrefix = "自動承認に失敗しました: ";

    // エラーメッセージ
    /// <summary>タグ未検出エラーメッセージ。</summary>
    public const string TagNotFound = "Tag not found";

    /// <summary>無効なリクエストタイプエラーメッセージ。</summary>
    public const string InvalidRequestType = "無効なリクエストタイプです。";

    /// <summary>対象の TagEdge 未検出エラーメッセージ。</summary>
    public const string TagEdgeNotFound = "対象の TagEdge が見つかりません。";

    /// <summary>タグ紐付け未検出エラーメッセージ。</summary>
    public const string TagEdgeAttachmentNotFound = "対象のタグ紐付けが見つかりません。";

    /// <summary>対象のタグ付け関係未検出エラーメッセージ。</summary>
    public const string TagRelationNotFound = "対象のタグ付けが見つかりません。";

    /// <summary>未知の契約型エラーメッセージ。</summary>
    public const string UnknownContractType = "DBに未知の契約型が存在します。";

    // 成功・操作メッセージ
    /// <summary>TagEdge へのタグ紐付け完了メッセージ。</summary>
    public const string TagEdgeTagAttached = "TagEdge にタグを紐付けました。";

    /// <summary>TagEdge からのタグ解除または減量完了メッセージ。</summary>
    public const string TagEdgeTagDetachedOrDecreased = "TagEdge からタグを解除または減量しました。";

    /// <summary>契約承認完了メッセージ。</summary>
    public const string ContractApproved = "リクエストを承認しました。";

    /// <summary>契約承認失敗時のプレフィックス。</summary>
    public const string ContractApprovalFailedPrefix = "承認に失敗しました: ";

    /// <summary>契約却下完了メッセージ。</summary>
    public const string ContractRejected = "リクエストを却下しました。";

    /// <summary>契約却下失敗時のプレフィックス。</summary>
    public const string ContractRejectionFailedPrefix = "却下に失敗しました: ";
}