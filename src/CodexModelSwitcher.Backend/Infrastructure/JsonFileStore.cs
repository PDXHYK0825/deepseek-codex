using System.Text.Json;

namespace CodexModelSwitcher.Infrastructure;

internal sealed class JsonFileStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AtomicFileWriter _writer;

    public JsonFileStore(AtomicFileWriter writer)
    {
        _writer = writer;
    }

    public async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    public Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, Options) + "\n";
        return _writer.WriteTextAsync(path, json, cancellationToken);
    }
}
