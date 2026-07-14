namespace a2n.Vista.Client.TypeScript.Write;

/// <summary>
/// The default, real <see cref="IFileSystem"/> backed by <see cref="System.IO"/>. This is the
/// implementation the parameterless <see cref="OutputWriter"/> constructor wires in for the pipeline
/// (task 12.2); tests substitute a deterministic double through the writer's test-only constructor.
/// </summary>
internal sealed class PhysicalFileSystem : IFileSystem
{
    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool IsDirectoryWritable(string path)
    {
        // Probe writability by creating and immediately removing a uniquely named zero-byte file inside the
        // directory. A permission or read-only-volume failure surfaces as a caught exception -> not writable
        // (Requirement 10.5). The probe never throws out of this method.
        var probe = Path.Combine(path, $".vista-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException)
        {
            TryDeleteProbe(probe);
            return false;
        }
    }

    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public void WriteAllBytes(string path, byte[] contents) => File.WriteAllBytes(path, contents);

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);

    /// <inheritdoc />
    public void DeleteDirectory(string path, bool recursive)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }
    }

    private static void TryDeleteProbe(string probe)
    {
        try
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException)
        {
            // Best-effort cleanup of the probe file; ignore.
        }
    }
}
