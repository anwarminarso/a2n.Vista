using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Cli;

/// <summary>
/// The seam between argument handling (this task, 12.1) and the buffered generation pipeline
/// (task 12.2). <see cref="CliHost"/> parses arguments, resolves a <see cref="GenerationConfig"/>,
/// and delegates the actual acquire → parse → resolve → model → emit → write run to an
/// <see cref="IPipelineRunner"/>, then formats the returned <see cref="GenerationResult"/> into the
/// success/failure report and exit code (Requirements 10.6, 10.7).
/// </summary>
/// <remarks>
/// Task 12.2 provides the concrete implementation that composes the pipeline stages and buffers all
/// generated files before writing. Keeping the runner behind this interface lets the host — and the
/// CLI tests in task 12.3 — be exercised with a fake runner, independent of the real pipeline.
/// </remarks>
public interface IPipelineRunner
{
    /// <summary>
    /// Runs the full generation pipeline for <paramref name="config"/> and returns the typed outcome.
    /// Implementations never throw for expected failures; every fatal cause is a
    /// <see cref="GenerationError"/> carried on a failed <see cref="GenerationResult"/>.
    /// </summary>
    /// <param name="config">The validated configuration produced from the command line.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    Task<GenerationResult> RunAsync(GenerationConfig config, CancellationToken cancellationToken = default);
}
