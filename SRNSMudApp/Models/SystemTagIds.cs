namespace SRNSMudApp.Models;

/// <summary>現在ユーザーの投票用システムタグ ID。</summary>
public readonly record struct SystemTagIds(int? GoodTagId, int? BadTagId)
{
    /// <summary>good/bad 両方のタグが揃っているかどうか。</summary>
    public bool IsComplete => GoodTagId.HasValue && BadTagId.HasValue;
}

/// <summary>現在ユーザーのリアクション用システムタグ ID（真実・善・美）。</summary>
public readonly record struct ReactionTagIds(int? ShinjiTagId, int? ZenTagId, int? BiTagId)
{
    /// <summary>真実・善・美すべてのタグが揃っているかどうか。</summary>
    public bool IsComplete => ShinjiTagId.HasValue && ZenTagId.HasValue && BiTagId.HasValue;
}