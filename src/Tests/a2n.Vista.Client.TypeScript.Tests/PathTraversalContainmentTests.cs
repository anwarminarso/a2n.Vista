using System.Reflection;

using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Write;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Regression tests for audit finding <c>SEC-06</c> (path traversal through a hostile OpenAPI document). The
/// acquired document is external input — it may be fetched over HTTPS — and its <c>operationId</c> flows into
/// the derived view name, the emitted file name, and finally the output path. Two independent guards are
/// pinned here: the model-stage name validation (a typed error, never an unhandled exception) and the
/// write-stage containment check (nothing is written outside <c>--out</c>).
/// </summary>
public sealed class PathTraversalContainmentTests
{
    // ---- Guard 1: an unsafe derived view name is a typed model-stage error --------------------------

    [Test]
    public async Task Traversing_View_Name_Is_A_Typed_Error()
    {
        GenerationError? error = ViewNameGuard.Validate("../../../evil");

        await Assert.That(error).IsTypeOf<GenerationError.UnsafeViewName>();
        await Assert.That(((GenerationError.UnsafeViewName)error!).ViewName).IsEqualTo("../../../evil");
    }

    [Test]
    public async Task Empty_View_Name_Is_A_Typed_Error_Not_An_Exception()
    {
        // A document with a `/list` path and no operationId produced an empty name, which used to crash the
        // CLI with an unhandled ArgumentException instead of a reported GenerationError.
        await Assert.That(ViewNameGuard.Validate(string.Empty)).IsTypeOf<GenerationError.UnsafeViewName>();
        await Assert.That(ViewNameGuard.Validate(null)).IsTypeOf<GenerationError.UnsafeViewName>();
    }

    [Test]
    public async Task Separator_And_Dot_Bearing_Names_Are_Rejected()
    {
        await Assert.That(ViewNameGuard.Validate("a/b")).IsNotNull();
        await Assert.That(ViewNameGuard.Validate("a\\b")).IsNotNull();
        await Assert.That(ViewNameGuard.Validate("a.b")).IsNotNull();
        await Assert.That(ViewNameGuard.Validate("1leading")).IsNotNull();
    }

    [Test]
    public async Task Ordinary_View_Names_Are_Accepted()
    {
        await Assert.That(ViewNameGuard.Validate("Customers")).IsNull();
        await Assert.That(ViewNameGuard.Validate("vOrderDetail")).IsNull();
        await Assert.That(ViewNameGuard.Validate("_internal2")).IsNull();
    }

    // ---- Guard 2: the write stage refuses any path that escapes the output root ---------------------

    [Test]
    public async Task Escaping_Relative_Path_Is_Refused_Before_Anything_Is_Written()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), $"vista-out-{Guid.NewGuid():N}");
        var fs = new RecordingFileSystem();

        var files = new[]
        {
            new GeneratedFile("index.ts", "export {};\n"),
            new GeneratedFile("views/../../../evil.ts", "// payload\n"),
        };

        Result<WriteOutcome, GenerationError> result = CreateWithFileSystem(fs).Write(files, outputDir);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<GenerationError.OutputPathEscapesRoot>();

        // The check runs before staging, so the traversal never created a directory or wrote a file.
        await Assert.That(fs.Writes).IsEmpty();
        await Assert.That(fs.CreatedDirectories).IsEmpty();
    }

    [Test]
    public async Task Rooted_Relative_Path_Is_Refused()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), $"vista-out-{Guid.NewGuid():N}");
        var fs = new RecordingFileSystem();

        var rooted = OperatingSystem.IsWindows() ? "C:/evil.ts" : "/evil.ts";
        var files = new[] { new GeneratedFile(rooted, "// payload\n") };

        Result<WriteOutcome, GenerationError> result = CreateWithFileSystem(fs).Write(files, outputDir);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<GenerationError.OutputPathEscapesRoot>();
        await Assert.That(fs.Writes).IsEmpty();
    }

    [Test]
    public async Task Nested_Paths_Inside_The_Root_Are_Still_Accepted()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), $"vista-out-{Guid.NewGuid():N}");
        var fs = new RecordingFileSystem();

        var files = new[]
        {
            new GeneratedFile("index.ts", "export {};\n"),
            new GeneratedFile("runtime/auth.ts", "export interface AuthProvider {}\n"),
            new GeneratedFile("views/orders.ts", "export const orders = 1;\n"),
        };

        Result<WriteOutcome, GenerationError> result = CreateWithFileSystem(fs).Write(files, outputDir);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(result.Value.FileCount).IsEqualTo(files.Length);
    }

    // ---- Fixtures ----------------------------------------------------------------------------------

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

    /// <summary>A permissive <see cref="IFileSystem"/> double that records effects and never fails.</summary>
    private sealed class RecordingFileSystem : IFileSystem
    {
        public List<string> Writes { get; } = new();

        public List<string> CreatedDirectories { get; } = new();

        public bool FileExists(string path) => false;

        public bool DirectoryExists(string path) => false;

        public bool IsDirectoryWritable(string path) => true;

        public void CreateDirectory(string path) => CreatedDirectories.Add(path);

        public void WriteAllBytes(string path, byte[] contents) => Writes.Add(path);

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
        }

        public void DeleteDirectory(string path, bool recursive)
        {
        }
    }
}
