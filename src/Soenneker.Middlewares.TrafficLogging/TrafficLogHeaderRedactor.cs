using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace Soenneker.Middlewares.TrafficLogging;

internal static class TrafficLogHeaderRedactor
{
    private static readonly HashSet<string> _sensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "Api-Key",
        "Proxy-Authorization"
    };

    public static Dictionary<string, string> Redact(IHeaderDictionary headers, int maxValueLength = 512, bool enableRedaction = true)
    {
        var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> kvp in headers)
        {
            if (enableRedaction && _sensitiveHeaders.Contains(kvp.Key))
            {
                result[kvp.Key] = "[REDACTED]";
                continue;
            }

            var joined = kvp.Value.ToString();
            result[kvp.Key] = TrafficLogSanitizer.Sanitize(joined, maxValueLength) ?? string.Empty;
        }

        return result;
    }
}
