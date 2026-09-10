using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace SRNSMudApp.Components.Diagram;

public class ItemNode : NodeModel
{
    public Data.Item Item { get; }
    public IReadOnlyList<Data.Item> AllContextItems { get; }
    public bool IsExpanded { get; set; }

    public ItemNode(Data.Item item, IReadOnlyList<Data.Item> allContextItems, Point? position = null) : base(position)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        AllContextItems = allContextItems ?? [];
        _ = AddPort(PortAlignment.Left);
        _ = AddPort(PortAlignment.Right);
        _ = AddPort(PortAlignment.Top);
        _ = AddPort(PortAlignment.Bottom);
    }
}