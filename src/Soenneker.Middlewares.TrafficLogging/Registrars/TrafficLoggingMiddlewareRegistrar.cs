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
    /// Set <c>TrafficLogging:EnableHeaderRedaction</c> in configuration to false to disable redaction (default is true).
    /// </summary>
    /// <param name="services">Service collection that receives the registration.</param>
    /// <returns>The same service collection, so additional registrations can be chained.</returns>
    public static IServiceCollection AddTrafficLoggingMiddlewareAsSingleton(this IServiceCollection services)
    {
        services.AddMemoryStreamUtilAsSingleton();

        return services;
    }

    /// <summary>
    /// Adds traffic logging for each request. Be careful! This logs the full HTTP request and response, including headers and body, using buffered memory streams. <para/>
    /// Be sure to register first via <code>AddTrafficLoggingMiddlewareAsSingleton()</code>
    /// </summary>
    /// <param name="builder">Builder to configure.</param>
    /// <returns>The same builder instance, so additional classes or variants can be chained.</returns>
    public static IApplicationBuilder UseTrafficLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TrafficLoggingMiddleware>();
    }
}
