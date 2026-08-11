// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Regression tests for issue #4 — a server-trusted, DB-backed row scope had no supported seam:
/// <list type="bullet">
/// <item><b>D151</b> — <see cref="IViewAuthorizer.ShapeQueryAsync"/> is the awaited shaping door, so a
/// scope that needs I/O no longer forces <c>GetAwaiter().GetResult()</c> and receives the request
/// cancellation token. Its default implementation forwards to the synchronous
/// <see cref="IViewAuthorizer.ShapeQuery"/>, so an existing authorizer is untouched.</item>
/// <item><b>D152</b> — the Style A <c>AddView&lt;TSource, TRow&gt;(name, source, projection)</c> overload
/// keeps source and projection separate, so a central-template view is executed by
/// <see cref="SplitViewExecutionPlan{TSource, TRow}"/> and server-trusted predicates are AND-ed
/// pre-projection instead of failing closed (D141).</item>
/// </list>
/// The two are only useful together for Style A, which is why they are tested together: without the
/// split, any scope the async hook adds makes <c>IViewScope.RowFilterCount &gt; 0</c> and the combined
/// plan refuses to execute.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "These tests drive the reflection-based Style A authoring/execution path by design; trimming is not used for tests.")]
public sealed class AsyncScopeAndStyleASplitTests
{
    // ---- D151: the async shaping door ---------------------------------------------------------------

    /// <summary>
    /// D151: an authorizer written before the async hook existed implements only <c>ShapeQuery</c>. The
    /// default <c>ShapeQueryAsync</c> forwards to it, so its scope still reaches the pipeline unchanged.
    /// </summary>
    [Test]
    public async Task Default_ShapeQueryAsync_Forwards_To_The_Synchronous_ShapeQuery()
    {
        IViewAuthorizer authorizer = new SyncOnlyAuthorizer();
        var scope = new ViewScope();

        await authorizer.ShapeQueryAsync(Context(), scope, CancellationToken.None);

        await Assert.That(scope.RowFilterCount).IsEqualTo(1);
        await Assert.That(scope.GetRowFilters<Widget>().Count).IsEqualTo(1);
    }

    /// <summary>
    /// D151: the pipeline calls <c>ShapeQueryAsync</c> and hands it the request cancellation token, so a
    /// client abort actually cancels the scope query. It also proves the async override — not the
    /// synchronous member — is what the pipeline invokes.
    /// </summary>
    [Test]
    public async Task Pipeline_Awaits_ShapeQueryAsync_With_The_Request_Token()
    {
        using var harness = WidgetTestHarness.Create();
        var viewName = "async-shape-token";
        var authorizer = new AsyncScopeAuthorizer(maxId: 10);

        using var cts = new CancellationTokenSource();
        var (glue, http) = BuildGlue(harness, viewName, authorizer);
        http.RequestAborted = cts.Token;

        _ = await glue.ListAsync(
            http,
            viewName,
            new ViewQueryRequest(Filter: null, Sort: [new SortSpec(nameof(WidgetRow.Id))], Page: 0, PageSize: 5));

        await Assert.That(authorizer.AsyncCallCount).IsEqualTo(1);
        await Assert.That(authorizer.SyncCallCount).IsEqualTo(0);
        await Assert.That(authorizer.ObservedToken).IsEqualTo(cts.Token);
    }

    // ---- D152: the Style A source/projection split --------------------------------------------------

    /// <summary>
    /// D152: a view registered through <c>AddView&lt;TSource, TRow&gt;</c> is executed by the
    /// §4.1-aligned split plan (not the type-erased combined one), and both server-trusted predicate
    /// sources — the authored <c>WithRowFilter&lt;TSource&gt;</c> and the per-request scope — are applied
    /// pre-projection instead of failing closed.
    /// </summary>
    [Test]
    public async Task Split_AddView_Applies_Authored_And_Scoped_Row_Filters_Pre_Projection()
    {
        var definition = new SplitWidgetTemplate().BuildViews().Single();
        await Assert.That(definition.SourceProjection).IsNotNull();
        await Assert.That(definition.SourceProjection!.SourceType).IsEqualTo(typeof(Widget));
        await Assert.That(definition.SourceProjection.RowType).IsEqualTo(typeof(WidgetRow));

        var plan = ViewExecutionPlan.FromTemplateDefinition(definition);
        await Assert.That(plan).IsTypeOf<SplitViewExecutionPlan<Widget, WidgetRow>>();

        using var db = WidgetDatabase.Create();
        using var services = new ServiceCollection().BuildServiceProvider();

        // The template declares WithRowFilter<Widget>(w => w.Id <= 20), so an empty scope already narrows
        // the source from 25 rows to 20 — proof the authored filter is honored rather than refused.
        var authoredOnly = (IQueryable<WidgetRow>)plan.CreateScopedQueryable(db.Context, services, new ViewScope());
        await Assert.That(authoredOnly.Count()).IsEqualTo(20);

        // A per-request server-trusted filter (what ShapeQueryAsync adds) is AND-ed on top, pre-projection.
        var scope = new ViewScope();
        scope.AddRowFilter<Widget>(w => w.Id > 15);
        var scoped = (IQueryable<WidgetRow>)plan.CreateScopedQueryable(db.Context, services, scope);
        await Assert.That(scoped.Count()).IsEqualTo(5);
        await Assert.That(scoped.OrderBy(r => r.Id).First().Id).IsEqualTo(16);
    }

    /// <summary>
    /// D152: the combined single-delegate overload is unchanged — it still produces the type-erased plan
    /// that fails closed on a populated scope (D141). The split overload is opt-in, not a silent
    /// behavioral change to existing views.
    /// </summary>
    [Test]
    public async Task Combined_AddView_Still_Fails_Closed_On_A_Populated_Scope()
    {
        var definition = new CombinedWidgetTemplate().BuildViews().Single();
        await Assert.That(definition.SourceProjection).IsNull();

        var plan = ViewExecutionPlan.FromTemplateDefinition(definition);
        await Assert.That(plan).IsTypeOf<ProjectedViewExecutionPlan>();

        using var db = WidgetDatabase.Create();
        using var services = new ServiceCollection().BuildServiceProvider();

        var scope = new ViewScope();
        scope.AddRowFilter<Widget>(w => w.Id > 15);

        await Assert.That(() => plan.CreateScopedQueryable(db.Context, services, scope))
            .Throws<NotSupportedException>();
    }

    /// <summary>
    /// D152: a row filter declared over an entity other than the view's source cannot be AND-ed
    /// pre-projection, so it is rejected at authoring/registration time rather than becoming a per-request
    /// cast failure.
    /// </summary>
    [Test]
    public async Task Split_AddView_Rejects_A_Row_Filter_Over_A_Different_Entity()
    {
        await Assert.That(() => new MismatchedFilterTemplate().BuildViews())
            .Throws<InvalidOperationException>();
    }

    // ---- The two together, end to end over HTTP -----------------------------------------------------

    /// <summary>
    /// The whole point of the issue: a Style A view whose row scope is <em>loaded</em> per request. The
    /// authorizer awaits its resolver in <c>ShapeQueryAsync</c> (no sync-over-async), the split plan
    /// applies the resulting predicate pre-projection, and the unfiltered total counts only in-scope rows —
    /// so the scope cannot be probed through paging metadata either.
    /// </summary>
    [Test]
    public async Task Async_Loaded_Scope_Narrows_A_Style_A_View_End_To_End()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync(
            "/api/views/SplitWidgets/list",
            new StringContent(
                "{\"page\":0,\"pageSize\":50,\"sort\":[{\"field\":\"Id\",\"desc\":false}]}",
                Encoding.UTF8,
                "application/json"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // AsyncScopeAuthorizer awaits a resolver that yields "ids 1..10", so 10 of the 25 seeded rows are
        // visible — and the unfiltered total reflects the scope because it is applied pre-projection.
        var items = root.GetProperty("page").GetProperty("items");
        await Assert.That(items.GetArrayLength()).IsEqualTo(10);
        await Assert.That(root.GetProperty("totalRowsUnfiltered").GetInt64()).IsEqualTo(10L);
        await Assert.That(items[0].GetProperty("id").GetInt32()).IsEqualTo(1);
        await Assert.That(items[9].GetProperty("id").GetInt32()).IsEqualTo(10);
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    private static ViewAuthContext Context() =>
        new(
            new ClaimsPrincipal(new ClaimsIdentity()),
            "any-view",
            ViewFacet.List,
            new DefaultHttpContext(),
            new ServiceCollection().BuildServiceProvider());

    /// <summary>
    /// Wires the one-door glue over the seeded harness (the <see cref="GeneratedPathOneDoorTests"/>
    /// pattern): registry + executor + authorizer, reachable from a <see cref="DefaultHttpContext"/>.
    /// </summary>
    private static (ViewRequestExecutor Glue, DefaultHttpContext Http) BuildGlue(
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

        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity()),
        };

        return (new ViewRequestExecutor(registry), http);
    }

    // ---- Templates ----------------------------------------------------------------------------------

    /// <summary>The §4.1-aligned Style A form: source and projection registered separately (D152).</summary>
    private sealed class SplitWidgetTemplate : ViewTemplate<WidgetContext>
    {
        protected override void Configure(IViewTemplateBuilder<WidgetContext> views)
        {
            views.AddView(
                    "SplitWidgets",
                    static (WidgetContext db, IServiceProvider sp) => db.Widgets,
                    static w => new WidgetRow { Id = w.Id, Name = w.Name, Price = w.Price })
                .WithRowFilter<Widget>(static sp => w => w.Id <= 20)
                .Key(nameof(WidgetRow.Id));
        }
    }

    /// <summary>The unchanged combined form: one delegate that already projected (fails closed on scope).</summary>
    private sealed class CombinedWidgetTemplate : ViewTemplate<WidgetContext>
    {
        protected override void Configure(IViewTemplateBuilder<WidgetContext> views)
        {
            views.AddView(
                    "CombinedWidgets",
                    static (WidgetContext db, IServiceProvider sp) =>
                        db.Widgets.Select(w => new WidgetRow { Id = w.Id, Name = w.Name, Price = w.Price }))
                .Key(nameof(WidgetRow.Id));
        }
    }

    /// <summary>A split view whose authored row filter is expressed over the wrong entity.</summary>
    private sealed class MismatchedFilterTemplate : ViewTemplate<WidgetContext>
    {
        protected override void Configure(IViewTemplateBuilder<WidgetContext> views)
        {
            views.AddView(
                    "MismatchedWidgets",
                    static (WidgetContext db, IServiceProvider sp) => db.Widgets,
                    static w => new WidgetRow { Id = w.Id, Name = w.Name, Price = w.Price })
                .WithRowFilter<WidgetRow>(static sp => r => r.Id <= 20)
                .Key(nameof(WidgetRow.Id));
        }
    }

    /// <summary>The end-to-end template: split, unfiltered at authoring time, scoped only per request.</summary>
    private sealed class HostedSplitWidgetTemplate : ViewTemplate<WidgetContext>
    {
        protected override void Configure(IViewTemplateBuilder<WidgetContext> views)
        {
            views.AddView(
                    "SplitWidgets",
                    static (WidgetContext db, IServiceProvider sp) => db.Widgets,
                    static w => new WidgetRow { Id = w.Id, Name = w.Name, Price = w.Price })
                .Key(nameof(WidgetRow.Id));
        }
    }

    // ---- Authorizers --------------------------------------------------------------------------------

    /// <summary>An authorizer predating D151: it implements only the synchronous shaping member.</summary>
    private sealed class SyncOnlyAuthorizer : IViewAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(ViewAuthContext context) => ValueTask.FromResult(true);

        public void ShapeQuery(ViewAuthContext context, IViewScope scope)
            => scope.AddRowFilter<Widget>(w => w.Id <= 10);
    }

    /// <summary>
    /// The shape issue #4 asked for: the accessible-id set is <em>loaded</em>, so shaping is awaited and
    /// honors the request token. The synchronous member is never expected to run and records it if it does.
    /// </summary>
    private sealed class AsyncScopeAuthorizer : IViewAuthorizer
    {
        private readonly int _maxId;

        public AsyncScopeAuthorizer(int maxId) => _maxId = maxId;

        public int AsyncCallCount { get; private set; }

        public int SyncCallCount { get; private set; }

        public CancellationToken ObservedToken { get; private set; }

        public ValueTask<bool> IsAllowedAsync(ViewAuthContext context) => ValueTask.FromResult(true);

        public void ShapeQuery(ViewAuthContext context, IViewScope scope) => SyncCallCount++;

        public async ValueTask ShapeQueryAsync(
            ViewAuthContext context,
            IViewScope scope,
            CancellationToken cancellationToken)
        {
            AsyncCallCount++;
            ObservedToken = cancellationToken;

            // Stands in for the grants-table read: genuinely asynchronous, and cancellable.
            var maxId = await ResolveMaxIdAsync(cancellationToken).ConfigureAwait(false);
            scope.AddRowFilter<Widget>(w => w.Id <= maxId);
        }

        private async Task<int> ResolveMaxIdAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return _maxId;
        }
    }

    // ---- Fixtures -----------------------------------------------------------------------------------

    /// <summary>A seeded in-memory SQLite <see cref="WidgetContext"/>, owning its connection.</summary>
    private sealed class WidgetDatabase : IDisposable
    {
        private readonly SqliteConnection _connection;

        private WidgetDatabase(SqliteConnection connection, WidgetContext context)
        {
            _connection = connection;
            Context = context;
        }

        public WidgetContext Context { get; }

        public static WidgetDatabase Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var context = new WidgetContext(
                new DbContextOptionsBuilder<WidgetContext>().UseSqlite(connection).Options);
            context.Database.EnsureCreated();
            context.Widgets.AddRange(Enumerable.Range(1, 25)
                .Select(i => new Widget { Id = i, Name = $"Widget {i}", Price = i * 10m }));
            context.SaveChanges();

            return new WidgetDatabase(connection, context);
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }

    /// <summary>An in-process host serving the split Style A view behind the async-shaping authorizer.</summary>
    private sealed class TestApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private TestApp(IHost host, SqliteConnection connection, HttpClient client)
        {
            _host = host;
            _connection = connection;
            Client = client;
        }

        public HttpClient Client { get; }

        [RequiresUnreferencedCode("Vista endpoint mapping uses the reflection bridge by design.")]
        public static async Task<TestApp> StartAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<WidgetContext>(o => o.UseSqlite(connection));
                        services.AddVista(v => v.RegisterTemplate<HostedSplitWidgetTemplate, WidgetContext>());

                        // A real authorizer (not AllowAnonymousAccess), so the D94 posture is the natural
                        // authorizer-present one and the shaping hook is exercised through the host pipeline.
                        services.AddVistaEndpoints(e => e.UseAuthorizer<HostAsyncScopeAuthorizer>());
                    })
                    .Configure(app =>
                    {
                        app.UseVistaExceptionHandling();
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapVistaViews());
                    }))
                .StartAsync();

            using (var scope = host.Services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<WidgetContext>();
                ctx.Database.EnsureCreated();
                ctx.Widgets.AddRange(Enumerable.Range(1, 25)
                    .Select(i => new Widget { Id = i, Name = $"Widget {i}", Price = i * 10m }));
                ctx.SaveChanges();
            }

            return new TestApp(host, connection, host.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            _connection.Dispose();
        }
    }

    /// <summary>The hosted authorizer: allows every facet, loads its row scope asynchronously.</summary>
    private sealed class HostAsyncScopeAuthorizer : IViewAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(ViewAuthContext context) => ValueTask.FromResult(true);

        public void ShapeQuery(ViewAuthContext context, IViewScope scope)
            => throw new InvalidOperationException(
                "The pipeline must call ShapeQueryAsync when it is overridden (D151).");

        public async ValueTask ShapeQueryAsync(
            ViewAuthContext context,
            IViewScope scope,
            CancellationToken cancellationToken)
        {
            var accessibleIds = await LoadAccessibleIdsAsync(cancellationToken).ConfigureAwait(false);
            scope.AddRowFilter<Widget>(w => accessibleIds.Contains(w.Id));
        }

        private static async Task<List<int>> LoadAccessibleIdsAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return [.. Enumerable.Range(1, 10)];
        }
    }
}
