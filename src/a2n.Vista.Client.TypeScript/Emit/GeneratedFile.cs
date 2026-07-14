namespace a2n.Vista.Client.TypeScript.Emit;

/// <summary>
/// One emitted TypeScript (or documentation) file, buffered in memory before the write stage: a
/// forward-slash, output-directory-relative path plus its full UTF-8 text content. The whole
/// <see cref="Generated_Output"/> is a set of these, produced purely from the in-memory model and written
/// in one atomic pass (design §A.7/§A.8), so the emit stage performs no I/O of its own.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RelativePath"/> always uses <c>/</c> separators (e.g. <c>runtime/auth.ts</c>) so the emitted
/// layout is identical on every operating system; the write stage maps it onto the host path convention.
/// <see cref="Content"/> uses a fixed <c>\n</c> line terminator and is written as UTF-8 without a BOM, the
/// single fixed encoding/line-terminator the determinism guarantee rests on (Requirement 9.1).
/// </para>
/// </remarks>
/// <param name="RelativePath">
/// The output-directory-relative path, using <c>/</c> separators (e.g. <c>runtime/auth.ts</c>).
/// </param>
/// <param name="Content">The full file text, using <c>\n</c> line terminators (written as UTF-8, no BOM).</param>
public sealed record GeneratedFile(string RelativePath, string Content);
