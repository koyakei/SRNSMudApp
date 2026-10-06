namespace SRNSMudApp.Components.Diagram;

using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

/// <summary>
///     タグダイアグラムのフォーカスバーコンポーネントのコードビハインド。
///     始点・終点タグの選択状態、ツリーポップオーバー、エッジ作成モード切り替え、既存エッジの検出状態の表示を制御する。
///     子タグ数計算や既存エッジの検索ロジックは <see cref="TagDiagramFocusBarViewModel"/> に委譲する。
/// </summary>
public partial class TagDiagramFocusBar : ComponentBase
{
    [Parameter] public TagEntity? FocusedTag { get; set; }
    [Parameter] public TagEntity? SecondFocusedTag { get; set; }
    [Parameter] public IReadOnlyList<TagEntity> AllTags { get; set; } = [];
    [Parameter] public IReadOnlyList<TagEdge> Edges { get; set; } = [];
    [Parameter] public bool IsEdgeCreationMode { get; set; }
    [Parameter] public string CurrentUserId { get; set; } = "";

    [Parameter] public EventCallback OnClearFirstTag { get; set; }
    [Parameter] public EventCallback OnClearSecondTag { get; set; }
    [Parameter] public EventCallback OnClearFocus { get; set; }
    [Parameter] public EventCallback OnSwapFocusedTags { get; set; }
    [Parameter] public EventCallback<TagEntity> OnShowChildNodes { get; set; }
    [Parameter] public EventCallback<int> OnFocusTagFromTree { get; set; }
    [Parameter] public EventCallback<TagEntity?> OnAddChildTag { get; set; }
    [Parameter] public EventCallback OnToggleEdgeCreationMode { get; set; }
    [Parameter] public EventCallback<(TagEntity? Source, TagEntity? Target)> OnOpenCreateEdgeDialog { get; set; }
    [Parameter] public EventCallback<TagEdge> OnSelectEdge { get; set; }
    [Parameter] public EventCallback<TagEntity?> OnSecondTagFocusedFromSearch { get; set; }
    [Parameter] public EventCallback<TagEntity> OnHideFocusedTag { get; set; }

    private bool _isFocusTreeOpen;
    private bool _isSecondFocusTreeOpen;

    private async Task HandleFocusFromTree(int tagId)
    {
        _isFocusTreeOpen = false;
        _isSecondFocusTreeOpen = false;
        await OnFocusTagFromTree.InvokeAsync(tagId);
    }

    private int GetChildTagsCount(int tagId) =>
        TagDiagramFocusBarViewModel.GetChildTagsCount(AllTags, tagId);

    private TagEdge? GetExistingEdgeBetween(int tagId1, int tagId2) =>
        TagDiagramFocusBarViewModel.GetExistingEdgeBetween(Edges, tagId1, tagId2);
}