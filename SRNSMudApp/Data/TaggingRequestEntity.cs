using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Data;

public class TaggingRequestEntity : BaseEntity, IDirectTaggable
{
    [MaxLength(50)] public string RequesterUserId { get; set; } = string.Empty;

    [MaxLength(50)] public string TagOwnerUserId { get; set; } = string.Empty;

    public int TargetId { get; set; }
    public TaggableTarget Target { get; set; } = null!;

    private int _targetItemId;
    private Item? _targetItem;

    /// <summary>後方互換性プロパティ（対象が Item の場合、または初期化用）</summary>
    [NotMapped]
    public int TargetItemId
    {
        get => Target?.Item?.Id ?? _targetItem?.Id ?? _targetItemId;
        set => _targetItemId = value;
    }

    /// <summary>後方互換性プロパティ（対象が Item の場合）</summary>
    [NotMapped]
    public Item? TargetItem
    {
        get => Target?.Item ?? _targetItem;
        set
        {
            _targetItem = value;
            if (value?.TagTarget != null)
            {
                Target = value.TagTarget;
                TargetId = value.TagTarget.Id;
            }
            else if (value != null && value.TagTargetId > 0)
            {
                TargetId = value.TagTargetId;
            }
        }
    }

    public int RequestedTagId { get; set; }
    public Tag RequestedTag { get; set; } = null!;

    public TradeStatus Status { get; set; } = TradeStatus.Proposed;
    public int ProposedWeight { get; set; } = 1;

    // The asset that will be burned when this contract is accepted. (Optional)
    public int? ConsumedRightAssetId { get; set; }
    public RightAsset? ConsumedRightAsset { get; set; }

    public int? RequestItemId { get; set; }
    public Item? RequestItem { get; set; }

    public TaggingRequestType RequestType { get; set; } = TaggingRequestType.Add;
    public ICollection<Item> Replies { get; init; } = [];

    // Rejection Information
    public string RejectionInfoJson { get; set; } = string.Empty;

    /// <summary>RejectionInfo union の JSON シリアライズ/デシリアライズ用オプション</summary>
    private static readonly JsonSerializerOptions RejectionJsonOptions = new()
    {
        Converters = { new RejectionInfoConverter() }
    };

    [NotMapped]
    public RejectionInfo Rejection
    {
        get => string.IsNullOrEmpty(RejectionInfoJson) ? new NoRejection() : JsonSerializer.Deserialize<RejectionInfo>(RejectionInfoJson, RejectionJsonOptions);
        set => RejectionInfoJson = JsonSerializer.Serialize(value, RejectionJsonOptions);
    }

    // --- Merged Subclass Properties ---

    // Type discriminator equivalent
    [MaxLength(50)] public string ContractType { get; set; } = string.Empty;

    // Contract Payload (Serialized JSON of ContractPayload union)
    public string ContractPayloadJson { get; set; } = string.Empty;

    /// <summary>ContractPayload union の JSON シリアライズ/デシリアライズ用オプション（キャッシュ）</summary>
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        Converters = { new ContractPayloadConverter() }
    };

    [NotMapped]
    public ContractPayload Payload
    {
        get => string.IsNullOrEmpty(ContractPayloadJson)
            ? new EmptyPayload()
            : JsonSerializer.Deserialize<ContractPayload>(ContractPayloadJson, PayloadJsonOptions);
        set => ContractPayloadJson = JsonSerializer.Serialize(value, PayloadJsonOptions);
    }

    // ITaggable Implementation
    public virtual ICollection<Tag> Tags { get; init; } = [];
}