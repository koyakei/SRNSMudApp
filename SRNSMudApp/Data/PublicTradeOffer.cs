namespace SRNSMudApp.Data;

public class PublicTradeOffer : BaseEntity
{
    // 提供されるタグ
    public int OfferedTagId { get; set; }
    public Tag OfferedTag { get; set; } = null!;

    // ユーザーが支払う必要がある RightAsset の要求量
    // ※ ユーザーが提供する RightAsset の TargetTagId は、OfferedTagId と一致しているか判定に使用します。
    public int RequiredAssetAmount { get; set; }

    // オファーが有効かどうか（取り下げ可能）
    public bool IsActive { get; set; } = true;
}