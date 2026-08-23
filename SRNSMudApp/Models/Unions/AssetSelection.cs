using SRNSMudApp.Data;

namespace SRNSMudApp.Models.Unions;

public record NoAssetSelected();
public record AssetSelected(RightAsset SelectedAsset);

public union AssetSelection(NoAssetSelected, AssetSelected);