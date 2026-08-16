namespace SRNSMudApp.Data;

public class TaggingRequestReply : BaseEntity
{
    public int TaggingRequestEntityId { get; set; }
    public TaggingRequestEntity TaggingRequest { get; set; } = null!;

    public string Message { get; set; } = string.Empty;
}
