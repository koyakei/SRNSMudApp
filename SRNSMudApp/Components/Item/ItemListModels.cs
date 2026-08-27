// 兄弟名前空間 SRNSMudApp.Components.Tag より先に Data.Tag 型を解決させるため、
// using を名前空間の内側に置く

namespace SRNSMudApp.Components.Item;

using Tag = SRNSMudApp.Data.Tag;

/// <summary>タグ検索フィルタ 1 件分 (特定 Tag.Id 指定 または Tag.Name 同名全件指定)。</summary>
public class TagFilter
{
    public int? TagId { get; set; }
    public string TagName { get; set; } = "";
    public Tag? Tag { get; set; }
    public string? UserName { get; set; }
    public string DisplayText => string.IsNullOrWhiteSpace(UserName) ? TagName : $"{TagName} ({UserName})";
}

/// <summary>ソート条件 1 件分。</summary>
public class SortCondition
{
    public Tag Tag { get; set; } = null!;
    public SortOrder Order { get; set; } = SortOrder.Desc;
}