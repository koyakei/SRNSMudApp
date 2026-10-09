namespace SRNSMudApp.Components.Tag;

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
///     子タグ作成ダイアログコンポーネントのコードビハインド。
///     タグ名・内容の入力と結果返却、類似候補タグの表示を制御する。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034:Nested types should not be visible",
    Justification = "ダイアログ結果型として既存の呼び出し元との互換性を維持するため入れ子型を維持する")]
public partial class TagCreateChildDialog : ComponentBase
{
    [Inject] private ITagSearchQueryService TagSearchQueryService { get; set; } = null!;
    [Inject] private ITagSimilarityService TagSimilarityService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    private string _name = string.Empty;
    private string _content = string.Empty;
    private IReadOnlyList<Tag> _allTags = [];
    private IReadOnlyList<SimilarTagCandidate> _similarTags = [];

    private string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                UpdateSimilarTags();
            }
        }
    }

    public record Result(string Name, string Content);

    protected override async Task OnInitializedAsync()
    {
        _allTags = await TagSearchQueryService.GetAllTagsAsync();
        UpdateSimilarTags();
    }

    private void UpdateSimilarTags()
    {
        _similarTags = TagSimilarityService.FindSimilarTags(_allTags, _name);
    }

    private void ApplyCandidate(Tag tag)
    {
        _name = tag.Name;
        UpdateSimilarTags();
    }

    private void Cancel()
    {
        MudDialog.Cancel();
    }

    private void Submit()
    {
        if (string.IsNullOrWhiteSpace(_name))
        {
            return;
        }

        MudDialog.Close(DialogResult.Ok(new Result(_name, _content ?? string.Empty)));
    }
}