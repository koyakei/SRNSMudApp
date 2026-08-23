namespace SRNSMudApp.Components.Pages;

// 兄弟名前空間 SRNSMudApp.Components.{Item,Tag} より先に Data の型を解決させるため、
// using を名前空間の内側に置く
using SRNSMudApp.Data;

using Item = SRNSMudApp.Data.Item;
using Tag = SRNSMudApp.Data.Tag;

/// <summary>タイムラインのグループ 1 件分 (同一ターゲットへのイベント群)。</summary>
public class TimelineFeedGroup
{
    public string TimelineTargetJson { get; set; } = "";
    public DateTime LatestEventDate { get; set; }

    public Item? Item { get; set; }
    public Tag? Tag { get; set; }

    public List<TimelineEvent> Events { get; set; } = [];
}
