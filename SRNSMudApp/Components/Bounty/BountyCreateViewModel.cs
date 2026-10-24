#region

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;

using ItemEntity = SRNSMudApp.Data.Item;
using TagEntity = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Components.Bounty;

/// <summary>
///     タグ付け依頼（バウンティ）作成ダイアログ用 ViewModel。
///     アイテム・タグ・報酬アセットの選択状態管理、検索処理、およびバウンティ作成コマンドの実行をカプセル化する。
/// </summary>
public sealed class BountyCreateViewModel
{
    private readonly IContractLookupDataProvider _contractData;
    private readonly ICommandHandler<CreateBountyCommand, Result<bool>> _createBountyHandler;

    public BountyCreateViewModel(
        IContractLookupDataProvider contractData,
        ICommandHandler<CreateBountyCommand, Result<bool>> createBountyHandler)
    {
        _contractData = contractData ?? throw new ArgumentNullException(nameof(contractData));
        _createBountyHandler = createBountyHandler ?? throw new ArgumentNullException(nameof(createBountyHandler));
    }

    public string CurrentUserId { get; private set; } = string.Empty;
    public ItemEntity? TargetItem { get; set; }
    public TagEntity? SelectedTag { get; set; }
    public RightAsset? SelectedRewardAsset { get; set; }
    public IReadOnlyList<RightAsset> MyAssets { get; private set; } = [];

    public bool IsLoadingAssets { get; private set; }
    public bool IsSubmitting { get; private set; }

    public bool CanSubmit =>
        !IsSubmitting &&
        !IsLoadingAssets &&
        TargetItem != null &&
        SelectedTag != null &&
        !string.IsNullOrWhiteSpace(CurrentUserId);

    /// <summary>
    ///     対象アイテムおよびユーザーIDを設定して初期化し、提供可能な報酬アセット一覧を読み込む。
    /// </summary>
    public async Task InitializeAsync(
        ItemEntity? targetItem,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        CurrentUserId = currentUserId ?? string.Empty;
        TargetItem = targetItem;
        SelectedTag = null;
        SelectedRewardAsset = null;
        IsSubmitting = false;

        await LoadMyAssetsAsync(cancellationToken);
    }

    /// <summary>
    ///     ユーザーが保有する提供可能なアセット一覧を取得する。
    /// </summary>
    public async Task LoadMyAssetsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            MyAssets = [];
            return;
        }

        IsLoadingAssets = true;
        try
        {
            var assets = await _contractData.GetAvailableRightAssetsAsync(CurrentUserId);
            MyAssets = assets ?? [];
        }
        finally
        {
            IsLoadingAssets = false;
        }
    }

    /// <summary>
    ///     対象アイテムを検索する。
    /// </summary>
    public async Task<IEnumerable<ItemEntity>> SearchItemsAsync(string query, CancellationToken cancellationToken = default)
    {
        return await _contractData.SearchItemsAsync(query, cancellationToken);
    }

    /// <summary>
    ///     希望タグを検索する。
    /// </summary>
    public async Task<IEnumerable<TagEntity>> SearchTagsAsync(string query, CancellationToken cancellationToken = default)
    {
        return await _contractData.SearchTagsByNameAsync(query, cancellationToken);
    }

    /// <summary>
    ///     バウンティ作成コマンドを実行する。
    /// </summary>
    public async Task<Result<bool>> SubmitAsync(CancellationToken cancellationToken = default)
    {
        if (IsSubmitting)
        {
            return new Failure(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage);
        }

        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return new Failure("ユーザー情報が取得できませんでした。");
        }

        if (TargetItem == null)
        {
            return new Failure("アイテムを選択してください。");
        }

        if (SelectedTag == null)
        {
            return new Failure("タグを選択してください。");
        }

        IsSubmitting = true;
        try
        {
            var bounty = new TaggingRequestEntity
            {
                ContractType = "Bounty",
                OwnerId = CurrentUserId,
                RequesterUserId = CurrentUserId,
                TagOwnerUserId = SelectedTag.OwnerId,
                TargetItemId = TargetItem.Id,
                RequestedTagId = SelectedTag.Id,
                Payload = new BountyPayload(SelectedRewardAsset?.Id ?? 0)
            };

            return await _createBountyHandler.HandleAsync(new CreateBountyCommand(bounty), cancellationToken);
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}