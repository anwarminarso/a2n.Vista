using a2n.Vista.Client.TypeScript.Cli;
using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript;

/// <summary>
/// The generator's process entry point. It binds the real <c>Console</c> streams and the concrete
/// <see cref="PipelineRunner"/> to <see cref="CliHost.RunAsync"/> and returns the exit code the host
/// computes (Requirements 10.1–10.3, 10.6, 10.7). All argument handling, usage, and reporting live in the
/// testable <see cref="CliHost"/>/<see cref="CommandLine"/> pair; the buffered acquire → parse → resolve →
/// model → emit → write pipeline lives in <see cref="PipelineRunner"/> (task 12.2).
/// </summary>
internal static class Program
{
    private static Task<int> Main(string[] args) =>
        CliHost.RunAsync(args, new PipelineRunner(), Console.Out, Console.Error);
}
