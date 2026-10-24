#region

using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services.Contracts;

#endregion

namespace SRNSMudApp.Tests.Services.Contracts;

/// <summary>
///     <see cref="TriggerContractExecutor" /> のドメインバリデーションロジックに対する高速な分離単体テスト。
///     DB 接続や Testcontainers を介さずに、引数検証および基本契約種別をテストする。
/// </summary>
public sealed class TriggerContractExecutorUnitTests
{
    private readonly ApplicationDbContext _dummyContext = new(new DbContextOptions<ApplicationDbContext>());
    private readonly TriggerContractExecutor _executor = new();

    [Fact]
    public void ContractType_ReturnsTrigger()
    {
        // Assert
        Assert.Equal(ContractTypes.Trigger, _executor.ContractType);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDbContextIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var contract = new TaggingRequestEntity { OwnerId = "user1" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _executor.ExecuteAsync(null!, contract, "user1"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenContractIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _executor.ExecuteAsync(_dummyContext, null!, "user1"));
    }
}