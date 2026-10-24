namespace SRNSMudApp.Client.Models.Api;

using System.Diagnostics.CodeAnalysis;

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
    string? OwnerUserName,
    int RelationId = 0
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

/// <summary>
/// タグ管理・ロック情報 DTO。
/// </summary>
public sealed record TagLockItemDto(
    int Id,
    string Name,
    int Level,
    int? ParentTagId,
    string? ParentTagName,
    string? OwnerUserName,
    bool IsDirectlyLocked,
    bool IsLevelLocked,
    bool IsSiblingLocked,
    bool IsLockedEffective
);

/// <summary>
/// タグ CSV インポート API のレスポンス DTO。
/// </summary>
public sealed record ImportTagCsvResponse(
    int CreatedCount,
    int UpdatedCount,
    string Message
);

/// <summary>
/// タグ付け構造 JSON インポート API のレスポンス DTO。
/// </summary>
public sealed record ImportTaggingResponse(
    int CreatedItemsCount,
    int CreatedTagsCount,
    int CreatedRelationsCount,
    int CreatedEdgesCount,
    int CreatedAttachmentsCount,
    IReadOnlyList<string> Messages
);

/// <summary>
/// ユーザーメール確認状態 DTO。
/// </summary>
public sealed record UserConfirmationDto(
    string UserId,
    string? UserName,
    string? Email,
    bool EmailConfirmed
);

/// <summary>
/// タグ名変更提案 DTO。
/// </summary>
public sealed record TagNameProposalDto(
    int Id,
    int TagId,
    string TargetTagName,
    string ProposedName,
    string RequesterUserId,
    string? RequesterUserName,
    string OwnerUserId,
    string? Reason,
    string Status,
    string? RejectReason,
    DateTimeOffset CreatedDate
);

/// <summary>
/// タグ内容変更提案 DTO。
/// </summary>
public sealed record TagContentProposalDto(
    int Id,
    int TagId,
    string TargetTagName,
    string ProposedContent,
    string RequesterUserId,
    string? RequesterUserName,
    string OwnerUserId,
    string? Reason,
    string Status,
    string? RejectReason,
    DateTimeOffset CreatedDate
);

/// <summary>
/// タグに対する保留中提案一覧 DTO。
/// </summary>
public sealed record TagPendingProposalsDto(
    IReadOnlyList<TagNameProposalDto> NameProposals,
    IReadOnlyList<TagContentProposalDto> ContentProposals
);

/// <summary>
/// タグ提案・推論結果 DTO。
/// </summary>
public sealed record SuggestedTagDto(int TagId, string TagName, float Score);

/// <summary>
/// アイテム分割リクエスト DTO。
/// </summary>
public sealed record ItemSplitRequestDto(
    int Id,
    int OriginalItemId,
    string SelectedText,
    string RequesterUserId,
    string? RequesterUserName,
    string OwnerUserId,
    string Status,
    string? RejectReason,
    DateTimeOffset CreatedDate,
    int? RequesterQuoteItemId = null,
    int? CreatedItemId = null
);

/// <summary>
/// ライトアセット（タグ権利アセット）情報 DTO。
/// </summary>
public sealed record RightAssetDto(
    int Id,
    int TargetTagId,
    string TargetTagName,
    int Amount,
    bool IsBurned
);

/// <summary>
/// JPYC ネットワーク情報 DTO。
/// </summary>
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "URL strings are used directly for UI binding and links")]
[SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "URL strings are used directly for UI binding and links")]
public sealed record JpycNetworkDto(
    string Name,
    string DisplayName,
    long ChainId,
    string ContractAddress,
    string? FaucetUrl,
    string ExplorerUrl,
    bool IsTestnet,
    bool IsRecommended = false
);

/// <summary>
/// ユーザー専用 JPYC 受取ウォレット情報 DTO。
/// </summary>
public sealed record DepositWalletDto(
    string UserId,
    string NetworkName,
    string DepositAddress,
    DateTime CreatedAt
);

/// <summary>
/// JPYC 購入完了結果 DTO。
/// </summary>
public sealed record PurchaseRightAssetResultDto(
    int RightAssetId,
    int TagId,
    string TagName,
    int Amount,
    int TotalJpycAmount,
    string NetworkName,
    string TransactionHash,
    string DepositAddress,
    DateTime PurchasedAt
);

/// <summary>
/// タグ重み台帳（WeightLedger）レコード DTO。
/// </summary>
public sealed record TagWeightLedgerDto(
    int Id,
    int TagId,
    string TagName,
    int? ItemId,
    string? OwnerUserId,
    string? OwnerUserName,
    string Reason,
    string SourceType,
    int PreviousWeight,
    int Delta,
    int NewWeight,
    DateTime CreatedDate,
    int? CommentItemId = null,
    string? Comment = null
);

/// <summary>
/// リンクプレビュー表示用タグアイテム DTO。
/// </summary>
public sealed record TagPreviewItemDto(
    int Id,
    string Name,
    string? OwnerName,
    int Weight
);

/// <summary>
/// リンクプレビュー（OGP / 内部リソースメタデータ）DTO。
/// </summary>
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "URL strings are used directly for UI binding and links")]
[SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "URL strings are used directly for UI binding and links")]
public sealed record LinkPreviewDto(
    string Url,
    string Title,
    string Description,
    string ImageUrl,
    string SiteName,
    bool IsSuccess,
    string? DisplayText,
    IReadOnlyList<TagPreviewItemDto> Tags
);

/// <summary>
/// 内部タグリンク変換候補 DTO。
/// </summary>
public sealed record LinkConversionCandidateDto(
    string OriginalText,
    int TagId,
    string TagName,
    float Similarity,
    int StartIndex,
    int Length,
    bool IsAutoReplace
);

/// <summary>
/// 内部タグリンク検出結果 DTO。
/// </summary>
public sealed record InternalLinkConversionResultDto(
    IReadOnlyList<LinkConversionCandidateDto> AutoReplaceCandidates,
    IReadOnlyList<LinkConversionCandidateDto> ManualCandidates
);

/// <summary>
/// 内部タグリンク置換結果 DTO。
/// </summary>
public sealed record ApplyLinkReplacementsResponse(string ConvertedContent);

/// <summary>
/// 類似タグ候補 DTO。
/// </summary>
public sealed record SimilarTagCandidateDto(
    int TagId,
    string TagName,
    float Similarity
);

/// <summary>
/// タグダイアグラムのエッジ紐付けタグ（Attachment）DTO。
/// </summary>
public sealed record TagEdgeAttachmentDto(
    int Id,
    int EdgeId,
    int TagId,
    string TagName,
    string? OwnerUserName,
    int Weight,
    DateTime CreatedDate
);