using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services;

/// <summary>
///     タイムラインイベント（TimelineEvent）の記録を担当するドメインサービス実装。
/// </summary>
public class TimelineRecorder : ITimelineRecorder
{
    /// <inheritdoc />
    public void RecordTagRelationAdded(ApplicationDbContext context, string userId, int itemId, int tagId, int weight = 1)
    {
        ArgumentNullException.ThrowIfNull(context);

        _ = context.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = userId,
            Target = new ItemTarget(itemId),
            FollowedTagId = tagId,
            EventType = "Insert",
            NewWeight = weight
        });
    }

    /// <inheritdoc />
    public void RecordTagRelationDeleted(ApplicationDbContext context, string userId, int itemId, int tagId, int previousWeight)
    {
        ArgumentNullException.ThrowIfNull(context);

        _ = context.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = userId,
            Target = new ItemTarget(itemId),
            FollowedTagId = tagId,
            EventType = "Delete",
            PreviousWeight = previousWeight
        });
    }

    /// <inheritdoc />
    public void RecordTagRelationUpdated(ApplicationDbContext context, string userId, int itemId, int tagId, int previousWeight, int newWeight)
    {
        ArgumentNullException.ThrowIfNull(context);

        _ = context.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = userId,
            Target = new ItemTarget(itemId),
            FollowedTagId = tagId,
            EventType = "Update",
            PreviousWeight = previousWeight,
            NewWeight = newWeight
        });
    }
}