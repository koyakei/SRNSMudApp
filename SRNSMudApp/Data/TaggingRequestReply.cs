namespace SRNSMudApp.Data;

public class TaggingRequestReply : BaseEntity, ITaggable
{
    public int TaggingRequestEntityId { get; set; }
    public TaggingRequestEntity TaggingRequest { get; set; } = null!;

    public string Message { get; set; } = string.Empty;

    // ITaggable Implementation
    public virtual ICollection<Tag> Tags { get; set; } = new List<Tag>();
}
