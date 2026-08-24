using CodexModelSwitcher.Abstractions;
using CodexModelSwitcher.Application;
using CodexModelSwitcher.Configuration;
using CodexModelSwitcher.Domain;
using CodexModelSwitcher.Infrastructure;

namespace CodexModelSwitcher.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
            {
                PrintHelp();
                return 0;
            }

            using var runtime = BackendRuntime.CreateDefault();

            var command = args[0].ToLowerInvariant();
            return command switch
            {
                "status" or "diagnose" => await ShowStatusAsync(runtime.Status, ResolvePaths(args)),
                "switch" => await SwitchAsync(args, runtime.SecretStore, runtime.Switcher),
                "restore" => await RestoreAsync(args, runtime.Switcher),
                "credential" => HandleCredential(args, runtime.SecretStore),
                "restart" => await RestartAsync(runtime.ChatGptLifecycle),
                _ => UnknownCommand(command)
            };
        }
        catch (SwitcherException exception)
        {
            Console.Error.WriteLine($"[{exception.Code}] {exception.Message}");
            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[unexpected_error] {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> ShowStatusAsync(CodexStatusService service, CodexPaths paths)
    {
        var status = await service.GetStatusAsync(paths);
        Console.WriteLine($"State:          {status.State}");
        Console.WriteLine($"Model:          {status.Model ?? "<default>"}");
        Console.WriteLine($"Provider:       {status.Provider ?? "openai (default)"}");
        Console.WriteLine($"Codex home:     {status.CodexHome}");
        Console.WriteLine($"Baseline:       {(status.HasBaseline ? "yes" : "no")}");
        Console.WriteLine($"API key stored: {(status.HasStoredApiKey ? "yes" : "no")}");
        Console.WriteLine($"External edits: {(status.HasExternalChanges ? "yes" : "no")}");
        foreach (var diagnostic in status.Diagnostics)
        {
            Console.WriteLine($"- {diagnostic}");
        }

        return status.State == ProviderState.Broken ? 2 : 0;
    }

    private static async Task<int> SwitchAsync(
        string[] args,
        ISecretStore secretStore,
        SwitchOrchestrator orchestrator)
    {
        if (args.Length < 2 || !ModelProfileExtensions.TryParseCliName(args[1], out var profile))
        {
            throw new SwitcherException("profile_required", "Choose flash, pro, vision, or gpt.");
        }

        if (profile == ModelProfile.OpenAI)
        {
            return await RestoreAsync(args, orchestrator);
        }

        var apiKey = Environment.GetEnvironmentVariable(DeepSeekApiKey.EnvironmentVariable);
        if (HasFlag(args, "--api-key-stdin") || (string.IsNullOrWhiteSpace(apiKey) && !secretStore.Contains(DeepSeekApiKey.SecretName)))
        {
            apiKey = SecretInput.Read("DeepSeek API key: ");
        }

        var result = await orchestrator.SwitchToDeepSeekAsync(
            ResolvePaths(args),
            profile,
            apiKey,
            CredentialCommandFactory.Create(),
            ResolveOptions(args));
        PrintResult(result);
        return result.Restart is { Started: false } ? 4 : 0;
    }

    private static async Task<int> RestoreAsync(string[] args, SwitchOrchestrator orchestrator)
    {
        var result = await orchestrator.RestoreOpenAiAsync(
            ResolvePaths(args),
            CredentialCommandFactory.Create(),
            ResolveOptions(args));
        PrintResult(result);
        return result.Restart is { Started: false } ? 4 : 0;
    }

    private static int HandleCredential(string[] args, ISecretStore secretStore)
    {
        if (args.Length < 3 || !args[2].Equals("deepseek", StringComparison.OrdinalIgnoreCase))
        {
            throw new SwitcherException("credential_usage", "Usage: credential get|set|delete deepseek");
        }

        return args[1].ToLowerInvariant() switch
        {
            "get" => GetCredential(secretStore),
            "set" => SetCredential(secretStore),
            "delete" => DeleteCredential(secretStore),
            _ => throw new SwitcherException("credential_usage", "Usage: credential get|set|delete deepseek")
        };
    }

    private static int GetCredential(ISecretStore secretStore)
    {
        var value = secretStore.Read(DeepSeekApiKey.SecretName)
                    ?? throw new SwitcherException("credential_missing", "No DeepSeek API key is stored.");
        Console.Out.Write(value);
        return 0;
    }

    private static int SetCredential(ISecretStore secretStore)
    {
        var value = DeepSeekApiKey.Validate(SecretInput.Read("DeepSeek API key: "));
        secretStore.Write(DeepSeekApiKey.SecretName, value);
        Console.Error.WriteLine("DeepSeek API key saved in Windows Credential Manager.");
        return 0;
    }

    private static int DeleteCredential(ISecretStore secretStore)
    {
        var deleted = secretStore.Delete(DeepSeekApiKey.SecretName);
        Console.Error.WriteLine(deleted ? "DeepSeek API key deleted." : "No DeepSeek API key was stored.");
        return 0;
    }

    private static async Task<int> RestartAsync(IChatGptLifecycle lifecycle)
    {
        var result = await lifecycle.RestartAsync();
        Console.WriteLine(result.Started ? "ChatGPT restarted." : result.Message ?? "ChatGPT restart failed.");
        return result.Started ? 0 : 4;
    }

    private static CodexPaths ResolvePaths(string[] args)
    {
        var codexHome = GetOption(args, "--codex-home");
        return CodexPaths.Resolve(codexHome);
    }

    private static SwitchOptions ResolveOptions(string[] args) => new(
        RestartChatGpt: !HasFlag(args, "--no-restart"),
        AcceptExternalChanges: HasFlag(args, "--accept-external-changes"),
        RefreshCatalog: !HasFlag(args, "--cached-catalog"));

    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                throw new SwitcherException("option_value_missing", $"Option {name} requires a value.");
            }

            return args[i + 1];
        }

        return null;
    }

    private static bool HasFlag(IEnumerable<string> args, string flag) =>
        args.Any(value => value.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static void PrintResult(SwitchResult result)
    {
        Console.WriteLine($"Active profile: {result.ActiveProfile.ToDisplayName()}");
        Console.WriteLine($"Codex home:     {result.CodexHome}");
        foreach (var message in result.Messages)
        {
            Console.WriteLine($"- {message}");
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Codex Model Switcher backend CLI

            Commands:
              status [--codex-home PATH]
              switch flash|pro|vision [--api-key-stdin] [--no-restart]
              switch gpt [--no-restart]
              restore [--no-restart]
              credential get|set|delete deepseek
              restart

            Safety options:
              --accept-external-changes  Continue after configuration changed outside this app.
              --cached-catalog           Use the last validated DeepSeek model catalog.

            API keys are never accepted as command-line arguments. Use the hidden prompt,
            redirected standard input, or the DEEPSEEK_API_KEY environment variable.
            """);
    }
}
