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

/// <inheritdoc cref="ITrafficLoggingMiddleware"/>
public sealed class TrafficLoggingMiddleware : ITrafficLoggingMiddleware
{
    private const int _maxLoggedBodyBytes = 32 * 1024; // 32KB cap to protect memory/logs

    private static readonly string[] _textLikeContentTypes =
    [
        "application/json",
        "application/problem+json",
        "application/xml",
        "application/*+json",
        "text/",
        "application/x-www-form-urlencoded"
    ];

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
        await LogRequest(context).NoSync();
        await LogResponse(context).NoSync();
    }

    private async ValueTask LogRequest(HttpContext context)
    {
        HttpRequest? req = context.Request;

        // Skip bodies we shouldn't touch
        if (context.WebSockets?.IsWebSocketRequest == true)
            return;

        // Read/rewind only when there's a body, and it looks text-like and method generally has a body
        if (!ShouldReadBody(req.Method, req.ContentLength, req.ContentType))
        {
            _logger.LogInformation("HTTP Request: {Scheme} {Host} {Path} {QueryString} Status:{Status} {@Headers}", req.Scheme, req.Host, req.Path,
                req.QueryString.ToString(), context.Response.StatusCode, req.Headers);
            return;
        }

        req.EnableBuffering(); // buffers in memory/disk internally

        // Read directly from the request stream (no extra MemoryStream copy)
        (string bodyText, long? totalLength) = await ReadTextUpToAsync(req.Body, _maxLoggedBodyBytes).NoSync();

        // Rewind for the rest of the pipeline
        req.Body.Position = 0;

        _logger.LogInformation(
            "HTTP Request: {Scheme} {Host} {Path} {QueryString} Status:{Status} {@Headers} BodyLength:{BodyLength} Body(TruncatedTo:{Cap}): {Body}", req.Scheme,
            req.Host, req.Path, req.QueryString.ToString(), context.Response.StatusCode, req.Headers, totalLength, _maxLoggedBodyBytes, bodyText);
    }

    private async ValueTask LogResponse(HttpContext context)
    {
        Stream? originalBody = context.Response.Body;

        await using MemoryStream responseBuffer = await _memoryStreamUtil.Get().NoSync();
        context.Response.Body = responseBuffer;

        try
        {
            await _next(context).NoSync();

            // Only attempt to decode if it looks text-like
            string? responseText = null;
            long bodyLength = responseBuffer.Length;

            if (LooksTextLike(context.Response.ContentType))
            {
                responseBuffer.ToStart();

                if (responseBuffer.TryGetBuffer(out ArraySegment<byte> seg))
                {
                    int len = Math.Min(seg.Count, _maxLoggedBodyBytes);
                    responseText = Decode(seg.AsSpan(0, len), GetEncoding(context.Response.ContentType));
                }
                else
                {
                    // Fallback path; copies but capped
                    byte[] rented = ArrayPool<byte>.Shared.Rent(_maxLoggedBodyBytes);
                    try
                    {
                        int read = await responseBuffer.ReadAsync(rented, 0, _maxLoggedBodyBytes).NoSync();
                        responseText = Decode(new ReadOnlySpan<byte>(rented, 0, read), GetEncoding(context.Response.ContentType));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }

                responseBuffer.ToStart();
            }

            // Structured logging (no JSON stringify)
            _logger.LogInformation(
                "HTTP Response: {Scheme} {Host} {Path} Status:{Status} {QueryString} {@Headers} BodyLength:{BodyLength} Body(TruncatedTo:{Cap}): {Body}",
                context.Request.Scheme, context.Request.Host, context.Request.Path, context.Response.StatusCode, context.Request.QueryString.ToString(),
                context.Response.Headers, bodyLength, _maxLoggedBodyBytes, responseText);

            // Copy buffered response to the real body stream
            responseBuffer.ToStart();
            await responseBuffer.CopyToAsync(originalBody).NoSync();
        }
        finally
        {
            context.Response.Body = originalBody; // always restore
        }
    }

    private static bool ShouldReadBody(string method, long? contentLength, string? contentType)
    {
        // Methods that usually have no body
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsDelete(method) || HttpMethods.IsTrace(method))
            return false;

        // Skip huge bodies
        if (contentLength is > 5 * 1024 * 1024) // 5MB guardrail (tune as needed)
            return false;

        return LooksTextLike(contentType);
    }

    private static bool LooksTextLike(string? contentType)
    {
        if (contentType.IsNullOrEmpty())
            return false;

        foreach (string prefix in _textLikeContentTypes)
        {
            if (contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Encoding GetEncoding(string? contentType)
    {
        // very light-weight charset sniffing; defaults to UTF8
        // e.g. "application/json; charset=utf-16"
        if (contentType is null)
            return Encoding.UTF8;

        int idx = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            string charset = contentType[(idx + "charset=".Length)..].Trim().TrimEnd(';').Trim();
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch
            {
                /* ignored */
            }
        }

        return Encoding.UTF8;
    }

    private static string Decode(ReadOnlySpan<byte> bytes, Encoding encoding) => encoding.GetString(bytes);

    private static async ValueTask<(string text, long? totalLength)> ReadTextUpToAsync(Stream s, int cap)
    {
        // Read up to cap bytes without holding large strings/buffers
        byte[] rented = ArrayPool<byte>.Shared.Rent(cap);
        try
        {
            var totalRead = 0;

            while (totalRead < cap)
            {
                int read = await s.ReadAsync(rented, totalRead, cap - totalRead).NoSync();

                if (read == 0)
                    break;

                totalRead += read;
            }

            // Try to get total length if stream supports it
            long? totalLength = null;

            try
            {
                if (s.CanSeek) totalLength = s.Length;
            }
            catch
            {
                /* ignore */
            }

            Encoding encoding = Encoding.UTF8; // request charset rarely set; default to UTF-8
            string text = encoding.GetString(rented, 0, totalRead);
            return (text, totalLength);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}