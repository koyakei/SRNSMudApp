using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Data;

public class TaggingRequestEntity : BaseEntity, ITaggable
{
    [MaxLength(50)] public string RequesterUserId { get; set; } = string.Empty;

    [MaxLength(50)] public string TagOwnerUserId { get; set; } = string.Empty;

    public int TargetItemId { get; set; }
    public Item TargetItem { get; set; } = null!;

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

    [NotMapped]
    public RejectionInfo Rejection
    {
        get => string.IsNullOrEmpty(RejectionInfoJson) ? new NoRejection() : JsonSerializer.Deserialize<RejectionInfo>(RejectionInfoJson);
        set => RejectionInfoJson = JsonSerializer.Serialize(value);
    }


    // --- Merged Subclass Properties ---

    // Type discriminator equivalent
    [MaxLength(50)] public string ContractType { get; set; } = string.Empty;

    // Contract Payload (Serialized JSON of ContractPayload union)
    public string ContractPayloadJson { get; set; } = string.Empty;

    /// <summary>ContractPayload union の JSON シリアライズ/デシリアライズ用オプション（キャッシュ）</summary>
    private static readonly JsonSerializerOptions PayloadJsonOptions = new JsonSerializerOptions
    {
        Converters = { new ContractPayloadConverter() }
    };

    [NotMapped]
    public ContractPayload Payload
    {
        get
        {
            if (string.IsNullOrEmpty(ContractPayloadJson))
                return new EmptyPayload();
            return JsonSerializer.Deserialize<ContractPayload>(ContractPayloadJson, PayloadJsonOptions);
        }
        set => ContractPayloadJson = JsonSerializer.Serialize(value, PayloadJsonOptions);
    }


    // ITaggable Implementation
    public virtual ICollection<Tag> Tags { get; init; } = [];
}