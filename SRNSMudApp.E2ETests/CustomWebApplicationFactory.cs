#region

using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SRNSMudApp.Data;
using SRNSMudApp.Services.Auth;

using Testcontainers.MsSql;

#endregion

namespace SRNSMudApp.E2ETests;

public class MockExternalTokenVerificationService : IExternalTokenVerificationService
{
    public Task<(string? Email, string? ProviderKey)> VerifyTokenAsync(string provider, string token)
    {
        if (token.StartsWith("mock-"))
        {
            var providerKey = token.Replace("mock-", "") + "-id";
            var email = $"{token.Replace("mock-", "")}@example.com";
            return Task.FromResult<(string?, string?)>((email, providerKey));
        }

        return Task.FromResult<(string?, string?)>((null, null));
    }
}

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private string? _connectionString;

    private bool _disposed;
    private IHost? _host;
    private MsSqlContainer? _msSqlContainer;

    public string ServerAddress { get; private set; } = "http://localhost";

    // Test code should use AppServices to get the DI container of the running Kestrel app
    public IServiceProvider AppServices =>
        _host?.Services ?? throw new InvalidOperationException("Host is not built yet.");

    public void EnsureServer()
    {
        if (_host != null)
        {
            return;
        }

        try
        {
            // This triggers CreateHost, which builds Kestrel.
            // It will throw because we don't configure a TestServer, so we catch it.
            using HttpClient _ = CreateDefaultClient();
        }
        catch (InvalidOperationException)
        {
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_msSqlContainer == null)
        {
            _msSqlContainer = new MsSqlBuilder().Build();
            _msSqlContainer.StartAsync().GetAwaiter().GetResult();
            _connectionString = _msSqlContainer.GetConnectionString();
        }

        _ = builder.UseEnvironment("Testing");

        _ = builder.ConfigureServices(services =>
        {
            _ = services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            _ = services.RemoveAll<DbContextOptions>();
            _ = services.RemoveAll<IDbContextFactory<ApplicationDbContext>>();
            _ = services.RemoveAll<ApplicationDbContext>();

            _ = services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(_connectionString),
                ServiceLifetime.Scoped, ServiceLifetime.Singleton);

            _ = services.AddDbContextFactory<ApplicationDbContext>(options =>
                options.UseSqlServer(_connectionString));

            // Inject Mock ExternalTokenVerificationService
            _ = services.RemoveAll<IExternalTokenVerificationService>();
            _ = services.AddScoped<IExternalTokenVerificationService, MockExternalTokenVerificationService>();
        });

        _ = builder.ConfigureLogging(logging => logging.AddProvider(new FileLoggerProvider("/tmp/kestrel-test.log")));
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // 1. Build a dummy host to return so WebApplicationFactory is satisfied
        IHost dummyHost = builder.Build();

        IWebHostEnvironment dummyEnv = dummyHost.Services.GetRequiredService<IWebHostEnvironment>();
        _ = builder.UseContentRoot(dummyEnv.ContentRootPath);

        // 2. Add Kestrel and build the actual running host
        _ = builder.ConfigureWebHost(webHostBuilder =>
        {
            _ = webHostBuilder.UseSetting(WebHostDefaults.ApplicationKey, dummyEnv.ApplicationName);
            _ = webHostBuilder.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            _ = webHostBuilder.UseStaticWebAssets();
        });

        _host = builder.Build();
        _host.Start();

        EndpointDataSource endpoints = _host.Services.GetRequiredService<EndpointDataSource>();
        File.WriteAllLines("/tmp/endpoints.txt",
            endpoints.Endpoints.Select(e => e.GetType().Name + ": " + (e.DisplayName ?? "unknown")));


        IServer server = _host.Services.GetRequiredService<IServer>();
        IServerAddressesFeature? addresses = server.Features.Get<IServerAddressesFeature>();

        ClientOptions.BaseAddress = new Uri(addresses.Addresses
            .Select(x => x.Replace("127.0.0.1", "localhost").Replace("[::1]", "localhost"))
            .Last());

        ServerAddress = ClientOptions.BaseAddress.ToString().TrimEnd('/');

        return dummyHost;
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            try
            {
                _msSqlContainer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                /* Container disposal failures are non-critical during cleanup */
            }

            _host?.Dispose();
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    private class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _path;

        public FileLoggerProvider(string path)
        {
            _path = path;
            File.WriteAllText(_path, "");
        }

        public ILogger CreateLogger(string categoryName) => new FileLogger(_path, categoryName);
        public void Dispose() { }
    }

    private class FileLogger(string path, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var msg = $"{DateTime.Now:HH:mm:ss} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception != null)
            {
                msg += $"\n{exception}";
            }

            Console.WriteLine(msg);
            File.AppendAllText(path, msg + "\n");
        }
    }
}