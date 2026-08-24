namespace CodexModelSwitcher.Configuration;

public sealed record TomlInspection(
    string? Model,
    string? Provider,
    bool HasDeepSeekProvider,
    IReadOnlyList<string> DuplicateTopLevelKeys,
    IReadOnlyList<string> Diagnostics);

public sealed class TomlConfigInspector
{
    public TomlInspection Inspect(string? content)
    {
        var diagnostics = new List<string>();
        var topLevelKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        string? model = null;
        string? provider = null;
        var hasDeepSeekProvider = false;
        var currentSection = string.Empty;
        var scanner = new TomlLineScanner();

        foreach (var line in (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var isStructural = !scanner.IsInsideMultilineString;
            if (isStructural && TomlLineScanner.TryGetHeader(line, out var section))
            {
                currentSection = section;
                if (section.Equals("model_providers.deepseek", StringComparison.Ordinal))
                {
                    hasDeepSeekProvider = true;
                }

                scanner.Consume(line);
                continue;
            }

            if (isStructural && currentSection.Length == 0 && TomlLineScanner.TryGetSimpleAssignment(line, out var key, out var value))
            {
                topLevelKeys[key] = topLevelKeys.GetValueOrDefault(key) + 1;
                if (key.Equals("model", StringComparison.Ordinal))
                {
                    model = Unquote(value);
                }
                else if (key.Equals("model_provider", StringComparison.Ordinal))
                {
                    provider = Unquote(value);
                }
            }

            scanner.Consume(line);
        }

        if (scanner.IsInsideMultilineString)
        {
            diagnostics.Add("The TOML file appears to contain an unterminated multi-line string.");
        }

        var duplicates = topLevelKeys.Where(pair => pair.Value > 1).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
        if (duplicates.Length > 0)
        {
            diagnostics.Add($"Duplicate top-level keys: {string.Join(", ", duplicates)}");
        }

        return new TomlInspection(model, provider, hasDeepSeekProvider, duplicates, diagnostics);
    }

    private static string Unquote(string value)
    {
        var comment = value.IndexOf('#');
        if (comment >= 0)
        {
            value = value[..comment].TrimEnd();
        }

        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
