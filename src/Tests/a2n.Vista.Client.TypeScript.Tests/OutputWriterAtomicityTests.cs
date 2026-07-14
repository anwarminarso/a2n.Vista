using System.Reflection;
using System.Text;

using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Write;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Unit tests for <see cref="OutputWriter"/>'s write-failure atomicity (task 11.2; Requirements 9.4, 10.5,
/// 10.7). Every filesystem effect the writer performs is driven through a deterministic
/// <see cref="FakeFileSystem"/> double injected via the writer's test-only
/// <c>OutputWriter(IFileSystem)</c> constructor (reached by reflection, matching the repo's
/// no-<c>InternalsVisibleTo</c> convention — see <c>HttpsSourceTests.CreateWithBudget</c>). The double can
/// force a failure at a chosen operation (the k-th byte write, or a move) and records exactly what was
/// written and moved, so the guarantees are pinned without touching the real disk:
/// <list type="bullet">
///   <item>an output path that exists but is not a writable directory yields
///     <see cref="GenerationError.OutputPathNotWritable"/> with nothing written or moved (Requirement 10.5);</item>
///   <item>a staging-phase write failure yields <see cref="GenerationError.WriteFailure"/> naming the
///     affected path, never touches the target (no move), and removes the staging area (Requirements 9.4, 10.7);</item>
///   <item>a move-phase failure yields <see cref="GenerationError.WriteFailure"/> and still removes staging;</item>
///   <item>pre-existing output is left completely unmodified when staging fails;</item>
///   <item>the success path stages every file then moves it to the correct host-mapped target path; and</item>
///   <item>the exact bytes handed to the filesystem are UTF-8 without a BOM with <c>\n</c> preserved verbatim.</item>
/// </list>
/// </summary>
public sealed class OutputWriterAtomicityTests
{
    /// <summary>
    /// A deterministic <see cref="IFileSystem"/> double. It tracks which paths "exist" as files or
    /// directories and whether directories are writable, records every write/move/create/delete, and can be
    /// configured to throw an <see cref="IOException"/> on a chosen (1-based) write or move call. Staging
    /// paths carry the writer's unique <c>vista-staging</c> marker, which the assertions use to separate
    /// staging effects from target effects.
    /// </summary>
    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _files = new(StringComparer.Ordinal);
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
        private readonly HashSet<string> _nonWritableDirectories = new(StringComparer.Ordinal);

        private int _writeCalls;
        private int _moveCalls;

        /// <summary>1-based index of the <see cref="WriteAllBytes"/> call that should throw, if any.</summary>
        public int? ThrowOnWriteCall { get; set; }

        /// <summary>1-based index of the <see cref="MoveFile"/> call that should throw, if any.</summary>
        public int? ThrowOnMoveCall { get; set; }

        public List<(string Path, byte[] Bytes)> Writes { get; } = new();

        public List<(string Source, string Destination, bool Overwrite)> Moves { get; } = new();

        public List<string> CreatedDirectories { get; } = new();

        public List<string> DeletedDirectories { get; } = new();

        public void MarkFile(string path) => _files.Add(path);

        public void MarkDirectory(string path) => _directories.Add(path);

        public void MarkDirectoryNonWritable(string path)
        {
            _directories.Add(path);
            _nonWritableDirectories.Add(path);
        }

        public bool FileExists(string path) => _files.Contains(path);

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public bool IsDirectoryWritable(string path) => !_nonWritableDirectories.Contains(path);

        public void CreateDirectory(string path)
        {
            CreatedDirectories.Add(path);
            _directories.Add(path);
        }

        public void WriteAllBytes(string path, byte[] contents)
        {
            _writeCalls++;
            if (_writeCalls == ThrowOnWriteCall)
            {
                // The message embeds the offending path so the writer's WriteFailure.Detail carries it.
                throw new IOException($"Simulated disk-full failure writing '{path}'.");
            }

            Writes.Add((path, (byte[])contents.Clone()));
            _files.Add(path);
        }

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            _moveCalls++;
            if (_moveCalls == ThrowOnMoveCall)
            {
                throw new IOException($"Simulated move failure moving to '{destinationPath}'.");
            }

            Moves.Add((sourcePath, destinationPath, overwrite));
            _files.Remove(sourcePath);
            _files.Add(destinationPath);
        }

        public void DeleteDirectory(string path, bool recursive) => DeletedDirectories.Add(path);
    }

    // The writer marks staging directories/files with this fragment (see OutputWriter.ComputeStagingPath).
    private const string StagingMarker = "vista-staging";

    /// <summary>
    /// Constructs an <see cref="OutputWriter"/> over the supplied <see cref="IFileSystem"/> through its
    /// test-only internal constructor via reflection (the repo's no-<c>InternalsVisibleTo</c> convention).
    /// </summary>
    private static OutputWriter CreateWithFileSystem(IFileSystem fileSystem)
    {
        ConstructorInfo ctor = typeof(OutputWriter).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            new[] { typeof(IFileSystem) },
            modifiers: null)
            ?? throw new InvalidOperationException("The internal OutputWriter(IFileSystem) constructor was not found.");

        return (OutputWriter)ctor.Invoke(new object[] { fileSystem });
    }

    // A fresh, absolute output directory path. It is never created on disk — the fake models existence —
    // so Path.GetFullPath returns it unchanged and the writer's normalized target equals this value.
    private static string FreshOutputDirectory() =>
        Path.Combine(Path.GetTempPath(), $"vista-out-{Guid.NewGuid():N}");

    private static IReadOnlyList<GeneratedFile> SampleFiles() =>
        new[]
        {
            new GeneratedFile("index.ts", "export * from './types';\n"),
            new GeneratedFile("types.ts", "export interface Foo {\n  id: number;\n}\n"),
            new GeneratedFile("runtime/auth.ts", "export interface AuthProvider {}\n"),
            new GeneratedFile("views/orders.ts", "export const orders = 1;\n"),
        };

    private static bool IsStaging(string path) => path.Contains(StagingMarker, StringComparison.Ordinal);

    [Test]
    public async Task Unwritable_Output_Path_That_Is_A_File_Reports_NotWritable_Without_Writing()
    {
        string outputDir = FreshOutputDirectory();
        string target = Path.GetFullPath(outputDir);
        var fs = new FakeFileSystem();
        // The path exists as a file where a directory is expected (Requirement 10.5).
        fs.MarkFile(target);

        OutputWriter writer = CreateWithFileSystem(fs);
        Result<WriteOutcome, GenerationError> result = writer.Write(SampleFiles(), outputDir);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<GenerationError.OutputPathNotWritable>();
        await Assert.That(((GenerationError.OutputPathNotWritable)result.Error).Path).IsEqualTo(target);

        // Nothing was staged or committed: no writes, no moves, no directories created.
        await Assert.That(fs.Writes).IsEmpty();
        await Assert.That(fs.Moves).IsEmpty();
        await Assert.That(fs.CreatedDirectories).IsEmpty();
    }

    [Test]
    public async Task Unwritable_Output_Path_NonWritable_Directory_Reports_NotWritable_Without_Writing()
    {
        string outputDir = FreshOutputDirectory();
        string target = Path.GetFullPath(outputDir);
        var fs = new FakeFileSystem();
        // The path exists as a directory that cannot be written to (Requirement 10.5).
        fs.MarkDirectoryNonWritable(target);

        OutputWriter writer = CreateWithFileSystem(fs);
        Result<WriteOutcome, GenerationError> result = writer.Write(SampleFiles(), outputDir);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<GenerationError.OutputPathNotWritable>();
        await Assert.That(((GenerationError.OutputPathNotWritable)result.Error).Path).IsEqualTo(target);

        await Assert.That(fs.Writes).IsEmpty();
        await Assert.That(fs.Moves).IsEmpty();
        await Assert.That(fs.CreatedDirectories).IsEmpty();
    }

    [Test]
    public async Task Mid_Write_Staging_Failure_Reports_WriteFailure_Touches_No_Target_And_Removes_Staging()
    {
        string outputDir = FreshOutputDirectory();
        string target = Path.GetFullPath(outputDir);
        IReadOnlyList<GeneratedFile> files = SampleFiles();
        var fs = new FakeFileSystem
        {
            // Fail on the third byte write — well into staging, before the commit phase begins.
            ThrowOnWriteCall = 3,
        };

        OutputWriter writer = CreateWithFileSystem(fs);
        Result<WriteOutcome, GenerationError> result = writer.Write(files, outputDir);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<GenerationError.WriteFailure>();
        var failure = (GenerationError.WriteFailure)result.Error;

        // The affected file path is reported via the failure detail (the fake embeds it in the exception).
        string affectedFragment = files[2].RelativePath.Replace('/', Path.DirectorySeparatorChar);
        await Assert.That(failure.Detail).Contains(affectedFragment);

        // A staging-only failure never touches the target: no file was moved into place.
        await Assert.That(fs.Moves).IsEmpty();
        await Assert.That(fs.Writes.Any(w => !IsStaging(w.Path))).IsFalse();

        // The staging directory was removed on failure (Requirement 10.7).
        await Assert.That(fs.DeletedDirectories.Any(IsStaging)).IsTrue();
    }

    [Test]
    public async Task Move_Phase_Failure_Reports_WriteFailure_And_Removes_Staging()
    {
        string outputDir = FreshOutputDirectory();
        IReadOnlyList<GeneratedFile> files = SampleFiles();
        var fs = new FakeFileSystem
        {
            // Fail on the second move — the narrow mid-commit window the writer documents and flags.
            ThrowOnMoveCall = 2,
        };

        OutputWriter writer = CreateWithFileSystem(fs);
        Result<WriteOutcome, GenerationError> result = writer.Write(files, outputDir);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<GenerationError.WriteFailure>();

        // All files staged successfully before the move failure.
        await Assert.That(fs.Writes.Count).IsEqualTo(files.Count);
        // The first move landed; the second threw, so it never recorded.
        await Assert.That(fs.Moves.Count).IsEqualTo(1);

        // Staging is still cleaned up even for a mid-commit fault (Requirement 10.7).
        await Assert.That(fs.DeletedDirectories.Any(IsStaging)).IsTrue();
    }

    [Test]
    public async Task Pre_Existing_Output_Is_Left_Unmodified_When_Staging_Fails()
    {
        string outputDir = FreshOutputDirectory();
        string target = Path.GetFullPath(outputDir);
        IReadOnlyList<GeneratedFile> files = SampleFiles();

        var fs = new FakeFileSystem
        {
            // Fail on the very first staging write, before the commit phase can start.
            ThrowOnWriteCall = 1,
        };
        // Simulate a target directory that already holds prior output.
        fs.MarkDirectory(target);
        string priorFileA = Path.Combine(target, "index.ts");
        string priorFileB = Path.Combine(target, "types.ts");
        fs.MarkFile(priorFileA);
        fs.MarkFile(priorFileB);

        OutputWriter writer = CreateWithFileSystem(fs);
        Result<WriteOutcome, GenerationError> result = writer.Write(files, outputDir);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<GenerationError.WriteFailure>();

        // The pre-existing output is untouched: nothing was moved over it, and no target file was written.
        await Assert.That(fs.Moves).IsEmpty();
        await Assert.That(fs.Writes.Any(w => !IsStaging(w.Path))).IsFalse();
        // Both prior files still exist exactly as before (never removed or replaced).
        await Assert.That(fs.FileExists(priorFileA)).IsTrue();
        await Assert.That(fs.FileExists(priorFileB)).IsTrue();
    }

    [Test]
    public async Task Success_Path_Stages_Then_Moves_Every_File_To_The_Correct_Target_Path()
    {
        string outputDir = FreshOutputDirectory();
        string target = Path.GetFullPath(outputDir);
        IReadOnlyList<GeneratedFile> files = SampleFiles();
        var fs = new FakeFileSystem();

        OutputWriter writer = CreateWithFileSystem(fs);
        Result<WriteOutcome, GenerationError> result = writer.Write(files, outputDir);

        await Assert.That(result.IsOk).IsTrue();
        await Assert.That(result.Value.FileCount).IsEqualTo(files.Count);

        // Every file was staged (all writes went to the staging area) and then moved into the target.
        await Assert.That(fs.Writes.Count).IsEqualTo(files.Count);
        await Assert.That(fs.Writes.All(w => IsStaging(w.Path))).IsTrue();
        await Assert.That(fs.Moves.Count).IsEqualTo(files.Count);

        // Each move lands the staged file at the host-mapped target path (forward slashes → host separators).
        foreach (GeneratedFile file in files)
        {
            string expectedTarget = Path.Combine(target, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            await Assert.That(fs.Moves.Any(m => m.Destination == expectedTarget && m.Overwrite)).IsTrue();
        }

        // Subdirectories for nested outputs were created under the target (e.g. runtime/, views/).
        string runtimeDir = Path.Combine(target, "runtime");
        string viewsDir = Path.Combine(target, "views");
        await Assert.That(fs.CreatedDirectories.Contains(runtimeDir)).IsTrue();
        await Assert.That(fs.CreatedDirectories.Contains(viewsDir)).IsTrue();

        // Staging is cleaned up on success too.
        await Assert.That(fs.DeletedDirectories.Any(IsStaging)).IsTrue();
    }

    [Test]
    public async Task Written_Bytes_Are_Utf8_Without_Bom_And_Preserve_Newlines()
    {
        string outputDir = FreshOutputDirectory();
        // Content with LF line breaks and a multi-byte character to prove faithful UTF-8 encoding.
        const string content = "line-one\nline-two\ncafé — π\n";
        var files = new[] { new GeneratedFile("types.ts", content) };
        var fs = new FakeFileSystem();

        OutputWriter writer = CreateWithFileSystem(fs);
        Result<WriteOutcome, GenerationError> result = writer.Write(files, outputDir);

        await Assert.That(result.IsOk).IsTrue();
        await Assert.That(fs.Writes.Count).IsEqualTo(1);
        byte[] written = fs.Writes[0].Bytes;

        // Bytes equal UTF-8 (no BOM) of the content exactly.
        byte[] expected = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        await Assert.That(written).IsEquivalentTo(expected);

        // No UTF-8 BOM prefix (EF BB BF).
        bool hasBom = written.Length >= 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF;
        await Assert.That(hasBom).IsFalse();

        // No carriage return was introduced: the \n terminators are preserved verbatim.
        await Assert.That(written.Contains((byte)'\r')).IsFalse();
        await Assert.That(written.Count(b => b == (byte)'\n')).IsEqualTo(3);
    }
}
