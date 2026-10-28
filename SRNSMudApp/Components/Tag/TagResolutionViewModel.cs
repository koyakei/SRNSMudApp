using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Models;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     TagResolutionDialog の状態管理、候補検索・補完、バリデーション、および決定モデル生成を担当する ViewModel。
///     UI（Blazor / bUnit）から切り離し、純粋な xUnit + Moq 単体テストを可能にする。
///     プライマリコンストラクタにより依存データプロバイダーを注入する。
/// </summary>
public class TagResolutionViewModel(ITaggingImportDataProvider dataProvider)
{

    public string TagName { get; set; } = string.Empty;
    public string TagKind { get; set; } = "UserCustomTag";
    public TagEntity? SuggestedParentTag { get; set; }

    public TagResolutionAction Action { get; set; } = TagResolutionAction.UseExisting;
    public bool HasCandidate { get; private set; } = true;
    public bool UseSuggestedParent { get; set; } = true;

    public TagEntity? SelectedExistingTag { get; set; }
    public TagEntity? SelectedCustomParentTag { get; set; }

    public IReadOnlyList<TagEntity> InitialSuggestions { get; private set; } = [];
    public IReadOnlyList<TagEntity> ParentSuggestions { get; private set; } = [];

    public bool CanSubmit
    {
        get
        {
            if (Action == TagResolutionAction.Skip) return true;
            if (Action == TagResolutionAction.UseExisting) return SelectedExistingTag != null;
            if (Action == TagResolutionAction.CreateNew)
            {
                return UseSuggestedParent && SuggestedParentTag != null ? true : SelectedCustomParentTag != null;
            }
            return false;
        }
    }

    /// <summary>
    ///     指定されたタグ名とサジェスト親タグに基づいて候補を検索・補填し初期化する。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "検索例外時にも画面をクラッシュさせず初期化を完了するため捕捉する")]
    public async Task InitializeAsync(
        string tagName,
        TagEntity? suggestedParentTag,
        CancellationToken cancellationToken = default)
    {
        TagName = tagName;
        SuggestedParentTag = suggestedParentTag;
        UseSuggestedParent = suggestedParentTag != null;

        var suggestions = new List<TagEntity>();
        if (!string.IsNullOrWhiteSpace(TagName))
        {
            try
            {
                var results = await dataProvider.SearchTagsAsync(TagName, cancellationToken);
                if (results != null)
                {
                    suggestions.AddRange(results);
                }
            }
            catch
            {
                // エラー時は空候補として継続
            }
        }

        HasCandidate = suggestions.Count > 0;
        if (HasCandidate)
        {
            InitialSuggestions = suggestions.Take(8).ToList();
            if (SelectedExistingTag == null && InitialSuggestions.Count > 0)
            {
                SelectedExistingTag = InitialSuggestions[0];
            }
        }
        else
        {
            InitialSuggestions = [];
            SelectedExistingTag = null;
        }

        var parentCandidates = new List<TagEntity>();
        if (!string.IsNullOrWhiteSpace(TagName))
        {
            try
            {
                var parentResults = await dataProvider.SearchParentCandidateTagsAsync(TagName, cancellationToken);
                if (parentResults != null)
                {
                    parentCandidates.AddRange(parentResults);
                }
            }
            catch
            {
                // エラー時は空候補として継続
            }
        }

        if (parentCandidates.Count < 5)
        {
            try
            {
                var defaultParents = await dataProvider.SearchParentCandidateTagsAsync(null, cancellationToken);
                if (defaultParents != null)
                {
                    // 重複しないデフォルト親候補を LINQ ExceptBy と Take で取得し AddRange で追加
                    parentCandidates.AddRange(defaultParents
                        .ExceptBy(parentCandidates.Select(s => s.Id), p => p.Id)
                        .Take(8 - parentCandidates.Count));
                }
            }
            catch
            {
                // エラー時は現在の候補で継続
            }
        }

        ParentSuggestions = parentCandidates.Take(8).ToList();
        if (SelectedCustomParentTag == null && ParentSuggestions.Count > 0)
        {
            SelectedCustomParentTag = ParentSuggestions[0];
        }
    }

    /// <summary>
    ///     サジェスト親タグの使用フラグを切り替える。
    /// </summary>
    public void SetUseSuggestedParent(bool useSuggested)
    {
        UseSuggestedParent = useSuggested;
        if (!useSuggested && SelectedCustomParentTag == null && ParentSuggestions.Count > 0)
        {
            SelectedCustomParentTag = ParentSuggestions[0];
        }
    }

    /// <summary>
    ///     既存タグを検索する（検索文字列が空の場合は初期候補リストを返却）。
    /// </summary>
    public async Task<IEnumerable<TagEntity>> SearchExistingTagsAsync(string? value, CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(value) && InitialSuggestions.Count > 0
            ? InitialSuggestions
            : await dataProvider.SearchTagsAsync(value, cancellationToken);
    }

    /// <summary>
    ///     親タグ候補を検索する（検索文字列が空の場合は親候補リストを返却）。
    /// </summary>
    public async Task<IEnumerable<TagEntity>> SearchParentCandidateTagsAsync(string? value, CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(value) && ParentSuggestions.Count > 0
            ? ParentSuggestions
            : await dataProvider.SearchParentCandidateTagsAsync(value, cancellationToken);
    }

    /// <summary>
    ///     現在の状態から解決決定モデル（TagResolutionDecision）を生成する。
    /// </summary>
    public TagResolutionDecision CreateDecision()
    {
        int? parentId = null;
        if (Action == TagResolutionAction.CreateNew)
        {
            parentId = UseSuggestedParent ? SuggestedParentTag?.Id : SelectedCustomParentTag?.Id;
        }

        return new TagResolutionDecision(
            Action,
            Action == TagResolutionAction.UseExisting ? SelectedExistingTag?.Id : null,
            parentId,
            TagName);
    }

    /// <summary>
    ///     スキップ決定モデル（TagResolutionDecision）を生成する。
    /// </summary>
    public TagResolutionDecision CreateSkipDecision()
    {
        return new TagResolutionDecision(
            TagResolutionAction.Skip,
            null,
            null,
            TagName);
    }
}