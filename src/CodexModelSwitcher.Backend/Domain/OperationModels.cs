namespace CodexModelSwitcher.Domain;

public sealed record SwitchOptions(
    bool RestartChatGpt = true,
    bool AcceptExternalChanges = false,
    bool RefreshCatalog = true);

public sealed record RestartResult(
    bool WasRunning,
    bool Stopped,
    bool Started,
    string? Message = null);

public sealed record SwitchResult(
    ModelProfile ActiveProfile,
    string CodexHome,
    bool BaselineCreated,
    bool SafetySnapshotCreated,
    RestartResult? Restart,
    IReadOnlyList<string> Messages);

public sealed record CodexStatus(
    ProviderState State,
    string? Model,
    string? Provider,
    string CodexHome,
    bool ConfigExists,
    bool ModelsCatalogExists,
    bool HasBaseline,
    bool HasStoredApiKey,
    bool HasExternalChanges,
    IReadOnlyList<string> Diagnostics);

public sealed record CredentialCommandSpec(string Command, IReadOnlyList<string> Arguments);

public sealed record ModelCatalogDocument(string Json, string SourceVersion, string Sha256);
