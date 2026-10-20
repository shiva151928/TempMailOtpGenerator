using System.Text;
using System.Text.RegularExpressions;

namespace TempMailOtp.Api.Services.Content;

/// <summary>
/// Normalizes email text: unifies whitespace, removes zero-width characters
/// (sometimes inserted between digits), trims lines and collapses blank lines.
/// </summary>
public static class TextNormalizer
{
    private static readonly Regex MultipleSpaces = new(
        "[ ]{2,}", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var cleaned = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\r':
                    break;
                case ' ' or ' ' or ' ' or '\t' or '\f' or '\v':
                    cleaned.Append(' ');
                    break;
                case '​' or '‌' or '‍' or '⁠' or '﻿' or '­':
                    break;
                default:
                    cleaned.Append(c);
                    break;
            }
        }

        var output = new StringBuilder(cleaned.Length);
        var blankLines = 0;
        foreach (var rawLine in cleaned.ToString().Split('\n'))
        {
            var line = MultipleSpaces.Replace(rawLine, " ").Trim();
            if (line.Length == 0)
            {
                blankLines++;
                if (blankLines == 1 && output.Length > 0)
                {
                    output.Append('\n');
                }

                continue;
            }

            blankLines = 0;
            output.Append(line).Append('\n');
        }

        return output.ToString().TrimEnd();
    }

    /// <summary>Builds a single-line preview of at most <paramref name="maxLength"/> characters.</summary>
    public static string ToPreview(string? text, int maxLength = 200)
    {
        var normalized = Normalize(text).Replace('\n', ' ');
        normalized = MultipleSpaces.Replace(normalized, " ").Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..(maxLength - 1)].TrimEnd() + "…";
    }
}
