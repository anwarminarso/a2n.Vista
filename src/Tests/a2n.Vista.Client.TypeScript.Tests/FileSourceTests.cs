using System.Text;
using a2n.Vista.Client.TypeScript.Acquire;
using a2n.Vista.Client.TypeScript.Pipeline;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Unit tests for <see cref="FileSource"/>, the acquire-stage file reader (Requirements 1.1, 1.2).
/// They pin the three behaviours the design promises: an existing file reads back its exact bytes as an
/// <c>Ok</c> result; a missing path degrades to the typed <see cref="AcquireError.FileUnreadable"/>
/// identifying the requested file without throwing; and an unreadable path (a directory passed as a file)
/// degrades the same way. Every case inspects the returned <see cref="Result{T, E}"/> — no exception is
/// allowed to escape <see cref="FileSource.ReadAsync"/> for these expected failures.
/// </summary>
public sealed class FileSourceTests
{
    [Test]
    public async Task ReadAsync_ExistingFile_ReturnsOk_WithExactBytes()
    {
        // A temp file with known, non-trivial byte content (includes multi-byte UTF-8 to prove no re-encoding).
        var payload = Encoding.UTF8.GetBytes("{ \"openapi\": \"3.0.4\", \"note\": \"café — π\" }");
        var path = Path.Combine(Path.GetTempPath(), $"vista-filesource-{Guid.NewGuid():N}.json");
        await File.WriteAllBytesAsync(path, payload);

        try
        {
            var source = new FileSource(path);

            var result = await source.ReadAsync(CancellationToken.None);

            await Assert.That(result.IsOk).IsTrue();
            // The bytes come back verbatim — the source reads raw, it does not transcode or trim.
            await Assert.That(result.Value.ToArray()).IsEquivalentTo(payload);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ReadAsync_MissingFile_ReturnsFileUnreadable_IdentifyingThePath_WithoutThrowing()
    {
        // A path that is guaranteed not to exist (fresh GUID under the temp directory, never created).
        var missingPath = Path.Combine(Path.GetTempPath(), $"vista-missing-{Guid.NewGuid():N}.json");
        await Assert.That(File.Exists(missingPath)).IsFalse();

        var source = new FileSource(missingPath);

        // The call must complete and yield a Result — never throw for this expected failure.
        var result = await source.ReadAsync(CancellationToken.None);

        await Assert.That(result.IsError).IsTrue();
        var error = result.Error;
        await Assert.That(error).IsTypeOf<AcquireError.FileUnreadable>();
        // The typed error names the exact file the caller asked for.
        await Assert.That(((AcquireError.FileUnreadable)error).Path).IsEqualTo(missingPath);
    }

    [Test]
    public async Task ReadAsync_UnreadablePath_DirectoryAsFile_ReturnsFileUnreadable_WithoutThrowing()
    {
        // A directory is a valid path but not a readable file: reading it as a file is an expected failure.
        var directoryPath = Path.Combine(Path.GetTempPath(), $"vista-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);

        try
        {
            var source = new FileSource(directoryPath);

            var result = await source.ReadAsync(CancellationToken.None);

            await Assert.That(result.IsError).IsTrue();
            var error = result.Error;
            await Assert.That(error).IsTypeOf<AcquireError.FileUnreadable>();
            // The typed error identifies the offending path rather than surfacing a raw I/O exception.
            await Assert.That(((AcquireError.FileUnreadable)error).Path).IsEqualTo(directoryPath);
        }
        finally
        {
            Directory.Delete(directoryPath);
        }
    }

    [Test]
    public async Task ReadAsync_MissingFile_DoesNotThrow_And_YieldsAReportableMessage()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"vista-missing-{Guid.NewGuid():N}.json");
        var source = new FileSource(missingPath);

        var result = await source.ReadAsync(CancellationToken.None);

        await Assert.That(result.IsError).IsTrue();
        // The error carries an English, path-identifying message for stderr reporting (design §A.2).
        await Assert.That(result.Error.Message).Contains(missingPath);
    }
}
