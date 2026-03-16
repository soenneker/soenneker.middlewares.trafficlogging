using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Collections.Frozen;

namespace Soenneker.Middlewares.TrafficLogging;

internal static class TrafficLogHeaderRedactor
{
    private static readonly FrozenSet<string> _sensitiveHeaders = new[]
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "Api-Key",
        "Proxy-Authorization"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> Redact(IHeaderDictionary headers, int maxValueLength = 512, bool enableRedaction = true)
    {
        var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);

        foreach ((string key, StringValues value) in headers)
        {
            if (enableRedaction && _sensitiveHeaders.Contains(key))
            {
                result[key] = "[REDACTED]";
                continue;
            }

            string joined = value.ToString();
            result[key] = TrafficLogSanitizer.Sanitize(joined, maxValueLength) ?? string.Empty;
        }

        return result;
    }
}