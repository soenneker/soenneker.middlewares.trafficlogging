using System;
using Soenneker.Extensions.String;
using Soenneker.Utils.PooledStringBuilders;

namespace Soenneker.Middlewares.TrafficLogging;

internal static class TrafficLogSanitizer
{
    public static string? Sanitize(string? value, int maxLength = 4096)
    {
        if (value.IsNullOrEmpty())
            return value;

        ReadOnlySpan<char> source = value.AsSpan();
        int len = Math.Min(source.Length, maxLength);

        using var sb = new PooledStringBuilder(len);

        for (var i = 0; i < len; i++)
        {
            char c = source[i];

            switch (c)
            {
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        if (source.Length > maxLength)
            sb.Append("...[truncated]");

        return sb.ToString();
    }
}
