using System.ComponentModel.DataAnnotations.Schema;

namespace SRNSMudApp.Data;

public class UserTagFollow : BaseEntity
{
    public int TagId { get; set; }

    [ForeignKey("TagId")]
    public Tag Tag { get; set; } = null!;
}