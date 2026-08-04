using CasePriority.Api.Configuration;
using CasePriority.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CasePriority.Api.Tests;

/// <summary>
/// Unit test for the piece the HTTP tests can't see: the correlation ID is also
/// opened as a logging scope, so logs produced during the request carry it.
/// </summary>
public sealed class CorrelationIdMiddlewareTests
{
    private sealed class ScopeRecordingLogger<T> : ILogger<T>
    {
        public List<object?> Scopes { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            Scopes.Add(state);
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        { }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task Invoke_OpensLoggingScope_WithCorrelationId()
    {
        var logger = new ScopeRecordingLogger<CorrelationIdMiddleware>();
        var options = Options.Create(
            new RequestTracingOptions { HeaderName = "X-Correlation-ID", MaxLength = 64 });
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, options, logger);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = "support-ticket-123";

        await middleware.InvokeAsync(context);

        Assert.Equal("support-ticket-123", context.TraceIdentifier);

        var scope = Assert.Single(logger.Scopes);
        var fields = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(scope);
        Assert.Contains(fields, kv => kv.Key == "CorrelationId" && (string?)kv.Value == "support-ticket-123");
    }
}
