using System.ComponentModel.DataAnnotations;

namespace SRNSMudApp.Data;

public sealed class GratisTaggingContract : TaggingRequestEntity
{
    [MaxLength(200)]
    public string? RequesterMessage { get; set; }
}