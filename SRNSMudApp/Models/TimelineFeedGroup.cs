namespace SRNSMudApp.Models;

using System;
using System.Collections.Generic;

using SRNSMudApp.Data;

/// <summary>タイムラインのグループ 1 件分 (同一ターゲットへのイベント群)。</summary>
public class TimelineFeedGroup
{
    public string TimelineTargetJson { get; set; } = "";
    public DateTime LatestEventDate { get; set; }

    public Item? Item { get; set; }
    public Tag? Tag { get; set; }

    public IReadOnlyList<TimelineEvent> Events { get; set; } = [];
}

/// <summary>
///     タイムラインの Virtualize 用 EqualityComparer。
/// </summary>
public sealed class TimelineFeedGroupComparer : IEqualityComparer<TimelineFeedGroup>
{
    public bool Equals(TimelineFeedGroup? x, TimelineFeedGroup? y)
    {
        return x?.TimelineTargetJson == y?.TimelineTargetJson;
    }

    public int GetHashCode(TimelineFeedGroup obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return HashCode.Combine(obj.TimelineTargetJson);
    }
}