using System.Text.RegularExpressions;

namespace CodexModelSwitcher.Configuration;

internal sealed partial class TomlLineScanner
{
    private string? _multilineDelimiter;

    public bool IsInsideMultilineString => _multilineDelimiter is not null;

    public void Consume(string line)
    {
        if (_multilineDelimiter is not null)
        {
            if (ContainsDelimiter(line, _multilineDelimiter))
            {
                _multilineDelimiter = null;
            }

            return;
        }

        var basic = IndexOfUnescaped(line, "\"\"\"");
        var literal = line.IndexOf("'''", StringComparison.Ordinal);
        if (basic < 0 && literal < 0)
        {
            return;
        }

        var delimiter = basic >= 0 && (literal < 0 || basic < literal) ? "\"\"\"" : "'''";
        var first = delimiter == "\"\"\"" ? basic : literal;
        var remainder = line[(first + 3)..];
        if (!ContainsDelimiter(remainder, delimiter))
        {
            _multilineDelimiter = delimiter;
        }
    }

    public static bool TryGetHeader(string line, out string section)
    {
        section = string.Empty;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        var arrayTable = trimmed.StartsWith("[[", StringComparison.Ordinal);
        var closing = arrayTable ? "]]" : "]";
        var end = trimmed.IndexOf(closing, arrayTable ? 2 : 1, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        var start = arrayTable ? 2 : 1;
        section = trimmed[start..end].Trim().Replace("\"", string.Empty, StringComparison.Ordinal).Replace("'", string.Empty, StringComparison.Ordinal);
        return section.Length > 0;
    }

    public static bool TryGetSimpleAssignment(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        var match = AssignmentRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        key = match.Groups[1].Value;
        value = match.Groups[2].Value.Trim();
        return true;
    }

    private static bool ContainsDelimiter(string value, string delimiter) =>
        delimiter == "\"\"\""
            ? IndexOfUnescaped(value, delimiter) >= 0
            : value.Contains(delimiter, StringComparison.Ordinal);

    private static int IndexOfUnescaped(string value, string needle)
    {
        var index = value.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            var slashCount = 0;
            for (var i = index - 1; i >= 0 && value[i] == '\\'; i--)
            {
                slashCount++;
            }

            if (slashCount % 2 == 0)
            {
                return index;
            }

            index = value.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return -1;
    }

    [GeneratedRegex("^\\s*([A-Za-z0-9_-]+)\\s*=\\s*(.*?)\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex AssignmentRegex();
}
