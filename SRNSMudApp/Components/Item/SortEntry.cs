namespace SRNSMudApp.Components.Item;

public enum SortOrder { Desc, Asc }

/// <summary>1件のソート条件（タグID＋昇降順）。</summary>
public sealed record SortEntry(int TagId, SortOrder Order);