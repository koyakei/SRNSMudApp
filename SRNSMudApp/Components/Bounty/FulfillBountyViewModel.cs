#region

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Components.Bounty;

/// <summary>
///     タグ付け依頼（バウンティ）承諾・完了ダイアログ用 ViewModel。
///     依頼内容および報酬の確認、消費する RightAsset の選択、オーナー権限の判定、契約承認処理をカプセル化する。
/// </summary>
public sealed class FulfillBountyViewModel
{
    private readonly IContractLookupDataProvider _contractData;
    private readonly ITaggingContractService _contractService;

    public FulfillBountyViewModel(
        IContractLookupDataProvider contractData,
        ITaggingContractService contractService)
    {
        _contractData = contractData ?? throw new ArgumentNullException(nameof(contractData));
        _contractService = contractService ?? throw new ArgumentNullException(nameof(contractService));
    }

    public TaggingRequestEntity? Bounty { get; private set; }
    public string CurrentUserId { get; private set; } = string.Empty;
    public bool IsLoading { get; private set; } = true;
    public bool IsSubmitting { get; private set; }

    public bool CanFulfillAsOwner { get; private set; }
    public IReadOnlyList<RightAsset> MyValidAssets { get; private set; } = [];
    public RightAsset? SelectedAsset { get; set; }
    public RightAsset? OfferedRewardAsset { get; private set; }

    public bool CanSubmit =>
        !IsSubmitting &&
        !IsLoading &&
        Bounty != null &&
        !string.IsNullOrWhiteSpace(CurrentUserId) &&
        (CanFulfillAsOwner || (MyValidAssets.Count > 0 && SelectedAsset != null));

    /// <summary>
    ///     対象バウンティおよびユーザーIDを設定して初期化し、報酬アセットおよび消費可能なアセット情報を取得する。
    /// </summary>
    public async Task InitializeAsync(
        TaggingRequestEntity bounty,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        Bounty = bounty ?? throw new ArgumentNullException(nameof(bounty));
        CurrentUserId = currentUserId ?? string.Empty;
        SelectedAsset = null;
        OfferedRewardAsset = null;
        MyValidAssets = [];
        IsSubmitting = false;

        await LoadDataAsync(cancellationToken);
    }

    /// <summary>
    ///     報酬アセットおよび消費可能な RightAsset 一覧を読み込む。
    /// </summary>
    public async Task LoadDataAsync(CancellationToken cancellationToken = default)
    {
        if (Bounty == null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            if (Bounty.Payload is BountyPayload { OfferedRewardAssetId: not 0 } bp)
            {
                OfferedRewardAsset = await _contractData.GetRightAssetByIdAsync(bp.OfferedRewardAssetId);
            }

            CanFulfillAsOwner = Bounty.RequestedTag != null && Bounty.RequestedTag.OwnerId == CurrentUserId;

            if (!CanFulfillAsOwner && !string.IsNullOrWhiteSpace(CurrentUserId))
            {
                var assets = await _contractData.GetValidRightAssetsAsync(CurrentUserId, Bounty.RequestedTagId);
                MyValidAssets = assets ?? [];

                if (MyValidAssets.Count == 1)
                {
                    SelectedAsset = MyValidAssets[0];
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    ///     依頼を承諾して契約を完了する。
    /// </summary>
    public async Task<Result<string>> SubmitAsync()
    {
        if (IsSubmitting)
        {
            return new Failure(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage);
        }

        if (Bounty == null)
        {
            return new Failure("依頼が指定されていません。");
        }

        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return new Failure("ユーザー情報が取得できませんでした。");
        }

        if (!CanFulfillAsOwner && SelectedAsset == null)
        {
            return new Failure("消費するアセットを選択してください。");
        }

        IsSubmitting = true;
        try
        {
            int? assetIdToConsume = CanFulfillAsOwner ? null : SelectedAsset?.Id;
            return await _contractService.AcceptContractAsync(Bounty.Id, CurrentUserId, assetIdToConsume);
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}