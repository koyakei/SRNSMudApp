using Microsoft.EntityFrameworkCore;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.TestSupport;

/// <summary>
/// 各テストメソッドが独立したスコープ（DbContext、Service、一意な識別子）を持つためのヘルパー。
/// </summary>
public sealed record TaggingTestScope(
    ApplicationDbContext DbContext,
    TaggingContractService Service,
    string Tid) : IAsyncDisposable
{
    public void Deconstruct(out ApplicationDbContext dbContext, out TaggingContractService service, out string tid)
    {
        dbContext = DbContext;
        service = Service;
        tid = Tid;
    }

    public ValueTask DisposeAsync() => DbContext.DisposeAsync();
}

/// <summary>
/// 並列実行に対応した TaggingContractService 統合テスト共通基底クラス。
/// 共通の SharedMsSqlTestDatabase を共有し、各テストは Tid による完全データ分離（Namespacing）で競合なく実行する。
/// </summary>
public abstract class TaggingContractTestBase : IAsyncLifetime
{
    protected MsSqlTestDatabase SharedDb { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        SharedDb = await SharedMsSqlTestDatabase.GetInstanceAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// テスト固有の一意な名前空間を持つスコープを生成する。
    /// </summary>
    protected TaggingTestScope CreateTestScope()
    {
        var tid = Guid.NewGuid().ToString("N")[..8];
        var ctx = new ApplicationDbContext(SharedDb.Options);
        var svc = new TaggingContractService(ctx);
        return new TaggingTestScope(ctx, svc, tid);
    }
}
