
using SRNSMudApp.Data;

namespace SRNSMudApp.Components.Pages;

/// <summary>タイムラインのグループ 1 件分 (同一ターゲットへのイベント群)。</summary>
public class TimelineFeedGroup
{
    public string TimelineTargetJson { get; set; } = "";
    public DateTime LatestEventDate { get; set; }

    public Data.Item? Item { get; set; }
    public Data.Tag? Tag { get; set; }

    public IReadOnlyList<TimelineEvent> Events { get; set; } = [];
}