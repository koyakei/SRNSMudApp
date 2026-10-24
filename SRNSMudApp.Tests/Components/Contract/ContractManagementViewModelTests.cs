// Components/Contract/ContractManagementViewModelTests.cs
#region

using Moq;

using MudBlazor;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Components.Contract;

/// <summary>
///     <see cref="ContractManagementViewModel" /> の単体テスト。
/// </summary>
public class ContractManagementViewModelTests
{
    private readonly Mock<IContractManagementDataProvider> _contractDataMock = new();
    private readonly Mock<ITaggingContractService> _contractServiceMock = new();

    private ContractManagementViewModel CreateViewModel() =>
        new(_contractDataMock.Object, _contractServiceMock.Object);

    [Fact]
    public void Constructor_WhenContractDataIsNull_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() =>
            new ContractManagementViewModel(null!, _contractServiceMock.Object));
    }

    [Fact]
    public void Constructor_WhenContractServiceIsNull_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() =>
            new ContractManagementViewModel(_contractDataMock.Object, null!));
    }

    [Fact]
    public async Task LoadDataAsync_WhenSuccessful_SetsPageStateToLoaded()
    {
        // Arrange
        var userId = "user-123";
        var pageData = new ContractManagementPageData(
            new List<TaggingRequestEntity> { new() { Id = 1, OwnerId = userId } },
            new List<TaggingRequestEntity> { new() { Id = 2, OwnerId = userId } });

        _contractDataMock
            .Setup(x => x.GetContractsAsync(userId))
            .ReturnsAsync(pageData);

        var vm = CreateViewModel();

        // Act
        await vm.LoadDataAsync(userId);

        // Assert
        Assert.True(vm.PageState is Loaded<ContractManagementData> loaded &&
                    loaded.Data.IncomingContracts.Count == 1 &&
                    loaded.Data.OutgoingContracts.Count == 1);
        _contractDataMock.Verify(x => x.GetContractsAsync(userId), Times.Once);
    }

    [Fact]
    public async Task LoadDataAsync_WhenThrows_SetsPageStateToFailed()
    {
        // Arrange
        var userId = "user-123";
        var expectedException = new InvalidOperationException("DB error");

        _contractDataMock
            .Setup(x => x.GetContractsAsync(userId))
            .ThrowsAsync(expectedException);

        var vm = CreateViewModel();

        // Act
        await vm.LoadDataAsync(userId);

        // Assert
        Assert.True(vm.PageState is Failed failed && ReferenceEquals(expectedException, failed.Error));
    }

    [Fact]
    public async Task AcceptContractAsync_WhenSuccess_ReloadsDataAndReturnsSuccess()
    {
        // Arrange
        var userId = "user-123";
        var contractId = 42;
        var pageData = new ContractManagementPageData([], []);

        _contractServiceMock
            .Setup(x => x.AcceptContractAsync(contractId, userId, null))
            .ReturnsAsync(new Success<string>("Contract accepted"));

        _contractDataMock
            .Setup(x => x.GetContractsAsync(userId))
            .ReturnsAsync(pageData);

        var vm = CreateViewModel();

        // Act
        var result = await vm.AcceptContractAsync(contractId, userId);

        // Assert
        Assert.True(result is Success<string>);
        _contractServiceMock.Verify(x => x.AcceptContractAsync(contractId, userId, null), Times.Once);
        _contractDataMock.Verify(x => x.GetContractsAsync(userId), Times.Once);
        Assert.True(vm.PageState is Loaded<ContractManagementData>);
    }

    [Fact]
    public async Task AcceptContractAsync_WhenFailure_ReturnsFailureWithoutReloadingData()
    {
        // Arrange
        var userId = "user-123";
        var contractId = 42;

        _contractServiceMock
            .Setup(x => x.AcceptContractAsync(contractId, userId, null))
            .ReturnsAsync(new Failure("Unauthorized"));

        var vm = CreateViewModel();

        // Act
        var result = await vm.AcceptContractAsync(contractId, userId);

        // Assert
        Assert.True(result is Failure f && f.ErrorMessage == "Unauthorized");
        _contractServiceMock.Verify(x => x.AcceptContractAsync(contractId, userId, null), Times.Once);
        _contractDataMock.Verify(x => x.GetContractsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RejectContractAsync_WhenSuccess_ReloadsDataAndReturnsSuccess()
    {
        // Arrange
        var userId = "user-123";
        var contractId = 42;
        var pageData = new ContractManagementPageData([], []);

        _contractServiceMock
            .Setup(x => x.RejectContractAsync(contractId, userId, It.IsAny<string?>()))
            .ReturnsAsync(new Success<string>("Contract rejected"));

        _contractDataMock
            .Setup(x => x.GetContractsAsync(userId))
            .ReturnsAsync(pageData);

        var vm = CreateViewModel();

        // Act
        var result = await vm.RejectContractAsync(contractId, userId);

        // Assert
        Assert.True(result is Success<string>);
        _contractServiceMock.Verify(x => x.RejectContractAsync(contractId, userId, It.IsAny<string?>()), Times.Once);
        _contractDataMock.Verify(x => x.GetContractsAsync(userId), Times.Once);
        Assert.True(vm.PageState is Loaded<ContractManagementData>);
    }

    [Fact]
    public async Task RejectContractAsync_WhenFailure_ReturnsFailureWithoutReloadingData()
    {
        // Arrange
        var userId = "user-123";
        var contractId = 42;

        _contractServiceMock
            .Setup(x => x.RejectContractAsync(contractId, userId, It.IsAny<string?>()))
            .ReturnsAsync(new Failure("Not allowed"));

        var vm = CreateViewModel();

        // Act
        var result = await vm.RejectContractAsync(contractId, userId);

        // Assert
        Assert.True(result is Failure f && f.ErrorMessage == "Not allowed");
        _contractServiceMock.Verify(x => x.RejectContractAsync(contractId, userId, It.IsAny<string?>()), Times.Once);
        _contractDataMock.Verify(x => x.GetContractsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CancelContractAsync_WhenSuccess_ReloadsDataAndReturnsSuccess()
    {
        // Arrange
        var userId = "user-123";
        var contractId = 42;
        var pageData = new ContractManagementPageData([], []);

        _contractServiceMock
            .Setup(x => x.CancelContractAsync(contractId, userId))
            .ReturnsAsync(new Success<string>("Contract canceled"));

        _contractDataMock
            .Setup(x => x.GetContractsAsync(userId))
            .ReturnsAsync(pageData);

        var vm = CreateViewModel();

        // Act
        var result = await vm.CancelContractAsync(contractId, userId);

        // Assert
        Assert.True(result is Success<string>);
        _contractServiceMock.Verify(x => x.CancelContractAsync(contractId, userId), Times.Once);
        _contractDataMock.Verify(x => x.GetContractsAsync(userId), Times.Once);
        Assert.True(vm.PageState is Loaded<ContractManagementData>);
    }

    [Fact]
    public async Task CancelContractAsync_WhenFailure_ReturnsFailureWithoutReloadingData()
    {
        // Arrange
        var userId = "user-123";
        var contractId = 42;

        _contractServiceMock
            .Setup(x => x.CancelContractAsync(contractId, userId))
            .ReturnsAsync(new Failure("Already executed"));

        var vm = CreateViewModel();

        // Act
        var result = await vm.CancelContractAsync(contractId, userId);

        // Assert
        Assert.True(result is Failure f && f.ErrorMessage == "Already executed");
        _contractServiceMock.Verify(x => x.CancelContractAsync(contractId, userId), Times.Once);
        _contractDataMock.Verify(x => x.GetContractsAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(TradeStatus.Proposed, "提案中")]
    [InlineData(TradeStatus.Executed, "承認済み")]
    [InlineData(TradeStatus.Rejected, "拒否")]
    [InlineData(TradeStatus.Canceled, "キャンセル")]
    [InlineData((TradeStatus)999, "その他")]
    public void GetStatusDisplayText_MapsTradeStatusToJapaneseText(TradeStatus status, string expectedText)
    {
        var actual = ContractManagementViewModel.GetStatusDisplayText(status);
        Assert.Equal(expectedText, actual);
    }

    [Theory]
    [InlineData(TradeStatus.Proposed, Color.Info)]
    [InlineData(TradeStatus.Executed, Color.Success)]
    [InlineData(TradeStatus.Rejected, Color.Error)]
    [InlineData(TradeStatus.Canceled, Color.Default)]
    [InlineData((TradeStatus)999, Color.Default)]
    public void GetStatusColor_MapsTradeStatusToMudColor(TradeStatus status, Color expectedColor)
    {
        var actual = ContractManagementViewModel.GetStatusColor(status);
        Assert.Equal(expectedColor, actual);
    }

    [Fact]
    public void CanCancelContract_WhenProposed_ReturnsTrue()
    {
        var contract = new TaggingRequestEntity { OwnerId = "test-owner" };
        Assert.Equal(TradeStatus.Proposed, contract.Status);

        var actual = ContractManagementViewModel.CanCancelContract(contract);

        Assert.True(actual);
    }

    [Fact]
    public void CanCancelContract_WhenExecuted_ReturnsFalse()
    {
        var contract = new TaggingRequestEntity { OwnerId = "test-owner" };
        _ = contract.Execute();

        var actual = ContractManagementViewModel.CanCancelContract(contract);

        Assert.False(actual);
    }

    [Fact]
    public void CanCancelContract_WhenCanceled_ReturnsFalse()
    {
        var contract = new TaggingRequestEntity { OwnerId = "test-owner" };
        _ = contract.Cancel();

        var actual = ContractManagementViewModel.CanCancelContract(contract);

        Assert.False(actual);
    }

    [Fact]
    public void CanCancelContract_WhenRejected_ReturnsFalse()
    {
        var contract = new TaggingRequestEntity { OwnerId = "test-owner" };
        _ = contract.Reject(new RejectionInfo(new RejectionReason("test reason")));

        var actual = ContractManagementViewModel.CanCancelContract(contract);

        Assert.False(actual);
    }

    [Fact]
    public void CanCancelContract_WhenContractIsNull_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => ContractManagementViewModel.CanCancelContract(null!));
    }
}