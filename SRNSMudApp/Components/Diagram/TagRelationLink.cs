using Blazor.Diagrams.Core.Models;

namespace SRNSMudApp.Components.Diagram;

public class TagRelationLink : LinkModel
{
    public TagRelationLink(NodeModel sourceNode, NodeModel targetNode) : base(sourceNode, targetNode)
    {
    }
}