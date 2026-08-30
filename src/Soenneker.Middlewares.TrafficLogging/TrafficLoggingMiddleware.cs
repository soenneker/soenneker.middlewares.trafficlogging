using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.Stream;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Task;
using Soenneker.Middlewares.TrafficLogging.Abstract;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Soenneker.Middlewares.TrafficLogging;

public sealed class TrafficLoggingMiddleware : ITrafficLoggingMiddleware
{
    private const int _maxLoggedBodyBytes = 32 * 1024;
    private const long _maxReadableRequestBodyBytes = 5 * 1024 * 1024;

    private readonly RequestDelegate _next;
    private readonly ILogger<TrafficLoggingMiddleware> _logger;
    private readonly bool _logHeaders;
    private readonly bool _logQueryString;
    private readonly bool _logRequestBody;
    private readonly bool _logResponseBody;

    public TrafficLoggingMiddleware(RequestDelegate next, ILogger<TrafficLoggingMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _logHeaders = configuration.GetValue("TrafficLogging:LogHeaders", false);
        _logQueryString = configuration.GetValue("TrafficLogging:LogQueryString", false);
        _logRequestBody = configuration.GetValue("TrafficLogging:LogRequestBody", false);
        _logResponseBody = configuration.GetValue("TrafficLogging:LogResponseBody", false);
    }

    public async Task Invoke(HttpContext context)
    {
        if (context.WebSockets?.IsWebSocketRequest == true || !_logger.IsEnabled(LogLevel.Information))
        {
            await _next(context).NoSync();
            return;
        }

        await LogRequest(context).ConfigureAwait(false);

        Stream originalBody = context.Response.Body;
        PrefixCaptureStream? capture = null;

        if (_logResponseBody)
        {
            capture = new PrefixCaptureStream(originalBody, _maxLoggedBodyBytes);
            context.Response.Body = capture;
        }

        try
        {
            try
            {
                await _next(context).NoSync();
            }
            finally
            {
                context.Response.Body = originalBody;
            }

            LogResponse(context, capture);
        }
        finally
        {
            capture?.Dispose();
        }
    }

    private async ValueTask LogRequest(HttpContext context)
    {
        HttpRequest request = context.Request;
        string? body = null;
        long? bodyLength = request.ContentLength;

        if (_logRequestBody && ShouldReadBody(request.Method, request.ContentLength, request.ContentType))
        {
            request.EnableBuffering();

            (string bodyText, long? totalLength) = await request.Body.ReadTextUpTo(_maxLoggedBodyBytes, context.RequestAborted).ConfigureAwait(false);
            body = TrafficLogSanitizer.Sanitize(bodyText, _maxLoggedBodyBytes);
            bodyLength = totalLength ?? bodyLength;

            if (request.Body.CanSeek)
                request.Body.Position = 0;
        }

        _logger.LogInformation(
            "HTTP Request {Method} {Scheme}://{Host}{Path} Query:{QueryString} Headers:{@Headers} BodyLength:{BodyLength} Body:{Body}",
            request.Method, TrafficLogSanitizer.Sanitize(request.Scheme), TrafficLogSanitizer.Sanitize(request.Host.Value),
            TrafficLogSanitizer.Sanitize(request.Path.Value), GetQueryString(request), GetHeaders(request.Headers), bodyLength, body);
    }

    private void LogResponse(HttpContext context, PrefixCaptureStream? capture)
    {
        HttpResponse response = context.Response;
        string? body = null;

        if (capture is not null && response.StatusCode is not (StatusCodes.Status204NoContent or StatusCodes.Status304NotModified) &&
            LooksTextLike(response.ContentType))
        {
            body = TrafficLogSanitizer.Sanitize(Decode(capture.Captured, GetEncoding(response.ContentType)), _maxLoggedBodyBytes);
        }

        _logger.LogInformation(
            "HTTP Response {Method} {Scheme}://{Host}{Path} Status:{StatusCode} Query:{QueryString} Headers:{@Headers} BodyLength:{BodyLength} Body:{Body}",
            context.Request.Method, TrafficLogSanitizer.Sanitize(context.Request.Scheme), TrafficLogSanitizer.Sanitize(context.Request.Host.Value),
            TrafficLogSanitizer.Sanitize(context.Request.Path.Value), response.StatusCode, GetQueryString(context.Request), GetHeaders(response.Headers),
            capture?.TotalBytesWritten ?? response.ContentLength, body);
    }

    private Dictionary<string, string>? GetHeaders(IHeaderDictionary headers) => _logHeaders ? TrafficLogHeaderRedactor.Redact(headers) : null;

    private string? GetQueryString(HttpRequest request) =>
        _logQueryString ? TrafficLogSanitizer.Sanitize(request.QueryString.Value) : null;

    private static bool ShouldReadBody(string method, long? contentLength, string? contentType)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsDelete(method) || HttpMethods.IsTrace(method))
            return false;

        if (contentLength == 0 || contentLength is > _maxReadableRequestBodyBytes)
            return false;

        return LooksTextLike(contentType);
    }

    private static bool LooksTextLike(string? contentType)
    {
        if (contentType.IsNullOrEmpty())
            return false;

        ReadOnlySpan<char> mediaType = contentType.AsSpan();
        int semi = mediaType.IndexOf(';');
        if (semi >= 0)
            mediaType = mediaType[..semi];

        mediaType = mediaType.Trim();

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (mediaType.Equals("application/json".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/problem+json".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/xml".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/x-www-form-urlencoded".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return true;

        int plus = mediaType.LastIndexOf('+');
        if (plus <= 0)
            return false;

        ReadOnlySpan<char> suffix = mediaType[(plus + 1)..];
        return suffix.Equals("json".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals("xml".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static Encoding GetEncoding(string? contentType)
    {
        if (contentType.IsNullOrEmpty())
            return Encoding.UTF8;

        ReadOnlySpan<char> value = contentType.AsSpan();
        int index = value.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return Encoding.UTF8;

        ReadOnlySpan<char> charset = value[(index + "charset=".Length)..];
        int semi = charset.IndexOf(';');
        if (semi >= 0)
            charset = charset[..semi];

        charset = charset.Trim().Trim('"');
        if (charset.Length == 0)
            return Encoding.UTF8;

        try
        {
            return Encoding.GetEncoding(charset.ToString());
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static string Decode(ReadOnlySpan<byte> bytes, Encoding encoding) => encoding.GetString(bytes);
}
