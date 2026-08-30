using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Soenneker.Middlewares.TrafficLogging.Registrars;

/// <summary>
/// Registers HTTP traffic logging.
/// </summary>
public static class TrafficLoggingMiddlewareRegistrar
{
    /// <summary>
    /// Retained for source compatibility. Traffic logging does not require a service registration.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection.</returns>
    [Obsolete("No service registration is required. Call UseTrafficLogging() on the application builder.")]
    public static IServiceCollection AddTrafficLoggingMiddlewareAsSingleton(this IServiceCollection services) => services;

    /// <summary>
    /// Adds traffic logging to the application pipeline.
    /// </summary>
    /// <param name="builder">Application builder to configure.</param>
    /// <returns>The same builder instance, so additional middleware can be chained.</returns>
    public static IApplicationBuilder UseTrafficLogging(this IApplicationBuilder builder) => builder.UseMiddleware<TrafficLoggingMiddleware>();
}
