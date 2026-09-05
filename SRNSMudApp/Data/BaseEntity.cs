
namespace SRNSMudApp.Data;

/// <summary>
///     ドメインモデルの基本エンティティ基底クラス。
///     サロゲートキー Id、所有者情報、および作成・更新日時を管理する。
/// </summary>
public abstract class BaseEntity : IOwnable
{
    public int Id { get; set; }

    // 外部キー
    public required string OwnerId { get; set; }

    // ナビゲーションプロパティ
    public ApplicationUser Owner { get; set; } = null!;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     エンティティの等価性を判定する。
    ///     同一型かつ永続化済み（Id != 0）の場合に主キー ID の一致で等価とみなす。
    ///     永続化前（Id == 0）のエンティティ同士は、異なる未保存インスタンスとして区別するため参照等価でない限り false を返す。
    /// </summary>
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