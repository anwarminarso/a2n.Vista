using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Cli;

/// <summary>
/// The pure, testable result of parsing command-line arguments (design "Stage responsibilities",
/// CLI row). A closed, three-way outcome so the host can, without exceptions:
/// print usage and exit zero (<see cref="HelpRequested"/>), run the pipeline
/// (<see cref="Parsed"/>), or print usage plus the error and exit nonzero (<see cref="Invalid"/>).
/// </summary>
public abstract record CommandLineParseOutcome
{
    private CommandLineParseOutcome() { }

    /// <summary>The user asked for usage (<c>--help</c>/<c>-h</c>); print it to stdout and exit zero.</summary>
    public sealed record HelpRequested : CommandLineParseOutcome;

    /// <summary>The arguments parsed into a valid <see cref="GenerationConfig"/>.</summary>
    /// <param name="Config">The configuration to hand to the pipeline.</param>
    public sealed record Parsed(GenerationConfig Config) : CommandLineParseOutcome;

    /// <summary>The arguments were invalid; <see cref="Error"/> carries the typed, English cause.</summary>
    /// <param name="Error">The fatal argument error (missing required value or invalid argument).</param>
    public sealed record Invalid(GenerationError Error) : CommandLineParseOutcome;
}

/// <summary>
/// Parses <c>string[]</c> arguments into a <see cref="GenerationConfig"/> (Requirements 10.1–10.3).
/// The parser is a pure function of its input — no I/O, no environment, no <c>Console</c> — so CLI
/// tests (task 12.3) can drive it directly without spawning a process.
/// </summary>
/// <remarks>
/// <para>CLI surface (see <see cref="UsageText"/>):</para>
/// <list type="bullet">
///   <item><c>--source &lt;file-path-or-https-url&gt;</c> (required) — the OpenAPI document. If the value
///   parses as an absolute <c>https</c> URL it becomes <see cref="OpenApiSourceLocation.Https"/>;
///   otherwise it is treated as a local file path (<see cref="OpenApiSourceLocation.File"/>). An
///   absolute <c>http</c> URL is rejected (secure-by-default; only <c>https</c> URLs are fetched).</item>
///   <item><c>--out &lt;dir&gt;</c> (required) — the output directory.</item>
///   <item><c>--emit-write-facets</c> (optional flag, default off) — enables create/update/delete
///   generation (Requirement 10.2 / 5.1).</item>
///   <item><c>--base-url &lt;url&gt;</c> (optional) — baked into the generated client's default base URL.</item>
///   <item><c>--help</c> / <c>-h</c> — print usage and exit zero.</item>
/// </list>
/// </remarks>
public static class CommandLine
{
    /// <summary>The <c>--source</c> option name.</summary>
    public const string SourceOption = "--source";

    /// <summary>The <c>--out</c> option name.</summary>
    public const string OutOption = "--out";

    /// <summary>The <c>--emit-write-facets</c> flag name.</summary>
    public const string EmitWriteFacetsOption = "--emit-write-facets";

    /// <summary>The <c>--base-url</c> option name.</summary>
    public const string BaseUrlOption = "--base-url";

    /// <summary>The long <c>--help</c> flag name.</summary>
    public const string HelpOption = "--help";

    /// <summary>The short <c>-h</c> help flag name.</summary>
    public const string HelpOptionShort = "-h";

    /// <summary>
    /// The English usage guidance printed on <c>--help</c> (to stdout) and alongside every argument
    /// error (to stderr). A single source of truth so both paths stay consistent (Requirement 10.3).
    /// </summary>
    public static string UsageText { get; } = string.Join(
        '\n',
        "a2n.Vista TypeScript client generator",
        "",
        "Generates a framework-agnostic TypeScript client from an OpenAPI 3.0.x/3.1.x document.",
        "",
        "Usage:",
        "  vista-ts --source <file-or-https-url> --out <dir> [options]",
        "",
        "Required:",
        "  --source <file|https-url>   OpenAPI document to read: a local file path or an https URL.",
        "  --out <dir>                 Output directory for the generated TypeScript.",
        "",
        "Options:",
        "  --emit-write-facets         Emit create/update/delete operations (default: off).",
        "  --base-url <url>            Default base URL baked into the generated client.",
        "  -h, --help                  Show this help and exit.");

    /// <summary>
    /// Parses <paramref name="args"/> into a <see cref="CommandLineParseOutcome"/>. Never throws:
    /// every malformed-input case is returned as <see cref="CommandLineParseOutcome.Invalid"/> and a
    /// help request as <see cref="CommandLineParseOutcome.HelpRequested"/>.
    /// </summary>
    /// <param name="args">The raw command-line arguments.</param>
    public static CommandLineParseOutcome Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? source = null;
        string? output = null;
        string? baseUrl = null;
        var emitWriteFacets = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case HelpOption:
                case HelpOptionShort:
                    // Help takes precedence over everything else, even if other args are invalid.
                    return new CommandLineParseOutcome.HelpRequested();

                case SourceOption:
                    if (!TryTakeValue(args, ref i, SourceOption, out var sourceValue, out var sourceError))
                    {
                        return sourceError;
                    }

                    source = sourceValue;
                    break;

                case OutOption:
                    if (!TryTakeValue(args, ref i, OutOption, out var outValue, out var outError))
                    {
                        return outError;
                    }

                    output = outValue;
                    break;

                case BaseUrlOption:
                    if (!TryTakeValue(args, ref i, BaseUrlOption, out var baseUrlValue, out var baseUrlError))
                    {
                        return baseUrlError;
                    }

                    baseUrl = baseUrlValue;
                    break;

                case EmitWriteFacetsOption:
                    emitWriteFacets = true;
                    break;

                default:
                    return Invalid($"Unrecognized argument '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return Missing(SourceOption);
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return Missing(OutOption);
        }

        if (!TryResolveSource(source, out var location, out var sourceLocationError))
        {
            return sourceLocationError;
        }

        var config = new GenerationConfig(
            Source: location,
            OutputDirectory: output,
            EmitWriteFacets: emitWriteFacets,
            DefaultBaseUrl: string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl);

        return new CommandLineParseOutcome.Parsed(config);
    }

    /// <summary>
    /// Consumes the value that must follow an option at <paramref name="index"/>. Advances
    /// <paramref name="index"/> past the value on success; on a missing value returns a typed
    /// <see cref="CommandLineParseOutcome.Invalid"/> (Requirement 10.3).
    /// </summary>
    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string option,
        out string value,
        out CommandLineParseOutcome error)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = Missing(option);
            return false;
        }

        var next = args[index + 1];

        // A following token that is itself an option means the value was omitted.
        if (IsOption(next))
        {
            value = string.Empty;
            error = Missing(option);
            return false;
        }

        index++;
        value = next;
        error = new CommandLineParseOutcome.HelpRequested(); // unused on the success path
        return true;
    }

    /// <summary>
    /// Classifies the <c>--source</c> value into a file path or an <c>https</c> URL. An absolute
    /// <c>http</c> URL is rejected (only <c>https</c> is fetched); every non-URL value is a file path.
    /// </summary>
    private static bool TryResolveSource(
        string source,
        out OpenApiSourceLocation location,
        out CommandLineParseOutcome error)
    {
        error = new CommandLineParseOutcome.HelpRequested(); // unused on the success path

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                location = new OpenApiSourceLocation.Https(uri);
                return true;
            }

            if (uri.Scheme == Uri.UriSchemeHttp)
            {
                location = null!;
                error = Invalid(
                    $"The {SourceOption} URL '{source}' uses http; only https URLs are supported. " +
                    "Pass an https URL or a local file path.");
                return false;
            }

            // file:// or a plain drive-rooted path (scheme 'file'): treat as a local file path.
        }

        location = new OpenApiSourceLocation.File(source);
        return true;
    }

    private static bool IsOption(string token) =>
        token.Length >= 2 && token[0] == '-' && !IsNegativeNumber(token);

    private static bool IsNegativeNumber(string token) =>
        token.Length >= 2 && token[0] == '-' && char.IsDigit(token[1]);

    private static CommandLineParseOutcome.Invalid Missing(string valueName) =>
        new(new GenerationError.MissingConfigValue(valueName, UsageText));

    private static CommandLineParseOutcome.Invalid Invalid(string detail) =>
        new(new GenerationError.InvalidArgument(detail, UsageText));
}
