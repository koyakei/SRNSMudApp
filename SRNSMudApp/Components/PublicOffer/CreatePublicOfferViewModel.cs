using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;

namespace SRNSMudApp.Components.PublicOffer;

/// <summary>
///     CreatePublicOfferDialog の状態管理、タグ検索、およびオファー作成ロジックを担当する ViewModel。
///     UI（Blazor / bUnit）から切り離し、純粋な xUnit + Moq 単体テストを可能にする。
/// </summary>
public class CreatePublicOfferViewModel
{
    private readonly IContractLookupDataProvider _contractData;
    private readonly ICommandHandler<CreatePublicOfferCommand, Result<bool>> _createPublicOfferHandler;

    public CreatePublicOfferViewModel(
        IContractLookupDataProvider contractData,
        ICommandHandler<CreatePublicOfferCommand, Result<bool>> createPublicOfferHandler)
    {
        _contractData = contractData;
        _createPublicOfferHandler = createPublicOfferHandler;
    }

    public string CurrentUserId { get; set; } = string.Empty;
    public Data.Tag? SelectedTag { get; set; }
    public int RequiredAssetAmount { get; set; }

    public bool CanSubmit => SelectedTag != null && RequiredAssetAmount >= 0 && !string.IsNullOrEmpty(CurrentUserId);

    /// <summary>
    ///     提供するタグを非同期検索する（自分が作成したタグのみ）。
    /// </summary>
    public async Task<IEnumerable<Data.Tag>> SearchMyTagsAsync(string? value, CancellationToken cancellationToken = default)
    {
        return await _contractData.SearchMyTagsAsync(CurrentUserId, value, cancellationToken);
    }

    /// <summary>
    ///     新しい公開オファーを作成する。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "例外発生時にも画面崩壊を防ぎ、UI にエラー結果を返却するため捕捉する")]
    public async Task<Result<bool>> SubmitAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedTag == null)
        {
            return Result.Fail<bool>("タグを選択してください。");
        }

        if (string.IsNullOrEmpty(CurrentUserId))
        {
            return Result.Fail<bool>("ログインが必要です。");
        }

        if (RequiredAssetAmount < 0)
        {
            return Result.Fail<bool>("要求するアセット量は0以上を指定してください。");
        }

        // 所有権チェック: 自身が所有するタグのみオファー作成可能 (Issue 5-4)
        if (SelectedTag.OwnerId != CurrentUserId)
        {
            return Result.Fail<bool>("自身が所有していないタグのオファーは作成できません。");
        }

        try
        {
            var offer = new PublicTradeOffer
            {
                OwnerId = CurrentUserId,
                OfferedTagId = SelectedTag.Id,
                RequiredAssetAmount = RequiredAssetAmount,
                IsActive = true
            };

            return await _createPublicOfferHandler.HandleAsync(new CreatePublicOfferCommand(offer), cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Fail<bool>($"オファーの作成に失敗しました: {ex.Message}");
        }
    }
}