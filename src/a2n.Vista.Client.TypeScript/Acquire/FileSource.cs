using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Acquire;

/// <summary>
/// An <see cref="IOpenApiSource"/> that reads the raw OpenAPI document bytes from a local file path
/// (Requirements 1.1, 1.2). A missing or unreadable file is an expected failure: it is reported as a
/// typed <see cref="AcquireError.FileUnreadable"/> identifying the path, never as a thrown exception.
/// </summary>
public sealed class FileSource : IOpenApiSource
{
    private readonly string _path;

    /// <summary>
    /// Creates a file source for the document at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The local path to the OpenAPI document.</param>
    public FileSource(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = path;
    }

    /// <inheritdoc />
    public async Task<Result<ReadOnlyMemory<byte>, AcquireError>> ReadAsync(CancellationToken ct)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(_path, ct).ConfigureAwait(false);
            return Result<ReadOnlyMemory<byte>, AcquireError>.Ok(bytes);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an acquisition failure: let it propagate so the pipeline can abort.
            throw;
        }
        catch (Exception ex) when (
            ex is FileNotFoundException
                or DirectoryNotFoundException
                or DriveNotFoundException
                or PathTooLongException
                or UnauthorizedAccessException
                or NotSupportedException
                or System.Security.SecurityException
                or ArgumentException
                or IOException)
        {
            // Any expected I/O or path failure degrades to the typed, path-identifying error (Requirement 1.2).
            return Result<ReadOnlyMemory<byte>, AcquireError>.Err(new AcquireError.FileUnreadable(_path));
        }
    }
}
