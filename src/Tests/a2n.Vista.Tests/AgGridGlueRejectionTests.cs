// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Adapters;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter (task 7.2) — glue integration tests over the REUSED AspNetCore path, mirroring
/// the DataTables reference adapter's R6.4 rejection parity. Each case drives an in-process
/// <see cref="TestServer"/> hosting a real SQLite-backed Gaya A view, with a <see cref="DbCommandInterceptor"/>
/// SQL spy attached so the "engine did NOT execute" guarantee is observable. The tests confirm:
/// <list type="bullet">
///   <item><description>A malformed JSON body → HTTP 400 <c>adapter-bind-failed</c> with no rows (R6.6).</description></item>
///   <item><description>An engine validation error (a non-positive <c>PageSize</c> from a zero-width block)
///   → the existing RFC 7807 Problem Details 400 with no rows, before any query executes (R6.5).</description></item>
///   <item><description>A disallowed structured leaf (<c>filterModel</c> targeting a non-filterable field)
///   → 400 under the <b>Filter</b> channel, no <c>rowData</c>, engine not executed (R8.4).</description></item>
///   <item><description>A quick-filter term targeting a non-searchable field → 400 under the <b>Search</b>
///   channel, no <c>rowData</c>, engine not executed (R8.3).</description></item>
/// </list>
/// <para>
/// The real <see cref="AgGridAdapter"/> (like the DataTables adapter) pre-filters the quick filter to
/// <c>IsSearchable &amp;&amp; string</c> fields, so it never itself emits a Search leaf the engine would
/// reject. R8.3's guarantee is a property of the reused glue + engine, exercised end to end here by a thin
/// adapter double (<see cref="SearchProbeAgGridAdapter"/>) that routes the quick-filter term to the Search
/// channel on a non-searchable target — the same construction the DataTables R6.4 parity uses when it
/// builds the disallowed Search leaf directly. The <c>filterModel</c> path (R8.4) needs no double: the AG
/// Grid filter parser does not pre-filter, so the real adapter forwards the disallowed leaf verbatim.
/// </para>
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration tests drive the reflection-based endpoint/executor path by design.")]
public sealed class AgGridGlueRejectionTests
{
    private const string Route = "/api/views/Widgets";
    private const string AgGridRoute = $"{Route}/aggrid";
    private const string SearchProbeRoute = $"{Route}/aggrid-search-probe";

    /// <summary>The projected, non-searchable/non-filterable field used as the disallowed target.</summary>
    private const string DisallowedField = "Price";

    // ================================================================================================
    // R6.6 — malformed body → 400 adapter-bind-failed, no rows.
    // ================================================================================================

    /// <summary>
    /// A syntactically invalid JSON body fails in <c>BindRequest</c> before <c>ToQuery</c>/the executor, so
    /// the endpoint returns 400 <c>adapter-bind-failed</c> with no AG Grid <c>rowData</c> and no query runs.
    /// </summary>
    [Test]
    public async Task Malformed_Body_Is_400_AdapterBindFailed_With_No_Rows()
    {
        await using var app = await TestApp.StartAsync();
        app.Spy.Clear();

        var response = await app.Client.PostAsync(AgGridRoute, JsonContent("{ this is not valid json"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        using var doc = await ReadJsonAsync(response);
        await Assert.That(GetCode(doc)).IsEqualTo("adapter-bind-failed");
        await Assert.That(HasRowData(doc)).IsFalse();

        // A bind failure never reaches the engine.
        await Assert.That(app.Spy.ExecutedCommands.Count).IsEqualTo(0);
    }

    // ================================================================================================
    // R6.5 — engine validation (non-positive PageSize) → RFC 7807 400, no rows, engine not executed.
    // ================================================================================================

    /// <summary>
    /// A zero-width block (<c>startRow == endRow</c>) binds cleanly (<c>EndRow &gt;= StartRow</c>) but yields
    /// a non-positive <c>PageSize</c>, which the engine rejects up front (<c>invalid-page-size</c>) before any
    /// DB round-trip — the existing RFC 7807 Problem Details 400, with no rows.
    /// </summary>
    [Test]
    public async Task NonPositive_PageSize_Is_RFC7807_400_With_No_Rows_And_No_Query()
    {
        await using var app = await TestApp.StartAsync();
        app.Spy.Clear();

        // startRow == endRow → PageSize = 0 (passed through unchanged by ToQuery), rejected by the engine.
        var response = await app.Client.PostAsync(
            AgGridRoute,
            JsonContent("{\"startRow\":10,\"endRow\":10}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        using var doc = await ReadJsonAsync(response);
        await Assert.That(GetCode(doc)).IsEqualTo("invalid-page-size");
        await Assert.That(HasRowData(doc)).IsFalse();

        // Paging is validated before any query is emitted (R10.3): the spy stays empty.
        await Assert.That(app.Spy.ExecutedCommands.Count).IsEqualTo(0);
    }

    // ================================================================================================
    // R8.4 — disallowed structured leaf → 400 under the Filter channel, no rowData, engine not executed.
    // ================================================================================================

    /// <summary>
    /// A <c>filterModel</c> entry targeting a non-filterable projected field (<c>Price</c>) is forwarded
    /// verbatim by the AG Grid filter parser (which never pre-filters), so the engine rejects it under the
    /// <b>Filter</b> channel with <c>filter-field-not-allowed</c> — no <c>rowData</c>, no query executed.
    /// </summary>
    [Test]
    public async Task Disallowed_Structured_Leaf_Is_400_Under_Filter_Channel_With_No_Rows_And_No_Query()
    {
        await using var app = await TestApp.StartAsync();
        app.Spy.Clear();

        // filterModel targets the non-filterable 'Price' field → a Filter-channel leaf the engine refuses.
        var response = await app.Client.PostAsync(
            AgGridRoute,
            JsonContent(
                "{\"startRow\":0,\"endRow\":10,\"filterModel\":{" +
                "\"Price\":{\"filterType\":\"number\",\"type\":\"equals\",\"filter\":10}}}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        using var doc = await ReadJsonAsync(response);
        await Assert.That(GetCode(doc)).IsEqualTo("filter-field-not-allowed");
        // The Filter-channel rejection message proves the origin (a non-filterable field, not search).
        await Assert.That(GetDetail(doc)).Contains("is not filterable");
        await Assert.That(HasRowData(doc)).IsFalse();

        // The reflection List path emits the unfiltered baseline COUNT before validating the client
        // channels, so at most that single COUNT round-trip may appear; the FILTERED/paged data query —
        // the one that returns rowData — never executes for a rejected request (a successful List would
        // additionally emit the filtered COUNT and the paged data fetch).
        await Assert.That(RanOnlyBaselineCount(app.Spy)).IsTrue();
    }

    // ================================================================================================
    // R8.3 — quick filter on a non-searchable target → 400 under the Search channel, no rowData, no query.
    // ================================================================================================

    /// <summary>
    /// A quick-filter term (<c>?q=</c>) routed to the <b>Search</b> channel against a non-searchable target
    /// (<c>Price</c>) is rejected by the engine with <c>filter-field-not-allowed</c> — no <c>rowData</c>, no
    /// query executed. Driven through the reused glue via <see cref="SearchProbeAgGridAdapter"/> because the
    /// real adapter pre-filters the quick filter to searchable string fields (see the class remarks).
    /// </summary>
    [Test]
    public async Task QuickFilter_On_NonSearchable_Target_Is_400_Under_Search_Channel_With_No_Rows_And_No_Query()
    {
        await using var app = await TestApp.StartAsync();
        app.Spy.Clear();

        // A positive block so paging is valid; the rejection is the Search-channel target, not the page size.
        var response = await app.Client.PostAsync(
            $"{SearchProbeRoute}?q=gadget",
            JsonContent("{\"startRow\":0,\"endRow\":10}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        using var doc = await ReadJsonAsync(response);
        await Assert.That(GetCode(doc)).IsEqualTo("filter-field-not-allowed");
        // The Search-channel rejection message proves the origin (global search, not a filterable column).
        await Assert.That(GetDetail(doc)).Contains("does not participate in global search");
        await Assert.That(HasRowData(doc)).IsFalse();

        // As with the Filter channel, at most the unfiltered baseline COUNT may appear; the FILTERED/paged
        // data query that would return rowData never executes for the rejected Search-channel request.
        await Assert.That(RanOnlyBaselineCount(app.Spy)).IsTrue();
    }

    // ================================================================================================
    // Helpers
    // ================================================================================================

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    /// <summary>Reads the RFC 7807 <c>code</c> extension (present on every Vista Problem Details body).</summary>
    private static string? GetCode(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;

    /// <summary>Reads the Problem Details <c>detail</c> (used to prove which channel rejected the request).</summary>
    private static string GetDetail(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() ?? string.Empty : string.Empty;

    /// <summary>True when the response carries an AG Grid <c>rowData</c> payload (never on an error).</summary>
    private static bool HasRowData(JsonDocument doc) => doc.RootElement.TryGetProperty("rowData", out _);

    /// <summary>
    /// True when the spy recorded at most the single unfiltered baseline <c>COUNT</c> the reflection List
    /// path runs before it validates the client channels — i.e. no row-returning (filtered/paged) query
    /// executed. A rejected request must never reach the filtered COUNT or the paged data fetch, so no rows
    /// are ever returned.
    /// </summary>
    private static bool RanOnlyBaselineCount(SqlSpyInterceptor spy) =>
        spy.ExecutedCommands.Count <= 1
        && spy.ExecutedCommands.All(c => c.Contains("COUNT", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A minimal Gaya A template projecting <c>Widget</c> rows. <c>Price</c> is made non-filterable so it is
    /// a disallowed <b>Filter</b> target (R8.4); it is a non-string field so it is also a non-searchable
    /// <b>Search</b> target (R8.3). <c>Name</c> stays searchable/filterable.
    /// </summary>
    private sealed class WidgetViews : ViewTemplate<WidgetContext>
    {
        protected override void Configure(IViewTemplateBuilder<WidgetContext> views)
        {
            views.AddView("Widgets", (db, sp) =>
                    from w in db.Widgets
                    select new { w.Id, w.Name, w.Price })
                .Field(x => x.Id, f => f.PrimaryKey())
                .Field(x => x.Price, f => f.Filterable(false));
        }
    }

    /// <summary>
    /// A thin AG Grid adapter double that reuses the real <see cref="AgGridAdapter"/> bind step but routes
    /// the quick-filter term to the <b>Search</b> channel on the non-searchable <see cref="DisallowedField"/>.
    /// This exercises the reused glue's per-channel Search enforcement (R8.3) end to end — the real adapter
    /// pre-filters the quick filter to searchable string fields, so it cannot itself produce such a leaf.
    /// </summary>
    private sealed class SearchProbeAgGridAdapter : ViewAdapter<AgGridRowsRequest, AgGridRowsResponse>
    {
        private static readonly AgGridAdapter Inner = new();

        public override string Id => "aggrid-search-probe";

        public override string? RouteSuffix => "aggrid-search-probe";

        public override AgGridRowsRequest BindRequest(AdapterRequest raw) => Inner.BindRequest(raw);

        public override ViewQueryRequest ToQuery(AgGridRowsRequest request, ViewMetadata view)
        {
            var pageSize = request.EndRow - request.StartRow;
            var page = pageSize > 0 ? request.StartRow / pageSize : 0;

            return new ViewQueryRequest(
                Filter: null,
                Sort: Array.Empty<SortSpec>(),
                Page: page,
                PageSize: pageSize,
                SelectFields: null,
                Search: new FilterLeaf(DisallowedField, FilterOperator.Contains, request.QuickFilter),
                Scope: null);
        }

        public override AgGridRowsResponse ToResponse(AdapterListResult result, AgGridRowsRequest request, ViewMetadata view) =>
            new() { RowData = result.Rows, RowCount = result.RecordsFiltered };
    }

    /// <summary>
    /// Records the command text of every command the provider executes. The rejection tests assert this list
    /// is empty, proving the engine never queried the data source for a refused request.
    /// </summary>
    private sealed class SqlSpyInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _executed = new();

        public IReadOnlyList<string> ExecutedCommands => _executed;

        public void Clear() => _executed.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _executed.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _executed.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            _executed.Add(command.CommandText);
            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            _executed.Add(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            _executed.Add(command.CommandText);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _executed.Add(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>A started in-process host + its test client, owning the in-memory SQLite connection and spy.</summary>
    private sealed class TestApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private TestApp(IHost host, SqliteConnection connection, HttpClient client, SqlSpyInterceptor spy)
        {
            _host = host;
            _connection = connection;
            Client = client;
            Spy = spy;
        }

        public HttpClient Client { get; }

        /// <summary>The SQL spy attached to the view's context; asserted empty after a rejected request.</summary>
        public SqlSpyInterceptor Spy { get; }

        [RequiresUnreferencedCode("Vista endpoint mapping uses the reflection bridge by design.")]
        public static async Task<TestApp> StartAsync()
        {
            var spy = new SqlSpyInterceptor();

            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<WidgetContext>(o => o.UseSqlite(connection).AddInterceptors(spy));
                        services.AddVista(v => v.RegisterTemplate<WidgetViews, WidgetContext>());
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
                        // Reused glue: the real adapter plus a thin double for the Search-channel probe (R8.3).
                        services.AddVistaAdapter<AgGridAdapter>();
                        services.AddVistaAdapter<SearchProbeAgGridAdapter>();
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

            return new TestApp(host, connection, host.GetTestClient(), spy);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            _connection.Dispose();
        }
    }
}
