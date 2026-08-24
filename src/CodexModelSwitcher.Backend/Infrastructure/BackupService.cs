using System.Text;
using CodexModelSwitcher.Configuration;
using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Infrastructure;

public sealed record BaselinePayload(BaselineBackupManifest Manifest, byte[]? Config, byte[]? Models);

public sealed class BackupService
{
    private const string ApplicationVersion = "0.1.0";
    private readonly AtomicFileWriter _writer;
    private readonly JsonFileStore _json;

    public BackupService(AtomicFileWriter writer)
    {
        _writer = writer;
        _json = new JsonFileStore(writer);
    }

    public async Task<bool> EnsureBaselineAsync(CodexPaths paths, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(paths.BaselineDirectory))
        {
            _ = await ReadBaselineAsync(paths, cancellationToken);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(paths.BaselineDirectory)!);
        var staging = paths.BaselineDirectory + $".pending-{Guid.NewGuid():N}";
        EnsureInsideManagement(paths, staging);
        Directory.CreateDirectory(staging);

        try
        {
            var config = await ReadOptionalBytesAsync(paths.ConfigPath, cancellationToken);
            var models = await ReadOptionalBytesAsync(paths.ModelsPath, cancellationToken);

            if (config is not null)
            {
                await _writer.WriteBytesAsync(Path.Combine(staging, "config.toml"), config, cancellationToken);
            }

            if (models is not null)
            {
                await _writer.WriteBytesAsync(Path.Combine(staging, "models.json"), models, cancellationToken);
            }

            var manifest = new BaselineBackupManifest
            {
                CreatedAt = DateTimeOffset.UtcNow,
                CodexHome = paths.CodexHome,
                ConfigExisted = config is not null,
                ModelsExisted = models is not null,
                ConfigSha256 = config is null ? null : Hashing.Sha256(config),
                ModelsSha256 = models is null ? null : Hashing.Sha256(models),
                ApplicationVersion = ApplicationVersion
            };
            await _json.WriteAsync(Path.Combine(staging, "manifest.json"), manifest, cancellationToken);
            Directory.Move(staging, paths.BaselineDirectory);
            return true;
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            throw;
        }
    }

    public async Task<bool> TryImportVendorBaselineAsync(CodexPaths paths, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(paths.BaselineDirectory) || !Directory.Exists(paths.VendorBackupDirectory))
        {
            return false;
        }

        var vendorManifestPath = Path.Combine(paths.VendorBackupDirectory, "manifest.txt");
        if (!File.Exists(vendorManifestPath))
        {
            throw new SwitcherException(
                "vendor_backup_incomplete",
                $"A DeepSeek vendor backup was detected, but its manifest is missing: {vendorManifestPath}");
        }

        var vendorManifest = await File.ReadAllTextAsync(vendorManifestPath, cancellationToken);
        var originalConfigExisted = !vendorManifest.Contains("original_config_existed=0", StringComparison.Ordinal);
        byte[]? originalConfig = null;
        if (originalConfigExisted)
        {
            var vendorConfig = Path.Combine(paths.VendorBackupDirectory, "config.toml");
            if (!File.Exists(vendorConfig))
            {
                throw new SwitcherException(
                    "vendor_backup_incomplete",
                    $"The DeepSeek vendor backup says config.toml existed, but the backup file is missing: {vendorConfig}");
            }

            originalConfig = await File.ReadAllBytesAsync(vendorConfig, cancellationToken);
        }

        await CreateBaselineAsync(paths, originalConfig, models: null, cancellationToken);
        return true;
    }

    public async Task<BaselinePayload> ReadBaselineAsync(CodexPaths paths, CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(paths.BaselineDirectory, "manifest.json");
        var manifest = await _json.ReadAsync<BaselineBackupManifest>(manifestPath, cancellationToken)
                       ?? throw new SwitcherException("baseline_missing", "No baseline backup exists for this Codex configuration directory.");

        if (!string.Equals(Path.GetFullPath(manifest.CodexHome), paths.CodexHome, StringComparison.OrdinalIgnoreCase))
        {
            throw new SwitcherException("baseline_mismatch", "The baseline belongs to a different Codex home directory.");
        }

        var config = manifest.ConfigExisted
            ? await ReadRequiredBytesAsync(Path.Combine(paths.BaselineDirectory, "config.toml"), cancellationToken)
            : null;
        var models = manifest.ModelsExisted
            ? await ReadRequiredBytesAsync(Path.Combine(paths.BaselineDirectory, "models.json"), cancellationToken)
            : null;

        if ((config is not null && !string.Equals(Hashing.Sha256(config), manifest.ConfigSha256, StringComparison.Ordinal)) ||
            (models is not null && !string.Equals(Hashing.Sha256(models), manifest.ModelsSha256, StringComparison.Ordinal)))
        {
            throw new SwitcherException("baseline_corrupt", "The baseline backup failed its SHA-256 integrity check.");
        }

        return new BaselinePayload(manifest, config, models);
    }

    public async Task<string> CreateSafetySnapshotAsync(
        CodexPaths paths,
        string reason,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.SnapshotsDirectory);
        var name = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var snapshot = Path.Combine(paths.SnapshotsDirectory, name);
        EnsureInsideManagement(paths, snapshot);
        Directory.CreateDirectory(snapshot);

        var config = await ReadOptionalBytesAsync(paths.ConfigPath, cancellationToken);
        var models = await ReadOptionalBytesAsync(paths.ModelsPath, cancellationToken);
        var (snapshotConfig, secretsRedacted) = RedactDeepSeekBearerToken(config);
        if (snapshotConfig is not null)
        {
            await _writer.WriteBytesAsync(Path.Combine(snapshot, "config.toml"), snapshotConfig, cancellationToken);
        }

        if (models is not null)
        {
            await _writer.WriteBytesAsync(Path.Combine(snapshot, "models.json"), models, cancellationToken);
        }

        var manifest = new SafetySnapshotManifest
        {
            CreatedAt = DateTimeOffset.UtcNow,
            Reason = reason,
            CodexHome = paths.CodexHome,
            ConfigExisted = config is not null,
            ModelsExisted = models is not null,
            SecretsRedacted = secretsRedacted
        };
        await _json.WriteAsync(Path.Combine(snapshot, "manifest.json"), manifest, cancellationToken);
        return snapshot;
    }

    private static async Task<byte[]?> ReadOptionalBytesAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;

    private static async Task<byte[]> ReadRequiredBytesAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new SwitcherException("baseline_corrupt", $"A required baseline file is missing: {path}");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private async Task CreateBaselineAsync(
        CodexPaths paths,
        byte[]? config,
        byte[]? models,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.BaselineDirectory)!);
        var staging = paths.BaselineDirectory + $".pending-{Guid.NewGuid():N}";
        EnsureInsideManagement(paths, staging);
        Directory.CreateDirectory(staging);
        try
        {
            if (config is not null)
            {
                await _writer.WriteBytesAsync(Path.Combine(staging, "config.toml"), config, cancellationToken);
            }

            if (models is not null)
            {
                await _writer.WriteBytesAsync(Path.Combine(staging, "models.json"), models, cancellationToken);
            }

            var manifest = new BaselineBackupManifest
            {
                CreatedAt = DateTimeOffset.UtcNow,
                CodexHome = paths.CodexHome,
                ConfigExisted = config is not null,
                ModelsExisted = models is not null,
                ConfigSha256 = config is null ? null : Hashing.Sha256(config),
                ModelsSha256 = models is null ? null : Hashing.Sha256(models),
                ApplicationVersion = ApplicationVersion
            };
            await _json.WriteAsync(Path.Combine(staging, "manifest.json"), manifest, cancellationToken);
            Directory.Move(staging, paths.BaselineDirectory);
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            throw;
        }
    }

    private static void EnsureInsideManagement(CodexPaths paths, string target)
    {
        var root = Path.GetFullPath(paths.ManagementRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullTarget = Path.GetFullPath(target);
        if (!fullTarget.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new SwitcherException("unsafe_backup_target", $"Refusing to use a backup path outside the management directory: {fullTarget}");
        }
    }

    private static (byte[]? Content, bool Redacted) RedactDeepSeekBearerToken(byte[]? config)
    {
        if (config is null)
        {
            return (null, false);
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(config);
        }
        catch (DecoderFallbackException)
        {
            return (config, false);
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var scanner = new TomlLineScanner();
        var inDeepSeekSection = false;
        var redacted = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!scanner.IsInsideMultilineString && TomlLineScanner.TryGetHeader(lines[i], out var section))
            {
                inDeepSeekSection = section.Equals("model_providers.deepseek", StringComparison.Ordinal) ||
                                    section.StartsWith("model_providers.deepseek.", StringComparison.Ordinal);
            }
            else if (inDeepSeekSection && !scanner.IsInsideMultilineString &&
                     TomlLineScanner.TryGetSimpleAssignment(lines[i], out var key, out _) &&
                     key.Equals("experimental_bearer_token", StringComparison.Ordinal))
            {
                var indentation = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)];
                lines[i] = indentation + "experimental_bearer_token = \"<redacted>\"";
                redacted = true;
            }

            scanner.Consume(lines[i]);
        }

        return (redacted ? new UTF8Encoding(false).GetBytes(string.Join('\n', lines)) : config, redacted);
    }
}
