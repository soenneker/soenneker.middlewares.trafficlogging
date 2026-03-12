using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.Stream;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Middlewares.TrafficLogging.Abstract;
using Soenneker.Utils.MemoryStream.Abstract;
using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Soenneker.Extensions.String;

namespace Soenneker.Middlewares.TrafficLogging;

/// <summary>
/// Logs inbound requests and outbound responses with bounded bodies for performance and safety.
/// </summary>
public sealed class TrafficLoggingMiddleware : ITrafficLoggingMiddleware
{
    private const int _maxLoggedBodyBytes = 32 * 1024; // 32KB cap to protect memory/logs

    private readonly RequestDelegate _next;
    private readonly ILogger<TrafficLoggingMiddleware> _logger;
    private readonly IMemoryStreamUtil _memoryStreamUtil;

    public TrafficLoggingMiddleware(RequestDelegate next, ILogger<TrafficLoggingMiddleware> logger, IMemoryStreamUtil memoryStreamUtil)
    {
        _next = next;
        _logger = logger;
        _memoryStreamUtil = memoryStreamUtil;
    }

    public async Task Invoke(HttpContext context)
    {
        // Skip entirely for WebSocket upgrades
        if (context.WebSockets?.IsWebSocketRequest == true)
        {
            await _next(context).NoSync();
            return;
        }

        await LogRequest(context).NoSync();
        await LogResponse(context).NoSync();
    }

    private async ValueTask LogRequest(HttpContext context)
    {
        HttpRequest req = context.Request;

        if (!ShouldReadBody(req.Method, req.ContentLength, req.ContentType))
        {
            _logger.LogInformation("HTTP Request: {Scheme} {Host} {Path} {QueryString} Status:{Status} {@Headers}", req.Scheme, req.Host, req.Path,
                req.QueryString.Value ?? string.Empty, context.Response.StatusCode, req.Headers);
            return;
        }

        req.EnableBuffering(); // internal buffering (memory/disk) managed by ASP.NET Core

        (string bodyText, long? totalLength) = await req.Body.ReadTextUpTo(_maxLoggedBodyBytes).NoSync();

        if (req.Body.CanSeek)
            req.Body.Position = 0;

        _logger.LogInformation(
            "HTTP Request: {Scheme} {Host} {Path} {QueryString} Status:{Status} {@Headers} BodyLength:{BodyLength} Body(TruncatedTo:{Cap}): {Body}", req.Scheme,
            req.Host, req.Path, req.QueryString.Value ?? string.Empty, context.Response.StatusCode, req.Headers, totalLength, _maxLoggedBodyBytes, bodyText);
    }

    private async ValueTask LogResponse(HttpContext context)
    {
        Stream originalBody = context.Response.Body;

        // Use your recyclable memory stream util to buffer response
        await using MemoryStream responseBuffer = await _memoryStreamUtil.Get().NoSync();
        context.Response.Body = responseBuffer;

        try
        {
            await _next(context).NoSync();
        }
        finally
        {
            // Always restore original body
            context.Response.Body = originalBody;
        }

        string? responseText = null;
        string? contentType = context.Response.ContentType;

        // 204/304 have no bodies by definition
        if (context.Response.StatusCode is not (StatusCodes.Status204NoContent or StatusCodes.Status304NotModified) && LooksTextLike(contentType))
        {
            responseBuffer.ToStart();

            if (responseBuffer.TryGetBuffer(out ArraySegment<byte> seg))
            {
                int len = Math.Min(seg.Count, _maxLoggedBodyBytes);
                responseText = Decode(seg.AsSpan(0, len), GetEncoding(contentType));
            }
            else
            {
                byte[] rented = ArrayPool<byte>.Shared.Rent(_maxLoggedBodyBytes);
                try
                {
                    int read = await responseBuffer.ReadAsync(rented, 0, _maxLoggedBodyBytes).NoSync();
                    responseText = Decode(new ReadOnlySpan<byte>(rented, 0, read), GetEncoding(contentType));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

        long bodyLength = responseBuffer.Length;

        _logger.LogInformation(
            "HTTP Response: {Scheme} {Host} {Path} Status:{Status} {QueryString} {@Headers} BodyLength:{BodyLength} Body(TruncatedTo:{Cap}): {Body}",
            context.Request.Scheme, context.Request.Host, context.Request.Path, context.Response.StatusCode, context.Request.QueryString.Value ?? string.Empty,
            context.Response.Headers, bodyLength, _maxLoggedBodyBytes, responseText);

        // Copy buffered response to the actual stream
        responseBuffer.ToStart();
        await responseBuffer.CopyToAsync(originalBody, context.RequestAborted).NoSync();
    }

    private static bool ShouldReadBody(string method, long? contentLength, string? contentType)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsDelete(method) || HttpMethods.IsTrace(method))
            return false;

        if (contentLength == 0)
            return false;

        if (contentLength is > 5 * 1024 * 1024)
            return false;

        return LooksTextLike(contentType);
    }

    private static bool LooksTextLike(string? contentType)
    {
        if (contentType.IsNullOrEmpty())
            return false;

        ReadOnlySpan<char> s = contentType.AsSpan();

        int semi = s.IndexOf(';');
        if (semi >= 0) s = s.Slice(0, semi);
        s = s.Trim();

        if (s.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (s.Equals("application/json".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            s.Equals("application/problem+json".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            s.Equals("application/xml".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            s.Equals("application/x-www-form-urlencoded".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return true;

        int plus = s.LastIndexOf('+');
        if (plus > 0)
        {
            ReadOnlySpan<char> suffix = s.Slice(plus + 1);
            if (suffix.Equals("json".AsSpan(), StringComparison.OrdinalIgnoreCase) || suffix.Equals("xml".AsSpan(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to determine the correct <see cref="Encoding"/> from a response or request
    /// <c>Content-Type</c> header string.
    /// </summary>
    /// <param name="contentType">
    /// The Content-Type header value, such as <c>"application/json; charset=utf-16"</c>.
    /// If <c>null</c> or no valid <c>charset</c> is specified, UTF-8 is returned.
    /// </param>
    /// <returns>
    /// A <see cref="Encoding"/> instance representing the declared <c>charset</c>, or
    /// <see cref="Encoding.UTF8"/> if no charset is specified or the charset is invalid.
    /// </returns>
    private static Encoding GetEncoding(string? contentType)
    {
        if (contentType.IsNullOrEmpty())
            return Encoding.UTF8;

        ReadOnlySpan<char> s = contentType.AsSpan();
        int idx = s.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return Encoding.UTF8;

        ReadOnlySpan<char> rest = s.Slice(idx + "charset=".Length);
        int semi = rest.IndexOf(';');
        if (semi >= 0) rest = rest.Slice(0, semi);
        rest = rest.Trim();

        try
        {
            return Encoding.GetEncoding(rest.ToString());
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static string Decode(ReadOnlySpan<byte> bytes, Encoding encoding) => encoding.GetString(bytes);
}