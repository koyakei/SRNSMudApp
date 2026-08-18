using SRNSMudApp.Data;

namespace SRNSMudApp.Models;

public class NotificationDto
{
    public int SourceId { get; set; }
    public string Type { get; set; } = string.Empty; // "TagRequest" | "ItemReply" | "RequestRejected" | "RequestApproved" | "RequestReply"
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public NotificationTarget TargetUrl { get; set; } = new RelativeUrl("#");
    public bool IsRead { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string IconColor { get; set; } = "Color.Default";
    public int AssociatedItemId { get; set; }
    public Item? AssociatedItem { get; set; }
    public int? HighlightTagId { get; set; }
    public SRNSMudApp.Components.UI.RequestInfo? RequestInfo { get; set; }
}