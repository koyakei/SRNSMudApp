using SRNSMudApp.Data;

namespace SRNSMudApp.Models;

public record NotificationDto
{
    public int SourceId { get; set; }
    public required NotificationType Kind { get; init; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public NotificationTarget TargetUrl { get; set; } = new RelativeUrl("#");
    public bool IsRead { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public int AssociatedItemId { get; set; }
    public Item? AssociatedItem { get; set; }
    public int? HighlightTagId { get; set; }
}