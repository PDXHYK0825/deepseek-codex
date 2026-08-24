using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Infrastructure;

internal sealed class OperationLock : IAsyncDisposable
{
    private readonly FileStream _stream;

    private OperationLock(FileStream stream)
    {
        _stream = stream;
    }

    public static async Task<OperationLock> AcquireAsync(
        CodexPaths paths,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.ManagementRoot);
        var lockPath = Path.Combine(paths.ManagementRoot, "operation.lock");
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new OperationLock(stream);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (IOException exception)
            {
                throw new SwitcherException("operation_busy", "Another model switch operation is still running.", exception);
            }
        }
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
