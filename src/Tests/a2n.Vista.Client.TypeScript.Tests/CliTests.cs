using a2n.Vista.Client.TypeScript.Cli;
using a2n.Vista.Client.TypeScript.Pipeline;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// CLI unit tests (task 12.3; Requirements 10.3, 10.6, 10.7). They pin the command-line contract at two
/// seams without spawning a process:
/// <list type="bullet">
///   <item><see cref="CommandLine.Parse(string[])"/> — the pure argument-to-<see cref="GenerationConfig"/>
///     function: required-value gating (Requirement 10.3), source classification (file vs https, http
///     rejected), the write-facet flag default, and help precedence.</item>
///   <item><see cref="CliHost.RunAsync(string[], IPipelineRunner?, System.IO.TextWriter, System.IO.TextWriter, System.Threading.CancellationToken)"/>
///     — the exit-code + reporting contract driven with in-memory <see cref="StringWriter"/>s and a
///     <see cref="RecordingPipelineRunner"/> fake: usage to stdout on help (exit 0), the error message to
///     stderr on bad args (nonzero exit), the success report on a good run (exit 0, Requirement 10.6), and
///     the error message on a failed run (nonzero exit, Requirement 10.7). The fake also proves the runner
///     is never invoked on a parse error and is invoked exactly once on valid arguments.</item>
/// </list>
/// Everything runs against the injectable writers and the fake runner, so no process is spawned and no disk
/// is touched (design "Stage responsibilities", CLI row).
/// </summary>
public sealed class CliTests
{
    /// <summary>
    /// A fake <see cref="IPipelineRunner"/> that records whether it was called and the
    /// <see cref="GenerationConfig"/> it received, then returns a pre-configured
    /// <see cref="GenerationResult"/>. It never touches the pipeline, the filesystem, or the network.
    /// </summary>
    private sealed class RecordingPipelineRunner : IPipelineRunner
    {
        private readonly GenerationResult _result;

        public RecordingPipelineRunner(GenerationResult result) => _result = result;

        /// <summary>Whether <see cref="RunAsync"/> was invoked.</summary>
        public bool WasCalled { get; private set; }

        /// <summary>How many times <see cref="RunAsync"/> was invoked.</summary>
        public int CallCount { get; private set; }

        /// <summary>The configuration handed to the most recent invocation, if any.</summary>
        public GenerationConfig? ReceivedConfig { get; private set; }

        public Task<GenerationResult> RunAsync(GenerationConfig config, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            CallCount++;
            ReceivedConfig = config;
            return Task.FromResult(_result);
        }
    }

    // A runner whose result is irrelevant because it must never be called (parse-error paths).
    private static RecordingPipelineRunner NeverCalledRunner() =>
        new(GenerationResult.Success("unused", 0));

    // ---------------------------------------------------------------------------------------------
    // CommandLine.Parse — required-value gating (Requirement 10.3)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task Parse_MissingSource_ReturnsInvalid_MissingConfigValue_NamingSource()
    {
        // --out is present but --source is omitted entirely.
        CommandLineParseOutcome outcome = CommandLine.Parse(new[] { CommandLine.OutOption, "out-dir" });

        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.Invalid>();
        GenerationError error = ((CommandLineParseOutcome.Invalid)outcome).Error;
        await Assert.That(error).IsTypeOf<GenerationError.MissingConfigValue>();
        await Assert.That(((GenerationError.MissingConfigValue)error).ValueName).IsEqualTo(CommandLine.SourceOption);
    }

    [Test]
    public async Task Parse_MissingOut_ReturnsInvalid_MissingConfigValue_NamingOut()
    {
        // --source is present but --out is omitted entirely.
        CommandLineParseOutcome outcome = CommandLine.Parse(new[] { CommandLine.SourceOption, "spec.json" });

        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.Invalid>();
        GenerationError error = ((CommandLineParseOutcome.Invalid)outcome).Error;
        await Assert.That(error).IsTypeOf<GenerationError.MissingConfigValue>();
        await Assert.That(((GenerationError.MissingConfigValue)error).ValueName).IsEqualTo(CommandLine.OutOption);
    }

    [Test]
    public async Task Parse_OptionWithNoValue_ReturnsInvalid_MissingConfigValue()
    {
        // --base-url is the last token with no following value → its required value is missing.
        CommandLineParseOutcome outcome = CommandLine.Parse(new[]
        {
            CommandLine.SourceOption, "spec.json",
            CommandLine.OutOption, "out-dir",
            CommandLine.BaseUrlOption,
        });

        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.Invalid>();
        GenerationError error = ((CommandLineParseOutcome.Invalid)outcome).Error;
        await Assert.That(error).IsTypeOf<GenerationError.MissingConfigValue>();
        await Assert.That(((GenerationError.MissingConfigValue)error).ValueName).IsEqualTo(CommandLine.BaseUrlOption);
    }

    // ---------------------------------------------------------------------------------------------
    // CommandLine.Parse — valid configurations (Requirements 10.1, 10.2)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task Parse_ValidFileSource_ReturnsParsed_WithFileLocation_AndDefaults()
    {
        CommandLineParseOutcome outcome = CommandLine.Parse(new[]
        {
            CommandLine.SourceOption, "openapi.json",
            CommandLine.OutOption, "generated",
        });

        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.Parsed>();
        GenerationConfig config = ((CommandLineParseOutcome.Parsed)outcome).Config;

        await Assert.That(config.Source).IsTypeOf<OpenApiSourceLocation.File>();
        await Assert.That(((OpenApiSourceLocation.File)config.Source).Path).IsEqualTo("openapi.json");
        await Assert.That(config.OutputDirectory).IsEqualTo("generated");
        // Write facets default off; base URL absent (Requirement 10.2).
        await Assert.That(config.EmitWriteFacets).IsFalse();
        await Assert.That(config.DefaultBaseUrl).IsNull();
    }

    [Test]
    public async Task Parse_EmitWriteFacetsFlag_SetsEmitWriteFacetsTrue()
    {
        CommandLineParseOutcome outcome = CommandLine.Parse(new[]
        {
            CommandLine.SourceOption, "openapi.json",
            CommandLine.OutOption, "generated",
            CommandLine.EmitWriteFacetsOption,
        });

        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.Parsed>();
        GenerationConfig config = ((CommandLineParseOutcome.Parsed)outcome).Config;
        await Assert.That(config.EmitWriteFacets).IsTrue();
    }

    [Test]
    public async Task Parse_BaseUrlOption_SetsDefaultBaseUrl()
    {
        CommandLineParseOutcome outcome = CommandLine.Parse(new[]
        {
            CommandLine.SourceOption, "openapi.json",
            CommandLine.OutOption, "generated",
            CommandLine.BaseUrlOption, "https://api.example.com",
        });

        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.Parsed>();
        GenerationConfig config = ((CommandLineParseOutcome.Parsed)outcome).Config;
        await Assert.That(config.DefaultBaseUrl).IsEqualTo("https://api.example.com");
    }

    [Test]
    public async Task Parse_HttpsSource_ReturnsParsed_WithHttpsLocation()
    {
        CommandLineParseOutcome outcome = CommandLine.Parse(new[]
        {
            CommandLine.SourceOption, "https://api.example.com/openapi.json",
            CommandLine.OutOption, "generated",
        });

        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.Parsed>();
        GenerationConfig config = ((CommandLineParseOutcome.Parsed)outcome).Config;
        await Assert.That(config.Source).IsTypeOf<OpenApiSourceLocation.Https>();
        await Assert.That(((OpenApiSourceLocation.Https)config.Source).Url.ToString())
            .IsEqualTo("https://api.example.com/openapi.json");
    }

    [Test]
    public async Task Parse_HttpSource_IsRejected_AsInvalidArgument()
    {
        // Secure-by-default: only https URLs are fetched; an absolute http URL is rejected.
        CommandLineParseOutcome outcome = CommandLine.Parse(new[]
        {
            CommandLine.SourceOption, "http://api.example.com/openapi.json",
            CommandLine.OutOption, "generated",
        });

        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.Invalid>();
        await Assert.That(((CommandLineParseOutcome.Invalid)outcome).Error)
            .IsTypeOf<GenerationError.InvalidArgument>();
    }

    [Test]
    public async Task Parse_UnknownArgument_ReturnsInvalidArgument()
    {
        CommandLineParseOutcome outcome = CommandLine.Parse(new[]
        {
            CommandLine.SourceOption, "openapi.json",
            CommandLine.OutOption, "generated",
            "--nonsense",
        });

        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.Invalid>();
        await Assert.That(((CommandLineParseOutcome.Invalid)outcome).Error)
            .IsTypeOf<GenerationError.InvalidArgument>();
    }

    [Test]
    public async Task Parse_LongHelp_ReturnsHelpRequested()
    {
        CommandLineParseOutcome outcome = CommandLine.Parse(new[] { CommandLine.HelpOption });
        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.HelpRequested>();
    }

    [Test]
    public async Task Parse_ShortHelp_ReturnsHelpRequested()
    {
        CommandLineParseOutcome outcome = CommandLine.Parse(new[] { CommandLine.HelpOptionShort });
        await Assert.That(outcome).IsTypeOf<CommandLineParseOutcome.HelpRequested>();
    }

    // ---------------------------------------------------------------------------------------------
    // CliHost.RunAsync — usage on help (Requirement 10.3)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task RunAsync_Help_WritesUsageToStdout_ReturnsZero_AndNeverCallsRunner()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        RecordingPipelineRunner runner = NeverCalledRunner();

        int exit = await CliHost.RunAsync(new[] { CommandLine.HelpOption }, runner, stdout, stderr);

        await Assert.That(exit).IsEqualTo(CliHost.ExitSuccess);
        await Assert.That(stdout.ToString()).Contains(CommandLine.UsageText);
        // Nothing went to stderr, and the pipeline was never touched for a help request.
        await Assert.That(stderr.ToString()).IsEmpty();
        await Assert.That(runner.WasCalled).IsFalse();
    }

    // ---------------------------------------------------------------------------------------------
    // CliHost.RunAsync — invalid/missing args (Requirement 10.3)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task RunAsync_MissingArgs_WritesErrorToStderr_ReturnsNonzero_AndNeverCallsRunner()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        RecordingPipelineRunner runner = NeverCalledRunner();

        // No --source and no --out at all: a missing required value.
        int exit = await CliHost.RunAsync(Array.Empty<string>(), runner, stdout, stderr);

        await Assert.That(exit).IsEqualTo(CliHost.ExitFailure);
        await Assert.That(exit).IsNotEqualTo(CliHost.ExitSuccess);
        // The typed error's English message is on stderr; the parse error never runs the pipeline.
        var expected = new GenerationError.MissingConfigValue(CommandLine.SourceOption, CommandLine.UsageText);
        await Assert.That(stderr.ToString()).Contains(expected.Message);
        await Assert.That(runner.WasCalled).IsFalse();
    }

    [Test]
    public async Task RunAsync_InvalidArgs_WritesErrorToStderr_ReturnsNonzero_AndNeverCallsRunner()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        RecordingPipelineRunner runner = NeverCalledRunner();

        // An unrecognized argument is an invalid-argument parse error.
        int exit = await CliHost.RunAsync(
            new[] { CommandLine.SourceOption, "spec.json", CommandLine.OutOption, "out", "--bogus" },
            runner,
            stdout,
            stderr);

        await Assert.That(exit).IsEqualTo(CliHost.ExitFailure);
        // Something explanatory reached stderr and the runner stayed untouched.
        await Assert.That(stderr.ToString().Length).IsGreaterThan(0);
        await Assert.That(runner.WasCalled).IsFalse();
    }

    // ---------------------------------------------------------------------------------------------
    // CliHost.RunAsync — successful run (Requirement 10.6)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task RunAsync_ValidArgs_SuccessResult_ReturnsZero_ReportsOutputAndViewsAndNotices()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        const string outputDir = "generated-out";
        const int viewCount = 3;
        var notice = GenerationNotice.PermissiveObjectMember("Orders", "meta");
        var success = GenerationResult.Success(outputDir, viewCount, new[] { notice });
        var runner = new RecordingPipelineRunner(success);

        int exit = await CliHost.RunAsync(
            new[] { CommandLine.SourceOption, "spec.json", CommandLine.OutOption, outputDir },
            runner,
            stdout,
            stderr);

        await Assert.That(exit).IsEqualTo(CliHost.ExitSuccess);

        // The runner was invoked exactly once with the parsed configuration.
        await Assert.That(runner.WasCalled).IsTrue();
        await Assert.That(runner.CallCount).IsEqualTo(1);
        await Assert.That(runner.ReceivedConfig!.OutputDirectory).IsEqualTo(outputDir);

        // stdout carries the full success report: output directory, view count, and the notice text.
        string outText = stdout.ToString();
        await Assert.That(outText).Contains(CliHost.FormatSuccessReport(success));
        await Assert.That(outText).Contains(outputDir);
        await Assert.That(outText).Contains(viewCount.ToString());
        await Assert.That(outText).Contains(notice.Message);
        // A successful run writes nothing to stderr.
        await Assert.That(stderr.ToString()).IsEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // CliHost.RunAsync — failed run (Requirement 10.7)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task RunAsync_ValidArgs_FailureResult_ReturnsNonzero_ReportsErrorToStderr()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var error = new GenerationError.MissingSchema("VistaListRequestBody");
        var failure = GenerationResult.Failure(error);
        var runner = new RecordingPipelineRunner(failure);

        int exit = await CliHost.RunAsync(
            new[] { CommandLine.SourceOption, "spec.json", CommandLine.OutOption, "out" },
            runner,
            stdout,
            stderr);

        await Assert.That(exit).IsEqualTo(CliHost.ExitFailure);
        await Assert.That(exit).IsNotEqualTo(CliHost.ExitSuccess);

        // The runner ran (valid args), and the fatal error's message is reported on stderr.
        await Assert.That(runner.WasCalled).IsTrue();
        await Assert.That(stderr.ToString()).Contains(error.Message);
        // No success report leaked to stdout for a failed run (no partial output contract).
        await Assert.That(stdout.ToString()).IsEmpty();
    }
}
