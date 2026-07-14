namespace a2n.Vista.Client.TypeScript.Write;

/// <summary>
/// The minimal filesystem seam the <see cref="OutputWriter"/> depends on for the atomic write stage
/// (design §A.8). Every filesystem effect the writer performs goes through this interface, so the
/// default real implementation can be swapped for a deterministic test double that forces a write or
/// move failure at a chosen path — the seam task 11.2's write-failure atomicity tests drive.
/// </summary>
/// <remarks>
/// The interface is deliberately primitive: it exposes only the discrete effects the writer needs
/// (existence probes, a writability probe, directory creation, byte writes, file moves, and staging
/// cleanup). All path composition and orchestration stays in <see cref="OutputWriter"/>, which is a
/// pure function of this seam plus its inputs. Implementations report expected failures by throwing;
/// the writer catches them and maps them onto a typed <c>GenerationError</c>.
/// </remarks>
public interface IFileSystem
{
    /// <summary>Returns <c>true</c> when <paramref name="path"/> exists and is a file (not a directory).</summary>
    /// <param name="path">The absolute path to probe.</param>
    bool FileExists(string path);

    /// <summary>Returns <c>true</c> when <paramref name="path"/> exists and is a directory.</summary>
    /// <param name="path">The absolute path to probe.</param>
    bool DirectoryExists(string path);

    /// <summary>
    /// Returns <c>true</c> when the existing directory at <paramref name="path"/> can be written to.
    /// Implementations must not throw: an inability to write (permissions, read-only volume) is
    /// reported as <c>false</c> so the writer can surface <c>OutputPathNotWritable</c> (Requirement 10.5).
    /// </summary>
    /// <param name="path">The existing directory to probe for writability.</param>
    bool IsDirectoryWritable(string path);

    /// <summary>
    /// Creates the directory at <paramref name="path"/> and any missing parents. A no-op when it already
    /// exists (Requirement 10.4). Throws on failure.
    /// </summary>
    /// <param name="path">The directory to create.</param>
    void CreateDirectory(string path);

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/>, overwriting any existing file, with
    /// no encoding transformation of its own (the writer supplies the already-encoded UTF-8 bytes). Throws
    /// on failure.
    /// </summary>
    /// <param name="path">The absolute file path to write.</param>
    /// <param name="contents">The exact bytes to write.</param>
    void WriteAllBytes(string path, byte[] contents);

    /// <summary>
    /// Moves the file at <paramref name="sourcePath"/> to <paramref name="destinationPath"/>, replacing an
    /// existing destination when <paramref name="overwrite"/> is <c>true</c>. Throws on failure.
    /// </summary>
    /// <param name="sourcePath">The staged source file.</param>
    /// <param name="destinationPath">The target file path.</param>
    /// <param name="overwrite">Whether an existing destination file is replaced.</param>
    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    /// <summary>
    /// Deletes the directory at <paramref name="path"/> and, when <paramref name="recursive"/> is
    /// <c>true</c>, all of its contents. Used only for best-effort staging cleanup; a missing directory is
    /// treated as already removed (no throw for that case).
    /// </summary>
    /// <param name="path">The directory to delete.</param>
    /// <param name="recursive">Whether contained files and subdirectories are removed too.</param>
    void DeleteDirectory(string path, bool recursive);
}
