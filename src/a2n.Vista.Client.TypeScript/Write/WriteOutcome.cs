namespace a2n.Vista.Client.TypeScript.Write;

/// <summary>
/// The successful result of the write stage: the number of files committed to the output directory.
/// The CLI report (task 12.1) pairs this count with the output directory and view count on a zero-exit
/// success (Requirement 10.6).
/// </summary>
/// <param name="FileCount">The number of <c>GeneratedFile</c>s written to the output directory.</param>
public sealed record WriteOutcome(int FileCount);
