#region

using System.Collections.Concurrent;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

#endregion

namespace SRNSMudApp.Tests;

public class MudPopoverIntegrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task NavigateToInteractivePages_ShouldNotLogPopoverError()
    {
        var logProvider = new TestLoggerProvider();
        HttpClient client = factory
            .WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logProvider)))
            .CreateClient();

        // Act - Navigate to the page that uses MudAutocomplete
        // /User/UserSearch は [Authorize] 保護されたため、MudAutocomplete を含む公開ページへ変更
        HttpResponseMessage response = await client.GetAsync("/Tag/TagSearch");
        _ = response.EnsureSuccessStatusCode();

        // The error might be logged asynchronously, but typically it happens during render.
        // Wait a small amount of time just in case.
        await Task.Delay(500);

        // Assert
        var popoverErrors = logProvider.Logs
            .Where(log => log.Contains("Missing <MudPopoverProvider />") || log.Contains("MudBlazor.PopoverService"))
            .ToList();

        Assert.Empty(popoverErrors);
    }
}

public class TestLoggerProvider : ILoggerProvider
{
    public ConcurrentBag<string> Logs { get; } = [];

    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, Logs);

    public void Dispose() => GC.SuppressFinalize(this);
}

public class TestLogger(string categoryName, ConcurrentBag<string> logs) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (formatter == null)
        {
            return;
        }

        _ = formatter(state, exception);
        logs.Add($"[{logLevel}] {categoryName}: {formatter(state, exception)}");
    }
}