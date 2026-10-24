#region

using SRNSMudApp.Services.Contracts;

#endregion

namespace SRNSMudApp.Tests.Services.Contracts;

/// <summary>
///     <see cref="ContractExecutorFactory" /> の単体テスト。
///     登録されたエグゼキューターの種別解決および大文字・小文字不問の安全な取得をテストする。
/// </summary>
public sealed class ContractExecutorFactoryUnitTests
{
    private readonly GratisContractExecutor _gratis = new();
    private readonly MutualContractExecutor _mutual = new();
    private readonly TriggerContractExecutor _trigger = new();
    private readonly BountyContractExecutor _bounty = new();
    private readonly MoveContractExecutor _move = new();

    private ContractExecutorFactory CreateFactory() =>
        new([_gratis, _mutual, _trigger, _bounty, _move]);

    [Fact]
    public void Constructor_WhenExecutorsIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ContractExecutorFactory(null!));
    }

    [Theory]
    [InlineData(ContractTypes.Gratis)]
    [InlineData(ContractTypes.Mutual)]
    [InlineData(ContractTypes.Trigger)]
    [InlineData(ContractTypes.Bounty)]
    [InlineData(ContractTypes.Move)]
    public void GetExecutor_WhenMatchingTypeExists_ReturnsCorrectExecutor(string contractType)
    {
        // Arrange
        ContractExecutorFactory factory = CreateFactory();

        // Act
        IContractExecutor? executor = factory.GetExecutor(contractType);

        // Assert
        Assert.NotNull(executor);
        Assert.Equal(contractType, executor.ContractType);
    }

    [Theory]
    [InlineData("gratis", ContractTypes.Gratis)]
    [InlineData("MUTUAL", ContractTypes.Mutual)]
    [InlineData("tRiGgEr", ContractTypes.Trigger)]
    [InlineData("bounty", ContractTypes.Bounty)]
    [InlineData("MOVE", ContractTypes.Move)]
    public void GetExecutor_IsCaseInsensitive(string inputType, string expectedType)
    {
        // Arrange
        ContractExecutorFactory factory = CreateFactory();

        // Act
        IContractExecutor? executor = factory.GetExecutor(inputType);

        // Assert
        Assert.NotNull(executor);
        Assert.Equal(expectedType, executor.ContractType);
    }

    [Fact]
    public void GetExecutor_WhenUnknownType_ReturnsNull()
    {
        // Arrange
        ContractExecutorFactory factory = CreateFactory();

        // Act
        IContractExecutor? executor = factory.GetExecutor("UnknownContractType");

        // Assert
        Assert.Null(executor);
    }
}