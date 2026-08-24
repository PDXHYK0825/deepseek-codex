using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Infrastructure;

public sealed class ManagedStateStore
{
    private readonly JsonFileStore _json;

    public ManagedStateStore(AtomicFileWriter writer)
    {
        _json = new JsonFileStore(writer);
    }

    public Task<ManagedState?> ReadAsync(CodexPaths paths, CancellationToken cancellationToken = default) =>
        _json.ReadAsync<ManagedState>(paths.StatePath, cancellationToken);

    public Task WriteAsync(CodexPaths paths, ManagedState state, CancellationToken cancellationToken = default) =>
        _json.WriteAsync(paths.StatePath, state, cancellationToken);
}
