using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Blazor.Diagrams;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Options;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;

using SRNSMudApp.Components.Diagram;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Contracts;
using SRNSMudApp.Services.Dialogs;

using ItemEntity = SRNSMudApp.Data.Item;
using TagEntity = SRNSMudApp.Data.Tag;
using TagRelationEntity = SRNSMudApp.Data.TagRelation;

namespace SRNSMudApp.Components.Pages;

/// <summary>
///     タグとエッジのネットワーク構造を視覚化・操作するダイアグラムページコンポーネント。
///     データアクセスやフィルタリングのドメインロジックは <see cref="TagDiagramPageViewModel"/> に委譲し、
///     本コンポーネントは BlazorDiagram のキャンバス描画と UI イベント処理に特化する。
/// </summary>
public partial class TagDiagramPage : ComponentBase
{
    [Inject] private TagDiagramPageViewModel ViewModel { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    [SupplyParameterFromQuery(Name = "t1")] public int? QueryTag1 { get; set; }
    [SupplyParameterFromQuery(Name = "t2")] public int? QueryTag2 { get; set; }
    [SupplyParameterFromQuery(Name = "e")] public int? QueryEdge { get; set; }
    [SupplyParameterFromQuery(Name = "itemId")] public int? QueryItemId { get; set; }

    private BlazorDiagram _diagram = null!;
    private bool _isUpdatingUrl;
    private bool _isProgrammaticSelection;

    protected override async Task OnInitializedAsync()
    {
        InitializeDiagram();

        if (AuthState != null)
        {
            AuthenticationState auth = await AuthState;
            ViewModel.CurrentUserId = auth.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        }

        await ReloadDiagramAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_isUpdatingUrl || ViewModel.IsLoading || ViewModel.Tags.Count == 0)
        {
            return;
        }

        bool contextChanged = await ViewModel.UpdateContextItemAsync(QueryItemId);
        bool queryChanged = ViewModel.ApplyQueryParameters(QueryTag1, QueryTag2, QueryEdge);

        if (contextChanged || queryChanged)
        {
            BuildDiagramElements();
            FocusNodesInDiagram();
            await InvokeAsync(StateHasChanged);
        }
    }

    private void InitializeDiagram()
    {
        var options = new BlazorDiagramOptions
        {
            AllowMultiSelection = false,
            Zoom =
            {
                Enabled = true,
                Inverse = true,
                Minimum = 0.3,
                Maximum = 2.0
            },
            Links =
            {
                DefaultPathGenerator = new Blazor.Diagrams.Core.PathGenerators.SmoothPathGenerator()
            }
        };

        _diagram = new BlazorDiagram(options);
        _diagram.RegisterComponent<TagNode, TagNodeWidget>();
        _diagram.RegisterComponent<ItemNode, ItemNodeWidget>();
        _diagram.RegisterComponent<TagEdgeLink, TagEdgeLinkWidget>();
        _diagram.RegisterComponent<TagRelationLink, TagRelationLinkWidget>();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Diagram loading handles general exceptions to display UI snackbar")]
    private async Task ReloadDiagramAsync(bool preserveExtraVisibleTags = false)
    {
        try
        {
            await ViewModel.ReloadDiagramAsync(QueryItemId, preserveExtraVisibleTags);
            BuildDiagramElements();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"ダイアグラム読み込みに失敗しました: {ex.Message}", Severity.Error);
        }
    }

    private void OnOnlyConnectedTagsChanged(bool value)
    {
        ViewModel.OnlyConnectedTags = value;
        BuildDiagramElements();
        FocusNodesInDiagram();
    }

    private void OnNeighborhoodOnlyChanged(bool value)
    {
        ViewModel.NeighborhoodOnly = value;
        BuildDiagramElements();
        FocusNodesInDiagram();
    }

    private void OnContextOnlyChanged(bool value)
    {
        ViewModel.ContextOnly = value;
        BuildDiagramElements();
        FocusNodesInDiagram();
    }

    private void BuildDiagramElements()
    {
        Dictionary<int, Blazor.Diagrams.Core.Geometry.Point> existingPositions = _diagram.Nodes.OfType<TagNode>()
            .ToDictionary(n => n.Tag.Id, n => n.Position);

        _diagram.Nodes.Clear();
        _diagram.Links.Clear();

        IReadOnlyList<TagEntity> displayList = ViewModel.GetTagsToDisplay(QueryItemId);
        var nodeMap = new Dictionary<int, TagNode>();
        int columns = Math.Max(3, (int)Math.Ceiling(Math.Sqrt(displayList.Count * 1.5)));
        var childOffsets = new Dictionary<int, int>();

        for (int i = 0; i < displayList.Count; i++)
        {
            TagEntity tag = displayList[i];
            Blazor.Diagrams.Core.Geometry.Point position;

            if (existingPositions.TryGetValue(tag.Id, out Blazor.Diagrams.Core.Geometry.Point? prevPos))
            {
                position = new Blazor.Diagrams.Core.Geometry.Point(prevPos.X, prevPos.Y);
            }
            else if (tag.ParentTagId != null && existingPositions.TryGetValue(tag.ParentTagId.Value, out Blazor.Diagrams.Core.Geometry.Point? parentPos))
            {
                int order = childOffsets.TryGetValue(tag.ParentTagId.Value, out int cur) ? cur : 0;
                childOffsets[tag.ParentTagId.Value] = order + 1;
                double offsetX = (order - 1) * 280.0;
                double offsetY = 240.0;
                position = new Blazor.Diagrams.Core.Geometry.Point(Math.Max(20, parentPos.X + offsetX), Math.Max(20, parentPos.Y + offsetY));
            }
            else
            {
                int col = i % columns;
                int row = i / columns;
                double x = 80 + (col * 280);
                double y = 80 + (row * 280);
                position = new Blazor.Diagrams.Core.Geometry.Point(x, y);
            }

            TagFocusRole role = TagFocusRole.None;
            if (ViewModel.FocusedTag != null && tag.Id == ViewModel.FocusedTag.Id)
            {
                role = TagFocusRole.Source;
            }
            else if (ViewModel.SecondFocusedTag != null && tag.Id == ViewModel.SecondFocusedTag.Id)
            {
                role = TagFocusRole.Target;
            }

            var node = new TagNode(tag, position)
            {
                FocusRole = role,
                AllTags = ViewModel.Tags,
                RequestFocusTag = FocusTagById,
                RequestAddChildTag = HandleAddChildTag,
                RequestShowChildNodes = HandleShowChildNodes,
                RequestHideNode = HandleHideTagNode
            };
            nodeMap[tag.Id] = node;
            _diagram.Nodes.Add(node);
        }

        double tagMaxY = 80 + (((displayList.Count / columns) + 1) * 280);
        ItemEntity? parentItem = ViewModel.ContextItems.FirstOrDefault(item => QueryItemId.HasValue && item.Id == QueryItemId.Value);
        List<ItemEntity> childItems = ViewModel.ContextItems
            .Where(item => (!QueryItemId.HasValue || item.Id != QueryItemId.Value) && !ViewModel.HiddenItemIds.Contains(item.Id))
            .ToList();
        var itemNodes = new List<ItemNode>();

        for (int i = 0; i < childItems.Count; i++)
        {
            ItemEntity item = childItems[i];
            double x = 80 + (i * 280);
            double y = tagMaxY + 140;
            var node = new ItemNode(item, ViewModel.ContextItems, new Blazor.Diagrams.Core.Geometry.Point(x, y))
            {
                RequestHideNode = HandleHideItemNode
            };
            itemNodes.Add(node);
            _diagram.Nodes.Add(node);
        }

        if (parentItem != null && !ViewModel.HiddenItemIds.Contains(parentItem.Id))
        {
            double x = 80 + (childItems.Count > 0 ? (childItems.Count - 1) * 280 / 2.0 : 0);
            double y = tagMaxY + 300;
            var node = new ItemNode(parentItem, ViewModel.ContextItems, new Blazor.Diagrams.Core.Geometry.Point(x, y))
            {
                RequestHideNode = HandleHideItemNode
            };
            itemNodes.Add(node);
            _diagram.Nodes.Add(node);
        }

        foreach (ItemNode node in itemNodes)
        {
            if (node.Item.TagRelations != null)
            {
                foreach (TagRelationEntity tr in node.Item.TagRelations)
                {
                    if (nodeMap.TryGetValue(tr.TagId, out TagNode? targetNode))
                    {
                        _diagram.Links.Add(new TagRelationLink(node, targetNode));
                    }
                }
            }
            if (parentItem != null && node.Item.Id != parentItem.Id)
            {
                ItemNode parentNode = itemNodes.First(n => n.Item.Id == parentItem.Id);
                _diagram.Links.Add(new LinkModel(parentNode, node));
            }
        }

        var pairLinkCount = new Dictionary<(int, int), int>();

        foreach (TagEdge edge in ViewModel.Edges)
        {
            if (nodeMap.TryGetValue(edge.SourceTagId, out TagNode? sourceNode) &&
                nodeMap.TryGetValue(edge.TargetTagId, out TagNode? targetNode))
            {
                double dx = targetNode.Position.X - sourceNode.Position.X;
                double dy = targetNode.Position.Y - sourceNode.Position.Y;
                PortAlignment sourceAlignment;
                PortAlignment targetAlignment;

                if (Math.Abs(dx) >= Math.Abs(dy))
                {
                    sourceAlignment = dx >= 0 ? PortAlignment.Right : PortAlignment.Left;
                    targetAlignment = dx >= 0 ? PortAlignment.Left : PortAlignment.Right;
                }
                else
                {
                    sourceAlignment = dy >= 0 ? PortAlignment.Bottom : PortAlignment.Top;
                    targetAlignment = dy >= 0 ? PortAlignment.Top : PortAlignment.Bottom;
                }

                PortModel sourcePort = sourceNode.GetPort(sourceAlignment) ?? sourceNode.Ports[0];
                PortModel targetPort = targetNode.GetPort(targetAlignment) ?? targetNode.Ports[0];

                var link = new TagEdgeLink(edge, sourcePort, targetPort);

                // 同一ノードペア（無向）間のエッジ重なりを防止するため、2本目以降にオフセットを付与
                (int, int) pairKey = (Math.Min(edge.SourceTagId, edge.TargetTagId), Math.Max(edge.SourceTagId, edge.TargetTagId));
                int pairIndex = pairLinkCount.TryGetValue(pairKey, out int currentCount) ? currentCount : 0;
                pairLinkCount[pairKey] = pairIndex + 1;

                if (pairIndex > 0)
                {
                    TagNode baseNodeA = edge.SourceTagId <= edge.TargetTagId ? sourceNode : targetNode;
                    TagNode baseNodeB = edge.SourceTagId <= edge.TargetTagId ? targetNode : sourceNode;
                    ApplyEdgeOffset(link, baseNodeA, baseNodeB, pairIndex);
                }

                _diagram.Links.Add(link);
            }
        }
    }

    /// <summary>
    ///     同一ノードペア間に複数のエッジが存在する場合に、エッジが視覚的に重ならないよう
    ///     経路の中間点に垂直方向のオフセット（中間ウェイポイント）を付与する。
    /// </summary>
    internal static void ApplyEdgeOffset(TagEdgeLink link, TagNode nodeA, TagNode nodeB, int pairIndex)
    {
        const double offsetStep = 40.0;

        double midX = (nodeA.Position.X + nodeB.Position.X) / 2.0;
        double midY = (nodeA.Position.Y + nodeB.Position.Y) / 2.0;

        double dx = nodeB.Position.X - nodeA.Position.X;
        double dy = nodeB.Position.Y - nodeA.Position.Y;
        double len = Math.Sqrt((dx * dx) + (dy * dy));

        if (len < 1.0)
        {
            return;
        }

        // nodeA -> nodeB の進行方向に垂直な単位ベクトル
        double perpX = -dy / len;
        double perpY = dx / len;

        // pairIndex: 1 -> +40, 2 -> -40, 3 -> +80, 4 -> -80 ...
        double sign = (pairIndex % 2 == 1) ? 1.0 : -1.0;
        double magnitude = Math.Ceiling(pairIndex / 2.0) * offsetStep;
        double offset = sign * magnitude;

        link.AddVertex(new Blazor.Diagrams.Core.Geometry.Point(
            midX + (perpX * offset),
            midY + (perpY * offset)));
    }

    private void FocusTagFromHeaderTree(int tagId) =>
        FocusTagById(tagId);

    private void FocusTagById(int tagId)
    {
        TagEntity? tag = ViewModel.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tag != null)
        {
            ViewModel.HandleTagSelection(tag);
            OnFocusedTagsChanged();
        }
    }

    private void OnTagFocusedFromSearch(TagEntity? tag)
    {
        ViewModel.OnTagFocusedFromSearch(tag);
        OnFocusedTagsChanged();
    }

    private void OnSecondTagFocusedFromSearch(TagEntity? tag)
    {
        ViewModel.OnSecondTagFocusedFromSearch(tag);
        OnFocusedTagsChanged();
    }

    private void OnFocusedTagsChanged()
    {
        ViewModel.SelectedEdge = null;
        BuildDiagramElements();
        FocusNodesInDiagram();
        _ = InvokeAsync(StateHasChanged);
        UpdateUrlQuery();
    }

    private void FocusNodesInDiagram()
    {
        _isProgrammaticSelection = true;
        try
        {
            TagNode? node1 = ViewModel.FocusedTag != null
                ? _diagram.Nodes.OfType<TagNode>().FirstOrDefault(n => n.Tag.Id == ViewModel.FocusedTag.Id)
                : null;
            TagNode? node2 = ViewModel.SecondFocusedTag != null
                ? _diagram.Nodes.OfType<TagNode>().FirstOrDefault(n => n.Tag.Id == ViewModel.SecondFocusedTag.Id)
                : null;

            foreach (TagNode n in _diagram.Nodes.OfType<TagNode>())
            {
                if (node1 != null && n.Tag.Id == node1.Tag.Id)
                {
                    n.FocusRole = TagFocusRole.Source;
                }
                else if (node2 != null && n.Tag.Id == node2.Tag.Id)
                {
                    n.FocusRole = TagFocusRole.Target;
                }
                else
                {
                    n.FocusRole = TagFocusRole.None;
                }
                n.Refresh();
            }

            double zoom = _diagram.Zoom;
            double containerWidth = _diagram.Container is { Width: > 0 } ? _diagram.Container.Width : 800;
            double containerHeight = _diagram.Container is { Height: > 0 } ? _diagram.Container.Height : 550;

            if (node1 != null && node2 != null)
            {
                _diagram.SelectModel(node1, unselectOthers: true);
                _diagram.SelectModel(node2, unselectOthers: false);

                double nodeWidth = node1.Size is { Width: > 0 } ? node1.Size.Width : 160;
                double nodeHeight = node1.Size is { Height: > 0 } ? node1.Size.Height : 50;

                double midX = (node1.Position.X + node2.Position.X + nodeWidth) / 2.0;
                double midY = (node1.Position.Y + node2.Position.Y + nodeHeight) / 2.0;

                double panX = (containerWidth / 2.0) - (midX * zoom);
                double panY = (containerHeight / 2.0) - (midY * zoom);
                _diagram.SetPan(panX, panY);
            }
            else if (node1 != null)
            {
                _diagram.SelectModel(node1, unselectOthers: true);

                double nodeWidth = node1.Size is { Width: > 0 } ? node1.Size.Width : 160;
                double nodeHeight = node1.Size is { Height: > 0 } ? node1.Size.Height : 50;

                double panX = (containerWidth / 2.0) - ((node1.Position.X + (nodeWidth / 2.0)) * zoom);
                double panY = (containerHeight / 2.0) - ((node1.Position.Y + (nodeHeight / 2.0)) * zoom);
                _diagram.SetPan(panX, panY);
            }
            else
            {
                _diagram.UnselectAll();
            }
        }
        finally
        {
            _isProgrammaticSelection = false;
        }
    }

    private void SwapFocusedTags()
    {
        ViewModel.SwapFocusedTags();
        OnFocusedTagsChanged();
    }

    private void ClearFirstTag()
    {
        ViewModel.ClearFirstTag();
        if (ViewModel.FocusedTag == null)
        {
            ClearFocus();
        }
        else
        {
            OnFocusedTagsChanged();
        }
    }

    private void ClearSecondTag()
    {
        ViewModel.ClearSecondTag();
        OnFocusedTagsChanged();
    }

    private void ClearFocus()
    {
        ViewModel.ClearFocus();
        foreach (TagNode n in _diagram.Nodes.OfType<TagNode>())
        {
            n.FocusRole = TagFocusRole.None;
            n.Refresh();
        }
        _diagram.UnselectAll();

        BuildDiagramElements();
        FocusNodesInDiagram();
        _ = InvokeAsync(StateHasChanged);
        UpdateUrlQuery();
    }

    private void HandleShowChildNodes(TagEntity parentTag)
    {
        IReadOnlyList<TagEntity> children = ViewModel.GetChildTags(parentTag.Id);
        if (children.Count == 0)
        {
            Snackbar.Add($"'{parentTag.Name}' に子タグはありません。", Severity.Info);
            return;
        }

        int newlyAdded = ViewModel.PinChildTags(parentTag.Id);

        if (ViewModel.FocusedTag == null)
        {
            ViewModel.FocusedTag = parentTag;
        }
        else if (ViewModel.FocusedTag.Id != parentTag.Id && ViewModel.SecondFocusedTag == null)
        {
            ViewModel.SecondFocusedTag = parentTag;
        }

        BuildDiagramElements();
        FocusNodesInDiagram();
        _ = InvokeAsync(StateHasChanged);
        UpdateUrlQuery();

        if (newlyAdded > 0)
        {
            Snackbar.Add($"'{parentTag.Name}' の子タグ ({newlyAdded}件) を画面上に表示しました。", Severity.Success);
        }
        else
        {
            Snackbar.Add($"'{parentTag.Name}' の子タグはすでに画面上に表示されています。", Severity.Info);
        }
    }

    private void HandleHideTagNode(TagEntity tag)
    {
        _isProgrammaticSelection = true;
        try
        {
            ViewModel.HideTag(tag.Id);
            _diagram.UnselectAll();
            BuildDiagramElements();
            FocusNodesInDiagram();
            _ = InvokeAsync(StateHasChanged);
            UpdateUrlQuery();
            Snackbar.Add($"タグ「{tag.Name}」を画面表示から消しました。", Severity.Info);
        }
        finally
        {
            _isProgrammaticSelection = false;
        }
    }

    private void HandleHideItemNode(ItemEntity item)
    {
        _isProgrammaticSelection = true;
        try
        {
            ViewModel.HideItem(item.Id);
            _diagram.UnselectAll();
            BuildDiagramElements();
            FocusNodesInDiagram();
            _ = InvokeAsync(StateHasChanged);
            Snackbar.Add($"Item #{item.Id} を画面表示から消しました。", Severity.Info);
        }
        finally
        {
            _isProgrammaticSelection = false;
        }
    }

    private void RestoreHiddenNodes()
    {
        int count = ViewModel.HiddenTagIds.Count + ViewModel.HiddenItemIds.Count;
        ViewModel.RestoreHiddenNodes();
        BuildDiagramElements();
        FocusNodesInDiagram();
        _ = InvokeAsync(StateHasChanged);
        Snackbar.Add($"非表示ノード ({count}件) を再表示しました。", Severity.Success);
    }

    private void ResetZoom()
    {
        _diagram.SetZoom(1.0);
        _diagram.SetPan(0, 0);
    }

    private void HandleEdgeSelected(TagEdge? edge)
    {
        ViewModel.SelectedEdge = edge;
        UpdateUrlQuery();
    }

    private void HandleNodeSelected(TagEntity? tag)
    {
        if (_isProgrammaticSelection)
        {
            return;
        }

        ViewModel.SelectedEdge = null;
        if (tag == null || ViewModel.HiddenTagIds.Contains(tag.Id))
        {
            return;
        }

        if (ViewModel.IsEdgeCreationMode)
        {
            if (!ViewModel.IsSelectingTargetInEdgeMode || ViewModel.EdgeCreationSourceTag == null)
            {
                ViewModel.SelectChildAsSource(tag);
            }
            else
            {
                ViewModel.SelectChildAsTarget(tag);
            }

            return;
        }

        ViewModel.HandleTagSelection(tag);
        OnFocusedTagsChanged();
    }

    private void CloseInspector()
    {
        ViewModel.SelectedEdge = null;
        _diagram.UnselectAll();
    }

    private async Task OpenCreateEdgeDialog(TagEntity? initialSource = null, TagEntity? initialTarget = null)
    {
        var parameters = new DialogParameters<CreateEdgeDialog>
        {
            { x => x.AvailableTags, ViewModel.Tags },
            { x => x.InitialSourceTag, initialSource },
            { x => x.InitialTargetTag, initialTarget }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };

        IDialogReference dialog = await DialogLauncher.ShowAsync<CreateEdgeDialog>("Edge の作成", parameters, options);
        DialogResult? result = await dialog.Result;

        if (result is not null && !result.Canceled && result.Data is ValueTuple<int, int> edgeData)
        {
            Result<TagEdge> createResult = await ViewModel.CreateEdgeAsync(edgeData.Item1, edgeData.Item2);
            switch (createResult)
            {
                case Success<TagEdge> s:
                    Snackbar.Add("Edge を作成しました。", Severity.Success);
                    await ReloadDiagramAsync();
                    ViewModel.SelectedEdge = ViewModel.Edges.FirstOrDefault(e => e.Id == s.Value.Id);
                    break;
                case Failure f:
                    Snackbar.Add($"Edge 作成に失敗しました: {f.ErrorMessage}", Severity.Error);
                    break;
                default:
                    break;
            }
        }
    }

    private async Task HandleDeleteEdge(TagEdge edge)
    {
        Result<bool> result = await ViewModel.DeleteEdgeAsync(edge.Id);
        switch (result)
        {
            case Success<bool>:
                Snackbar.Add("Edge を削除しました。", Severity.Success);
                ViewModel.SelectedEdge = null;
                await ReloadDiagramAsync();
                break;
            case Failure f:
                Snackbar.Add($"Edge 削除に失敗しました: {f.ErrorMessage}", Severity.Error);
                break;
            default:
                break;
        }
    }

    private async Task HandleOpenAttachDialog(TagEdge edge)
    {
        var parameters = new DialogParameters<AttachTagToEdgeDialog>
        {
            { x => x.Edge, edge },
            { x => x.CurrentUserId, ViewModel.CurrentUserId },
            { x => x.AvailableTags, ViewModel.Tags }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };

        IDialogReference dialog = await DialogLauncher.ShowAsync<AttachTagToEdgeDialog>("Edge へのタグ紐付け", parameters, options);
        DialogResult? result = await dialog.Result;

        if (result is not null && !result.Canceled && result.Data is ValueTuple<int, int, int> attachData)
        {
            Result<TagEdgeTagAttachment> attachResult = await ViewModel.AttachTagToEdgeAsync(
                edge.Id, attachData.Item1, attachData.Item2, attachData.Item3);

            switch (attachResult)
            {
                case Success<TagEdgeTagAttachment>:
                    Snackbar.Add(ContractMessages.TagEdgeTagAttached, Severity.Success);
                    await ReloadDiagramAsync();
                    ViewModel.SelectedEdge = ViewModel.Edges.FirstOrDefault(e => e.Id == edge.Id);
                    break;
                case Failure f:
                    Snackbar.Add($"タグ紐付けに失敗しました: {f.ErrorMessage}", Severity.Error);
                    break;
                default:
                    break;
            }
        }
    }

    private async Task HandleDetachTag(TagEdgeTagAttachment attachment)
    {
        Result<bool> result = await ViewModel.DetachTagFromEdgeAsync(attachment.Id);
        switch (result)
        {
            case Success<bool>:
                Snackbar.Add(ContractMessages.TagEdgeTagDetachedOrDecreased, Severity.Success);
                int edgeId = ViewModel.SelectedEdge?.Id ?? attachment.TagEdgeId;
                await ReloadDiagramAsync();
                ViewModel.SelectedEdge = ViewModel.Edges.FirstOrDefault(e => e.Id == edgeId);
                break;
            case Failure f:
                Snackbar.Add($"紐付け解除に失敗しました: {f.ErrorMessage}", Severity.Error);
                break;
            default:
                break;
        }
    }

    private async Task HandleAddChildTag(TagEntity? parentTag)
    {
        if (parentTag == null)
        {
            return;
        }

        var parameters = new DialogParameters { [nameof(TagAddDialog.DefaultParentTag)] = parentTag };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true };

        IDialogReference dialog = await DialogLauncher.ShowAsync<TagAddDialog>("子タグの追加", parameters, options);
        DialogResult? result = await dialog.Result;

        if (result is { Canceled: false, Data: TagEntity createdTag })
        {
            Snackbar.Add($"'{createdTag.Name}' を追加しました。", Severity.Success);
            await ReloadDiagramAsync();
        }
    }

    private async Task ToggleEdgeCreationModeAsync()
    {
        if (ViewModel.IsEdgeCreationMode)
        {
            ViewModel.ExitEdgeCreationMode();
        }
        else
        {
            int newlyAdded = await ViewModel.EnterEdgeCreationModeAsync();
            if (newlyAdded > 0)
            {
                BuildDiagramElements();
                FocusNodesInDiagram();
            }
        }
    }

    private void ExitEdgeCreationMode() =>
        ViewModel.ExitEdgeCreationMode();

    private void SelectChildAsSource(TagEntity child) =>
        ViewModel.SelectChildAsSource(child);

    private void SelectChildAsTarget(TagEntity child) =>
        ViewModel.SelectChildAsTarget(child);

    private void SwapEdgeCreationTags() =>
        ViewModel.SwapEdgeCreationTags();

    private void OnEdgeCreationSourceChanged(TagEntity? tag) =>
        ViewModel.OnEdgeCreationSourceChanged(tag);

    private void OnEdgeCreationTargetChanged(TagEntity? tag) =>
        ViewModel.OnEdgeCreationTargetChanged(tag);

    private async Task OnEdgeCreationAttachTagChanged(TagEntity? tag) =>
        await ViewModel.SetEdgeCreationAttachTagAsync(tag);

    private async Task CreateEdgeInModeAsync()
    {
        if (!ViewModel.CanCreateEdgeInMode)
        {
            return;
        }

        TagEntity sourceTag = ViewModel.EdgeCreationSourceTag!;
        TagEntity targetTag = ViewModel.EdgeCreationTargetTag!;
        string? attachTagName = ViewModel.EdgeCreationAttachTag?.Name;

        Result<(TagEdge Edge, TagEdgeTagAttachment? Attachment)> result = await ViewModel.CreateEdgeInModeAsync();
        switch (result)
        {
            case Success<(TagEdge Edge, TagEdgeTagAttachment? Attachment)> s:
                Snackbar.Add($"Edge（{sourceTag.Name} → {targetTag.Name}）を作成しました。", Severity.Success);
                if (s.Value.Attachment != null && attachTagName != null)
                {
                    Snackbar.Add($"エッジにタグ「{attachTagName}」を紐付けました。", Severity.Success);
                }
                BuildDiagramElements();
                FocusNodesInDiagram();
                break;
            case Failure f:
                Snackbar.Add($"Edge 作成に失敗しました: {f.ErrorMessage}", Severity.Error);
                break;
            default:
                break;
        }
    }

    private void UpdateUrlQuery()
    {
        if (ViewModel.IsLoading)
        {
            return;
        }

        var queryParams = new Dictionary<string, object?>
        {
            { "t1", ViewModel.FocusedTag?.Id },
            { "t2", ViewModel.SecondFocusedTag?.Id },
            { "e", ViewModel.SelectedEdge?.Id }
        };
        if (QueryItemId.HasValue)
        {
            queryParams["itemId"] = QueryItemId.Value;
        }

        string uri = NavigationManager.GetUriWithQueryParameters(queryParams);

        _isUpdatingUrl = true;
        try
        {
            NavigationManager.NavigateTo(uri, replace: false);
        }
        finally
        {
            _isUpdatingUrl = false;
        }
    }
}