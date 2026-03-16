using Soenneker.Extensions.String;
using Soenneker.Utils.PooledStringBuilders;
using System;
using System.Runtime.CompilerServices;

namespace Soenneker.Middlewares.TrafficLogging;

internal static class TrafficLogSanitizer
{
    public static string? Sanitize(string? value, int maxLength = 4096)
    {
        if (value.IsNullOrEmpty())
            return value;

        ReadOnlySpan<char> source = value.AsSpan();
        int len = Math.Min(source.Length, maxLength);

        bool needsEscaping = false;

        for (int i = 0; i < len; i++)
        {
            char c = source[i];

            if (c is '\r' or '\n' or '\t' || char.IsControl(c))
            {
                needsEscaping = true;
                break;
            }
        }

        if (!needsEscaping && source.Length <= maxLength)
            return value;

        var sb = new PooledStringBuilder(len + 16);

        for (int i = 0; i < len; i++)
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
                        AppendUnicodeEscape(ref sb, c);
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

        return sb.ToStringAndDispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendUnicodeEscape(ref PooledStringBuilder sb, char c)
    {
        sb.Append('\\');
        sb.Append('u');
        sb.Append(ToHexChar((c >> 12) & 0xF));
        sb.Append(ToHexChar((c >> 8) & 0xF));
        sb.Append(ToHexChar((c >> 4) & 0xF));
        sb.Append(ToHexChar(c & 0xF));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char ToHexChar(int value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
    }
}