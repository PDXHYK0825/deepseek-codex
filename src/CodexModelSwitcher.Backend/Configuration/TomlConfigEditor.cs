using System.Text;
using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Configuration;

public sealed class TomlConfigEditor
{
    private static readonly HashSet<string> ManagedTopLevelKeys = new(StringComparer.Ordinal)
    {
        "model",
        "model_provider",
        "preferred_auth_method",
        "forced_login_method",
        "model_reasoning_effort",
        "model_catalog_json",
        "profile",
        "oss_provider",
        "openai_base_url",
        "model_context_window",
        "model_auto_compact_token_limit",
        "model_auto_compact_token_limit_scope",
        "base_instructions",
        "model_instructions_file",
        "compact_prompt",
        "experimental_compact_prompt_file",
        "service_tier",
        "model_verbosity",
        "model_reasoning_summary",
        "plan_mode_reasoning_effort",
        "experimental_use_unified_exec_tool"
    };

    public string ComposeDeepSeekConfiguration(
        string? original,
        ModelProfile profile,
        string catalogPath,
        CredentialCommandSpec credentialCommand)
    {
        if (!profile.IsDeepSeek())
        {
            throw new ArgumentException("A DeepSeek profile is required.", nameof(profile));
        }

        var normalized = (original ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Length == 0 ? [] : normalized.TrimEnd('\n').Split('\n').ToList();
        var retained = RemoveManagedSettings(lines);
        var firstSection = FindFirstSectionIndex(retained);
        var preamble = retained.Take(firstSection).ToList();
        var sections = retained.Skip(firstSection).ToList();

        TrimTrailingBlankLines(preamble);
        TrimLeadingBlankLines(sections);
        TrimTrailingBlankLines(sections);

        if (preamble.Count > 0 && preamble[^1].Length > 0)
        {
            preamble.Add(string.Empty);
        }

        var catalogValue = Path.GetFullPath(catalogPath).Replace('\\', '/');
        preamble.Add($"model = {Quote(profile.ToModelSlug())}");
        preamble.Add("model_provider = \"deepseek\"");
        preamble.Add("model_reasoning_effort = \"high\"");
        preamble.Add($"model_catalog_json = {Quote(catalogValue)}");

        if (sections.Count > 0)
        {
            preamble.Add(string.Empty);
            preamble.AddRange(sections);
        }

        AppendDeepSeekProvider(preamble, credentialCommand);

        return string.Join('\n', preamble) + "\n";
    }

    public string ComposeOpenAiCompatibilityConfiguration(
        string? current,
        string? baseline,
        CredentialCommandSpec credentialCommand)
    {
        var currentLines = NormalizeLines(current);
        var baselineLines = NormalizeLines(baseline);
        var retained = RemoveManagedSettings(currentLines);
        var firstSection = FindFirstSectionIndex(retained);
        var preamble = retained.Take(firstSection).ToList();
        var sections = retained.Skip(firstSection).ToList();
        var restoredSettings = ExtractManagedTopLevelSettings(baselineLines);

        TrimTrailingBlankLines(preamble);
        TrimLeadingBlankLines(sections);
        TrimTrailingBlankLines(restoredSettings);

        if (preamble.Count > 0 && restoredSettings.Count > 0 && preamble[^1].Length > 0)
        {
            preamble.Add(string.Empty);
        }

        preamble.AddRange(restoredSettings);
        if (sections.Count > 0)
        {
            if (preamble.Count > 0 && preamble[^1].Length > 0)
            {
                preamble.Add(string.Empty);
            }

            preamble.AddRange(sections);
        }

        AppendDeepSeekProvider(preamble, credentialCommand);
        return string.Join('\n', preamble) + "\n";
    }

    private static List<string> RemoveManagedSettings(IReadOnlyList<string> lines)
    {
        var retained = new List<string>(lines.Count);
        var scanner = new TomlLineScanner();
        var currentSection = string.Empty;
        var removeCurrentSection = false;

        foreach (var line in lines)
        {
            var isStructural = !scanner.IsInsideMultilineString;
            if (isStructural && TomlLineScanner.TryGetHeader(line, out var section))
            {
                currentSection = section;
                removeCurrentSection = section.Equals("model_providers.deepseek", StringComparison.Ordinal) ||
                                       section.StartsWith("model_providers.deepseek.", StringComparison.Ordinal);
                if (!removeCurrentSection)
                {
                    retained.Add(line);
                }

                scanner.Consume(line);
                continue;
            }

            if (!removeCurrentSection)
            {
                var isManagedTopLevel = isStructural && currentSection.Length == 0 &&
                                        TomlLineScanner.TryGetSimpleAssignment(line, out var key, out _) &&
                                        ManagedTopLevelKeys.Contains(key);
                if (!isManagedTopLevel)
                {
                    retained.Add(line);
                }
            }

            scanner.Consume(line);
        }

        return retained;
    }

    private static List<string> ExtractManagedTopLevelSettings(IReadOnlyList<string> lines)
    {
        var settings = new List<string>();
        var scanner = new TomlLineScanner();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!scanner.IsInsideMultilineString && TomlLineScanner.TryGetHeader(line, out _))
            {
                break;
            }

            if (!scanner.IsInsideMultilineString &&
                TomlLineScanner.TryGetSimpleAssignment(line, out var key, out _) &&
                ManagedTopLevelKeys.Contains(key))
            {
                settings.Add(line);
                scanner.Consume(line);
                while (scanner.IsInsideMultilineString && ++index < lines.Count)
                {
                    settings.Add(lines[index]);
                    scanner.Consume(lines[index]);
                }

                continue;
            }

            scanner.Consume(lines[index]);
        }

        return settings;
    }

    private static List<string> NormalizeLines(string? content)
    {
        var normalized = (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.Length == 0 ? [] : normalized.TrimEnd('\n').Split('\n').ToList();
    }

    private static void AppendDeepSeekProvider(List<string> lines, CredentialCommandSpec credentialCommand)
    {
        TrimTrailingBlankLines(lines);
        if (lines.Count > 0)
        {
            lines.Add(string.Empty);
        }

        lines.Add("[model_providers.deepseek]");
        lines.Add("name = \"deepseek\"");
        lines.Add("base_url = \"https://api.deepseek.com/\"");
        lines.Add("wire_api = \"responses\"");
        lines.Add(string.Empty);
        lines.Add("[model_providers.deepseek.auth]");
        lines.Add($"command = {Quote(NormalizeCommand(credentialCommand.Command))}");
        lines.Add($"args = [{string.Join(", ", credentialCommand.Arguments.Select(Quote))}]");
    }

    private static int FindFirstSectionIndex(IReadOnlyList<string> lines)
    {
        var scanner = new TomlLineScanner();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!scanner.IsInsideMultilineString && TomlLineScanner.TryGetHeader(lines[i], out _))
            {
                return i;
            }

            scanner.Consume(lines[i]);
        }

        return lines.Count;
    }

    private static string NormalizeCommand(string command)
    {
        if (Path.IsPathFullyQualified(command))
        {
            return Path.GetFullPath(command).Replace('\\', '/');
        }

        return command;
    }

    private static string Quote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static void TrimLeadingBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }
    }

    private static void TrimTrailingBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }
}
