using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Hosting;
using a2n.Vista.Contracts;
using a2n.Vista.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Correctness Property 4 — fail-open sadar (design.md §"Property 4"; authoritative
/// docs/spec/01-view.md §5.6, Decision Log D43). Exercises the one-door authorization glue
/// (<see cref="ViewRequestExecutor"/>) and the startup fail-open warning
/// (<see cref="VistaStartupValidator"/>):
/// <list type="bullet">
/// <item>R7.1 — when an authorizer returns <see langword="false"/>, the glue raises
/// <see cref="VistaForbiddenException"/> (the signal the Task 10.4 mapper turns into HTTP 403).</item>
/// <item>R7.2 — when no authorizer is registered, access defaults to allow and the glue executes the
/// view normally (no throw).</item>
/// <item>R7.3 — when no authorizer is registered, <see cref="VistaStartupValidator"/> logs a single
/// <see cref="LogLevel.Warning"/> at startup; when one is registered, it logs nothing.</item>
/// </list>
/// The glue resolves the authorizer, the executor, and the user from the request's
/// <see cref="HttpContext"/>; tests build a <see cref="DefaultHttpContext"/> whose
/// <see cref="HttpContext.RequestServices"/> point at a hand-built <see cref="IServiceProvider"/>
/// containing the registered <see cref="IViewRegistry"/>, <see cref="IViewExecutor"/>, and (optionally)
/// the <see cref="IViewAuthorizer"/> under test.
/// </summary>
// ViewRequestExecutor.ListAsync is [RequiresUnreferencedCode] (it closes IViewExecutor.ListAsync<TRow>
// over the view's runtime row type via reflection; the AOT-clean route is the Pilar 3 source
// generator). These tests exercise that reflection path by design, so the trim/AOT diagnostic is
// suppressed at the class level.
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests exercise the runtime reflection path of ViewRequestExecutor by design; trimming is not used for tests.")]
public sealed class AuthorizationTests
{
    private static readonly IReadOnlyList<SortSpec> ById = new[] { new SortSpec(nameof(WidgetRow.Id)) };

    /// <summary>
    /// R7.1: a registered authorizer that denies (<see cref="IViewAuthorizer.IsAllowedAsync"/> returns
    /// <see langword="false"/>) causes the glue to throw <see cref="VistaForbiddenException"/>, carrying
    /// the offending view name and facet. The Task 10.4 mapper turns this exception into HTTP 403.
    /// </summary>
    [Test]
    public async Task Authorizer_Deny_Throws_Forbidden_With_View_And_Facet()
    {
        using var harness = WidgetTestHarness.Create();
        var (glue, http) = BuildGlue(harness, authorizer: new StubAuthorizer(allow: false));

        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 0, PageSize: 10);

        // Capture the exception so we can assert it carries the right view + facet (R7.1 payload).
        VistaForbiddenException? captured = null;
        try
        {
            await glue.ListAsync(http, "Widgets", request);
        }
        catch (VistaForbiddenException ex)
        {
            captured = ex;
        }

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.ViewName).IsEqualTo("Widgets");
        await Assert.That(captured.Facet).IsEqualTo(ViewFacet.List);
    }

    /// <summary>
    /// R7.2: with NO authorizer registered, access defaults to allow — the glue runs the executor and
    /// returns a non-null boxed <see cref="ViewListResult{TRow}"/> for the view's runtime row type.
    /// </summary>
    [Test]
    public async Task No_Authorizer_Defaults_To_Allow_And_Executes()
    {
        using var harness = WidgetTestHarness.Create();
        var (glue, http) = BuildGlue(harness, authorizer: null);

        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 0, PageSize: 10);

        var result = await glue.ListAsync(http, "Widgets", request);

        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsTypeOf<ViewListResult<WidgetRow>>();
        // Sanity: the default-allow path actually executed the seeded query end to end.
        var typed = (ViewListResult<WidgetRow>)result;
        await Assert.That(typed.TotalRowsUnfiltered).IsEqualTo((long)WidgetTestHarness.SeededRowCount);
    }

    /// <summary>
    /// R7.2 (positive guard): an authorizer that ALLOWS does not block execution — the glue still runs
    /// the executor and returns a result. Confirms the deny path above is the discriminating factor.
    /// </summary>
    [Test]
    public async Task Authorizer_Allow_Executes()
    {
        using var harness = WidgetTestHarness.Create();
        var (glue, http) = BuildGlue(harness, authorizer: new StubAuthorizer(allow: true));

        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 0, PageSize: 10);

        var result = await glue.ListAsync(http, "Widgets", request);

        await Assert.That(result).IsTypeOf<ViewListResult<WidgetRow>>();
    }

    /// <summary>
    /// R1.2 (D94): no authorizer in **Development** → access defaults to allow and the validator logs
    /// exactly one <see cref="LogLevel.Warning"/> describing the publicly-accessible posture.
    /// </summary>
    [Test]
    public async Task Startup_NoAuthorizer_Development_Warns()
    {
        var options = new VistaEndpointOptions(); // AuthorizerType null => HasAuthorizer false.
        var logger = new RecordingLogger<VistaStartupValidator>();
        var validator = new VistaStartupValidator(options, new TestHostEnvironment("Development"), logger);

        await validator.StartAsync(CancellationToken.None);

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToArray();
        await Assert.That(warnings.Length).IsEqualTo(1);
        await Assert.That(warnings[0].Message).Contains("publicly accessible");
        await Assert.That(warnings[0].Message).Contains("IViewAuthorizer");
    }

    /// <summary>
    /// R1.3 (D94): no authorizer in a **non-Development** environment without an explicit opt-in →
    /// startup **fails** with an actionable <see cref="InvalidOperationException"/>.
    /// </summary>
    [Test]
    public async Task Startup_NoAuthorizer_NonDevelopment_FailsClosed()
    {
        var options = new VistaEndpointOptions();
        var logger = new RecordingLogger<VistaStartupValidator>();
        var validator = new VistaStartupValidator(options, new TestHostEnvironment("Production"), logger);

        InvalidOperationException? caught = null;
        try
        {
            await validator.StartAsync(CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("AllowAnonymousAccess");
    }

    /// <summary>
    /// R1.4 (D94): no authorizer in a **non-Development** environment WITH an explicit
    /// <c>AllowAnonymousAccess()</c> opt-in → startup succeeds and logs a single warning recording the
    /// deliberate open posture.
    /// </summary>
    [Test]
    public async Task Startup_NoAuthorizer_NonDevelopment_ExplicitOptIn_Warns()
    {
        var services = new ServiceCollection();
        services.AddVistaEndpoints(b => b.AllowAnonymousAccess());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<VistaEndpointOptions>();
        await Assert.That(options.AllowAnonymous).IsTrue();

        var logger = new RecordingLogger<VistaStartupValidator>();
        var validator = new VistaStartupValidator(options, new TestHostEnvironment("Production"), logger);

        await validator.StartAsync(CancellationToken.None);

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToArray();
        await Assert.That(warnings.Length).IsEqualTo(1);
        await Assert.That(warnings[0].Message).Contains("explicit opt-in");
    }

    /// <summary>
    /// R1.1 (inverse): with an authorizer registered, the validator is a no-op in any environment.
    /// </summary>
    [Test]
    public async Task Startup_With_Authorizer_Logs_Nothing()
    {
        // Build options through the public configuration path so HasAuthorizer flips to true
        // (VistaEndpointOptions.AuthorizerType has an internal setter, set only via UseAuthorizer<T>).
        var services = new ServiceCollection();
        services.AddVistaEndpoints(b => b.UseAuthorizer<StubAuthorizer>());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<VistaEndpointOptions>();
        await Assert.That(options.HasAuthorizer).IsTrue();

        var logger = new RecordingLogger<VistaStartupValidator>();
        var validator = new VistaStartupValidator(options, new TestHostEnvironment("Production"), logger);

        await validator.StartAsync(CancellationToken.None);

        await Assert.That(logger.Entries.Count).IsEqualTo(0);
    }

    /// <summary>A minimal <see cref="IHostEnvironment"/> test double; only <see cref="EnvironmentName"/> matters here.</summary>
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "a2n.Vista.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Wires the one-door glue over the seeded harness: a <see cref="ViewRegistry"/> holding the Widgets
    /// view, the harness executor as <see cref="IViewExecutor"/>, and (optionally) the supplied
    /// authorizer — all reachable from a <see cref="DefaultHttpContext"/> with an anonymous user.
    /// </summary>
    private static (ViewRequestExecutor Glue, HttpContext Http) BuildGlue(
        WidgetTestHarness harness,
        IViewAuthorizer? authorizer)
    {
        var registry = new ViewRegistry();
        registry.Add(WidgetTestHarness.BuildView());

        var services = new ServiceCollection();
        services.AddSingleton<IViewRegistry>(registry);
        services.AddSingleton<IViewExecutor>(harness.Executor);
        if (authorizer is not null)
        {
            services.AddSingleton(authorizer);
        }

        var provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity()),
        };

        var glue = new ViewRequestExecutor(registry);
        return (glue, http);
    }
}

/// <summary>
/// Test double for <see cref="IViewAuthorizer"/> with a fixed allow/deny decision and a no-op
/// <see cref="ShapeQuery"/>. A parameterless constructor (default allow) lets it double as a registrable
/// type for the <c>UseAuthorizer&lt;T&gt;()</c> startup test.
/// </summary>
internal sealed class StubAuthorizer : IViewAuthorizer
{
    private readonly bool _allow;

    public StubAuthorizer()
        : this(allow: true)
    {
    }

    public StubAuthorizer(bool allow) => _allow = allow;

    public ValueTask<bool> IsAllowedAsync(ViewAuthContext context) => ValueTask.FromResult(_allow);

    public void ShapeQuery(ViewAuthContext context, IViewScope scope)
    {
        // No server-trusted filters needed for these tests.
    }
}

/// <summary>
/// Minimal in-memory <see cref="ILogger{TCategoryName}"/> that records every log entry's level and
/// formatted message, so the fail-open warning (R7.3) can be asserted without a logging framework.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
