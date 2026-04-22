using System.Text;
using System.Text.RegularExpressions;

namespace NurMarketKassa.Services;

internal static class ReceiptTextFormatter
{
    internal static string FormatForPrinter(string text, int width = 32)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);
        var previousBlank = false;

        foreach (var rawLine in lines)
        {
            // Не Trim() целиком — иначе ломается Center() и PadBoth() в чеке (32 колонки).
            var line = (rawLine ?? string.Empty).TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!previousBlank)
                    output.Add(string.Empty);
                previousBlank = true;
                continue;
            }

            previousBlank = false;

            if (LooksLikeSeparator(line.Trim()))
            {
                output.Add(new string(line.Contains('=') ? '=' : '-', width));
                continue;
            }

            if (TryFormatSummaryLine(line, width, out var summaryLine))
            {
                output.Add(summaryLine);
                continue;
            }

            if (ShouldCenter(line, width))
            {
                foreach (var wrapped in WrapLine(line.Trim(), width))
                    output.Add(Center(wrapped, width));
                continue;
            }

            // Уже сверстанная строка чека — не схлопывать пробелы.
            if (line.Length == width)
            {
                output.Add(line);
                continue;
            }

            foreach (var wrapped in WrapLine(CollapseInnerWhitespace(line.Trim()), width))
                output.Add(wrapped);
        }

        while (output.Count > 0 && output[^1].Length == 0)
            output.RemoveAt(output.Count - 1);

        return string.Join("\n", output);
    }

    private static bool TryFormatSummaryLine(string line, int width, out string formatted)
    {
        formatted = string.Empty;
        var normalized = CollapseInnerWhitespace(line);
        var summaryPrefixes = new[]
        {
            "ИТОГО К ОПЛАТЕ", "К ОПЛАТЕ", "ИТОГО", "ИТОГ", "СДАЧА", "НАЛИЧНЫМИ", "БЕЗНАЛ", "СКИДКА",
        };

        foreach (var prefix in summaryPrefixes)
        {
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = ExtractValue(normalized[prefix.Length..]);
            if (string.IsNullOrEmpty(value))
            {
                formatted = normalized;
                return true;
            }

            var left = prefix.EndsWith(':') ? prefix : prefix + ":";
            formatted = PadBoth(left, value, width);
            return true;
        }

        return false;
    }

    private static string ExtractValue(string tail)
    {
        var cleaned = tail.Trim();
        if (cleaned.StartsWith(":", StringComparison.Ordinal))
            cleaned = cleaned[1..].Trim();

        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned;
    }

    private static bool LooksLikeSeparator(string line)
    {
        if (line.Length < 3)
            return false;

        var compact = line.Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.All(ch => ch is '-' or '=' or '_' or '*' or '—');
    }

    private static bool ShouldCenter(string line, int width)
    {
        if (line.Length > width)
            return false;

        var upper = line.ToUpperInvariant();
        return upper.Contains("НУР МАРКЕТ", StringComparison.Ordinal) ||
               upper.Contains("MARKET PLUS", StringComparison.Ordinal) ||
               upper.Contains("СПАСИБО", StringComparison.Ordinal) ||
               upper.Contains("ДОБРО ПОЖАЛОВАТЬ", StringComparison.Ordinal) ||
               upper.StartsWith("ОФФЛАЙН", StringComparison.Ordinal) ||
               upper.StartsWith("ЧЕК", StringComparison.Ordinal) ||
               upper.StartsWith("ДАТА:", StringComparison.Ordinal);
    }

    private static IEnumerable<string> WrapLine(string line, int width)
    {
        if (line.Length <= width)
        {
            yield return line;
            yield break;
        }

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            yield return line[..Math.Min(line.Length, width)];
            yield break;
        }

        var current = new StringBuilder(width);
        foreach (var word in words)
        {
            if (word.Length > width)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                for (var i = 0; i < word.Length; i += width)
                    yield return word.Substring(i, Math.Min(width, word.Length - i));
                continue;
            }

            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            if (current.Length + 1 + word.Length <= width)
            {
                current.Append(' ').Append(word);
                continue;
            }

            yield return current.ToString();
            current.Clear();
            current.Append(word);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static string CollapseInnerWhitespace(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");

    private static string Center(string s, int width)
    {
        if (s.Length >= width)
            return s;

        var total = width - s.Length;
        var left = total / 2;
        return s.PadLeft(s.Length + left).PadRight(width);
    }

    private static string PadBoth(string left, string right, int width)
    {
        var gap = width - left.Length - right.Length;
        if (gap <= 1)
            return left + " " + right;
        return left + new string(' ', gap) + right;
    }
}
