namespace SRNSMudApp.Models;

/// <summary>
///     タグ付与アクションに伴うメタ情報（タグ名、タグオーナー名、付与したウェイト）。
///     タグ履歴コメントをリプライスレッドに表示する際に使用する。
/// </summary>
/// <param name="TagName">付与されたタグの名前。</param>
/// <param name="TagOwnerUserName">タグの所有者のユーザー名。</param>
/// <param name="Weight">付与されたウェイト。</param>
public sealed record TagActionMetadata(string TagName, string? TagOwnerUserName, int Weight);