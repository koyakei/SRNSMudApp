namespace SRNSMudApp.Models;

public class NotificationDto
{
    public int SourceId { get; set; }
    public string Type { get; set; } = string.Empty; // "TagRequest" | "ItemReply"
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string TargetUrl { get; set; } = string.Empty;
    public bool IsRead { get; set; }
}