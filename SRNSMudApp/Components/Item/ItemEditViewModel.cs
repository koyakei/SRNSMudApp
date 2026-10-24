#region

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Components.Item;

/// <summary>
///     アイテム編集ダイアログ用 ViewModel。
///     編集テキストの状態管理、文字数制約、内部リンク Pill HTML 生成、メンション検索、リンクプレビュー取得、更新処理を担当する。
/// </summary>
public sealed class ItemEditViewModel
{
    public const int MaxContentLength = 1000;

    private readonly ItemCardActionViewModel _actionViewModel;
    private readonly ITagSearchQueryService _tagSearchQueryService;
    private readonly IUserDataProvider _userDataProvider;
    private readonly ILinkPreviewService _linkPreviewService;
    private readonly Client.Services.IDoubleSubmissionGuard _submissionGuard;

    public ItemEditViewModel(
        ItemCardActionViewModel actionViewModel,
        ITagSearchQueryService tagSearchQueryService,
        IUserDataProvider userDataProvider,
        ILinkPreviewService linkPreviewService,
        Client.Services.IDoubleSubmissionGuard? submissionGuard = null)
    {
        _actionViewModel = actionViewModel ?? throw new ArgumentNullException(nameof(actionViewModel));
        _tagSearchQueryService = tagSearchQueryService ?? throw new ArgumentNullException(nameof(tagSearchQueryService));
        _userDataProvider = userDataProvider ?? throw new ArgumentNullException(nameof(userDataProvider));
        _linkPreviewService = linkPreviewService ?? throw new ArgumentNullException(nameof(linkPreviewService));
        _submissionGuard = submissionGuard ?? new Client.Services.DoubleSubmissionGuard();
    }

    public Data.Item? Item { get; private set; }
    public string EditContent { get; set; } = string.Empty;
    public bool IsSubmitting => _submissionGuard.IsSubmitting;

    public int CharacterCount => EditContent?.Length ?? 0;
    public bool IsOverCharacterLimit => CharacterCount > MaxContentLength;

    public IReadOnlyList<string> ExternalUrls => GetExternalUrls(EditContent);

    /// <summary>
    ///     対象アイテムを設定して初期化する。
    /// </summary>
    public void Initialize(Data.Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        EditContent = item.Content ?? string.Empty;
    }

    /// <summary>
    ///     指定された URL のリンクプレビューを取得する。
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "Blazor コンポーネントおよび ILinkPreviewService の仕様に合わせて string を受け取るため")]
    public Task<LinkPreviewData> GetPreviewAsync(string url) => _linkPreviewService.GetPreviewAsync(url);

    /// <summary>
    ///     アイテムの更新を保存する。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch generic exception types",
        Justification = "Blazor UI にエラーメッセージを表示するため汎用例外を捕捉する")]
    public async Task<(bool Success, string? ErrorMessage)> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (Item == null)
        {
            return (false, "対象アイテムが設定されていません。");
        }

        var operationKey = $"edit:{Item.Id}:{EditContent}";
        if (!_submissionGuard.TryBeginSubmit(operationKey))
        {
            return (false, Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage);
        }

        try
        {
            var result = await _actionViewModel.UpdateItemContentAsync(Item.Id, EditContent).ConfigureAwait(false);
            if (result.Success)
            {
                _submissionGuard.Complete(operationKey);
            }
            else
            {
                _submissionGuard.Reset();
            }

            return result;
        }
        catch (Exception ex)
        {
            _submissionGuard.Reset();
            return (false, ex.Message);
        }
    }

    /// <summary>
    ///     タグ補完候補を検索する。
    /// </summary>
    public async Task<IEnumerable<MentionItem>> SearchTagsAsync(string query, CancellationToken cancellationToken = default)
    {
        var tags = await _tagSearchQueryService.SearchTagsWithFallbackAsync(query, cancellationToken).ConfigureAwait(false);
        return tags.Select(t => new MentionItem("#" + t.Name, $"/TagDetail/{t.Id}"));
    }

    /// <summary>
    ///     ユーザー補完候補を検索する。
    /// </summary>
    public async Task<IEnumerable<MentionItem>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
    {
        var users = await _userDataProvider.SearchUsersAsync(query, cancellationToken).ConfigureAwait(false);
        return users.Select(u => new MentionItem("@" + u.UserName, $"/User/UserDetail/{u.Id}"));
    }

    /// <summary>
    ///     テキスト内の内部リンク（/TagDetail/ や /User/UserDetail/）を contenteditable 用の pill HTML に変換する。
    /// </summary>
    public static string ParsePillsToHtml(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var segments = ItemCardViewModel.GetContentSegments(line);
            var sb = new StringBuilder();
            foreach (var segment in segments)
            {
                if (segment.IsUrl && segment.Text.StartsWith('/', StringComparison.Ordinal))
                {
                    var displayText = segment.Text;
                    if (segment.Text.StartsWith("/TagDetail/", StringComparison.Ordinal))
                    {
                        displayText = "#タグ";
                    }
                    else if (segment.Text.StartsWith("/User/UserDetail/", StringComparison.Ordinal))
                    {
                        displayText = "@ユーザー";
                    }

                    sb.Append(CultureInfo.InvariantCulture, $"<span class=\"internal-link-preview-pill\" data-testid=\"internal-link-preview-pill\" data-url=\"{segment.Text}\" contenteditable=\"false\">{displayText}</span>&#8203;&nbsp;");
                }
                else
                {
                    sb.Append(WebUtility.HtmlEncode(segment.Text).Replace(" ", "&nbsp;", StringComparison.Ordinal));
                }
            }

            lines[i] = sb.ToString();
        }

        return string.Join("<br>", lines);
    }

    /// <summary>
    ///     テキストから外部URL（内部リンク以外）を抽出する。
    /// </summary>
    public static IReadOnlyList<string> GetExternalUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var allUrls = ItemCardViewModel.ExtractUrls(text);
        return allUrls.Where(u => !u.StartsWith('/', StringComparison.Ordinal)).ToList();
    }
}