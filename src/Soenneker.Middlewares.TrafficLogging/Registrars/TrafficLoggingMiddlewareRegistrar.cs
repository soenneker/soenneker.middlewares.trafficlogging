using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Middlewares.TrafficLogging.Abstract;
using Soenneker.Utils.MemoryStream.Registrars;

namespace Soenneker.Middlewares.TrafficLogging.Registrars;

/// <summary>
/// Middleware that logs the full HTTP request and response, including headers and body, using buffered memory streams.
/// </summary>
public static class TrafficLoggingMiddlewareRegistrar
{
    /// <summary>
    /// Adds <see cref="ITrafficLoggingMiddleware"/> as a singleton service. <para/>
    /// </summary>
    public static IServiceCollection AddTrafficLoggingMiddlewareAsSingleton(this IServiceCollection services)
    {
        services.AddMemoryStreamUtilAsSingleton();

        return services;
    }

    /// <summary>
    /// Adds traffic logging for each request. Be careful! This logs the full HTTP request and response, including headers and body, using buffered memory streams. <para/>
    /// Be sure to register first via <code>AddTrafficLoggingMiddlewareAsSingleton()</code>
    /// </summary>
    public static IApplicationBuilder UseTrafficLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TrafficLoggingMiddleware>();
    }
}
