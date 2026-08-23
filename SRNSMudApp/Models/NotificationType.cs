using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using MudBlazor;

using SRNSMudApp.Data;

namespace SRNSMudApp.Models;

public record TagRequestNotification(
    int RequestId, TaggingRequestType RequestType,
    int TargetItemId, string? TargetTagName, int TargetTagId,
    int ProposedWeight, TradeStatus Status);

public record ItemReplyNotification(int ReplyItemId, int ParentItemId, string ActorName);

public record RequestRejectedNotification(
    int RequestId, string? TagName, TaggingRequestType RequestType,
    string? RejectComment, int TargetItemId, int TargetTagId);

public record RequestApprovedNotification(
    int RequestId, string? TagName, TaggingRequestType RequestType,
    int TargetItemId, int TargetTagId);

public record RequestReplyNotification(int ReplyItemId, int RequestId, string ActorName);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union NotificationType(
    TagRequestNotification,
    ItemReplyNotification,
    RequestRejectedNotification,
    RequestApprovedNotification,
    RequestReplyNotification)
{
    public readonly string Icon => this switch
    {
        TagRequestNotification => Icons.Material.Filled.Mail,
        ItemReplyNotification => Icons.Material.Filled.Reply,
        RequestRejectedNotification => Icons.Material.Filled.Cancel,
        RequestApprovedNotification => Icons.Material.Filled.CheckCircle,
        RequestReplyNotification => Icons.Material.Filled.Forum,
        _ => throw new UnreachableException()
    };

    public readonly string IconColor => this switch
    {
        TagRequestNotification => "Primary",
        ItemReplyNotification => "Info",
        RequestRejectedNotification => "Error",
        RequestApprovedNotification => "Success",
        RequestReplyNotification => "Secondary",
        _ => throw new UnreachableException()
    };

    public readonly string SourceType => this switch
    {
        TagRequestNotification => "TagRequest",
        ItemReplyNotification => "ItemReply",
        RequestRejectedNotification => "RequestRejected",
        RequestApprovedNotification => "RequestApproved",
        RequestReplyNotification => "RequestReply",
        _ => throw new UnreachableException()
    };
}