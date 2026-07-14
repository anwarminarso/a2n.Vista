namespace a2n.Vista.Client.TypeScript.Pipeline;

/// <summary>
/// The typed, fatal outcome of any pipeline stage. Every fatal cause the generator can hit
/// (design "Error Handling" table) is a leaf of this closed hierarchy, so the pipeline returns a
/// <see cref="GenerationError"/> rather than throwing across the CLI boundary. <c>Program</c> maps
/// each error to a nonzero exit code and the English <see cref="Message"/> on stderr.
/// </summary>
/// <remarks>
/// The stage-level unions (<see cref="AcquireError"/>, <see cref="ParseError"/>,
/// <see cref="ResolveError"/>) derive from this type, so a stage seam returning
/// <c>Result&lt;T, AcquireError&gt;</c> widens to <c>Result&lt;T, GenerationError&gt;</c> without a wrapper.
/// </remarks>
public abstract record GenerationError
{
    private protected GenerationError() { }

    /// <summary>A human-readable, English description of the fatal cause for stderr reporting.</summary>
    public abstract string Message { get; }

    /// <summary>
    /// A required <c>Generation_Config</c> value was missing from the command line (Requirement 10.3).
    /// </summary>
    /// <param name="ValueName">The missing configuration value (for example, <c>--source</c> or <c>--out</c>).</param>
    /// <param name="Usage">Usage guidance printed alongside the error.</param>
    public sealed record MissingConfigValue(string ValueName, string Usage) : GenerationError
    {
        /// <inheritdoc />
        public override string Message =>
            $"Missing required configuration value '{ValueName}'.{Environment.NewLine}{Usage}";
    }

    /// <summary>
    /// A command-line argument was malformed, unrecognized, or otherwise invalid (Requirement 10.3).
    /// Distinct from <see cref="MissingConfigValue"/>, which reports an omitted required value.
    /// </summary>
    /// <param name="Detail">A human-readable description of what was wrong with the arguments.</param>
    /// <param name="Usage">Usage guidance printed alongside the error.</param>
    public sealed record InvalidArgument(string Detail, string Usage) : GenerationError
    {
        /// <inheritdoc />
        public override string Message =>
            $"{Detail}{Environment.NewLine}{Usage}";
    }

    /// <summary>
    /// A required Vista envelope or referenced type was absent from <c>components.schemas</c>
    /// (Requirement 2.7).
    /// </summary>
    /// <param name="SchemaName">The schema name that could not be found.</param>
    public sealed record MissingSchema(string SchemaName) : GenerationError
    {
        /// <inheritdoc />
        public override string Message =>
            $"Required schema '{SchemaName}' is absent from the document's components.schemas.";
    }

    /// <summary>
    /// The configured output path exists but is not a writable directory (Requirement 10.5).
    /// </summary>
    /// <param name="Path">The offending output path.</param>
    public sealed record OutputPathNotWritable(string Path) : GenerationError
    {
        /// <inheritdoc />
        public override string Message =>
            $"Output path '{Path}' exists but is not a writable directory.";
    }

    /// <summary>
    /// A file could not be written during the atomic write stage (Requirements 9.4, 10.7).
    /// </summary>
    /// <param name="Path">The file path that could not be written.</param>
    /// <param name="Detail">The underlying failure detail.</param>
    public sealed record WriteFailure(string Path, string Detail) : GenerationError
    {
        /// <inheritdoc />
        public override string Message =>
            $"Failed to write output file '{Path}': {Detail}";
    }
}

/// <summary>
/// A fatal failure while acquiring the raw document bytes (design §A.2). Returned by
/// <c>IOpenApiSource</c> implementations, which never throw for expected failures.
/// </summary>
public abstract record AcquireError : GenerationError
{
    private AcquireError() { }

    /// <summary>
    /// A local file source could not be found or opened for reading (Requirement 1.2).
    /// </summary>
    /// <param name="Path">The file path that could not be read.</param>
    public sealed record FileUnreadable(string Path) : AcquireError
    {
        /// <inheritdoc />
        public override string Message =>
            $"OpenAPI source file '{Path}' does not exist or could not be opened for reading.";
    }

    /// <summary>
    /// An HTTPS fetch timed out (30-second budget) or returned a non-success response
    /// (Requirement 1.4).
    /// </summary>
    /// <param name="Url">The source URL that was fetched.</param>
    /// <param name="Detail">The fetch failure detail (timeout, status code, or transport error).</param>
    public sealed record Fetch(string Url, string Detail) : AcquireError
    {
        /// <inheritdoc />
        public override string Message =>
            $"Failed to fetch the OpenAPI source over HTTPS from '{Url}': {Detail}";
    }
}

/// <summary>
/// A fatal failure while parsing the acquired bytes into the internal document model (design §A.3).
/// </summary>
public abstract record ParseError : GenerationError
{
    private ParseError() { }

    /// <summary>
    /// The document declares an <c>openapi</c> version outside the supported <c>3.0.x</c>–<c>3.1.x</c>
    /// range (Requirement 1.5).
    /// </summary>
    /// <param name="DeclaredVersion">The unsupported version string read from the document.</param>
    public sealed record UnsupportedVersion(string DeclaredVersion) : ParseError
    {
        /// <inheritdoc />
        public override string Message =>
            $"Unsupported OpenAPI version '{DeclaredVersion}'. Supported range is 3.0.x through 3.1.x.";
    }

    /// <summary>
    /// The document is not well-formed (Requirement 1.6).
    /// </summary>
    /// <param name="Location">Where the malformation was detected (for example, a JSON path or offset).</param>
    /// <param name="Detail">The nature of the malformation.</param>
    public sealed record Malformed(string Location, string Detail) : ParseError
    {
        /// <inheritdoc />
        public override string Message =>
            $"Malformed OpenAPI document at '{Location}': {Detail}";
    }
}

/// <summary>
/// A fatal failure while resolving local <c>$ref</c>s to their targets under <c>components</c>
/// (design §A.4).
/// </summary>
public abstract record ResolveError : GenerationError
{
    private ResolveError() { }

    /// <summary>
    /// A <c>$ref</c> resolved to no component (Requirement 1.8).
    /// </summary>
    /// <param name="RefValue">The dangling <c>$ref</c> value, included verbatim in the report.</param>
    public sealed record Dangling(string RefValue) : ResolveError
    {
        /// <inheritdoc />
        public override string Message =>
            $"Dangling reference: '{RefValue}' resolves to no component.";
    }
}
