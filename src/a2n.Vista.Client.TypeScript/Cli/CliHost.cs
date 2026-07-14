using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Cli;

/// <summary>
/// The command-line host: turns raw arguments plus an <see cref="IPipelineRunner"/> into a process
/// exit code, writing usage/reports to the supplied text writers (Requirements 10.1–10.3, 10.6, 10.7).
/// </summary>
/// <remarks>
/// The host takes its writers and runner as parameters (rather than reaching for <c>Console</c>
/// directly) so the exit-code and reporting contract is unit-testable without spawning a process
/// (task 12.3). <see cref="Program"/> is the only place that binds the real <c>Console</c> streams.
/// </remarks>
public static class CliHost
{
    /// <summary>The process exit code for a successful run (Requirement 10.6).</summary>
    public const int ExitSuccess = 0;

    /// <summary>The process exit code for any failure (Requirements 10.3, 10.7).</summary>
    public const int ExitFailure = 1;

    /// <summary>
    /// Parses <paramref name="args"/>, and on a valid configuration delegates to
    /// <paramref name="runner"/>; then reports the outcome and returns the process exit code.
    /// </summary>
    /// <param name="args">The raw command-line arguments.</param>
    /// <param name="runner">
    /// The pipeline runner wired in task 12.2. When <c>null</c>, the pipeline is not yet available:
    /// a valid configuration is reported as a not-yet-wired failure with a nonzero exit code, while
    /// argument parsing, usage, and the error/exit-code contract are fully exercised.
    /// </param>
    /// <param name="stdout">Where usage (for <c>--help</c>) and the success report are written.</param>
    /// <param name="stderr">Where argument and generation errors are written.</param>
    /// <param name="cancellationToken">A token to cancel a pipeline run.</param>
    public static async Task<int> RunAsync(
        string[] args,
        IPipelineRunner? runner,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var outcome = CommandLine.Parse(args);
        switch (outcome)
        {
            case CommandLineParseOutcome.HelpRequested:
                stdout.WriteLine(CommandLine.UsageText);
                return ExitSuccess;

            case CommandLineParseOutcome.Invalid invalid:
                stderr.WriteLine(invalid.Error.Message);
                return ExitFailure;

            case CommandLineParseOutcome.Parsed parsed:
                return await RunPipelineAsync(parsed.Config, runner, stdout, stderr, cancellationToken)
                    .ConfigureAwait(false);

            default:
                // Unreachable: the outcome union is closed.
                stderr.WriteLine("Internal error: unhandled command-line outcome.");
                return ExitFailure;
        }
    }

    private static async Task<int> RunPipelineAsync(
        GenerationConfig config,
        IPipelineRunner? runner,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        if (runner is null)
        {
            // The pipeline is wired in task 12.2. Until then, a valid configuration cannot be
            // executed; fail cleanly with a nonzero exit rather than pretending to succeed.
            stderr.WriteLine("The generation pipeline is not yet wired (pending task 12.2).");
            return ExitFailure;
        }

        var result = await runner.RunAsync(config, cancellationToken).ConfigureAwait(false);
        return Report(result, stdout, stderr);
    }

    /// <summary>
    /// Writes the success or failure report for <paramref name="result"/> and returns the exit code:
    /// zero and the output directory + view count + notices on success (Requirement 10.6); nonzero and
    /// the error message (plus any notices) on failure (Requirement 10.7).
    /// </summary>
    public static int Report(GenerationResult result, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (result.Succeeded)
        {
            stdout.WriteLine(FormatSuccessReport(result));
            return ExitSuccess;
        }

        stderr.WriteLine(result.Error!.Message);
        var notices = FormatNotices(result.Notices);
        if (notices is not null)
        {
            stderr.WriteLine(notices);
        }

        return ExitFailure;
    }

    /// <summary>
    /// Formats the successful-run report: the output directory, the count of generated views, and any
    /// non-fatal notices, in a stable, English form (Requirement 10.6). Reused by task 12.2.
    /// </summary>
    public static string FormatSuccessReport(GenerationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            throw new ArgumentException("Cannot format a success report for a failed result.", nameof(result));
        }

        var viewLabel = result.ViewCount == 1 ? "view" : "views";
        var lines = new List<string>
        {
            $"Generation succeeded. Output directory: {result.OutputDirectory}",
            $"Generated {result.ViewCount} {viewLabel}.",
        };

        var notices = FormatNotices(result.Notices);
        if (notices is not null)
        {
            lines.Add(notices);
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Formats the non-fatal notices as a stable, human-readable block, or <c>null</c> when there are
    /// none. Notices arrive pre-ordered from the pipeline, preserving deterministic reporting.
    /// </summary>
    private static string? FormatNotices(IReadOnlyList<GenerationNotice> notices)
    {
        if (notices is null || notices.Count == 0)
        {
            return null;
        }

        var noticeLabel = notices.Count == 1 ? "notice" : "notices";
        var lines = new List<string> { $"{notices.Count} {noticeLabel}:" };
        foreach (var notice in notices)
        {
            lines.Add($"  - {notice.Message}");
        }

        return string.Join('\n', lines);
    }
}
