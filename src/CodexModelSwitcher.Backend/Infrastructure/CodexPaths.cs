using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Infrastructure;

public sealed record CodexPaths
{
    private CodexPaths()
    {
    }

    public required string CodexHome { get; init; }
    public required string ConfigPath { get; init; }
    public required string ModelsPath { get; init; }
    public required string VendorBackupDirectory { get; init; }
    public required string ManagementRoot { get; init; }
    public required string BaselineDirectory { get; init; }
    public required string SnapshotsDirectory { get; init; }
    public required string StatePath { get; init; }
    public required string CatalogCachePath { get; init; }
    public required string PathId { get; init; }

    public static CodexPaths Resolve(string? codexHomeOverride = null, string? appDataOverride = null)
    {
        var codexHome = codexHomeOverride;
        if (string.IsNullOrWhiteSpace(codexHome))
        {
            codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        }

        if (string.IsNullOrWhiteSpace(codexHome))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                throw new SwitcherException("user_profile_missing", "Cannot resolve the current user's profile directory.");
            }

            codexHome = Path.Combine(userProfile, ".codex");
        }

        var localAppData = appDataOverride;
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new SwitcherException("local_app_data_missing", "Cannot resolve the current user's LocalAppData directory.");
        }

        var codexFullPath = Path.GetFullPath(codexHome);
        var root = Path.GetPathRoot(codexFullPath);
        if (string.Equals(codexFullPath.TrimEnd(Path.DirectorySeparatorChar), root?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new SwitcherException("unsafe_codex_home", "The Codex home directory cannot be a filesystem root.");
        }

        var pathId = Hashing.Sha256(codexFullPath.ToUpperInvariant())[..16];
        var managementRoot = Path.Combine(Path.GetFullPath(localAppData), "CodexModelSwitcher", pathId);

        return new CodexPaths
        {
            CodexHome = codexFullPath,
            ConfigPath = Path.Combine(codexFullPath, "config.toml"),
            ModelsPath = Path.Combine(codexFullPath, "models.json"),
            VendorBackupDirectory = Path.Combine(codexFullPath, "backup-deepseek"),
            ManagementRoot = managementRoot,
            BaselineDirectory = Path.Combine(managementRoot, "backups", "baseline"),
            SnapshotsDirectory = Path.Combine(managementRoot, "backups", "snapshots"),
            StatePath = Path.Combine(managementRoot, "state.json"),
            CatalogCachePath = Path.Combine(managementRoot, "catalog", "deepseek-models.json"),
            PathId = pathId
        };
    }

    public void EnsureCodexFileTarget(string path)
    {
        var target = Path.GetFullPath(path);
        var prefix = CodexHome.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new SwitcherException("unsafe_target", $"Refusing to modify a path outside Codex home: {target}");
        }
    }
}
