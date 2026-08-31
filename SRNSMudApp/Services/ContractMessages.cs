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
}