using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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
    private const int _maxLoggedBodyBytes = 32 * 1024;
    private const long _maxReadableRequestBodyBytes = 5 * 1024 * 1024;

    private readonly RequestDelegate _next;
    private readonly ILogger<TrafficLoggingMiddleware> _logger;
    private readonly IMemoryStreamUtil _memoryStreamUtil;
    private readonly bool _enableHeaderRedaction;

    public TrafficLoggingMiddleware(RequestDelegate next, ILogger<TrafficLoggingMiddleware> logger, IMemoryStreamUtil memoryStreamUtil,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _memoryStreamUtil = memoryStreamUtil;
        _enableHeaderRedaction = configuration.GetValue("TrafficLogging:EnableHeaderRedaction", true);
    }

    public async Task Invoke(HttpContext context)
    {
        if (context.WebSockets?.IsWebSocketRequest == true)
        {
            await _next(context)
                .NoSync();
            return;
        }

        if (!_logger.IsEnabled(LogLevel.Information))
        {
            await _next(context)
                .NoSync();
            return;
        }

        await LogRequest(context)
            .NoSync();
        await LogResponse(context)
            .NoSync();
    }

    private async ValueTask LogRequest(HttpContext context)
    {
        HttpRequest req = context.Request;

        string scheme = TrafficLogSanitizer.Sanitize(req.Scheme) ?? string.Empty;
        string host = TrafficLogSanitizer.Sanitize(req.Host.Value) ?? string.Empty;
        string path = TrafficLogSanitizer.Sanitize(req.Path.Value) ?? string.Empty;
        string queryString = TrafficLogSanitizer.Sanitize(req.QueryString.Value) ?? string.Empty;

        if (!ShouldReadBody(req.Method, req.ContentLength, req.ContentType))
        {
            _logger.LogInformation("HTTP Request: {Scheme} {Host} {Path} {QueryString} Status:{Status} {@Headers}", scheme, host, path, queryString,
                context.Response.StatusCode, TrafficLogHeaderRedactor.Redact(req.Headers, enableRedaction: _enableHeaderRedaction));

            return;
        }

        req.EnableBuffering();

        (string bodyText, long? totalLength) = await req.Body.ReadTextUpTo(_maxLoggedBodyBytes)
                                                        .NoSync();

        if (req.Body.CanSeek)
            req.Body.Position = 0;

        _logger.LogInformation(
            "HTTP Request: {Scheme} {Host} {Path} {QueryString} Status:{Status} {@Headers} BodyLength:{BodyLength} Body(TruncatedTo:{Cap}): {Body}", scheme,
            host, path, queryString, context.Response.StatusCode, TrafficLogHeaderRedactor.Redact(req.Headers, enableRedaction: _enableHeaderRedaction),
            totalLength, _maxLoggedBodyBytes, TrafficLogSanitizer.Sanitize(bodyText, _maxLoggedBodyBytes));
    }

    private async ValueTask LogResponse(HttpContext context)
    {
        Stream originalBody = context.Response.Body;

        await using MemoryStream responseBuffer = await _memoryStreamUtil.Get()
                                                                         .NoSync();
        context.Response.Body = responseBuffer;

        try
        {
            await _next(context)
                .NoSync();
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        string scheme = TrafficLogSanitizer.Sanitize(context.Request.Scheme) ?? string.Empty;
        string host = TrafficLogSanitizer.Sanitize(context.Request.Host.Value) ?? string.Empty;
        string path = TrafficLogSanitizer.Sanitize(context.Request.Path.Value) ?? string.Empty;
        string queryString = TrafficLogSanitizer.Sanitize(context.Request.QueryString.Value) ?? string.Empty;

        string? responseText = null;
        string? contentType = context.Response.ContentType;
        long bodyLength = responseBuffer.Length;

        if (context.Response.StatusCode is not (StatusCodes.Status204NoContent or StatusCodes.Status304NotModified) && bodyLength > 0 &&
            LooksTextLike(contentType))
        {
            responseBuffer.Position = 0;

            Encoding encoding = GetEncoding(contentType);

            if (responseBuffer.TryGetBuffer(out ArraySegment<byte> seg))
            {
                int len = (int)Math.Min(seg.Count, _maxLoggedBodyBytes);
                responseText = Decode(seg.AsSpan(0, len), encoding);
            }
            else
            {
                byte[] rented = ArrayPool<byte>.Shared.Rent(_maxLoggedBodyBytes);

                try
                {
                    int read = await responseBuffer.ReadAsync(rented.AsMemory(0, _maxLoggedBodyBytes), context.RequestAborted)
                                                   .NoSync();
                    responseText = Decode(rented.AsSpan(0, read), encoding);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

        _logger.LogInformation(
            "HTTP Response: {Scheme} {Host} {Path} Status:{Status} {QueryString} {@Headers} BodyLength:{BodyLength} Body(TruncatedTo:{Cap}): {Body}", scheme,
            host, path, context.Response.StatusCode, queryString,
            TrafficLogHeaderRedactor.Redact(context.Response.Headers, enableRedaction: _enableHeaderRedaction), bodyLength, _maxLoggedBodyBytes,
            TrafficLogSanitizer.Sanitize(responseText, _maxLoggedBodyBytes));

        responseBuffer.Position = 0;
        await responseBuffer.CopyToAsync(originalBody, context.RequestAborted)
                            .NoSync();
    }

    private static bool ShouldReadBody(string method, long? contentLength, string? contentType)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsDelete(method) || HttpMethods.IsTrace(method))
            return false;

        if (contentLength == 0)
            return false;

        if (contentLength is > _maxReadableRequestBodyBytes)
            return false;

        return LooksTextLike(contentType);
    }

    private static bool LooksTextLike(string? contentType)
    {
        if (contentType.IsNullOrEmpty())
            return false;

        ReadOnlySpan<char> s = contentType.AsSpan();

        int semi = s.IndexOf(';');
        if (semi >= 0)
            s = s[..semi];

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
            ReadOnlySpan<char> suffix = s[(plus + 1)..];
            if (suffix.Equals("json".AsSpan(), StringComparison.OrdinalIgnoreCase) || suffix.Equals("xml".AsSpan(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Encoding GetEncoding(string? contentType)
    {
        if (contentType.IsNullOrEmpty())
            return Encoding.UTF8;

        ReadOnlySpan<char> s = contentType.AsSpan();
        int idx = s.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return Encoding.UTF8;

        ReadOnlySpan<char> rest = s[(idx + "charset=".Length)..];
        int semi = rest.IndexOf(';');
        if (semi >= 0)
            rest = rest[..semi];

        rest = rest.Trim();

        if (rest.Length == 0)
            return Encoding.UTF8;

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