namespace SRNSMudApp.Data;

public sealed class BountyTaggingContract : TaggingRequestEntity
{
    // 依頼者（Requester）が「タグ付けしてくれた人」に渡す報酬アセット（リバースMutual用）
    // 無償のお願い（善意のウィッシュリスト）の場合は null
    public int? OfferedRewardAssetId { get; set; }
    public RightAsset? OfferedRewardAsset { get; set; }
}