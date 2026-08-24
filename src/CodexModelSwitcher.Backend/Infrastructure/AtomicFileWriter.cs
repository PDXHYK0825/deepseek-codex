using System.Text;

namespace CodexModelSwitcher.Infrastructure;

public sealed class AtomicFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default) =>
        WriteBytesAsync(path, Utf8NoBom.GetBytes(content), cancellationToken);

    public async Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Cannot resolve the parent directory for {fullPath}.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
