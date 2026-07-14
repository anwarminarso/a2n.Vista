using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.Authoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
/// Feature: ag-grid-adapter (task 7.1) — end-to-end HTTP integration harness over an in-process
/// <see cref="TestServer"/> with a real SQLite-backed Gaya A view, mirroring the DataTables reference
/// harness (<see cref="HttpEndpointIntegrationTests"/>). Registers <c>AddVistaAdapter&lt;AgGridAdapter&gt;()</c>
/// and maps the view through the EXISTING AspNetCore glue (no host change), and confirms:
/// <list type="bullet">
///   <item><description>Exactly one <c>POST {route}/aggrid</c> endpoint is exposed on a mapped view (R6.1).</description></item>
///   <item><description>A valid AG Grid request flows Bind → ToQuery → <c>ListForAdapterAsync</c> (D94 auth +
///   ShapeQuery) → ToResponse → serialized JSON (camelCase <c>rowData</c>/<c>rowCount</c>) (R6.2, R6.4).</description></item>
///   <item><description>The <c>?q=</c> quick filter is folded into <c>AdapterRequest.Values</c> (Search channel)
///   and the JSON body is captured into <c>AdapterRequest.JsonBody</c> (paging/sort applied) (R6.4).</description></item>
///   <item><description>Layering is preserved: the AG Grid adapter package gains no ASP.NET dependency (R6.7).</description></item>
/// </list>
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration tests drive the reflection-based endpoint/executor path by design.")]
public sealed class AgGridEndpointIntegrationTests
{
    private const string Route = "/api/views/Widgets";
    private const string AgGridRoute = $"{Route}/aggrid";

    /// <summary>A minimal Gaya A template projecting <c>Widget</c> rows, keyed by <c>Id</c>.</summary>
    private sealed class WidgetViews : ViewTemplate<WidgetContext>
    {
        protected override void Configure(IViewTemplateBuilder<WidgetContext> views)
        {
            views.AddView("Widgets", (db, sp) =>
                    from w in db.Widgets
                    select new { w.Id, w.Name, w.Price })
                .Field(x => x.Id, f => f.PrimaryKey());
        }
    }

    /// <summary>
    /// R6.1: registering the adapter exposes exactly one <c>POST {route}/aggrid</c> endpoint on the mapped
    /// view — through the existing endpoint mapper, with no new registration API.
    /// </summary>
    [Test]
    public async Task Registering_AgGrid_Adapter_Exposes_Exactly_One_Post_AgGrid_Endpoint()
    {
        await using var app = await TestApp.StartAsync();

        var dataSource = app.Services.GetRequiredService<EndpointDataSource>();

        var aggridEndpoints = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => string.Equals(
                e.RoutePattern.RawText?.TrimStart('/'),
                AgGridRoute.TrimStart('/'),
                StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                .Contains(HttpMethods.Post) == true)
            .ToList();

        await Assert.That(aggridEndpoints.Count).IsEqualTo(1);
    }

    /// <summary>
    /// R6.2/R6.4: a valid AG Grid request (JSON body captured into <c>JsonBody</c>) flows end to end through
    /// the reused glue and returns the AG Grid <c>{ rowData, rowCount }</c> shape with camelCase names.
    /// </summary>
    [Test]
    public async Task Valid_AgGrid_Request_Round_Trips_Through_Reused_Glue()
    {
        await using var app = await TestApp.StartAsync();

        // Block paging [0,5) with a single ascending sort key. The JSON body must be captured into
        // AdapterRequest.JsonBody for BindRequest to succeed; a missing body would 400.
        var response = await app.Client.PostAsync(
            AgGridRoute,
            JsonContent("{\"startRow\":0,\"endRow\":5,\"sortModel\":[{\"colId\":\"Name\",\"sort\":\"asc\"}]}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // camelCase LoadSuccessParams shape (R5.4/R8.6): both rowData and rowCount are present.
        await Assert.That(root.TryGetProperty("rowData", out var rowData)).IsTrue();
        await Assert.That(root.TryGetProperty("rowCount", out var rowCount)).IsTrue();

        // rowCount = filtered total (25 seeded, no filter); rowData = the requested [0,5) block.
        await Assert.That(rowCount.GetInt64()).IsEqualTo(25L);
        await Assert.That(rowData.GetArrayLength()).IsEqualTo(5);
    }

    /// <summary>
    /// R6.4: the <c>?q=</c> quick filter is folded into <c>AdapterRequest.Values</c> by the existing
    /// <c>AdapterRequestFactory</c> and applied via the Search channel, WHILE the JSON body (paging) is
    /// captured into <c>JsonBody</c> — both channels observable in one round-trip, no host change.
    /// </summary>
    [Test]
    public async Task Quick_Filter_From_Query_String_And_Body_Both_Apply()
    {
        await using var app = await TestApp.StartAsync();

        // ?q=Widget 1 matches Name Contains "Widget 1": "Widget 1" plus "Widget 10".."Widget 19" = 11 rows.
        // A wide block [0,100) proves the JSON body paging was bound from JsonBody (else all 25 or a 400).
        var response = await app.Client.PostAsync(
            $"{AgGridRoute}?q=Widget%201",
            JsonContent("{\"startRow\":0,\"endRow\":100}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        await Assert.That(root.GetProperty("rowCount").GetInt64()).IsEqualTo(11L);
        await Assert.That(root.GetProperty("rowData").GetArrayLength()).IsEqualTo(11);
    }

    /// <summary>
    /// R6.7 (layering, adapter → ASP.NET direction): the <c>a2n.Vista.Adapters.AgGrid</c> assembly's direct
    /// references contain no <c>Microsoft.AspNetCore*</c> assembly and no <c>a2n.Vista.AspNetCore</c> — the
    /// adapter builds against Core only, so exposing it through the glue adds no ASP.NET dependency.
    /// </summary>
    [Test]
    public async Task AgGrid_Adapter_Package_Has_No_AspNetCore_Dependency()
    {
        var adapterAssembly = typeof(AgGridAdapter).Assembly;
        await Assert.That(adapterAssembly.GetName().Name).IsEqualTo("a2n.Vista.Adapters.AgGrid");

        var references = adapterAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)
            .ToList();

        var referencesAspNetCore = references.Any(name =>
            name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || string.Equals(name, "a2n.Vista.AspNetCore", StringComparison.Ordinal));

        await Assert.That(referencesAspNetCore).IsFalse();

        // Positive sanity: the adapter DOES reference Core, so the absence above is a real layering property.
        var referencesCore = references.Any(name =>
            string.Equals(name, "a2n.Vista.Core", StringComparison.Ordinal));
        await Assert.That(referencesCore).IsTrue();
    }

    private static StringContent JsonContent(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    /// <summary>A started in-process host + its test client, owning the in-memory SQLite connection.</summary>
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

        /// <summary>The host's root service provider (for endpoint enumeration).</summary>
        public IServiceProvider Services => _host.Services;

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
                        services.AddVista(v => v.RegisterTemplate<WidgetViews, WidgetContext>());
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
                        // The whole point of task 7.1: reuse the existing adapter registration, no new API.
                        services.AddVistaAdapter<AgGridAdapter>();
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
}
