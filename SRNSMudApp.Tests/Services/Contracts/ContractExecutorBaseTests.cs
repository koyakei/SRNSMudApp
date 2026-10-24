using Microsoft.EntityFrameworkCore;

using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Contracts;

namespace SRNSMudApp.Tests.Services.Contracts;

/// <summary>
///     <see cref="ContractExecutorBase" /> の Template Method パターン検証単体テスト。
/// </summary>
public sealed class ContractExecutorBaseTests
{
    private sealed class TestContractExecutor(TimeProvider? timeProvider = null)
        : ContractExecutorBase(timeProvider)
    {
        public override string ContractType => "TestType";

        public TimeProvider ExposedTimeProvider => TimeProvider;

        protected override Task<Result<string>> ExecuteCoreAsync(
            ApplicationDbContext dbContext,
            TaggingRequestEntity contract,
            string currentUserId,
            int? fulfillerAssetId)
        {
            return Task.FromResult<Result<string>>(new Success<string>("Executed"));
        }
    }

    private readonly ApplicationDbContext _dummyContext = new(new DbContextOptions<ApplicationDbContext>());

    [Fact]
    public async Task ExecuteAsync_WhenDbContextIsNull_ThrowsArgumentNullException()
    {
        var executor = new TestContractExecutor();
        var contract = new TaggingRequestEntity { OwnerId = "user1" };

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            executor.ExecuteAsync(null!, contract, "user1"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenContractIsNull_ThrowsArgumentNullException()
    {
        var executor = new TestContractExecutor();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            executor.ExecuteAsync(_dummyContext, null!, "user1"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidArguments_CallsExecuteCoreAsync()
    {
        var executor = new TestContractExecutor();
        var contract = new TaggingRequestEntity { OwnerId = "user1" };

        var result = await executor.ExecuteAsync(_dummyContext, contract, "user1");

        Assert.True(result switch
        {
            Success<string> s when s.Value == "Executed" => true,
            _ => false
        });
    }

    [Fact]
    public void Constructor_DefaultTimeProvider_UsesTimeProviderSystem()
    {
        var executor = new TestContractExecutor();
        Assert.Same(TimeProvider.System, executor.ExposedTimeProvider);
    }
}