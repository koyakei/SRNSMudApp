using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Item;

/// <summary>
///     QuoteItemDialog の状態管理、バリデーション、プレビュー取得、および引用投稿ロジックを担当する ViewModel。
///     UI（Blazor / bUnit）から切り離し、純粋な xUnit + Moq 単体テストを可能にする。
/// </summary>
public class QuoteItemViewModel
{
    private readonly IItemQuoteService _itemQuoteService;
    private readonly ILinkPreviewService _linkPreviewService;
    private readonly Client.Services.IDoubleSubmissionGuard _submissionGuard;

    public QuoteItemViewModel(
        IItemQuoteService itemQuoteService,
        ILinkPreviewService linkPreviewService,
        Client.Services.IDoubleSubmissionGuard? submissionGuard = null)
    {
        _itemQuoteService = itemQuoteService ?? throw new ArgumentNullException(nameof(itemQuoteService));
        _linkPreviewService = linkPreviewService ?? throw new ArgumentNullException(nameof(linkPreviewService));
        _submissionGuard = submissionGuard ?? new Client.Services.DoubleSubmissionGuard();
    }

    public Data.Item? QuotedItem { get; set; }
    public string CurrentUserId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsSubmitting { get; private set; }

    public bool CanSubmit => !string.IsNullOrWhiteSpace(Content) && !IsSubmitting;

    public IReadOnlyList<string> ExtractedUrls => UI.ItemCardViewModel.ExtractUrls(Content);

    /// <summary>
    ///     指定された URL のリンクプレビューを取得する。
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "Blazor コンポーネントおよび ILinkPreviewService の仕様に合わせて string を受け取るため")]
    public Task<LinkPreviewData> GetPreviewAsync(string url) => _linkPreviewService.GetPreviewAsync(url);

    /// <summary>
    ///     引用投稿を作成する。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "例外発生時にも画面崩壊を防ぎ、UI にエラー結果を返却するため捕捉する")]
    public async Task<Result<Data.Item>> SubmitAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Content))
        {
            return Result.Fail<Data.Item>("本文を入力してください。");
        }

        if (string.IsNullOrEmpty(CurrentUserId))
        {
            return Result.Fail<Data.Item>("ログインが必要です。");
        }

        if (QuotedItem == null)
        {
            return Result.Fail<Data.Item>("引用元のアイテムが指定されていません。");
        }

        var operationKey = $"quote:{CurrentUserId}:{QuotedItem.Id}:{Content.Trim()}";
        if (IsSubmitting || !_submissionGuard.TryBeginSubmit(operationKey))
        {
            return Result.Fail<Data.Item>(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage);
        }

        IsSubmitting = true;
        try
        {
            var created = await _itemQuoteService.CreateQuoteItemAsync(
                QuotedItem.Id, Content, CurrentUserId, cancellationToken: cancellationToken);

            if (created is null)
            {
                _submissionGuard.Reset();
                return Result.Fail<Data.Item>("引用元のアイテムが見つかりませんでした。");
            }

            if (created.IsDuplicate)
            {
                _submissionGuard.Complete(operationKey);
                return Result.Fail<Data.Item>(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage);
            }

            _submissionGuard.Complete(operationKey);
            return Result.Ok(created);
        }
        catch (Exception ex)
        {
            _submissionGuard.Reset();
            return Result.Fail<Data.Item>($"エラーが発生しました: {ex.Message}");
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}