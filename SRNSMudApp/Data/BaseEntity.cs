
namespace SRNSMudApp.Data;

public abstract class BaseEntity : IOwnable
{
    public int Id { get; set; }

    // 外部キー
    public required string OwnerId { get; set; }

    // ナビゲーションプロパティ
    public ApplicationUser Owner { get; set; } = null!;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    public override bool Equals(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        var other = (BaseEntity)obj;

        return Id != 0 && other.Id != 0 && Id == other.Id;
    }

#pragma warning disable ReSharper
    // ReSharper disable NonReadonlyMemberInGetHashCode
    // ReSharper disable BaseObjectGetHashCodeCallInGetHashCode
    public override int GetHashCode() => Id == 0 ? base.GetHashCode() : Id.GetHashCode();
    // ReSharper restore BaseObjectGetHashCodeCallInGetHashCode
    // ReSharper restore NonReadonlyMemberInGetHashCode
#pragma warning restore ReSharper
}