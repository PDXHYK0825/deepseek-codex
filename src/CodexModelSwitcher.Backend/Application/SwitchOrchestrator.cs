using System.Text;
using CodexModelSwitcher.Abstractions;
using CodexModelSwitcher.Configuration;
using CodexModelSwitcher.Domain;
using CodexModelSwitcher.Infrastructure;

namespace CodexModelSwitcher.Application;

public sealed class SwitchOrchestrator
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly ISecretStore _secretStore;
    private readonly IModelCatalogProvider _catalogProvider;
    private readonly IChatGptLifecycle _chatGptLifecycle;
    private readonly AtomicFileWriter _writer;
    private readonly BackupService _backupService;
    private readonly ManagedStateStore _stateStore;
    private readonly TomlConfigEditor _editor;
    private readonly DeepSeekConfigurationValidator _validator;

    public SwitchOrchestrator(
        ISecretStore secretStore,
        IModelCatalogProvider catalogProvider,
        IChatGptLifecycle chatGptLifecycle,
        AtomicFileWriter writer,
        BackupService backupService,
        ManagedStateStore stateStore,
        TomlConfigEditor editor,
        DeepSeekConfigurationValidator validator)
    {
        _secretStore = secretStore;
        _catalogProvider = catalogProvider;
        _chatGptLifecycle = chatGptLifecycle;
        _writer = writer;
        _backupService = backupService;
        _stateStore = stateStore;
        _editor = editor;
        _validator = validator;
    }

    public async Task<SwitchResult> SwitchToDeepSeekAsync(
        CodexPaths paths,
        ModelProfile profile,
        string? apiKey,
        CredentialCommandSpec credentialCommand,
        SwitchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!profile.IsDeepSeek())
        {
            throw new ArgumentException("Use RestoreOpenAiAsync to switch back to OpenAI.", nameof(profile));
        }

        EnsureCodexHomeExists(paths);
        await using var operationLock = await OperationLock.AcquireAsync(paths, TimeSpan.FromSeconds(10), cancellationToken);
        var messages = new List<string>();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _secretStore.Write(DeepSeekApiKey.SecretName, DeepSeekApiKey.Validate(apiKey));
            messages.Add("DeepSeek API key saved in Windows Credential Manager.");
        }

        if (!_secretStore.Contains(DeepSeekApiKey.SecretName))
        {
            throw new SwitcherException("api_key_required", "A DeepSeek API key is required before switching providers.");
        }

        var stateBefore = await _stateStore.ReadAsync(paths, cancellationToken);
        var hasExternalChanges = HasExternalChanges(paths, stateBefore);
        if (hasExternalChanges && !options.AcceptExternalChanges)
        {
            throw new SwitcherException(
                "external_changes_detected",
                "Codex configuration files changed outside this application. Review the changes and retry with explicit acceptance.");
        }

        await RejectUnrecoverableDeepSeekBaselineAsync(paths, cancellationToken);

        var importedVendorBaseline = await _backupService.TryImportVendorBaselineAsync(paths, cancellationToken);
        var baselineCreated = importedVendorBaseline || await _backupService.EnsureBaselineAsync(paths, cancellationToken);
        if (importedVendorBaseline)
        {
            messages.Add("Imported the original configuration from DeepSeek's backup-deepseek directory.");
        }

        var catalog = await _catalogProvider.GetCatalogAsync(paths, options.RefreshCatalog, cancellationToken);
        var originalConfig = File.Exists(paths.ConfigPath)
            ? await File.ReadAllTextAsync(paths.ConfigPath, cancellationToken)
            : string.Empty;
        var newConfig = _editor.ComposeDeepSeekConfiguration(originalConfig, profile, paths.ModelsPath, credentialCommand);
        _validator.Validate(newConfig, catalog.Json, profile);

        await _backupService.CreateSafetySnapshotAsync(
            paths,
            hasExternalChanges ? "accepted-external-changes-before-deepseek-switch" : "before-deepseek-switch",
            cancellationToken);

        var newConfigBytes = Utf8NoBom.GetBytes(newConfig);
        var newModelsBytes = Utf8NoBom.GetBytes(catalog.Json);
        var newState = new ManagedState
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            ActiveProfile = profile,
            ManagedConfigSha256 = Hashing.Sha256(newConfigBytes),
            ManagedModelsSha256 = Hashing.Sha256(newModelsBytes),
            CatalogSourceVersion = catalog.SourceVersion
        };

        await ApplyTransactionAsync(paths, newConfigBytes, newModelsBytes, newState, cancellationToken);
        messages.Add($"Codex is configured for {profile.ToDisplayName()}.");
        messages.Add($"DeepSeek catalog source version: {catalog.SourceVersion}.");

        RestartResult? restart = null;
        if (options.RestartChatGpt)
        {
            restart = await _chatGptLifecycle.RestartAsync(cancellationToken);
            messages.Add(restart.Started
                ? "ChatGPT restarted successfully."
                : "The configuration was written, but ChatGPT could not be restarted automatically.");
        }

        return new SwitchResult(profile, paths.CodexHome, baselineCreated, true, restart, messages);
    }

    public async Task<SwitchResult> RestoreOpenAiAsync(
        CodexPaths paths,
        CredentialCommandSpec credentialCommand,
        SwitchOptions options,
        CancellationToken cancellationToken = default)
    {
        EnsureCodexHomeExists(paths);
        await using var operationLock = await OperationLock.AcquireAsync(paths, TimeSpan.FromSeconds(10), cancellationToken);
        var stateBefore = await _stateStore.ReadAsync(paths, cancellationToken);
        var hasExternalChanges = HasExternalChanges(paths, stateBefore);
        if (hasExternalChanges && !options.AcceptExternalChanges)
        {
            throw new SwitcherException(
                "external_changes_detected",
                "Codex configuration files changed outside this application. Review the changes and retry with explicit acceptance.");
        }

        var baseline = await _backupService.ReadBaselineAsync(paths, cancellationToken);
        var baselineConfig = baseline.Config is null
            ? string.Empty
            : Utf8NoBom.GetString(baseline.Config);
        var currentConfig = File.Exists(paths.ConfigPath)
            ? await File.ReadAllTextAsync(paths.ConfigPath, cancellationToken)
            : string.Empty;
        var compatibleConfig = Utf8NoBom.GetBytes(
            _editor.ComposeOpenAiCompatibilityConfiguration(currentConfig, baselineConfig, credentialCommand));
        await _backupService.CreateSafetySnapshotAsync(
            paths,
            hasExternalChanges ? "accepted-external-changes-before-openai-restore" : "before-openai-restore",
            cancellationToken);

        var newState = new ManagedState
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            ActiveProfile = ModelProfile.OpenAI,
            ManagedConfigSha256 = Hashing.Sha256(compatibleConfig),
            ManagedModelsSha256 = baseline.Models is null ? null : Hashing.Sha256(baseline.Models),
            CatalogSourceVersion = null
        };
        await ApplyTransactionAsync(paths, compatibleConfig, baseline.Models, newState, cancellationToken);

        RestartResult? restart = null;
        var messages = new List<string>
        {
            "The original OpenAI/Codex settings were restored from the baseline backup.",
            "The inactive DeepSeek provider definition was retained so existing DeepSeek threads can still be opened."
        };
        if (options.RestartChatGpt)
        {
            restart = await _chatGptLifecycle.RestartAsync(cancellationToken);
            messages.Add(restart.Started
                ? "ChatGPT restarted successfully."
                : "The original configuration was restored, but ChatGPT could not be restarted automatically.");
        }

        return new SwitchResult(ModelProfile.OpenAI, paths.CodexHome, false, true, restart, messages);
    }

    private async Task ApplyTransactionAsync(
        CodexPaths paths,
        byte[]? desiredConfig,
        byte[]? desiredModels,
        ManagedState desiredState,
        CancellationToken cancellationToken)
    {
        paths.EnsureCodexFileTarget(paths.ConfigPath);
        paths.EnsureCodexFileTarget(paths.ModelsPath);
        var previousConfig = await ReadOptionalAsync(paths.ConfigPath, cancellationToken);
        var previousModels = await ReadOptionalAsync(paths.ModelsPath, cancellationToken);
        var previousState = await ReadOptionalAsync(paths.StatePath, cancellationToken);

        try
        {
            await WriteOptionalAsync(paths.ModelsPath, desiredModels, cancellationToken);
            await WriteOptionalAsync(paths.ConfigPath, desiredConfig, cancellationToken);
            await _stateStore.WriteAsync(paths, desiredState, cancellationToken);
        }
        catch
        {
            await WriteOptionalAsync(paths.ModelsPath, previousModels, CancellationToken.None);
            await WriteOptionalAsync(paths.ConfigPath, previousConfig, CancellationToken.None);
            await WriteOptionalAsync(paths.StatePath, previousState, CancellationToken.None);
            throw;
        }
    }

    private async Task WriteOptionalAsync(string path, byte[]? content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        await _writer.WriteBytesAsync(path, content, cancellationToken);
    }

    private static async Task<byte[]?> ReadOptionalAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;

    private static bool HasExternalChanges(CodexPaths paths, ManagedState? state) =>
        state is not null &&
        (!string.Equals(Hashing.Sha256File(paths.ConfigPath), state.ManagedConfigSha256, StringComparison.Ordinal) ||
         !string.Equals(Hashing.Sha256File(paths.ModelsPath), state.ManagedModelsSha256, StringComparison.Ordinal));

    private static void EnsureCodexHomeExists(CodexPaths paths)
    {
        if (!Directory.Exists(paths.CodexHome))
        {
            throw new SwitcherException(
                "codex_home_missing",
                $"Codex configuration directory not found: {paths.CodexHome}. Run Codex or ChatGPT once and retry.");
        }
    }

    private static async Task RejectUnrecoverableDeepSeekBaselineAsync(
        CodexPaths paths,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(paths.BaselineDirectory) || Directory.Exists(paths.VendorBackupDirectory) || !File.Exists(paths.ConfigPath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(paths.ConfigPath, cancellationToken);
        var inspection = new TomlConfigInspector().Inspect(content);
        if (string.Equals(inspection.Provider, "deepseek", StringComparison.Ordinal))
        {
            throw new SwitcherException(
                "unrecoverable_deepseek_baseline",
                "An unmanaged DeepSeek configuration is active, but no original backup is available. Refusing to treat it as the OpenAI baseline.");
        }
    }
}
