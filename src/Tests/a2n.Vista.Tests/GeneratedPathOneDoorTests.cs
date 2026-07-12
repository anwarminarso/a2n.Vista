// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.Contracts;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Generator-driver example for Requirements R6.1 / R6.4 (Decision Log D123): the one-door pipeline
/// (authorize → <c>ShapeQuery</c> scope → executor) is preserved bit-for-bit when a source-generated
/// <see cref="IViewInvoker"/> is used, i.e. <see cref="ViewRequestExecutor"/> resolves the invoker
/// <em>after</em> <c>AuthorizeAndShapeAsync</c>. The reflection path (proven by
/// <see cref="HttpSurfaceR3Tests"/> and <see cref="AuthorizationTests"/>) is the behavioral oracle;
/// these tests mirror its ordering guarantees on the generated path by registering a stub invoker into
/// the process-wide <see cref="ViewInvokerStore"/> for a uniquely-named view (so the invoker branch is
/// taken) over the real SQLite-backed <see cref="WidgetTestExecutor"/> (so the executor's tri-whitelist
/// validation is genuine):
/// <list type="bullet">
/// <item>R6.1 — a denying authorizer turns List into 403 (<see cref="VistaForbiddenException"/>)
/// <em>before</em> the invoker/executor is ever reached (deny happens before dispatch).</item>
/// <item>R6.1 — the server-trusted <see cref="IViewScope"/> built by <c>ShapeQuery</c> is passed
/// unchanged into the generated invoker (honored on the generated path).</item>
/// <item>R6.4 — a disallowed client filter is rejected by the executor's tri-whitelist validation
/// before any SQL executes — identical to the reflection path (the generated dispatch does not alter
/// what is validated).</item>
/// </list>
/// Each test uses a unique view name so the first-wins, process-wide <see cref="ViewInvokerStore"/>
/// never collides across tests. The generated read invoker rides the executor's generic
/// <c>ListAsync&lt;TRow&gt;</c> facet, which is RUC in these reflection-backed tests, so IL2026 is
/// suppressed at the class level (trimming is not used for tests).
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The stub invoker forwards to the reflection-backed executor path by design; trimming is not used for tests.")]
public sealed class GeneratedPathOneDoorTests
{
    private static readonly IReadOnlyList<SortSpec> ById = new[] { new SortSpec(nameof(WidgetRow.Id)) };

    /// <summary>
    /// R6.1: with a generated invoker registered, a denying authorizer still turns List into
    /// <see cref="VistaForbiddenException"/> (HTTP 403) — and the invoker (hence the executor) is never
    /// reached, proving deny happens before dispatch on the generated path exactly as on the reflection
    /// path.
    /// </summary>
    [Test]
    public async Task Generated_Path_Deny_Forbids_Before_Invoker_Dispatch()
    {
        var viewName = UniqueName("deny");
        using var harness = WidgetTestHarness.Create();
        var invoker = new RecordingReadInvoker(harness.Executor);
        ViewInvokerStore.Register(viewName, invoker);

        var (glue, http) = BuildGlue(harness, viewName, new ShapingAuthorizer(allow: false));
        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 0, PageSize: 10);

        VistaForbiddenException? captured = null;
        try
        {
            await glue.ListAsync(http, viewName, request);
        }
        catch (VistaForbiddenException ex)
        {
            captured = ex;
        }

        // 403 (via the exception the RFC 7807 middleware maps) and the invoker was never dispatched.
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Facet).IsEqualTo(ViewFacet.List);
        await Assert.That(invoker.ListCallCount).IsEqualTo(0);
    }

    /// <summary>
    /// R6.1: the server-trusted scope produced by the authorizer's <c>ShapeQuery</c> is passed unchanged
    /// into the generated invoker. The invoker captures the scope handed to it; it carries exactly the
    /// one server-trusted row filter <c>ShapeQuery</c> added (honored on the generated path).
    /// </summary>
    [Test]
    public async Task Generated_Path_Honors_Server_Trusted_Scope()
    {
        var viewName = UniqueName("scope");
        using var harness = WidgetTestHarness.Create();
        var invoker = new RecordingReadInvoker(harness.Executor);
        ViewInvokerStore.Register(viewName, invoker);

        // The authorizer allows and shapes a server-trusted row filter over the SOURCE entity type.
        var (glue, http) = BuildGlue(harness, viewName, new ShapingAuthorizer(allow: true, shapeScope: true));
        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 0, PageSize: 10);

        _ = await glue.ListAsync(http, viewName, request);

        // The generated invoker was dispatched, and the scope it received carries the server-trusted
        // filter ShapeQuery added — the scope is not lost or re-derived on the generated path (R6.1/DR9).
        await Assert.That(invoker.ListCallCount).IsEqualTo(1);
        await Assert.That(invoker.CapturedScope).IsNotNull();
        await Assert.That(invoker.CapturedScope!.GetRowFilters<Widget>().Count).IsEqualTo(1);
    }

    /// <summary>
    /// R6.4: a disallowed client filter (an operator outside the field's whitelist) is rejected by the
    /// executor's tri-whitelist validation before any SQL executes — the generated dispatch does not
    /// alter what is validated, so the rejection is identical to the reflection path. The invoker is
    /// dispatched (deny is not the cause) and the executor throws the same
    /// <see cref="FilterValidationException"/> the reflection path surfaces (mapped to HTTP 400).
    /// </summary>
    [Test]
    public async Task Generated_Path_Rejects_Disallowed_Client_Filter_Before_Sql()
    {
        var viewName = UniqueName("filter");
        using var harness = WidgetTestHarness.Create();
        var invoker = new RecordingReadInvoker(harness.Executor);
        ViewInvokerStore.Register(viewName, invoker);

        var (glue, http) = BuildGlue(harness, viewName, new ShapingAuthorizer(allow: true));

        // "Id" is filterable but does NOT allow the Contains operator → an out-of-whitelist client
        // filter that the executor rejects during compile, before emitting any SQL.
        var request = new ViewQueryRequest(
            Filter: new FilterLeaf(nameof(WidgetRow.Id), FilterOperator.Contains, "1"),
            Sort: ById,
            Page: 0,
            PageSize: 10);

        FilterValidationException? captured = null;
        try
        {
            await glue.ListAsync(http, viewName, request);
        }
        catch (FilterValidationException ex)
        {
            captured = ex;
        }

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Code).IsEqualTo(FilterErrorCode.OperatorNotAllowed);
        // The invoker WAS dispatched (deny is not the cause); the executor rejected the filter — same as
        // the reflection path, which also validates the client filter inside the executor before SQL.
        await Assert.That(invoker.ListCallCount).IsEqualTo(1);
    }

    private static string UniqueName(string tag) => $"gen-onedoor-{tag}-{Guid.NewGuid():N}";

    /// <summary>
    /// Wires the one-door glue over the seeded harness: a <see cref="ViewRegistry"/> holding the
    /// (uniquely-named) WidgetRow view, the harness executor as <see cref="IViewExecutor"/>, and the
    /// supplied authorizer — all reachable from a <see cref="DefaultHttpContext"/> with an anonymous user.
    /// Mirrors <see cref="AuthorizationTests"/> so the generated path is driven through the same seams.
    /// </summary>
    private static (ViewRequestExecutor Glue, HttpContext Http) BuildGlue(
        WidgetTestHarness harness,
        string viewName,
        IViewAuthorizer authorizer)
    {
        var registry = new ViewRegistry();
        registry.Add(WidgetTestHarness.BuildView(viewName));

        var services = new ServiceCollection();
        services.AddSingleton<IViewRegistry>(registry);
        services.AddSingleton<IViewExecutor>(harness.Executor);
        services.AddSingleton(authorizer);

        var provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity()),
        };

        return (new ViewRequestExecutor(registry), http);
    }

    /// <summary>
    /// A one-door authorizer test double with a fixed allow/deny decision. When
    /// <see cref="_shapeScope"/> is set, <see cref="ShapeQuery"/> adds a single server-trusted row filter
    /// over the source entity type (<see cref="Widget"/>), so a test can assert the scope reaches the
    /// generated invoker unchanged.
    /// </summary>
    private sealed class ShapingAuthorizer : IViewAuthorizer
    {
        private readonly bool _allow;
        private readonly bool _shapeScope;

        public ShapingAuthorizer(bool allow, bool shapeScope = false)
        {
            _allow = allow;
            _shapeScope = shapeScope;
        }

        public ValueTask<bool> IsAllowedAsync(ViewAuthContext context) => ValueTask.FromResult(_allow);

        public void ShapeQuery(ViewAuthContext context, IViewScope scope)
        {
            if (_shapeScope)
            {
                // Server-trusted row filter (e.g. tenant/ownership) over the EF source entity — added
                // pre-projection and never whitelist-validated (DR9).
                scope.AddRowFilter<Widget>(w => w.Id <= 10);
            }
        }
    }

    /// <summary>
    /// A stub <see cref="IViewInvoker"/> standing in for the source-generated invoker: read-only, it
    /// records how many times List was dispatched and the scope it received, and forwards List exactly
    /// like a generated invoker would — closing <c>ListAsync&lt;WidgetRow&gt;</c> at compile time and
    /// awaiting it directly (so the executor's tri-whitelist validation is the real one). Write facets
    /// throw, matching a read-only view's generated invoker (R3.3).
    /// </summary>
    private sealed class RecordingReadInvoker : IViewInvoker
    {
        private readonly IViewExecutor _executor;

        public RecordingReadInvoker(IViewExecutor executor) => _executor = executor;

        public int ListCallCount { get; private set; }

        public IViewScope? CapturedScope { get; private set; }

        public bool IsWritable => false;

        [RequiresUnreferencedCode("Forwards to the reflection-backed executor test seam; mirrors a generated invoker on the AOT-clean compiled path.")]
        public async Task<ViewInvocationListResult> ListAsync(
            IViewExecutor executor,
            ViewMetadata view,
            ViewQueryRequest request,
            IViewScope scope,
            CancellationToken cancellationToken)
        {
            ListCallCount++;
            CapturedScope = scope;

            var result = await _executor
                .ListAsync<WidgetRow>(view, request, scope, cancellationToken)
                .ConfigureAwait(false);

            var rows = result.Page.Items.Cast<object?>().ToList();
            return new ViewInvocationListResult(result, rows, result.Page.TotalRows, result.TotalRowsUnfiltered);
        }

        public Task<object?> DetailAsync(
            IViewExecutor executor,
            ViewMetadata view,
            object key,
            IViewScope scope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This example exercises only the List facet.");

        public Task<object> CreateAsync(
            IViewExecutor executor,
            ViewMetadata view,
            object model,
            IViewScope scope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A read-only view invoker is not writable (R3.3).");

        public Task<bool> UpdateAsync(
            IViewExecutor executor,
            ViewMetadata view,
            object key,
            object model,
            IViewScope scope,
            string? concurrencyToken,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A read-only view invoker is not writable (R3.3).");
    }
}
