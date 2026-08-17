using System;
using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Data;

public class NotificationReadState
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public int SourceId { get; set; }

    [Required]
    [MaxLength(50)]
    public string SourceType { get; set; } = string.Empty; // "TagRequest" or "ItemReply"

    public DateTimeOffset ReadAt { get; set; } = DateTimeOffset.UtcNow;
}