#region

using SRNSMudApp.Models;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Tests.Services;

public class JpycTransactionVerifierTests
{
    private readonly JpycTransactionVerifier _sut = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task VerifyTransactionAsync_WhenHashIsNullOrWhitespace_ReturnsFailure(string? txHash)
    {
        // Act
        JpycTransactionVerificationResult result = await _sut.VerifyTransactionAsync(
            "polygon-amoy",
            txHash!,
            "0x1234567890abcdef1234567890abcdef12345678",
            100);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("トランザクションハッシュが入力されていません", result.ErrorMessage);
    }

    [Theory]
    [InlineData("not-a-tx-hash")]
    [InlineData("0x12345")] // 短すぎる
    [InlineData("0xzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // 非hex文字
    [InlineData("1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef12")] // 0xプレフィックスなし
    public async Task VerifyTransactionAsync_WhenInvalidFormatAndNotSimulated_ReturnsFailure(string invalidHash)
    {
        // Act
        JpycTransactionVerificationResult result = await _sut.VerifyTransactionAsync(
            "polygon-amoy",
            invalidHash,
            "0x1234567890abcdef1234567890abcdef12345678",
            100);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("有効なトランザクションハッシュ", result.ErrorMessage);
    }

    [Fact]
    public async Task VerifyTransactionAsync_WhenValidOnChainFormat_ReturnsSuccess()
    {
        // Arrange
        const string validTx = "0xabcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        const string recipient = "0x1234567890abcdef1234567890abcdef12345678";
        const int minAmount = 500;

        // Act (前後空白トリムも含めて検証)
        JpycTransactionVerificationResult result = await _sut.VerifyTransactionAsync(
            "polygon-amoy",
            $"  {validTx}  ",
            recipient,
            minAmount);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(validTx, result.TransactionHash);
        Assert.Equal(recipient, result.RecipientAddress);
        Assert.Equal(minAmount, result.AmountJpyc);
    }

    [Fact]
    public async Task VerifyTransactionAsync_SimulatedTx_CorrectRecipientAndAmount_ReturnsSuccess()
    {
        // Arrange
        const string recipient = "0x1234567890abcdef1234567890abcdef12345678";
        const int amount = 1000;
        const string customSender = "0x9999999999999999999999999999999999999999";

        string txHash = await _sut.SimulateDepositTransactionAsync(
            "polygon-amoy",
            recipient,
            amount,
            customSender);

        // Act
        JpycTransactionVerificationResult result = await _sut.VerifyTransactionAsync(
            "polygon-amoy",
            txHash,
            recipient,
            amount);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(txHash, result.TransactionHash);
        Assert.Equal(recipient, result.RecipientAddress);
        Assert.Equal(customSender, result.SenderAddress);
        Assert.Equal(amount, result.AmountJpyc);
    }

    [Fact]
    public async Task VerifyTransactionAsync_SimulatedTx_WrongRecipient_ReturnsFailure()
    {
        // Arrange
        const string actualRecipient = "0x1111111111111111111111111111111111111111";
        const string expectedRecipient = "0x2222222222222222222222222222222222222222";

        string txHash = await _sut.SimulateDepositTransactionAsync(
            "polygon-amoy",
            actualRecipient,
            500);

        // Act
        JpycTransactionVerificationResult result = await _sut.VerifyTransactionAsync(
            "polygon-amoy",
            txHash,
            expectedRecipient,
            500);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("送金先アドレスが一致しません", result.ErrorMessage);
        Assert.Equal(actualRecipient, result.RecipientAddress);
    }

    [Fact]
    public async Task VerifyTransactionAsync_SimulatedTx_InsufficientAmount_ReturnsFailure()
    {
        // Arrange
        const string recipient = "0x1234567890abcdef1234567890abcdef12345678";
        const int depositedAmount = 300;
        const int requiredAmount = 500;

        string txHash = await _sut.SimulateDepositTransactionAsync(
            "polygon-amoy",
            recipient,
            depositedAmount);

        // Act
        JpycTransactionVerificationResult result = await _sut.VerifyTransactionAsync(
            "polygon-amoy",
            txHash,
            recipient,
            requiredAmount);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("送金額が不足しています", result.ErrorMessage);
        Assert.Equal(depositedAmount, result.AmountJpyc);
    }

    [Fact]
    public async Task VerifyTransactionAsync_SimulatedTx_ExactMinimumAmount_ReturnsSuccess()
    {
        // Arrange (境界値: ちょうど最低金額)
        const string recipient = "0x1234567890abcdef1234567890abcdef12345678";
        const int amount = 250;

        string txHash = await _sut.SimulateDepositTransactionAsync(
            "polygon-amoy",
            recipient,
            amount);

        // Act
        JpycTransactionVerificationResult result = await _sut.VerifyTransactionAsync(
            "polygon-amoy",
            txHash,
            recipient,
            amount);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(amount, result.AmountJpyc);
    }

    [Fact]
    public async Task VerifyTransactionAsync_SimulatedTx_ExcessAmount_ReturnsSuccess()
    {
        // Arrange (最低金額を超える送金)
        const string recipient = "0x1234567890abcdef1234567890abcdef12345678";
        const int depositedAmount = 1000;
        const int minimumRequired = 500;

        string txHash = await _sut.SimulateDepositTransactionAsync(
            "polygon-amoy",
            recipient,
            depositedAmount);

        // Act
        JpycTransactionVerificationResult result = await _sut.VerifyTransactionAsync(
            "polygon-amoy",
            txHash,
            recipient,
            minimumRequired);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(depositedAmount, result.AmountJpyc);
    }

    [Fact]
    public async Task VerifyTransactionAsync_SimulatedTx_CaseInsensitiveHashLookup_ReturnsSuccess()
    {
        // Arrange
        const string recipient = "0x1234567890abcdef1234567890abcdef12345678";
        string txHash = await _sut.SimulateDepositTransactionAsync(
            "polygon-amoy",
            recipient,
            500);

        string upperHash = txHash.ToUpperInvariant();

        // Act
        JpycTransactionVerificationResult result = await _sut.VerifyTransactionAsync(
            "polygon-amoy",
            upperHash,
            recipient,
            500);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SimulateDepositTransactionAsync_WhenRecipientIsNullOrWhitespace_ThrowsArgumentException(string? invalidRecipient)
    {
        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _sut.SimulateDepositTransactionAsync("polygon-amoy", invalidRecipient!, 100));
    }

    [Fact]
    public async Task SimulateDepositTransactionAsync_ReturnsValidTxHashFormat()
    {
        // Act
        string txHash = await _sut.SimulateDepositTransactionAsync(
            "polygon-amoy",
            "0x1234567890abcdef1234567890abcdef12345678",
            100);

        // Assert
        Assert.StartsWith("0x", txHash);
        Assert.Equal(66, txHash.Length); // 0x + 64 hex chars
    }

    [Fact]
    public async Task SimulateDepositTransactionAsync_DefaultSender_UsesDefaultAddress()
    {
        // Arrange
        const string recipient = "0x1234567890abcdef1234567890abcdef12345678";

        // Act
        string txHash = await _sut.SimulateDepositTransactionAsync(
            "polygon-amoy",
            recipient,
            100);

        JpycTransactionVerificationResult result = await _sut.VerifyTransactionAsync(
            "polygon-amoy",
            txHash,
            recipient,
            100);

        // Assert
        Assert.Equal("0x70997970c51812dc3a010c7d01b50e0d17dc79c8", result.SenderAddress);
    }
}