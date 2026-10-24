using System.Diagnostics.CodeAnalysis;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     タグ追加・作成ダイアログの ViewModel。
///     既存タグの検索、親タグ候補の絞り込み、子タグ新規作成の検証および登録、類似タグ候補の算出を担当する。
/// </summary>
public class TagAddViewModel
{
    private readonly ITagSearchQueryService _tagSearchQueryService;
    private readonly ITagCommandService _tagCommandService;
    private readonly ITagLockService _tagLockService;
    private readonly ITagSimilarityService _tagSimilarityService;
    private readonly ISnackbar _snackbar;
    private readonly IUserGroupDataProvider? _userGroupData;

    public TagAddViewModel(
        ITagSearchQueryService tagSearchQueryService,
        ITagCommandService tagCommandService,
        ITagLockService tagLockService,
        ITagSimilarityService tagSimilarityService,
        ISnackbar snackbar,
        IUserGroupDataProvider? userGroupData = null)
    {
        _tagSearchQueryService = tagSearchQueryService;
        _tagCommandService = tagCommandService;
        _tagLockService = tagLockService;
        _tagSimilarityService = tagSimilarityService;
        _snackbar = snackbar;
        _userGroupData = userGroupData;
    }

    public IReadOnlyList<Data.Tag> AllTags { get; private set; } = [];
    public string SearchText { get; set; } = string.Empty;
    public IReadOnlyList<Data.Tag>? SearchResults { get; private set; }
    public bool IsSearching { get; private set; }
    public bool IsSubmitting { get; private set; }
    public Data.Tag? SelectedTag { get; set; }

    private Data.Tag? _parentTag;
    public Data.Tag? ParentTag
    {
        get => _parentTag;
        set
        {
            if (_parentTag != value)
            {
                _parentTag = value;
                if (_parentTag?.IsPrivate == true)
                {
                    IsPrivate = true;
                    SelectedGroupId = _parentTag.TargetUserGroupId;
                }
            }
        }
    }

    public bool IsPrivate { get; set; }
    public int? SelectedGroupId { get; set; }
    public IReadOnlyList<Data.UserGroup> UserGroups { get; private set; } = [];

    private string _newTagName = string.Empty;

    public string NewTagName
    {
        get => _newTagName;
        set
        {
            if (_newTagName != value)
            {
                _newTagName = value;
                UpdateSimilarTags();
            }
        }
    }

    public string? NewTagContent { get; set; }

    /// <summary>
    ///     入力中のタグ名に類似する既存タグの候補リスト。
    /// </summary>
    public IReadOnlyList<SimilarTagCandidate> SimilarTags { get; private set; } = [];

    /// <summary>
    ///     全タグリストを取得して初期化する。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "グループ取得失敗時にもタグ追加自体は継続可能にするため例外を捕捉する")]
    public async Task InitializeAsync(string? currentUserId = null)
    {
        AllTags = await _tagSearchQueryService.GetAllTagsAsync(currentUserId);
        if (_userGroupData != null && !string.IsNullOrEmpty(currentUserId))
        {
            try
            {
                UserGroups = await _userGroupData.GetUserGroupsForUserAsync(currentUserId);
            }
            catch
            {
                UserGroups = [];
            }
        }
        UpdateSimilarTags();
    }

    /// <summary>
    ///     類似候補タグを選択し、検索結果および選択状態を更新する。
    /// </summary>
    public void SelectSimilarTag(Data.Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        SelectedTag = tag;
        SearchText = tag.Name;
        SearchResults = [tag];
    }

    private void UpdateSimilarTags()
    {
        SimilarTags = _tagSimilarityService.FindSimilarTags(AllTags, _newTagName);
    }

    /// <summary>
    ///     検索テキストに基づいてタグを検索する。
    /// </summary>
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchResults = null;
            SelectedTag = null;
            return;
        }

        IsSearching = true;
        SelectedTag = null;

        try
        {
            var results = await _tagSearchQueryService.SearchTagsAsync(SearchText);
            SearchResults = results;

            if (results.Count == 1)
            {
                SelectedTag = results[0];
            }
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>
    ///     親タグ選択用に入力値でフィルタリングしたタグ候補を返す（最大15件）。
    /// </summary>
    public IEnumerable<Data.Tag> SearchParentTags(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? AllTags.Take(15)
            : AllTags
            .Where(t => t.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                        t.Content.Contains(value, StringComparison.OrdinalIgnoreCase))
            .Take(15);
    }

    /// <summary>
    ///     入力内容と権限を検証し、子タグを新規作成して登録する。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Display error message in snackbar on failure")]
    public async Task<Data.Tag?> CreateChildTagAsync(string? userId, bool isAdmin)
    {
        if (IsSubmitting)
        {
            _snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return null;
        }

        if (string.IsNullOrWhiteSpace(NewTagName))
        {
            _snackbar.Add("タグ名は必須です。", Severity.Warning);
            return null;
        }

        if (ParentTag is null)
        {
            _snackbar.Add("親タグを選択してください。", Severity.Warning);
            return null;
        }

        if (string.IsNullOrEmpty(userId))
        {
            _snackbar.Add("ユーザーが見つかりません。", Severity.Error);
            return null;
        }

        bool isRestricted = await _tagLockService.IsChildCreationRestrictedAsync(ParentTag.Id);
        if (isRestricted && !isAdmin)
        {
            _snackbar.Add("選択された親タグ配下（または兄弟）はロックされているため子タグを作成できません。", Severity.Warning);
            return null;
        }

        Data.Tag? existingTag = await _tagSearchQueryService.FindTagByNameAsync(NewTagName);
        if (existingTag is not null)
        {
            _snackbar.Add("同じ名前のタグが既に存在します。", Severity.Error);
            return null;
        }

        if (ParentTag.IsPrivate)
        {
            IsPrivate = true;
            SelectedGroupId = ParentTag.TargetUserGroupId;
        }

        IsSubmitting = true;
        try
        {
            var newTag = new Data.Tag
            {
                Name = NewTagName,
                Content = NewTagContent ?? string.Empty,
                OwnerId = userId,
                ParentTagId = ParentTag.Id,
                CachedWeight = 0,
                IsPrivate = IsPrivate,
                TargetUserGroupId = IsPrivate ? SelectedGroupId : null
            };

            await _tagCommandService.CreateTagAsync(newTag, isAdmin);
            return newTag;
        }
        catch (Exception ex)
        {
            _snackbar.Add($"タグ作成中にエラーが発生しました: {ex.Message}", Severity.Error);
            return null;
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}