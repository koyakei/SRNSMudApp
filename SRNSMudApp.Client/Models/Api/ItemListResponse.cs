namespace SRNSMudApp.Client.Models.Api;

/// <summary>
/// タグ情報 DTO。
/// </summary>
public sealed record TagDto(
    int Id,
    string Name,
    string? Content,
    string? OwnerUserName,
    string? OwnerUserId = null,
    int Score = 0,
    bool IsPrivate = false,
    string? TargetUserGroupName = null,
    IReadOnlyList<TagRelationDto>? TargetTagRelations = null
);

/// <summary>
/// アイテムに紐づくタグ関連付け情報 DTO。
/// </summary>
public sealed record TagRelationDto(
    int TagId,
    string TagName,
    int? Weight,
    string? OwnerUserName
);

/// <summary>
/// アイテム情報 DTO。
/// </summary>
public sealed record ItemDto(
    int Id,
    string Content,
    string? OwnerUserName,
    DateTimeOffset UpdatedDate,
    bool IsPrivate,
    IReadOnlyList<TagRelationDto> TagRelations,
    string ItemKindJson,
    int? ParentItemId = null,
    int? RootItemId = null,
    int? QuotedItemId = null,
    string? OwnerUserId = null,
    int Score = 0,
    int ShinjiScore = 0,
    int ZenScore = 0,
    int BiScore = 0,
    int ReplyCount = 0,
    int QuoteCount = 0,
    string? QuotedItemContent = null,
    string? QuotedItemOwnerUserName = null,
    string? TargetUserGroupName = null
);

/// <summary>
/// ItemList クエリ API のレスポンス DTO。
/// </summary>
public sealed record ItemListQueryResponse(
    IReadOnlyList<ItemDto> Items,
    IReadOnlyList<TagDto> Tags,
    int TotalCount
);

/// <summary>
/// ユーザーグループ情報 DTO。
/// </summary>
public sealed record UserGroupDto(
    int Id,
    string Name,
    string? Description
);

/// <summary>
/// タグダイアグラムのエッジ情報 DTO。
/// </summary>
public sealed record TagDiagramEdgeDto(
    int Id,
    int SourceTagId,
    int TargetTagId,
    string? OwnerUserName
);

/// <summary>
/// タグダイアグラムのデータレスポンス DTO。
/// </summary>
public sealed record TagDiagramResponse(
    IReadOnlyList<TagDto> Nodes,
    IReadOnlyList<TagDiagramEdgeDto> Edges,
    IReadOnlyList<ItemDto> ContextItems
);

/// <summary>
/// ユーザーグループメンバー DTO。
/// </summary>
public sealed record UserGroupMemberDto(
    string UserId,
    string? UserName,
    DateTimeOffset? JoinedDate = null
);

/// <summary>
/// ユーザーグループ詳細情報 DTO。
/// </summary>
public sealed record UserGroupDetailDto(
    int Id,
    string Name,
    string? Description,
    string OwnerUserId,
    string? OwnerUserName,
    IReadOnlyList<UserGroupMemberDto> Members
);

/// <summary>
/// ユーザー情報 DTO。
/// </summary>
public sealed record UserDto(string Id, string? UserName);

/// <summary>
/// タグツリーノード DTO。
/// </summary>
public sealed record TagTreeNodeDto(
    int Id,
    string Name,
    string? Content,
    int? ParentTagId,
    int Score,
    string? OwnerUserName,
    IReadOnlyList<TagTreeNodeDto> Children
);

/// <summary>
/// CSV インポート結果レスポンス DTO。
/// </summary>
public sealed record ImportCsvResponse(int ImportedCount, string? Message);

/// <summary>
/// タグ付けリクエスト（提案）情報 DTO。
/// </summary>
public sealed record TaggingRequestDto(
    int Id,
    int TargetItemId,
    int RequestedTagId,
    string RequestedTagName,
    string RequesterUserId,
    string? RequesterUserName,
    string TagOwnerUserId,
    int ProposedWeight,
    string Status,
    string RequestType
);

/// <summary>
/// タグ詳細情報 DTO。
/// </summary>
public sealed record TagDetailDto(
    int Id,
    string Name,
    string? Content,
    string? OwnerUserId,
    string? OwnerUserName,
    int Score,
    bool IsPrivate,
    string? TargetUserGroupName,
    bool IsFollowing,
    bool AutoAcceptIncomingTaggingRequests,
    bool IsLocked,
    IReadOnlyList<ItemDto> RelatedItems,
    IReadOnlyList<TagDto> RelatedTags,
    IReadOnlyList<TaggingRequestDto> PendingRequests
);

/// <summary>
/// タグフォロー状態切替レスポンス DTO。
/// </summary>
public sealed record ToggleFollowResponse(bool IsFollowing);

/// <summary>
/// 通知情報 DTO。
/// </summary>
public sealed record NotificationItemDto(
    int SourceId,
    string SourceType,
    string Message,
    DateTimeOffset CreatedAt,
    bool IsRead,
    string ActorName,
    int AssociatedItemId,
    int? HighlightTagId,
    string? TargetPath
);

/// <summary>
/// 未読通知件数レスポンス DTO。
/// </summary>
public sealed record UnreadCountResponse(int UnreadCount);

/// <summary>
/// ユーザー詳細情報 DTO。
/// </summary>
public sealed record UserDetailDto(
    string UserId,
    string UserName,
    bool IsBanned,
    bool IsFollowing,
    int FollowingCount,
    int FollowersCount,
    IReadOnlyList<ItemDto> Items,
    IReadOnlyList<TagDto> Tags
);

/// <summary>
/// バウンティ（タグ付け依頼）情報 DTO。
/// </summary>
public sealed record BountyDto(
    int Id,
    string RequesterUserId,
    string? RequesterUserName,
    int RequestedTagId,
    string RequestedTagName,
    int TargetItemId,
    string TargetItemContent,
    string? RewardTagName,
    int? RewardAssetId,
    DateTimeOffset CreatedDate,
    bool IsMyBounty,
    bool CanFulfill
);

/// <summary>
/// バウンティ掲示板レスポンス DTO。
/// </summary>
public sealed record BountyBoardResponse(
    IReadOnlyList<BountyDto> Bounties,
    int TotalCount
);

/// <summary>
/// 公開トレードオファー情報 DTO。
/// </summary>
public sealed record PublicOfferDto(
    int Id,
    int OfferedTagId,
    string OfferedTagName,
    int RequiredAssetAmount,
    string OwnerUserId,
    string? OwnerUserName,
    DateTimeOffset CreatedDate,
    bool IsOwner
);

/// <summary>
/// コントラクト（契約・タグ付け提案）情報 DTO。
/// </summary>
public sealed record ContractDto(
    int Id,
    string ContractType,
    string Status,
    int RequestedTagId,
    string RequestedTagName,
    int TargetItemId,
    string TargetItemContent,
    string RequesterUserId,
    string? RequesterUserName,
    string TagOwnerUserId,
    string? TagOwnerUserName,
    DateTimeOffset CreatedDate,
    bool CanCancel
);

/// <summary>
/// コントラクト管理画面レスポンス DTO。
/// </summary>
public sealed record ContractPageResponse(
    IReadOnlyList<ContractDto> IncomingContracts,
    IReadOnlyList<ContractDto> OutgoingContracts
);

/// <summary>
/// タグ別 RightAsset サマリー DTO。
/// </summary>
public sealed record TagRightAssetSummaryDto(
    int TagId,
    string TagName,
    string? TagContent,
    int TotalAmount,
    int HolderCount
);

/// <summary>
/// RightAsset 保有者サマリー DTO。
/// </summary>
public sealed record RightAssetHolderDto(
    string UserId,
    string? UserName,
    int TotalAmount,
    int ActiveAssetCount,
    int BurnedAmount,
    int BurnedAssetCount,
    DateTime? LastUpdated
);

/// <summary>
/// 個別 RightAsset レコード DTO。
/// </summary>
public sealed record RightAssetItemDto(
    int Id,
    string OwnerId,
    string? OwnerName,
    int Amount,
    bool IsBurned,
    string StatusSummary,
    DateTime CreatedDate,
    DateTime UpdatedDate
);

/// <summary>
/// RightAsset 状況概要レスポンス DTO。
/// </summary>
public sealed record RightAssetOverviewResponse(
    int TagId,
    string TagName,
    string? TagContent,
    int TagWeight,
    bool IsSystem,
    int TotalHoldersCount,
    int TotalActiveAmount,
    int TotalActiveAssetsCount,
    int TotalBurnedAmount,
    IReadOnlyList<RightAssetHolderDto> Holders,
    IReadOnlyList<RightAssetItemDto> Assets
);

/// <summary>
/// 通報情報 DTO。
/// </summary>
public sealed record ContentReportDto(
    int Id,
    string TargetType,
    int? ItemId,
    int? TagId,
    string TargetContentSnapshot,
    string Reason,
    string? Detail,
    string Status,
    string? ResolutionNote,
    string ReporterUserId,
    string? ReporterUserName,
    string? HandledByAdminId,
    string? HandledByAdminName,
    DateTime CreatedDate,
    DateTime? HandledDate
);

/// <summary>
/// 管理者用ユーザー管理項目 DTO。
/// </summary>
public sealed record ManagedUserDto(
    string UserId,
    string UserName,
    bool IsAdmin,
    bool IsBanned,
    bool IsProtected
);

/// <summary>
/// 招待情報 DTO。
/// </summary>
public sealed record InvitationDto(
    int Id,
    string Email,
    string InvitationCode,
    DateTime ExpirationDate,
    bool IsUsed,
    string Status,
    string InvitedByAdminId,
    DateTime CreatedDate
);