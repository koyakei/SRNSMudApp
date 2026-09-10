using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace SRNSMudApp.Components.Diagram;

public class ItemNode : NodeModel
{
    public Data.Item Item { get; }

    public ItemNode(Data.Item item, Point? position = null) : base(position)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        _ = AddPort(PortAlignment.Left);
        _ = AddPort(PortAlignment.Right);
        _ = AddPort(PortAlignment.Top);
        _ = AddPort(PortAlignment.Bottom);
    }
}