namespace SRNSMudApp.Components.Item;

// 兄弟名前空間 SRNSMudApp.Components.Tag より先に Data.Tag 型を解決させるため、
// using を名前空間の内側に置く

using Tag = SRNSMudApp.Data.Tag;

/// <summary>タグ検索フィルタ 1 件分。</summary>
public class TagFilter
{
    public Tag Tag { get; set; } = null!;
    public string? UserName { get; set; }
    public string DisplayText => string.IsNullOrWhiteSpace(UserName) ? Tag.Name : $"{Tag.Name} ({UserName})";
}

/// <summary>ソート条件 1 件分。</summary>
public class SortCondition
{
    public Tag Tag { get; set; } = null!;
    public SortOrder Order { get; set; } = SortOrder.Desc;
}