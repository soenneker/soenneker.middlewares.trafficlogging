using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soenneker.Tests.HostedUnit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Soenneker.Middlewares.TrafficLogging.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public class TrafficLoggingMiddlewareTests : HostedUnitTest
{
    public TrafficLoggingMiddlewareTests(Host host) : base(host)
    {
    }

    [Test]
    public async Task Response_body_larger_than_capture_limit_is_fully_forwarded()
    {
        byte[] payload = new byte[64 * 1024];
        Random.Shared.NextBytes(payload);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TrafficLogging:LogResponseBody"] = "true"
            })
            .Build();

        RequestDelegate next = async context =>
        {
            context.Response.ContentType = "application/octet-stream";
            await context.Response.Body.WriteAsync(payload);
        };

        var middleware = new TrafficLoggingMiddleware(next, new EnabledLogger<TrafficLoggingMiddleware>(), configuration);
        var context = new DefaultHttpContext();
        await using var destination = new MemoryStream();
        context.Response.Body = destination;

        await middleware.Invoke(context);

        if (!destination.ToArray().AsSpan().SequenceEqual(payload))
            throw new InvalidOperationException("The response capture stream did not forward the complete response body.");
    }

    private sealed class EnabledLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
