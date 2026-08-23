using System.Diagnostics.CodeAnalysis;

using SRNSMudApp.Data;

namespace SRNSMudApp.Models.Unions;

public record NoAssetSelected();
public record AssetSelected(RightAsset SelectedAsset);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union AssetSelection(NoAssetSelected, AssetSelected);