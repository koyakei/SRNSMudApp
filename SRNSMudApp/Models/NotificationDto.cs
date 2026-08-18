using SRNSMudApp.Data;

namespace SRNSMudApp.Models;

public class NotificationDto
{
    public int SourceId { get; set; }
    public string Type { get; set; } = string.Empty; // "TagRequest" | "ItemReply" | "RequestRejected" | "RequestApproved" | "RequestReply"
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string TargetUrl { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string IconColor { get; set; } = "Color.Default";
    public int AssociatedItemId { get; set; }
    public Item? AssociatedItem { get; set; }
    public int? HighlightTagId { get; set; }
    public SRNSMudApp.Components.Shared.RequestInfo? RequestInfo { get; set; }
}