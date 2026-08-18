namespace SRNSMudApp.Data;

public interface ITaggable
{
    int Id { get; set; }
    ICollection<Tag> Tags { get; init; }
}