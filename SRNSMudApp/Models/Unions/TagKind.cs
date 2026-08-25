using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Models.Unions;

/// <summary>
///     各ユーザーが各自で発行する投票・リアクション用タグ（good, bad など）。
///     ItemCard のスコア集計やリアクション機能で使用され、通常のタグチップ一覧には表示しない。
/// </summary>
public record VotingReactionTag(string Name, string OwnerId);

/// <summary>
///     システムが共通分類のために保持するタグ（OwnerId == "system" かつ投票用ではないタグ）。
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
public union TagKind(VotingReactionTag, SystemClassificationTag, UserCustomTag);
