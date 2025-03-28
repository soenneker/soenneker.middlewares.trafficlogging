using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.Stream;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Middlewares.TrafficLogging.Abstract;
using Soenneker.Utils.Json;
using Soenneker.Utils.MemoryStream.Abstract;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Soenneker.Middlewares.TrafficLogging;

/// <inheritdoc cref="ITrafficLoggingMiddleware"/>
public class TrafficLoggingMiddleware : ITrafficLoggingMiddleware
{
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
        context.Request.EnableBuffering();
        await using MemoryStream requestStream = await _memoryStreamUtil.Get().NoSync();

        //Copy the contents of the new memory stream (which contains the response) to the original stream, which is then returned to the client.
        await context.Request.Body.CopyToAsync(requestStream).NoSync();

        _logger.LogInformation("""
                               HTTP Request Information: {newLine}
                                    Schema: {scheme}
                                    Host: {host} 
                                    Path: {path} 
                                    Status: {status} 
                                    QueryString: {queryString} 
                                    Request Body: {body}
                                    Headers: {headers}
                               """, Environment.NewLine, context.Request.Scheme, context.Request.Host, context.Request.Path, context.Response.StatusCode,
            context.Request.QueryString, JsonUtil.Serialize(context.Request.Headers), ReadStreamInChunks(requestStream));

        context.Request.Body.Position = 0;
    }

    private async ValueTask LogResponse(HttpContext context)
    {
        Stream originalBodyStream = context.Response.Body;
        await using MemoryStream responseBody = await _memoryStreamUtil.Get().NoSync();
        context.Response.Body = responseBody;
        await _next(context);
        context.Response.Body.ToStart();
        string? text = await new StreamReader(context.Response.Body).ReadToEndAsync().NoSync();
        context.Response.Body.ToStart();

        _logger.LogInformation("""
                               HTTP Request Information: {newLine}
                                    Schema: {scheme}
                                    Host: {host} 
                                    Path: {path} 
                                    Status: {status} 
                                    QueryString: {queryString},
                                    Headers: {headers}
                                    Request Body: {body},

                               """, Environment.NewLine, context.Request.Scheme, context.Request.Host, context.Request.Path, context.Response.StatusCode,
            context.Request.QueryString, JsonUtil.Serialize(context.Response.Headers), text);

        //Copy the contents of the new memory stream (which contains the response) to the original stream, which is then returned to the client.
        await responseBody.CopyToAsync(originalBodyStream);
    }

    private static string ReadStreamInChunks(Stream stream)
    {
        const int readChunkBufferLength = 4096;
        stream.ToStart();
        using var textWriter = new StringWriter();
        using var reader = new StreamReader(stream);
        var readChunk = new char[readChunkBufferLength];
        int readChunkLength;
        do
        {
            readChunkLength = reader.ReadBlock(readChunk, 0, readChunkBufferLength);
            textWriter.Write(readChunk, 0, readChunkLength);
        } while (readChunkLength > 0);

        return textWriter.ToString();
    }
}