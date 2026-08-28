namespace SRNSMudApp.Components.Item;

/// <summary>タグ検索フィルタ 1 件分 (特定 Tag.Id 指定 または Tag.Name 同名全件指定)。</summary>
public class TagFilter
{
    public int? TagId { get; set; }
    public string TagName { get; set; } = "";
    public Data.Tag? Tag { get; set; }
    public string? UserName { get; set; }
    public string DisplayText => string.IsNullOrWhiteSpace(UserName) ? TagName : $"{TagName} ({UserName})";
}

/// <summary>ソート条件 1 件分。</summary>
public class SortCondition
{
    public Data.Tag Tag { get; set; } = null!;
    public SortOrder Order { get; set; } = SortOrder.Desc;
}