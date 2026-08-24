using CodexModelSwitcher.Abstractions;
using CodexModelSwitcher.Configuration;
using CodexModelSwitcher.Domain;
using CodexModelSwitcher.Infrastructure;

namespace CodexModelSwitcher.Application;

public sealed class CodexStatusService
{
    private readonly ISecretStore _secretStore;
    private readonly ManagedStateStore _stateStore;
    private readonly TomlConfigInspector _inspector;

    public CodexStatusService(
        ISecretStore secretStore,
        ManagedStateStore stateStore,
        TomlConfigInspector inspector)
    {
        _secretStore = secretStore;
        _stateStore = stateStore;
        _inspector = inspector;
    }

    public async Task<CodexStatus> GetStatusAsync(CodexPaths paths, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();
        if (!Directory.Exists(paths.CodexHome))
        {
            diagnostics.Add("The Codex home directory does not exist. Run Codex or ChatGPT once before switching models.");
            return new CodexStatus(
                ProviderState.Broken,
                null,
                null,
                paths.CodexHome,
                false,
                false,
                Directory.Exists(paths.BaselineDirectory),
                SafeContainsSecret(),
                false,
                diagnostics);
        }

        var configExists = File.Exists(paths.ConfigPath);
        var content = configExists ? await File.ReadAllTextAsync(paths.ConfigPath, cancellationToken) : string.Empty;
        var inspection = _inspector.Inspect(content);
        diagnostics.AddRange(inspection.Diagnostics);
        var managedState = await _stateStore.ReadAsync(paths, cancellationToken);
        var externalChanges = managedState is not null &&
                              (!string.Equals(Hashing.Sha256File(paths.ConfigPath), managedState.ManagedConfigSha256, StringComparison.Ordinal) ||
                               !string.Equals(Hashing.Sha256File(paths.ModelsPath), managedState.ManagedModelsSha256, StringComparison.Ordinal));
        if (externalChanges)
        {
            diagnostics.Add("Codex configuration files changed outside this application after the last managed operation.");
        }

        var state = ResolveState(paths, inspection, managedState, diagnostics);
        return new CodexStatus(
            state,
            inspection.Model,
            inspection.Provider,
            paths.CodexHome,
            configExists,
            File.Exists(paths.ModelsPath),
            Directory.Exists(paths.BaselineDirectory),
            SafeContainsSecret(),
            externalChanges,
            diagnostics);
    }

    private static ProviderState ResolveState(
        CodexPaths paths,
        TomlInspection inspection,
        ManagedState? managedState,
        ICollection<string> diagnostics)
    {
        if (inspection.Diagnostics.Count > 0)
        {
            return ProviderState.Broken;
        }

        if (inspection.Provider is null or "openai")
        {
            return ProviderState.OpenAI;
        }

        if (!inspection.Provider.Equals("deepseek", StringComparison.Ordinal))
        {
            diagnostics.Add($"The active provider '{inspection.Provider}' is not managed by this application.");
            return ProviderState.Unknown;
        }

        if (Directory.Exists(paths.VendorBackupDirectory) && managedState is null && !Directory.Exists(paths.BaselineDirectory))
        {
            diagnostics.Add("A configuration created by the official DeepSeek PowerShell script was detected and can be adopted.");
            return ProviderState.VendorScriptManaged;
        }

        return inspection.Model switch
        {
            "deepseek-v4-flash" => ProviderState.DeepSeekFlash,
            "deepseek-v4-pro" => ProviderState.DeepSeekPro,
            "deepseek-v4-flash-vision-exp" => ProviderState.DeepSeekVision,
            _ => ProviderState.Unknown
        };
    }

    private bool SafeContainsSecret()
    {
        try
        {
            return _secretStore.Contains(DeepSeekApiKey.SecretName);
        }
        catch
        {
            return false;
        }
    }
}
