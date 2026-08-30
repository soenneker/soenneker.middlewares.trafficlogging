using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Soenneker.Middlewares.TrafficLogging.Abstract;

/// <summary>
/// Logs HTTP request and response metadata with optional capped payload capture.
/// </summary>
public interface ITrafficLoggingMiddleware
{
    /// <summary>
    /// Invokes traffic logging for the supplied HTTP context.
    /// </summary>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>A task that completes with the remaining request pipeline.</returns>
    Task Invoke(HttpContext context);
}
