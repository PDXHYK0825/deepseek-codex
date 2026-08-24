namespace CodexModelSwitcher.Domain;

public sealed record BaselineBackupManifest
{
    public required DateTimeOffset CreatedAt { get; init; }
    public required string CodexHome { get; init; }
    public required bool ConfigExisted { get; init; }
    public required bool ModelsExisted { get; init; }
    public string? ConfigSha256 { get; init; }
    public string? ModelsSha256 { get; init; }
    public required string ApplicationVersion { get; init; }
}

public sealed record SafetySnapshotManifest
{
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Reason { get; init; }
    public required string CodexHome { get; init; }
    public required bool ConfigExisted { get; init; }
    public required bool ModelsExisted { get; init; }
    public required bool SecretsRedacted { get; init; }
}

public sealed record ManagedState
{
    public required DateTimeOffset UpdatedAt { get; init; }
    public required ModelProfile ActiveProfile { get; init; }
    public string? ManagedConfigSha256 { get; init; }
    public string? ManagedModelsSha256 { get; init; }
    public string? CatalogSourceVersion { get; init; }
}
