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
        "Proxy-Authorization",
        "X-Api-Key",
        "Api-Key",
        "X-Auth-Token",
        "X-Access-Token",
        "X-CSRF-Token",
        "X-XSRF-Token"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> Redact(IHeaderDictionary headers, int maxValueLength = 512)
    {
        var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);

        foreach ((string key, StringValues value) in headers)
        {
            if (IsSensitive(key))
            {
                result[key] = "[REDACTED]";
                continue;
            }

            result[key] = TrafficLogSanitizer.Sanitize(value.ToString(), maxValueLength) ?? string.Empty;
        }

        return result;
    }

    private static bool IsSensitive(string name)
    {
        if (_sensitiveHeaders.Contains(name))
            return true;

        return name.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("-key", StringComparison.OrdinalIgnoreCase);
    }
}
