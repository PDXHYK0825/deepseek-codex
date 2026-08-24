using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexModelSwitcher.Abstractions;
using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Infrastructure;

public sealed partial class OfficialDeepSeekCatalogProvider : IModelCatalogProvider
{
    public const string ScriptUrl = "https://cdn.deepseek.com/api-docs/codex-deepseek-setup-en.ps1";

    private const int MaximumScriptCharacters = 2_000_000;
    private static readonly string[] RequiredSlugs =
    [
        ModelProfile.DeepSeekFlash.ToModelSlug(),
        ModelProfile.DeepSeekPro.ToModelSlug(),
        ModelProfile.DeepSeekVision.ToModelSlug()
    ];

    private readonly HttpClient _httpClient;
    private readonly AtomicFileWriter _writer;

    public OfficialDeepSeekCatalogProvider(HttpClient httpClient, AtomicFileWriter writer)
    {
        _httpClient = httpClient;
        _writer = writer;
    }

    public async Task<ModelCatalogDocument> GetCatalogAsync(
        CodexPaths paths,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        if (!refresh && TryReadValidCatalog(paths.CatalogCachePath, out var cached))
        {
            return new ModelCatalogDocument(cached, "cache", Hashing.Sha256(cached));
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ScriptUrl);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new SwitcherException("catalog_download_failed", $"DeepSeek returned HTTP {(int)response.StatusCode} while downloading the catalog source.");
            }

            if (response.Content.Headers.ContentLength is > MaximumScriptCharacters)
            {
                throw new SwitcherException("catalog_too_large", "The official DeepSeek setup script exceeded the allowed size.");
            }

            var script = await response.Content.ReadAsStringAsync(cancellationToken);
            if (script.Length > MaximumScriptCharacters)
            {
                throw new SwitcherException("catalog_too_large", "The official DeepSeek setup script exceeded the allowed size.");
            }

            var extracted = ExtractCatalog(script);
            await _writer.WriteTextAsync(paths.CatalogCachePath, extracted.Json, cancellationToken);
            return extracted;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (TryReadValidCatalog(paths.CatalogCachePath, out var fallbackCached))
            {
                return new ModelCatalogDocument(fallbackCached, "cache-fallback", Hashing.Sha256(fallbackCached));
            }

            if (TryReadValidCatalog(paths.ModelsPath, out var existing))
            {
                return new ModelCatalogDocument(existing, "existing-catalog-fallback", Hashing.Sha256(existing));
            }

            throw new SwitcherException(
                "catalog_unavailable",
                "Could not download or locate a valid DeepSeek model catalog. No Codex files were modified.",
                exception);
        }
    }

    internal static ModelCatalogDocument ExtractCatalog(string script)
    {
        const string startMarker = "$ModelsJson = @'";
        var start = script.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new SwitcherException("catalog_marker_missing", "The official script no longer contains the expected model catalog marker.");
        }

        var bodyStart = start + startMarker.Length;
        if (bodyStart < script.Length && script[bodyStart] == '\r')
        {
            bodyStart++;
        }

        if (bodyStart < script.Length && script[bodyStart] == '\n')
        {
            bodyStart++;
        }

        var end = script.IndexOf("\n'@", bodyStart, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new SwitcherException("catalog_marker_missing", "The official script contains an unterminated model catalog block.");
        }

        var rawCatalog = script[bodyStart..end].TrimEnd('\r', '\n');
        var selectedCatalog = SelectRequiredModels(rawCatalog);
        var versionMatch = ScriptVersionRegex().Match(script);
        var version = versionMatch.Success ? versionMatch.Groups[1].Value : "unknown";
        return new ModelCatalogDocument(selectedCatalog, version, Hashing.Sha256(selectedCatalog));
    }

    internal static bool IsValidCatalog(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var slugs = models.EnumerateArray()
                .Where(model => model.TryGetProperty("slug", out _))
                .Select(model => model.GetProperty("slug").GetString())
                .Where(slug => slug is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            return RequiredSlugs.All(slugs.Contains);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string SelectRequiredModels(string rawCatalog)
    {
        using var document = JsonDocument.Parse(rawCatalog);
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            throw new SwitcherException("catalog_invalid", "The official model catalog does not contain a models array.");
        }

        var selected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var model in models.EnumerateArray())
        {
            if (!model.TryGetProperty("slug", out var slugElement))
            {
                continue;
            }

            var slug = slugElement.GetString();
            if (slug is not null && RequiredSlugs.Contains(slug, StringComparer.Ordinal))
            {
                selected[slug] = model;
            }
        }

        var missing = RequiredSlugs.Where(slug => !selected.ContainsKey(slug)).ToArray();
        if (missing.Length > 0)
        {
            throw new SwitcherException("catalog_models_missing", $"The official catalog is missing: {string.Join(", ", missing)}");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("models");
            writer.WriteStartArray();
            foreach (var slug in RequiredSlugs)
            {
                selected[slug].WriteTo(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static bool TryReadValidCatalog(string path, out string json)
    {
        json = string.Empty;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            json = File.ReadAllText(path, Encoding.UTF8);
            return IsValidCatalog(json);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [GeneratedRegex("\\$SCRIPT_VERSION\\s*=\\s*'([^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptVersionRegex();
}
