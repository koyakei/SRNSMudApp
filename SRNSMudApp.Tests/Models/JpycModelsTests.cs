#region

using SRNSMudApp.Models;

#endregion

namespace SRNSMudApp.Tests.Models;

public class JpycModelsTests
{
    [Theory]
    [InlineData(1, 100, 100)]
    [InlineData(5, 150, 750)]
    [InlineData(10, 200, 2000)]
    [InlineData(0, 100, 0)]
    [InlineData(100, 0, 0)]
    public void JpycPurchaseRequestDto_TotalJpycAmount_CalculatesCorrectly(int amount, int unitPrice, int expectedTotal)
    {
        // Arrange
        var dto = new JpycPurchaseRequestDto(
            RequestedTagId: 1,
            Amount: amount,
            UnitPriceJpyc: unitPrice,
            NetworkName: "polygon-amoy",
            TransactionHash: "0x123",
            SenderWalletAddress: "0xabc");

        // Assert
        Assert.Equal(expectedTotal, dto.TotalJpycAmount);
        Assert.Equal(1, dto.RequestedTagId);
        Assert.Equal(amount, dto.Amount);
        Assert.Equal(unitPrice, dto.UnitPriceJpyc);
        Assert.Equal("polygon-amoy", dto.NetworkName);
        Assert.Equal("0x123", dto.TransactionHash);
        Assert.Equal("0xabc", dto.SenderWalletAddress);
    }

    [Fact]
    public void JpycNetworkInfo_TestnetAndMainnetProperties_AreConsistent()
    {
        // Arrange
        var testnet = new JpycNetworkInfo(
            Name: "polygon-amoy",
            DisplayName: "Polygon Amoy (テストネット)",
            ChainId: 80002,
            ContractAddress: "0xE7C3D8C9a439feDe00D2600032D5dB0Be71C3c29",
            FaucetUrl: "https://faucet.jpyc.co.jp",
            ExplorerUrl: "https://amoy.polygonscan.com/",
            IsTestnet: true,
            IsRecommended: true);

        var mainnet = new JpycNetworkInfo(
            Name: "polygon-mainnet",
            DisplayName: "Polygon (メインネット)",
            ChainId: 137,
            ContractAddress: "0x431D5dfF03120AFA4bDf332c61A6e1766eF37BDB",
            FaucetUrl: null,
            ExplorerUrl: "https://polygonscan.com/",
            IsTestnet: false);

        // Assert
        Assert.True(testnet.IsTestnet);
        Assert.True(testnet.IsRecommended);
        Assert.NotNull(testnet.FaucetUrl);
        Assert.Equal(80002, testnet.ChainId);

        Assert.False(mainnet.IsTestnet);
        Assert.False(mainnet.IsRecommended);
        Assert.Null(mainnet.FaucetUrl);
        Assert.Equal(137, mainnet.ChainId);
    }

    [Fact]
    public void UserDepositWalletDto_Properties_MatchInitialization()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var dto = new UserDepositWalletDto(
            UserId: "user-123",
            NetworkName: "polygon-amoy",
            DepositAddress: "0x1234567890abcdef1234567890abcdef12345678",
            CreatedAt: now);

        // Assert
        Assert.Equal("user-123", dto.UserId);
        Assert.Equal("polygon-amoy", dto.NetworkName);
        Assert.Equal("0x1234567890abcdef1234567890abcdef12345678", dto.DepositAddress);
        Assert.Equal(now, dto.CreatedAt);
    }

    [Fact]
    public void JpycTransactionVerificationResult_SuccessAndFailure_States()
    {
        // Act - Success
        var success = new JpycTransactionVerificationResult(
            IsSuccess: true,
            TransactionHash: "0x123",
            RecipientAddress: "0xabc",
            SenderAddress: "0xdef",
            AmountJpyc: 500);

        // Assert - Success
        Assert.True(success.IsSuccess);
        Assert.Null(success.ErrorMessage);
        Assert.Equal("0x123", success.TransactionHash);
        Assert.Equal(500, success.AmountJpyc);

        // Act - Failure
        var failure = new JpycTransactionVerificationResult(
            IsSuccess: false,
            ErrorMessage: "送金額不足");

        // Assert - Failure
        Assert.False(failure.IsSuccess);
        Assert.Equal("送金額不足", failure.ErrorMessage);
        Assert.Equal(0, failure.AmountJpyc);
    }

    [Fact]
    public void JpycPurchaseResultDto_Properties_MatchInitialization()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var dto = new JpycPurchaseResultDto(
            RightAssetId: 10,
            TagId: 20,
            TagName: "テストタグ",
            Amount: 3,
            TotalJpycAmount: 300,
            NetworkName: "polygon-amoy",
            TransactionHash: "0xabc",
            DepositAddress: "0xdef",
            PurchasedAt: now);

        // Assert
        Assert.Equal(10, dto.RightAssetId);
        Assert.Equal(20, dto.TagId);
        Assert.Equal("テストタグ", dto.TagName);
        Assert.Equal(3, dto.Amount);
        Assert.Equal(300, dto.TotalJpycAmount);
        Assert.Equal("polygon-amoy", dto.NetworkName);
        Assert.Equal("0xabc", dto.TransactionHash);
        Assert.Equal("0xdef", dto.DepositAddress);
        Assert.Equal(now, dto.PurchasedAt);
    }
}