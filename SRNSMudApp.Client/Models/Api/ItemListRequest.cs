namespace SRNSMudApp.Client.Models.Api;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// タグフィルタ条件 DTO (TagId または TagName 指定)。
/// </summary>
public sealed record ItemListFilterDto(
    int? TagId = null,
    string? TagName = null,
    string? UserName = null
);

/// <summary>
/// ソート条件 DTO。
/// </summary>
public sealed record ItemListSortDto(int TagId, bool Ascending);

/// <summary>
/// ItemList クエリ API のリクエスト DTO。
/// </summary>
public sealed record ItemListQueryRequest(
    IReadOnlyList<ItemListFilterDto>? Filters = null,
    IReadOnlyList<ItemListSortDto>? Sorts = null,
    int Skip = 0,
    int Take = 50
);

/// <summary>
/// JSON エクスポート API のリクエスト DTO。
/// </summary>
public sealed record ItemListExportRequest(IReadOnlyList<int> ItemIds);

/// <summary>
/// アイテム投票 API のリクエスト DTO。
/// </summary>
public sealed record ItemVoteRequest(bool IsUpvote);

/// <summary>
/// アイテムリアクション API のリクエスト DTO。
/// </summary>
public sealed record ItemReactionRequest(string ReactionTagName, int Weight = 1);

/// <summary>
/// アイテム作成 API のリクエスト DTO。
/// </summary>
public sealed record CreateItemRequest(
    [property: StringLength(1000, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string Content,
    bool IsPrivate = false,
    IReadOnlyList<int>? InitialTagIds = null,
    int? QuotedItemId = null,
    int? TargetUserGroupId = null
);

/// <summary>
/// アイテム更新 API のリクエスト DTO。
/// </summary>
public sealed record UpdateItemRequest(
    [property: StringLength(1000, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string Content
);

/// <summary>
/// リプライ作成 API のリクエスト DTO。
/// </summary>
public sealed record CreateReplyRequest(
    [property: StringLength(1000, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string Content,
    bool IsPrivate = false,
    IReadOnlyList<string>? TargetUserIds = null
);

/// <summary>
/// タグ関連付け追加 API のリクエスト DTO。
/// </summary>
public sealed record AddTagRelationRequest(
    int TagId,
    [property: StringLength(1000, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string? Comment = null
);

/// <summary>
/// タグ投票 API のリクエスト DTO。
/// </summary>
public sealed record TagVoteRequest(bool IsUpvote);

/// <summary>
/// タグ作成 API のリクエスト DTO。
/// </summary>
public sealed record CreateTagRequest(
    [property: Required(ErrorMessage = "タグ名は必須です。")]
    [property: StringLength(100, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    [property: RegularExpression(@"^[\x20-\x7E\u3000-\u30FF\u4E00-\u9FFF\uFF01-\uFF9F\u2200-\u22FF]+$",
        ErrorMessage = "タグ名には漢字、ひらがな、カタカナ（半角/全角）、英数字（半角/全角）、アンダーバー、数学記号のみ使用できます。")]
    string Name,
    [property: StringLength(1000, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string? Content = null,
    bool IsPrivate = false,
    int? TargetUserGroupId = null,
    IReadOnlyList<int>? ParentTagIds = null
);

/// <summary>
/// タグ更新 API のリクエスト DTO。
/// </summary>
public sealed record UpdateTagRequest(
    [property: Required(ErrorMessage = "タグ名は必須です。")]
    [property: StringLength(100, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    [property: RegularExpression(@"^[\x20-\x7E\u3000-\u30FF\u4E00-\u9FFF\uFF01-\uFF9F\u2200-\u22FF]+$",
        ErrorMessage = "タグ名には漢字、ひらがな、カタカナ（半角/全角）、英数字（半角/全角）、アンダーバー、数学記号のみ使用できます。")]
    string Name,
    [property: StringLength(1000, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string? Content = null,
    bool IsPrivate = false,
    int? TargetUserGroupId = null
);

/// <summary>
/// 親タグ関連付け追加 API のリクエスト DTO。
/// </summary>
public sealed record AddParentTagRequest(int ParentTagId);

/// <summary>
/// ユーザーグループ作成 API のリクエスト DTO。
/// </summary>
public sealed record CreateUserGroupRequest(
    [property: Required(ErrorMessage = "グループ名は必須です。")]
    [property: StringLength(100, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string Name,
    [property: StringLength(500, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string? Description = null
);

/// <summary>
/// ユーザーグループ更新 API のリクエスト DTO。
/// </summary>
public sealed record UpdateUserGroupRequest(
    [property: Required(ErrorMessage = "グループ名は必須です。")]
    [property: StringLength(100, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string Name,
    [property: StringLength(500, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string? Description = null
);

/// <summary>
/// ユーザーグループメンバー追加 API のリクエスト DTO。
/// </summary>
public sealed record AddGroupMemberRequest(string UserId);

/// <summary>
/// タグウェイト変更 API のリクエスト DTO。
/// </summary>
public sealed record UpdateTagWeightRequest(int Delta);

/// <summary>
/// CSV インポート API のリクエスト DTO。
/// </summary>
public sealed record ImportCsvRequest(string CsvContent);

/// <summary>
/// タグ変更 API のリクエスト DTO。
/// </summary>
public sealed record ChangeItemTagRequest(int NewTagId);

/// <summary>
/// タグ付けリクエスト却下 API のリクエスト DTO。
/// </summary>
public sealed record RejectTaggingRequest(
    [property: StringLength(500, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string? Reason = null
);

/// <summary>
/// コントラクト拒否 API のリクエスト DTO。
/// </summary>
public sealed record RejectContractRequest(
    [property: StringLength(500, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string? Reason = null
);

/// <summary>
/// 通知既読化 API のリクエスト DTO。
/// </summary>
public sealed record MarkAsReadRequest(string SourceType);

/// <summary>
/// バウンティ（タグ付け依頼）作成 API のリクエスト DTO。
/// </summary>
public sealed record CreateBountyRequest(
    int TargetItemId,
    int RequestedTagId,
    int? OfferedRewardAssetId = null
);

/// <summary>
/// バウンティ完了 API のリクエスト DTO。
/// </summary>
public sealed record FulfillBountyRequest(
    int? AssetIdToConsume = null
);

/// <summary>
/// 公開オファー作成 API のリクエスト DTO。
/// </summary>
public sealed record CreatePublicOfferRequest(
    int OfferedTagId,
    [property: Range(0, int.MaxValue, ErrorMessage = "{0}は0以上の値を指定してください。")]
    int RequiredAssetAmount = 0
);

/// <summary>
/// 公開オファー承諾（トリガー）API のリクエスト DTO。
/// </summary>
public sealed record TriggerPublicOfferRequest(
    int TargetItemId,
    int? AssetIdToConsume = null
);

/// <summary>
/// 通報作成 API のリクエスト DTO。
/// </summary>
public sealed record CreateReportRequest(
    [property: Required(ErrorMessage = "対象種別は必須です。")]
    string TargetType,
    int? ItemId,
    int? TagId,
    [property: Required(ErrorMessage = "通報理由は必須です。")]
    [property: StringLength(100, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string Reason,
    [property: StringLength(1000, ErrorMessage = "{0}は{1}文字以内で入力してください。")]
    string? Detail
);

/// <summary>
/// 通報ステータス更新 API のリクエスト DTO。
/// </summary>
public sealed record UpdateReportStatusRequest(
    string Status,
    string? ResolutionNote
);

/// <summary>
/// 通報処置実行 API のリクエスト DTO。
/// </summary>
public sealed record ResolveReportRequest(
    bool DeleteTarget,
    bool HideTarget,
    string? ResolutionNote
);

/// <summary>
/// ユーザー管理者権限設定 API のリクエスト DTO。
/// </summary>
public sealed record SetUserAdminRequest(bool IsAdmin);

/// <summary>
/// ユーザー利用停止（BAN）設定 API のリクエスト DTO。
/// </summary>
public sealed record SetUserBanRequest(bool IsBanned);

/// <summary>
/// 招待作成 API のリクエスト DTO。
/// </summary>
public sealed record CreateInvitationRequest(string Email);

/// <summary>
/// 階層ロックレベル設定 API のリクエスト DTO。
/// </summary>
public sealed record SetHierarchyLockLevelRequest(int Level);

/// <summary>
/// タグ CSV インポート API のリクエスト DTO。
/// </summary>
public sealed record ImportTagCsvRequest(
    string ParentTagName,
    string CsvContent,
    bool AsSystem = false
);

/// <summary>
/// タグ付け JSON インポート API のリクエスト DTO。
/// </summary>
public sealed record ImportTaggingJsonRequest(string JsonText);

/// <summary>
/// タグ名変更提案 API のリクエスト DTO。
/// </summary>
public sealed record ProposeTagNameRequest(int TagId, string ProposedName, string? Reason = null);

/// <summary>
/// タグ内容変更提案 API のリクエスト DTO。
/// </summary>
public sealed record ProposeTagContentRequest(int TagId, string ProposedContent, string? Reason = null);

/// <summary>
/// 提案却下 API のリクエスト DTO。
/// </summary>
public sealed record RejectProposalRequest(string? RejectReason = null);

/// <summary>
/// タグ提案・推論 API のリクエスト DTO。
/// </summary>
public sealed record TagSuggestionRequest(string Content, float? MinScore = null);

/// <summary>
/// アイテム直接分割 API のリクエスト DTO。
/// </summary>
public sealed record DirectSplitItemRequest(int OriginalItemId, string SelectedText);

/// <summary>
/// アイテム分割リクエスト申請 API のリクエスト DTO。
/// </summary>
public sealed record RequestItemSplitRequest(int OriginalItemId, string SelectedText, int? RequesterQuoteItemId = null);

/// <summary>
/// アイテム分割リクエスト却下 API のリクエスト DTO。
/// </summary>
public sealed record RejectSplitRequest(string? RejectReason = null);

/// <summary>
/// Gratis（無償）コントラクト提案 API のリクエスト DTO。
/// </summary>
public sealed record ProposeGratisContractRequest(
    int TargetItemId,
    int RequestedTagId,
    int ProposedWeight = 1,
    string? Message = null,
    string RequestType = "Add"
);

/// <summary>
/// Mutual（相互）コントラクト提案 API のリクエスト DTO。
/// </summary>
public sealed record ProposeMutualContractRequest(
    int TargetItemId,
    int RequestedTagId,
    int OfferedTargetItemId,
    int OfferedTagId,
    int ConsumedRightAssetId,
    int ProposedWeight = 1,
    string RequestType = "Add"
);

/// <summary>
/// JPYC 送金シミュレーション API のリクエスト DTO。
/// </summary>
public sealed record SimulateDepositRequest(string NetworkName, int AmountJpyc);

/// <summary>
/// JPYC による RightAsset 購入 API のリクエスト DTO。
/// </summary>
public sealed record PurchaseRightAssetRequest(
    int RequestedTagId,
    int Amount,
    int UnitPriceJpyc,
    string NetworkName,
    string? TransactionHash = null,
    string? SenderWalletAddress = null
);

/// <summary>
/// リンクプレビュー取得 API のリクエスト DTO。
/// </summary>
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Internal and external URLs are passed as strings for preview extraction")]
[SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Internal and external URLs are passed as strings for preview extraction")]
public sealed record LinkPreviewRequest(string Url);

/// <summary>
/// 内部タグリンク候補検出 API のリクエスト DTO。
/// </summary>
public sealed record DetectLinkCandidatesRequest(
    string Content,
    float AutoReplaceThreshold = 0.85f
);

/// <summary>
/// 内部タグリンク置換適用 API のリクエスト DTO。
/// </summary>
public sealed record ApplyLinkReplacementsRequest(
    string Content,
    IReadOnlyList<LinkConversionCandidateDto> Candidates
);

/// <summary>
/// タグダイアグラムのエッジ作成 API のリクエスト DTO。
/// </summary>
public sealed record CreateTagEdgeRequest(int SourceTagId, int TargetTagId);

/// <summary>
/// タグダイアグラムのエッジへの意味付けタグ紐付け API のリクエスト DTO。
/// </summary>
public sealed record AttachTagToEdgeRequest(int TagId, int RightAssetId, int Weight = 1);

/// <summary>
/// AI による親タグ階層判定・提案 API のリクエスト DTO。
/// </summary>
public sealed record SuggestParentTagRequest(string TagName);