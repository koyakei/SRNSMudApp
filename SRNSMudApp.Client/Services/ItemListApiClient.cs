namespace SRNSMudApp.Client.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using SRNSMudApp.Client.Models;
using SRNSMudApp.Client.Models.Api;

/// <summary>
/// ItemList 向け Web API クライアントインターフェース。
/// </summary>
public interface IItemListApiClient : ITagApiClient, ITagProposalApiClient, ITagLedgerApiClient, ITaggingRequestApiClient
{
    /// <summary>
    /// タグ名のサジェスト候補を検索します。
    /// </summary>
    Task<IReadOnlyList<TagSuggestion>> SearchTagsAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 特定タグに関連付けられたユーザー名一覧を検索します。
    /// </summary>
    Task<IReadOnlyList<string>> SearchTagUsersAsync(string tagName, string? userSearch = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテム・タグ一覧および総件数をクエリ条件に基づき取得します。
    /// </summary>
    Task<ItemListQueryResponse> QueryItemsAsync(ItemListQueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定アイテム一覧を JSON 形式でエクスポートします。
    /// </summary>
    Task<string> ExportItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムへの投票（アップボート / ダウンボート）を行います。
    /// </summary>
    Task<bool> VoteItemAsync(int itemId, bool isUpvote, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムへのリアクション（真実・善・美）を行います。
    /// </summary>
    Task<bool> ReactItemAsync(int itemId, string reactionTagName, int weight = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムの詳細情報を取得します。
    /// </summary>
    Task<ItemDto?> GetItemByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新規アイテムを作成します。
    /// </summary>
    Task<ItemDto?> CreateItemAsync(CreateItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムのリプライ一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<ItemDto>> GetRepliesAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムにリプライを投稿します。
    /// </summary>
    Task<ItemDto?> AddReplyAsync(int itemId, CreateReplyRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムを引用しているアイテム一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<ItemDto>> GetQuotesAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテム本文を更新します。
    /// </summary>
    Task<bool> UpdateItemAsync(int itemId, UpdateItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムを削除します。
    /// </summary>
    Task<bool> DeleteItemAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムにタグを追加します。
    /// </summary>
    Task<bool> AddTagToItemAsync(int itemId, AddTagRelationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムに紐づくタグのウェイト（重み）を変更します。
    /// </summary>
    Task<bool> UpdateTagWeightAsync(int itemId, int relationId, int delta, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムからタグ関連付けを削除します。
    /// </summary>
    Task<bool> RemoveTagFromItemAsync(int itemId, int relationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテムに紐づくタグ関連付けのタグを変更します。
    /// </summary>
    Task<bool> ChangeItemTagAsync(int itemId, int relationId, ChangeItemTagRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ログインユーザーが所属・管理するグループ一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<UserGroupDto>> GetUserGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 新規タグを作成します。
    /// </summary>
    Task<TagDto?> CreateTagAsync(CreateTagRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 親タグとの関連付けを追加します。
    /// </summary>
    Task<bool> AddParentTagAsync(int tagId, int parentTagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 親タグとの関連付けを削除します。
    /// </summary>
    Task<bool> RemoveParentTagAsync(int tagId, int relationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新規ユーザーグループを作成します。
    /// </summary>
    Task<UserGroupDto?> CreateUserGroupAsync(CreateUserGroupRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーグループの詳細（メンバー一覧を含む）を取得します。
    /// </summary>
    Task<UserGroupDetailDto?> GetUserGroupDetailAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーグループを更新します。
    /// </summary>
    Task<bool> UpdateUserGroupAsync(int id, UpdateUserGroupRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーグループを削除します。
    /// </summary>
    Task<bool> DeleteUserGroupAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーグループにメンバーを追加します。
    /// </summary>
    Task<bool> AddUserGroupMemberAsync(int id, AddGroupMemberRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーグループからメンバーを削除します。
    /// </summary>
    Task<bool> RemoveUserGroupMemberAsync(int id, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ログインユーザーが管理するグループ一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<UserGroupDto>> GetManagedUserGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// タグダイアグラムのデータ（ノード・エッジ・コンテキストアイテム）を取得します。
    /// </summary>
    Task<TagDiagramResponse?> GetTagDiagramDataAsync(string? itemIds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザー候補を検索します。
    /// </summary>
    Task<IReadOnlyList<UserDto>> SearchUsersAsync(string? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグ階層ツリー構造を取得します。
    /// </summary>
    Task<IReadOnlyList<TagTreeNodeDto>> GetTagTreeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定アイテムを引用しているアイテム一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<ItemDto>> GetQuotedByItemsAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// CSV データをインポートします。
    /// </summary>
    Task<ImportCsvResponse?> ImportCsvAsync(ImportCsvRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ログインユーザーの通知一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<NotificationItemDto>> GetNotificationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ログインユーザーの未読通知件数を取得します。
    /// </summary>
    Task<int> GetUnreadNotificationCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定された通知を既読化します。
    /// </summary>
    Task<bool> MarkNotificationAsReadAsync(int sourceId, string sourceType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 全ての通知を一括で既読化します。
    /// </summary>
    Task<bool> MarkAllNotificationsAsReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーの詳細情報を取得します。
    /// </summary>
    Task<UserDetailDto?> GetUserDetailAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーのフォロー状態を切り替えます。
    /// </summary>
    Task<bool> ToggleUserFollowAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// アクティブなバウンティ一覧を取得します。
    /// </summary>
    Task<BountyBoardResponse?> GetBountiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 新規バウンティを作成します。
    /// </summary>
    Task<bool> CreateBountyAsync(CreateBountyRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// バウンティを完了・受諾します。
    /// </summary>
    Task<bool> FulfillBountyAsync(int bountyId, FulfillBountyRequest? request = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// アクティブな公開オファー一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<PublicOfferDto>> GetPublicOffersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 新規公開オファーを出品します。
    /// </summary>
    Task<bool> CreatePublicOfferAsync(CreatePublicOfferRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 自分の公開オファーを取り下げます。
    /// </summary>
    Task<bool> DeactivatePublicOfferAsync(int offerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 公開オファーを承諾（トリガー実行）します。
    /// </summary>
    Task<bool> TriggerPublicOfferAsync(int offerId, TriggerPublicOfferRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ログインユーザーのコントラクト一覧（受信・送信）を取得します。
    /// </summary>
    Task<ContractPageResponse?> GetContractsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// コントラクトを承認します。
    /// </summary>
    Task<bool> AcceptContractAsync(int contractId, CancellationToken cancellationToken = default);

    /// <summary>
    /// コントラクトを拒否します。
    /// </summary>
    Task<bool> RejectContractAsync(int contractId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 自分が送信したコントラクトを取り下げます。
    /// </summary>
    Task<bool> CancelContractAsync(int contractId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ログインユーザーの利用可能ライトアセット一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<RightAssetDto>?> GetAvailableRightAssetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gratis（無償）コントラクト提案を送信します。
    /// </summary>
    Task<ContractDto?> ProposeGratisContractAsync(ProposeGratisContractRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mutual（相互）コントラクト提案を送信します。
    /// </summary>
    Task<ContractDto?> ProposeMutualContractAsync(ProposeMutualContractRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// RightAsset が発行されている代表的なタグ一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<TagRightAssetSummaryDto>> GetTopTagsWithRightAssetsAsync(int count = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたタグの RightAsset 保有状況および詳細アセット一覧を取得します。
    /// </summary>
    Task<RightAssetOverviewResponse?> GetRightAssetOverviewAsync(int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// サポートされている JPYC ネットワーク一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<JpycNetworkDto>> GetJpycNetworksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザー専用の JPYC 受取ウォレットを取得または新規発行します。
    /// </summary>
    Task<DepositWalletDto?> GetUserDepositWalletAsync(string networkName = "polygon-amoy", CancellationToken cancellationToken = default);

    /// <summary>
    /// テストネット用にユーザー専用受取アドレスへの JPYC 送金をシミュレートします。
    /// </summary>
    Task<string?> SimulateDepositAsync(SimulateDepositRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// JPYC による RightAsset の購入・発行を行います。
    /// </summary>
    Task<PurchaseRightAssetResultDto?> PurchaseRightAssetAsync(PurchaseRightAssetRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 不適切なコンテンツの通報を作成します。
    /// </summary>
    Task<ContentReportDto?> CreateReportAsync(CreateReportRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通報一覧を取得します（管理者専用）。
    /// </summary>
    Task<IReadOnlyList<ContentReportDto>> GetReportsAsync(string? status = null, string? targetType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通報詳細を取得します（管理者専用）。
    /// </summary>
    Task<ContentReportDto?> GetReportByIdAsync(int reportId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通報ステータスを更新します（管理者専用）。
    /// </summary>
    Task<bool> UpdateReportStatusAsync(int reportId, UpdateReportStatusRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通報への処置（削除または非公開化）を実行します（管理者専用）。
    /// </summary>
    Task<bool> ResolveReportAsync(int reportId, ResolveReportRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 管理者用ユーザー管理一覧を取得します（管理者専用）。
    /// </summary>
    Task<IReadOnlyList<ManagedUserDto>> GetManagementUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーの管理者（Admin）権限を設定します（管理者専用）。
    /// </summary>
    Task<bool> SetUserAdminAsync(string userId, SetUserAdminRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーの利用停止（BAN）状態を設定します（管理者専用）。
    /// </summary>
    Task<bool> SetUserBanAsync(string userId, SetUserBanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 招待一覧を取得します（管理者専用）。
    /// </summary>
    Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 新規招待を発行します（管理者専用）。
    /// </summary>
    Task<InvitationDto?> CreateInvitationAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 招待を削除します（管理者専用）。
    /// </summary>
    Task<bool> DeleteInvitationAsync(int invitationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 階層ロックレベルを取得します（管理者専用）。
    /// </summary>
    Task<int> GetTagLockLevelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 階層ロックレベルを設定します（管理者専用）。
    /// </summary>
    Task<bool> SetTagLockLevelAsync(int level, CancellationToken cancellationToken = default);

    /// <summary>
    /// 全タグのロック状態一覧を取得します（管理者専用）。
    /// </summary>
    Task<IReadOnlyList<TagLockItemDto>> GetTagLockItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// CSV 形式のタグ階層データを親タグ配下にインポートします。
    /// </summary>
    Task<ImportTagCsvResponse?> ImportTagCsvAsync(ImportTagCsvRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// JSON 形式のタグ付け構造データ（Item, TagRelation, TagEdge）を一括インポートします。
    /// </summary>
    Task<ImportTaggingResponse?> ImportTaggingJsonAsync(ImportTaggingJsonRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 全ユーザーのメール確認状態一覧を取得します（管理者専用）。
    /// </summary>
    Task<IReadOnlyList<UserConfirmationDto>?> GetConfirmedAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定したユーザーのメール確認状態を反転します（管理者専用）。
    /// </summary>
    Task<bool> ToggleUserConfirmationAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 入力テキストから関連タグを推論・提案します。
    /// </summary>
    Task<IReadOnlyList<SuggestedTagDto>?> GetTagSuggestionsAsync(TagSuggestionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテム所有者として、選択テキストを新規アイテムへ直接分割します。
    /// </summary>
    Task<ItemDto?> SplitItemDirectlyAsync(DirectSplitItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 他ユーザーのアイテムに対し、テキスト分割リクエストを申請します。
    /// </summary>
    Task<ItemSplitRequestDto?> RequestItemSplitAsync(RequestItemSplitRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたアイテムに対する保留中の分割リクエスト一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<ItemSplitRequestDto>?> GetPendingSplitRequestsAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテム所有者として分割リクエストを承認します。
    /// </summary>
    Task<ItemDto?> ApproveItemSplitAsync(int requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// アイテム所有者として分割リクエストを却下します。
    /// </summary>
    Task<bool> RejectItemSplitAsync(int requestId, RejectSplitRequest? request = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 申請した分割リクエストを取り下げます（申請者本人のみ）。
    /// </summary>
    Task<bool> CancelItemSplitAsync(int requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたアイテムのタグ重み変動台帳（TagWeightLedger）履歴を取得します。
    /// </summary>
    Task<IReadOnlyList<TagWeightLedgerDto>> GetItemTagWeightLedgersAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定された URL のメタデータ・OGP プレビューを取得します。
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Relative and absolute URLs are accepted as strings")]
    Task<LinkPreviewDto?> GetLinkPreviewAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// 入力テキストからタグ候補を検出し、内部リンク置換候補を返却します。
    /// </summary>
    Task<InternalLinkConversionResultDto?> DetectLinkCandidatesAsync(DetectLinkCandidatesRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 選択された変換候補をテキストに適用し、内部タグリンクに置換した文字列を返却します。
    /// </summary>
    Task<string?> ApplyLinkReplacementsAsync(ApplyLinkReplacementsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたタグ名に類似した既存タグ候補を取得します（表記揺れ検出・重複防止用）。
    /// </summary>
    Task<IReadOnlyList<SimilarTagCandidateDto>> FindSimilarTagsAsync(string name, float minSimilarity = 0.5f, int maxResults = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたタグ名に最適な親タグの候補を AI / ベクトル類似度により推論・提案します。
    /// </summary>
    Task<TagDto?> SuggestParentTagAsync(SuggestParentTagRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグダイアグラムに新規エッジを作成します。
    /// </summary>
    Task<TagDiagramEdgeDto?> CreateTagEdgeAsync(CreateTagEdgeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// タグダイアグラムのエッジを削除します。
    /// </summary>
    Task<bool> DeleteTagEdgeAsync(int edgeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたエッジに紐付けられている意味付けタグ一覧を取得します。
    /// </summary>
    Task<IReadOnlyList<TagEdgeAttachmentDto>> GetAttachmentsForEdgeAsync(int edgeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// エッジに意味付けタグを紐付けます（RightAssetを消費）。
    /// </summary>
    Task<TagEdgeAttachmentDto?> AttachTagToEdgeAsync(int edgeId, AttachTagToEdgeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// エッジから意味付けタグの紐付けを解除します。
    /// </summary>
    Task<bool> DetachTagFromEdgeAsync(int attachmentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IItemListApiClient"/> の HTTP 実装クラス。
/// </summary>
public sealed class ItemListApiClient(HttpClient httpClient) : IItemListApiClient
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<IReadOnlyList<TagSuggestion>> SearchTagsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = $"api/item/search-tags?q={Uri.EscapeDataString(query)}";
        List<TagSuggestion>? result = await _httpClient.GetFromJsonAsync<List<TagSuggestion>>(url, cancellationToken);
        return result ?? [];
    }

    public async Task<IReadOnlyList<string>> SearchTagUsersAsync(string tagName, string? userSearch = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return [];
        }

        var url = $"api/item/tag-users?tagName={Uri.EscapeDataString(tagName)}";
        if (!string.IsNullOrWhiteSpace(userSearch))
        {
            url += $"&userSearch={Uri.EscapeDataString(userSearch)}";
        }

        List<string>? result = await _httpClient.GetFromJsonAsync<List<string>>(url, cancellationToken);
        return result ?? [];
    }

    public async Task<ItemListQueryResponse> QueryItemsAsync(ItemListQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/item/query", request, cancellationToken);
        _ = response.EnsureSuccessStatusCode();

        ItemListQueryResponse? result = await response.Content.ReadFromJsonAsync<ItemListQueryResponse>(cancellationToken);
        return result ?? new ItemListQueryResponse([], [], 0);
    }

    public async Task<string> ExportItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var request = new ItemListExportRequest(itemIds);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/item/export", request, cancellationToken);
        _ = response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<bool> VoteItemAsync(int itemId, bool isUpvote, CancellationToken cancellationToken = default)
    {
        var request = new ItemVoteRequest(isUpvote);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/item/{itemId}/vote", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ReactItemAsync(int itemId, string reactionTagName, int weight = 1, CancellationToken cancellationToken = default)
    {
        var request = new ItemReactionRequest(reactionTagName, weight);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/item/{itemId}/reaction", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<ItemDto?> GetItemByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<ItemDto>($"api/item/{id}", cancellationToken);
    }

    public async Task<ItemDto?> CreateItemAsync(CreateItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/item/create", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ItemDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<ItemDto>> GetRepliesAsync(int itemId, CancellationToken cancellationToken = default)
    {
        List<ItemDto>? result = await _httpClient.GetFromJsonAsync<List<ItemDto>>($"api/item/{itemId}/replies", cancellationToken);
        return result ?? [];
    }

    public async Task<ItemDto?> AddReplyAsync(int itemId, CreateReplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/item/{itemId}/replies", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ItemDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<ItemDto>> GetQuotesAsync(int itemId, CancellationToken cancellationToken = default)
    {
        List<ItemDto>? result = await _httpClient.GetFromJsonAsync<List<ItemDto>>($"api/item/{itemId}/quotes", cancellationToken);
        return result ?? [];
    }

    public async Task<bool> UpdateItemAsync(int itemId, UpdateItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/item/{itemId}", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteItemAsync(int itemId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/item/{itemId}", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> AddTagToItemAsync(int itemId, AddTagRelationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/item/{itemId}/tags", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateTagWeightAsync(int itemId, int relationId, int delta, CancellationToken cancellationToken = default)
    {
        var request = new UpdateTagWeightRequest(delta);
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/item/{itemId}/tags/{relationId}/weight", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveTagFromItemAsync(int itemId, int relationId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/item/{itemId}/tags/{relationId}", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ChangeItemTagAsync(int itemId, int relationId, ChangeItemTagRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/item/{itemId}/tags/{relationId}/change", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<UserGroupDto>> GetUserGroupsAsync(CancellationToken cancellationToken = default)
    {
        List<UserGroupDto>? result = await _httpClient.GetFromJsonAsync<List<UserGroupDto>>("api/item/user-groups", cancellationToken);
        return result ?? [];
    }

    public async Task<bool> VoteTagAsync(int tagId, bool isUpvote, CancellationToken cancellationToken = default)
    {
        var request = new TagVoteRequest(isUpvote);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/tag/{tagId}/vote", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<TagDto?> CreateTagAsync(CreateTagRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/tag/create", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TagDto>(cancellationToken);
    }

    public async Task<bool> UpdateTagAsync(int tagId, UpdateTagRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/tag/{tagId}", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteTagAsync(int tagId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/tag/{tagId}", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> AddParentTagAsync(int tagId, int parentTagId, CancellationToken cancellationToken = default)
    {
        var request = new AddParentTagRequest(parentTagId);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/tag/{tagId}/parents", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveParentTagAsync(int tagId, int relationId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/tag/{tagId}/parents/{relationId}", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<UserGroupDto?> CreateUserGroupAsync(CreateUserGroupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/usergroup", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<UserGroupDto>(cancellationToken);
    }

    public async Task<UserGroupDetailDto?> GetUserGroupDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<UserGroupDetailDto>($"api/usergroup/{id}", cancellationToken);
    }

    public async Task<bool> UpdateUserGroupAsync(int id, UpdateUserGroupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/usergroup/{id}", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteUserGroupAsync(int id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/usergroup/{id}", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> AddUserGroupMemberAsync(int id, AddGroupMemberRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/usergroup/{id}/members", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveUserGroupMemberAsync(int id, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/usergroup/{id}/members/{Uri.EscapeDataString(userId)}", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<UserGroupDto>> GetManagedUserGroupsAsync(CancellationToken cancellationToken = default)
    {
        List<UserGroupDto>? result = await _httpClient.GetFromJsonAsync<List<UserGroupDto>>("api/usergroup/managed", cancellationToken);
        return result ?? [];
    }

    public async Task<TagDiagramResponse?> GetTagDiagramDataAsync(string? itemIds = null, CancellationToken cancellationToken = default)
    {
        var url = "api/tag-diagram/data";
        if (!string.IsNullOrWhiteSpace(itemIds))
        {
            url += $"?itemIds={Uri.EscapeDataString(itemIds)}";
        }

        return await _httpClient.GetFromJsonAsync<TagDiagramResponse>(url, cancellationToken);
    }

    public async Task<IReadOnlyList<UserDto>> SearchUsersAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(query)
            ? "api/user/search"
            : $"api/user/search?q={Uri.EscapeDataString(query.Trim())}";

        List<UserDto>? result = await _httpClient.GetFromJsonAsync<List<UserDto>>(url, cancellationToken);
        return result ?? [];
    }

    public async Task<IReadOnlyList<TagTreeNodeDto>> GetTagTreeAsync(CancellationToken cancellationToken = default)
    {
        List<TagTreeNodeDto>? result = await _httpClient.GetFromJsonAsync<List<TagTreeNodeDto>>("api/tag/tree", cancellationToken);
        return result ?? [];
    }

    public async Task<IReadOnlyList<ItemDto>> GetQuotedByItemsAsync(int itemId, CancellationToken cancellationToken = default)
    {
        List<ItemDto>? result = await _httpClient.GetFromJsonAsync<List<ItemDto>>($"api/item/{itemId}/quoted-by", cancellationToken);
        return result ?? [];
    }

    public async Task<ImportCsvResponse?> ImportCsvAsync(ImportCsvRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/item/import", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ImportCsvResponse>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<TaggingRequestDto>> GetTaggingRequestsAsync(int itemId, CancellationToken cancellationToken = default)
    {
        List<TaggingRequestDto>? result = await _httpClient.GetFromJsonAsync<List<TaggingRequestDto>>($"api/item/{itemId}/tag-requests", cancellationToken);
        return result ?? [];
    }

    public async Task<bool> ApproveTaggingRequestAsync(int requestId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/item/tag-requests/{requestId}/approve", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RejectTaggingRequestAsync(int requestId, RejectTaggingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/item/tag-requests/{requestId}/reject", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<TagDetailDto?> GetTagDetailAsync(int tagId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<TagDetailDto>($"api/tag/{tagId}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<bool> ToggleTagFollowAsync(int tagId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/tag/{tagId}/follow", UriKind.Relative), null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        ToggleFollowResponse? result = await response.Content.ReadFromJsonAsync<ToggleFollowResponse>(cancellationToken: cancellationToken);
        return result?.IsFollowing ?? false;
    }

    public async Task<IReadOnlyList<NotificationItemDto>> GetNotificationsAsync(CancellationToken cancellationToken = default)
    {
        List<NotificationItemDto>? result = await _httpClient.GetFromJsonAsync<List<NotificationItemDto>>("api/notification", cancellationToken);
        return result ?? [];
    }

    public async Task<int> GetUnreadNotificationCountAsync(CancellationToken cancellationToken = default)
    {
        UnreadCountResponse? result = await _httpClient.GetFromJsonAsync<UnreadCountResponse>("api/notification/unread-count", cancellationToken);
        return result?.UnreadCount ?? 0;
    }

    public async Task<bool> MarkNotificationAsReadAsync(int sourceId, string sourceType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceType);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/notification/{sourceId}/read", new MarkAsReadRequest(sourceType), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> MarkAllNotificationsAsReadAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri("api/notification/read-all", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<TagDto>> GetTagListAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(query)
            ? "api/tag/list"
            : $"api/tag/list?q={Uri.EscapeDataString(query.Trim())}";

        List<TagDto>? result = await _httpClient.GetFromJsonAsync<List<TagDto>>(url, cancellationToken);
        return result ?? [];
    }

    public async Task<UserDetailDto?> GetUserDetailAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        try
        {
            return await _httpClient.GetFromJsonAsync<UserDetailDto>($"api/user/{Uri.EscapeDataString(userId)}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<bool> ToggleUserFollowAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/user/{Uri.EscapeDataString(userId)}/follow", UriKind.Relative), null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        ToggleFollowResponse? result = await response.Content.ReadFromJsonAsync<ToggleFollowResponse>(cancellationToken: cancellationToken);
        return result?.IsFollowing ?? false;
    }

    public async Task<BountyBoardResponse?> GetBountiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<BountyBoardResponse>("api/bounty", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<bool> CreateBountyAsync(CreateBountyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/bounty", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> FulfillBountyAsync(int bountyId, FulfillBountyRequest? request = null, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/bounty/{bountyId}/fulfill", request ?? new FulfillBountyRequest(), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<PublicOfferDto>> GetPublicOffersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            List<PublicOfferDto>? result = await _httpClient.GetFromJsonAsync<List<PublicOfferDto>>("api/public-offer", cancellationToken);
            return result ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<bool> CreatePublicOfferAsync(CreatePublicOfferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/public-offer", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeactivatePublicOfferAsync(int offerId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/public-offer/{offerId}", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> TriggerPublicOfferAsync(int offerId, TriggerPublicOfferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/public-offer/{offerId}/trigger", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<ContractPageResponse?> GetContractsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ContractPageResponse>("api/contract", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<bool> AcceptContractAsync(int contractId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/contract/{contractId}/accept", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RejectContractAsync(int contractId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/contract/{contractId}/reject", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelContractAsync(int contractId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/contract/{contractId}/cancel", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<RightAssetDto>?> GetAvailableRightAssetsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<RightAssetDto>>("api/contract/available-assets", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<ContractDto?> ProposeGratisContractAsync(ProposeGratisContractRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/contract/gratis", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ContractDto>(cancellationToken);
    }

    public async Task<ContractDto?> ProposeMutualContractAsync(ProposeMutualContractRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/contract/mutual", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ContractDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<TagRightAssetSummaryDto>> GetTopTagsWithRightAssetsAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        List<TagRightAssetSummaryDto>? result = await _httpClient.GetFromJsonAsync<List<TagRightAssetSummaryDto>>($"api/right-asset/top-tags?count={count}", cancellationToken);
        return result ?? [];
    }

    public async Task<RightAssetOverviewResponse?> GetRightAssetOverviewAsync(int tagId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<RightAssetOverviewResponse>($"api/right-asset/overview/{tagId}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<JpycNetworkDto>> GetJpycNetworksAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            List<JpycNetworkDto>? result = await _httpClient.GetFromJsonAsync<List<JpycNetworkDto>>("api/right-asset/networks", cancellationToken);
            return result ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<DepositWalletDto?> GetUserDepositWalletAsync(string networkName = "polygon-amoy", CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<DepositWalletDto>($"api/right-asset/deposit-wallet?networkName={Uri.EscapeDataString(networkName)}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<string?> SimulateDepositAsync(SimulateDepositRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/right-asset/simulate-deposit", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<PurchaseRightAssetResultDto?> PurchaseRightAssetAsync(PurchaseRightAssetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/right-asset/purchase", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<PurchaseRightAssetResultDto>(cancellationToken);
    }

    public async Task<ContentReportDto?> CreateReportAsync(CreateReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/report", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ContentReportDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<ContentReportDto>> GetReportsAsync(string? status = null, string? targetType = null, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            queryParams.Add($"status={Uri.EscapeDataString(status)}");
        }
        if (!string.IsNullOrWhiteSpace(targetType))
        {
            queryParams.Add($"targetType={Uri.EscapeDataString(targetType)}");
        }

        string url = queryParams.Count > 0 ? $"api/report?{string.Join("&", queryParams)}" : "api/report";
        List<ContentReportDto>? result = await _httpClient.GetFromJsonAsync<List<ContentReportDto>>(url, cancellationToken);
        return result ?? [];
    }

    public async Task<ContentReportDto?> GetReportByIdAsync(int reportId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ContentReportDto>($"api/report/{reportId}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<bool> UpdateReportStatusAsync(int reportId, UpdateReportStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/report/{reportId}/status", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ResolveReportAsync(int reportId, ResolveReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/report/{reportId}/resolve", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<ManagedUserDto>> GetManagementUsersAsync(CancellationToken cancellationToken = default)
    {
        List<ManagedUserDto>? result = await _httpClient.GetFromJsonAsync<List<ManagedUserDto>>("api/user/management", cancellationToken);
        return result ?? [];
    }

    public async Task<bool> SetUserAdminAsync(string userId, SetUserAdminRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/user/{userId}/admin", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SetUserBanAsync(string userId, SetUserBanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/user/{userId}/ban", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync(CancellationToken cancellationToken = default)
    {
        List<InvitationDto>? result = await _httpClient.GetFromJsonAsync<List<InvitationDto>>("api/admin/invitation", cancellationToken);
        return result ?? [];
    }

    public async Task<InvitationDto?> CreateInvitationAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/admin/invitation", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<InvitationDto>(cancellationToken);
    }

    public async Task<bool> DeleteInvitationAsync(int invitationId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/admin/invitation/{invitationId}", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<int> GetTagLockLevelAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<int>("api/admin/tag-lock/level", cancellationToken);
    }

    public async Task<bool> SetTagLockLevelAsync(int level, CancellationToken cancellationToken = default)
    {
        var request = new SetHierarchyLockLevelRequest(level);
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync("api/admin/tag-lock/level", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<TagLockItemDto>> GetTagLockItemsAsync(CancellationToken cancellationToken = default)
    {
        List<TagLockItemDto>? result = await _httpClient.GetFromJsonAsync<List<TagLockItemDto>>("api/admin/tag-lock/tags", cancellationToken);
        return result ?? [];
    }

    public async Task<bool> ToggleTagLockAsync(int tagId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/admin/tag-lock/{tagId}/toggle", UriKind.Relative), null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        return await response.Content.ReadFromJsonAsync<bool>(cancellationToken);
    }

    public async Task<ImportTagCsvResponse?> ImportTagCsvAsync(ImportTagCsvRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/tag/import/csv", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ImportTagCsvResponse>(cancellationToken);
    }

    public async Task<ImportTaggingResponse?> ImportTaggingJsonAsync(ImportTaggingJsonRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/tag/import/tagging", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ImportTaggingResponse>(cancellationToken);
    }

    public async Task<IReadOnlyList<UserConfirmationDto>?> GetConfirmedAccountsAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<List<UserConfirmationDto>>("api/user/confirmed-accounts", cancellationToken);
    }

    public async Task<bool> ToggleUserConfirmationAsync(string userId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/user/{Uri.EscapeDataString(userId)}/toggle-confirmation", UriKind.Relative), null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        return await response.Content.ReadFromJsonAsync<bool>(cancellationToken);
    }

    public async Task<TagNameProposalDto?> ProposeTagNameAsync(ProposeTagNameRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/tag-proposal/name", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TagNameProposalDto>(cancellationToken);
    }

    public async Task<TagContentProposalDto?> ProposeTagContentAsync(ProposeTagContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/tag-proposal/content", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TagContentProposalDto>(cancellationToken);
    }

    public async Task<TagPendingProposalsDto?> GetPendingProposalsForTagAsync(int tagId, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<TagPendingProposalsDto>($"api/tag-proposal/tag/{tagId}/pending", cancellationToken);
    }

    public async Task<bool> ApproveTagNameProposalAsync(int proposalId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/tag-proposal/name/{proposalId}/approve", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RejectTagNameProposalAsync(int proposalId, RejectProposalRequest? request = null, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/tag-proposal/name/{proposalId}/reject", request ?? new RejectProposalRequest(), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ApproveTagContentProposalAsync(int proposalId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/tag-proposal/content/{proposalId}/approve", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RejectTagContentProposalAsync(int proposalId, RejectProposalRequest? request = null, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/tag-proposal/content/{proposalId}/reject", request ?? new RejectProposalRequest(), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelTagNameProposalAsync(int proposalId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/tag-proposal/name/{proposalId}/cancel", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelTagContentProposalAsync(int proposalId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/tag-proposal/content/{proposalId}/cancel", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<SuggestedTagDto>?> GetTagSuggestionsAsync(TagSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/tag/suggestions", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<SuggestedTagDto>>(cancellationToken);
    }

    public async Task<ItemDto?> SplitItemDirectlyAsync(DirectSplitItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/item-split/direct", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ItemDto>(cancellationToken);
    }

    public async Task<ItemSplitRequestDto?> RequestItemSplitAsync(RequestItemSplitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/item-split/request", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ItemSplitRequestDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<ItemSplitRequestDto>?> GetPendingSplitRequestsAsync(int itemId, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<List<ItemSplitRequestDto>>($"api/item-split/item/{itemId}/pending", cancellationToken);
    }

    public async Task<ItemDto?> ApproveItemSplitAsync(int requestId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/item-split/{requestId}/approve", UriKind.Relative), null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ItemDto>(cancellationToken);
    }

    public async Task<bool> RejectItemSplitAsync(int requestId, RejectSplitRequest? request = null, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/item-split/{requestId}/reject", request ?? new RejectSplitRequest(), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelItemSplitAsync(int requestId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri($"api/item-split/{requestId}/cancel", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<TagWeightLedgerDto>> GetTagWeightLedgersAsync(int tagId, CancellationToken cancellationToken = default)
    {
        List<TagWeightLedgerDto>? result = await _httpClient.GetFromJsonAsync<List<TagWeightLedgerDto>>($"api/tag/{tagId}/ledger", cancellationToken);
        return result ?? [];
    }

    public async Task<IReadOnlyList<TagWeightLedgerDto>> GetItemTagWeightLedgersAsync(int itemId, CancellationToken cancellationToken = default)
    {
        List<TagWeightLedgerDto>? result = await _httpClient.GetFromJsonAsync<List<TagWeightLedgerDto>>($"api/item/{itemId}/tag-ledger", cancellationToken);
        return result ?? [];
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Relative and absolute URLs are accepted as strings")]
    public async Task<LinkPreviewDto?> GetLinkPreviewAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/link/preview", new LinkPreviewRequest(url), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<LinkPreviewDto>(cancellationToken);
    }

    public async Task<InternalLinkConversionResultDto?> DetectLinkCandidatesAsync(DetectLinkCandidatesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/link/convert-candidates", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<InternalLinkConversionResultDto>(cancellationToken);
    }

    public async Task<string?> ApplyLinkReplacementsAsync(ApplyLinkReplacementsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/link/apply-replacements", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        ApplyLinkReplacementsResponse? result = await response.Content.ReadFromJsonAsync<ApplyLinkReplacementsResponse>(cancellationToken);
        return result?.ConvertedContent;
    }

    public async Task<IReadOnlyList<SimilarTagCandidateDto>> FindSimilarTagsAsync(string name, float minSimilarity = 0.5f, int maxResults = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var url = $"api/tag/similar?name={Uri.EscapeDataString(name)}&minSimilarity={minSimilarity.ToString(System.Globalization.CultureInfo.InvariantCulture)}&maxResults={maxResults}";
        List<SimilarTagCandidateDto>? result = await _httpClient.GetFromJsonAsync<List<SimilarTagCandidateDto>>(url, cancellationToken);
        return result ?? [];
    }

    public async Task<TagDto?> SuggestParentTagAsync(SuggestParentTagRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/tag/suggest-parent", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TagDto>(cancellationToken);
    }

    public async Task<TagDiagramEdgeDto?> CreateTagEdgeAsync(CreateTagEdgeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/tag-diagram/edges", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TagDiagramEdgeDto>(cancellationToken);
    }

    public async Task<bool> DeleteTagEdgeAsync(int edgeId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/tag-diagram/edges/{edgeId}", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<TagEdgeAttachmentDto>> GetAttachmentsForEdgeAsync(int edgeId, CancellationToken cancellationToken = default)
    {
        List<TagEdgeAttachmentDto>? result = await _httpClient.GetFromJsonAsync<List<TagEdgeAttachmentDto>>($"api/tag-diagram/edges/{edgeId}/attachments", cancellationToken);
        return result ?? [];
    }

    public async Task<TagEdgeAttachmentDto?> AttachTagToEdgeAsync(int edgeId, AttachTagToEdgeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/tag-diagram/edges/{edgeId}/attachments", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TagEdgeAttachmentDto>(cancellationToken);
    }

    public async Task<bool> DetachTagFromEdgeAsync(int attachmentId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/tag-diagram/attachments/{attachmentId}", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode;
    }
}