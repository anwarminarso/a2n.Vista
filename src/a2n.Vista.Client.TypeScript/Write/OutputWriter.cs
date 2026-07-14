using System.Text;

using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Write;

/// <summary>
/// The pipeline's final stage (design §A.8): commits the buffered <see cref="GeneratedFile"/> set to the
/// output directory with a fixed encoding and an all-or-nothing discipline. Every file is written as
/// <b>UTF-8 without a BOM</b> with its content's <c>\n</c> line terminators preserved verbatim, so the
/// output is byte-identical on every run and OS (Requirement 9.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pre-check (Requirements 10.4, 10.5).</b> If the output path exists but is not a writable directory
/// (it is a file, or the directory cannot be written to), the writer aborts with
/// <see cref="GenerationError.OutputPathNotWritable"/> and touches nothing. If the path is absent it is
/// created during the commit phase.
/// </para>
/// <para>
/// <b>Atomic strategy (Requirements 9.4, 10.7).</b> All files are first written into a fresh temporary
/// staging directory (created as a sibling of the target so the final moves are same-volume renames). Only
/// after <i>every</i> file has staged successfully does the writer create the target directory and move the
/// staged files into place, replacing any existing files. Because nothing touches the target until staging
/// is complete, the overwhelmingly common failure modes — a disk-full or permission error while producing
/// file content — occur during staging and leave any pre-existing output completely unmodified. On any
/// failure the staging directory is removed and a typed <see cref="GenerationError.WriteFailure"/> naming
/// the affected path is returned.
/// </para>
/// <para>
/// <b>Honest atomicity limit.</b> The move phase is file-by-file rather than a single kernel-atomic
/// directory swap (the latter is not portable across volumes or the three target runtimes). If a move fails
/// partway — after files are already staged — some files may have already been replaced in the target. This
/// is a deliberately narrow window: staging has already succeeded, so a move failure implies an external
/// filesystem fault mid-commit rather than a generation error. The writer does not attempt to roll back
/// replaced files (that would require pre-move backups of prior output); it reports the failing path and
/// leaves cleanup of the staging area guaranteed. For the guarantee callers rely on — a <i>generation</i>
/// failure never producing partial output — buffering plus stage-before-commit is sufficient.
/// </para>
/// </remarks>
public sealed class OutputWriter
{
    // The single fixed encoding the determinism guarantee rests on: UTF-8, no BOM (Requirement 9.1).
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IFileSystem _fs;

    /// <summary>
    /// Creates a writer backed by the real filesystem. This is the constructor the pipeline uses (task 12.2).
    /// </summary>
    public OutputWriter()
        : this(new PhysicalFileSystem())
    {
    }

    /// <summary>
    /// Test-only seam: creates a writer over an arbitrary <see cref="IFileSystem"/> so a deterministic
    /// double can force a write or move failure at a chosen path (task 11.2). Kept <see langword="internal"/>
    /// to keep the public surface the pipeline sees minimal; tests reach it via reflection, matching the
    /// repository's no-<c>InternalsVisibleTo</c> convention.
    /// </summary>
    /// <param name="fileSystem">The filesystem seam the writer performs all effects through.</param>
    internal OutputWriter(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fs = fileSystem;
    }

    /// <summary>
    /// Commits <paramref name="files"/> to <paramref name="outputDirectory"/> atomically (see the type
    /// remarks), or returns a typed <see cref="GenerationError"/> without producing partial output.
    /// </summary>
    /// <param name="files">The buffered generated files, each with a forward-slash relative path.</param>
    /// <param name="outputDirectory">The target output directory (created if absent).</param>
    /// <returns>
    /// <see cref="Result{T, E}.Ok"/> carrying the written file count on success; otherwise
    /// <see cref="Result{T, E}.Err"/> carrying <see cref="GenerationError.OutputPathNotWritable"/> or
    /// <see cref="GenerationError.WriteFailure"/>.
    /// </returns>
    public Result<WriteOutcome, GenerationError> Write(
        IReadOnlyList<GeneratedFile> files,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        // Normalize the target to a full path up front; an unusable path string is itself "not writable".
        string target;
        try
        {
            target = Path.GetFullPath(outputDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException)
        {
            return Fail(new GenerationError.OutputPathNotWritable(outputDirectory));
        }

        // Pre-check the existing path (Requirement 10.5): a file where a directory is expected, or a
        // directory that cannot be written to, is fatal before anything is staged. An absent path is fine —
        // it is created during the commit phase (Requirement 10.4).
        if (_fs.FileExists(target) && !_fs.DirectoryExists(target))
        {
            return Fail(new GenerationError.OutputPathNotWritable(target));
        }

        if (_fs.DirectoryExists(target) && !_fs.IsDirectoryWritable(target))
        {
            return Fail(new GenerationError.OutputPathNotWritable(target));
        }

        string staging = ComputeStagingPath(target);
        try
        {
            // Phase 1 — stage every file. Any failure here has not touched the target at all.
            _fs.CreateDirectory(staging);
            foreach (GeneratedFile file in files)
            {
                string stagedPath = ToHostPath(staging, file.RelativePath);
                string? stagedDir = Path.GetDirectoryName(stagedPath);
                if (!string.IsNullOrEmpty(stagedDir))
                {
                    _fs.CreateDirectory(stagedDir);
                }

                _fs.WriteAllBytes(stagedPath, Utf8NoBom.GetBytes(file.Content));
            }

            // Phase 2 — commit. Create the target only now (Requirement 10.4), then move staged files into
            // place, replacing any existing files.
            _fs.CreateDirectory(target);
            foreach (GeneratedFile file in files)
            {
                string stagedPath = ToHostPath(staging, file.RelativePath);
                string targetPath = ToHostPath(target, file.RelativePath);
                string? targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    _fs.CreateDirectory(targetDir);
                }

                _fs.MoveFile(stagedPath, targetPath, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException)
        {
            TryRemoveStaging(staging);
            return Fail(new GenerationError.WriteFailure(target, ex.Message));
        }

        // Best-effort cleanup of the (now-empty) staging directory on success.
        TryRemoveStaging(staging);
        return Result<WriteOutcome, GenerationError>.Ok(new WriteOutcome(files.Count));
    }

    // Derives a fresh, unique staging directory beside the target so the commit-phase moves are same-volume
    // renames. Falls back to the temp path when the target has no parent (e.g. a volume root).
    private static string ComputeStagingPath(string target)
    {
        string leaf = $".{Path.GetFileName(target)}.vista-staging-{Guid.NewGuid():N}";
        string? parent = Path.GetDirectoryName(target);
        return string.IsNullOrEmpty(parent)
            ? Path.Combine(Path.GetTempPath(), leaf)
            : Path.Combine(parent, leaf);
    }

    // Maps a forward-slash, output-relative path onto the host path convention under a root directory.
    private static string ToHostPath(string root, string relativePath)
    {
        string native = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(root, native);
    }

    private void TryRemoveStaging(string staging)
    {
        try
        {
            _fs.DeleteDirectory(staging, recursive: true);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException)
        {
            // Staging lives under a unique, throwaway name; a failed cleanup is not itself a generation
            // failure and must not mask a real result.
        }
    }

    private static Result<WriteOutcome, GenerationError> Fail(GenerationError error) =>
        Result<WriteOutcome, GenerationError>.Err(error);
}
