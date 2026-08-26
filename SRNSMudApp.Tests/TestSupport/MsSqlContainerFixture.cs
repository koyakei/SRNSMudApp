using Testcontainers.MsSql;

using Xunit;

namespace SRNSMudApp.Tests.TestSupport;

/// <summary>
///     テストアセンブリ全体で 1 つだけ MSSQL Testcontainers コンテナを起動・共有する xUnit フィクスチャ。
/// </summary>
public class MsSqlContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException("MsSqlContainer is not initialized.");

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder().Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }
}