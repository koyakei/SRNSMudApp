using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Models.Unions;

/// <summary>
///     各ユーザーが各自で発行する投票用タグ（good, bad など）。
///     ItemCard のスコア集計（Upvote / Downvote）で使用され、通常のタグチップ一覧には表示しない。
/// </summary>
public record VoteTag(string Name, string OwnerId);

/// <summary>
///     各ユーザーが各自で発行するリアクション用タグ（真実、善、美 など）。
///     ItemCard のリアクション機能で使用され、通常のタグチップ一覧には表示しない。
/// </summary>
public record ReactionTag(string Name, string OwnerId);

/// <summary>
///     システムが共通分類のために保持するタグ（OwnerId == "system" かつ投票・リアクション用ではないタグ）。
///     ItemCard にタグチップとして表示され、どのユーザーも無制限に TagRelation を使ってタグ付け可能。
/// </summary>
public record SystemClassificationTag(string Name);

/// <summary>
///     ユーザーが独自に作成したカスタムタグ。
///     タグチップとして表示され、オーナー以外が付与・変更する場合はコントラクト提案が必要。
/// </summary>
public record UserCustomTag(string Name, string OwnerId);

/// <summary>
///     タグの種別を表す Union 型。
/// </summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union TagKind(VoteTag, ReactionTag, SystemClassificationTag, UserCustomTag);