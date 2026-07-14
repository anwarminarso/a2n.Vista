namespace a2n.Vista.Client.TypeScript.Pipeline;

/// <summary>
/// The overall, typed outcome of a generation run (design "Cross-cutting"). A run either succeeds
/// (reporting the output directory and view count for Requirement 10.6) or fails with a typed
/// <see cref="GenerationError"/> (Requirement 10.7). In both cases any non-fatal notices collected
/// before the outcome are carried along, deterministically ordered.
/// </summary>
public sealed record GenerationResult
{
    private GenerationResult(
        bool succeeded,
        string? outputDirectory,
        int viewCount,
        GenerationError? error,
        IReadOnlyList<GenerationNotice> notices)
    {
        Succeeded = succeeded;
        OutputDirectory = outputDirectory;
        ViewCount = viewCount;
        Error = error;
        Notices = notices;
    }

    /// <summary>Gets a value indicating whether generation completed successfully.</summary>
    public bool Succeeded { get; }

    /// <summary>The output directory that was written, when <see cref="Succeeded"/> is <c>true</c>.</summary>
    public string? OutputDirectory { get; }

    /// <summary>The number of views generated, when <see cref="Succeeded"/> is <c>true</c>.</summary>
    public int ViewCount { get; }

    /// <summary>The fatal error, when <see cref="Succeeded"/> is <c>false</c>.</summary>
    public GenerationError? Error { get; }

    /// <summary>The non-fatal notices recorded during the run, in deterministic order.</summary>
    public IReadOnlyList<GenerationNotice> Notices { get; }

    /// <summary>
    /// Creates a successful outcome (Requirement 10.6). <paramref name="notices"/> defaults to an
    /// empty list when omitted.
    /// </summary>
    public static GenerationResult Success(
        string outputDirectory,
        int viewCount,
        IReadOnlyList<GenerationNotice>? notices = null)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        return new GenerationResult(
            succeeded: true,
            outputDirectory: outputDirectory,
            viewCount: viewCount,
            error: null,
            notices: notices ?? Array.Empty<GenerationNotice>());
    }

    /// <summary>
    /// Creates a failed outcome carrying the fatal <paramref name="error"/> (Requirement 10.7).
    /// Any notices collected before the failure may still be carried along.
    /// </summary>
    public static GenerationResult Failure(
        GenerationError error,
        IReadOnlyList<GenerationNotice>? notices = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new GenerationResult(
            succeeded: false,
            outputDirectory: null,
            viewCount: 0,
            error: error,
            notices: notices ?? Array.Empty<GenerationNotice>());
    }
}
