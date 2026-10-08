namespace SRNSMudApp.Components.Tag;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

/// <summary>
///     タグ階層ツリーポップオーバーコンテンツコンポーネントのコードビハインド。
///     ツリー行のクリック展開/折りたたみ、タグ詳細への遷移、親タグ/子タグの入替・追加コールバック呼び出しを制御する。
///     ツリーデータ構築アルゴリズムは <see cref="TagTreePopoverViewModel"/> に委譲する。
/// </summary>
public partial class TagTreePopoverContent : ComponentBase
{
    [Parameter] public Tag TargetTag { get; set; } = null!;
    [Parameter] public IEnumerable<Tag> AllTags { get; set; } = [];
    [Parameter] public bool EnableSwap { get; set; }
    [Parameter] public bool EnableExpand { get; set; } = true;
    [Parameter] public bool EnableNavigation { get; set; } = true;
    [Parameter] public bool EnableAddChild { get; set; }
    [Parameter] public string MaxHeight { get; set; } = "min(300px, 60vh)";
    [Parameter] public string? Style { get; set; }
    [Parameter] public EventCallback<int> OnSwap { get; set; }
    [Parameter] public EventCallback<Tag> OnAddChildTag { get; set; }
    [Parameter] public EventCallback<int> OnTagClick { get; set; }

    private readonly List<TagTreeLine> _treeLines = [];
    private readonly HashSet<int> _expandedTreeTagIds = [];

    protected override void OnParametersSet()
    {
        if (TargetTag is null || AllTags is null)
        {
            return;
        }

        if (_expandedTreeTagIds.Count == 0)
        {
            // Auto-expand parents of TargetTag
            foreach (var id in TagTreePopoverViewModel.GetAutoExpandIds(TargetTag, AllTags.ToList()))
            {
                _expandedTreeTagIds.Add(id);
            }
        }

        BuildTree();
    }

    private void BuildTree()
    {
        _treeLines.Clear();
        if (TargetTag == null || !AllTags.Any())
        {
            return;
        }

        _treeLines.AddRange(TagTreePopoverViewModel.BuildTreeLines(
            TargetTag, AllTags.ToList(), _expandedTreeTagIds, EnableExpand));
    }

    private void OnLineClicked(TagTreeLine line)
    {
        if (!EnableExpand)
        {
            return;
        }

        if (!_expandedTreeTagIds.Remove(line.TagId))
        {
            _expandedTreeTagIds.Add(line.TagId);
        }

        BuildTree();
    }

    private async Task AddChildTagClicked(int tagId)
    {
        if (OnAddChildTag.HasDelegate)
        {
            var targetTag = AllTags.FirstOrDefault(t => t.Id == tagId);
            if (targetTag is not null)
            {
                await OnAddChildTag.InvokeAsync(targetTag);
            }
        }
    }
}