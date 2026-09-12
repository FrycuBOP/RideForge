using System.Collections.Concurrent;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RideForgeApi.Tests;

/// <summary>
/// A signed-in host whose database refuses connections on the spot (port 1 on loopback), with a log
/// sink so a test can see what the API wrote about the failure.
/// <para>
/// The dead port is the point, not a compromise. It makes every hermetic case meaningful in both
/// directions: a request validation should refuse comes back 400, and one that slips past validation
/// reaches the database and comes back 503 — it cannot pass by accident. Shared by the save and read
/// suites, and in its own file for the same reason <see cref="RideForgeApiFactory"/> and
/// <see cref="PostgresApiFactory"/> are: a fixture two classes depend on should not live inside one
/// of them.
/// </para>
/// </summary>
public class UnreachableDatabaseFactory : AuthenticatedApiFactory
{
    public const string DatabaseHost = "127.0.0.1";

    /// <summary>Not a credential for anything; distinctive so a leak is unmistakable.</summary>
    public const string DatabasePassword = "leak-canary-5b1f";

    public ConcurrentQueue<string> Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting(
            "ConnectionStrings:RideForge",
            $"Host={DatabaseHost};Port=1;Database=rideforge;Username=rideforge_api;Password={DatabasePassword}");

        builder.ConfigureTestServices(services =>
            services.AddSingleton<ILoggerProvider>(new CapturingLoggerProvider(Logs)));
    }

    /// <summary>Collects each log line, with its exception rendered in full, as the host emits it.</summary>
    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                sink.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
        }
    }
}
