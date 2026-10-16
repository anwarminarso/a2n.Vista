using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using a2n.Vista.Adapters.TanStackTable;
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
/// End-to-end HTTP harness for <c>POST {route}/tanstack</c> over the existing AspNetCore adapter glue
/// (no host change). A SQLite-backed view proves paging, sorting, the global-filter search channel, and
/// a column filter all survive Bind → ToQuery → List → ToResponse.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration tests drive the reflection-based endpoint/executor path by design.")]
public sealed class TanStackEndpointIntegrationTests
{
    private const string Route = "/api/views/Widgets";
    private const string TanStackRoute = $"{Route}/tanstack";

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

    [Test]
    public async Task Registering_TanStack_Adapter_Exposes_Exactly_One_Post_Endpoint()
    {
        await using var app = await TestApp.StartAsync();

        var dataSource = app.Services.GetRequiredService<EndpointDataSource>();
        var endpoints = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => string.Equals(
                e.RoutePattern.RawText?.TrimStart('/'),
                TanStackRoute.TrimStart('/'),
                StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true)
            .ToList();

        await Assert.That(endpoints.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Valid_Request_Round_Trips_CamelCase_Page()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync(
            TanStackRoute,
            Json("""{"pagination":{"pageIndex":0,"pageSize":5},"sorting":[{"id":"Name","desc":true}]}"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var rows = root.GetProperty("rows");

        await Assert.That(root.GetProperty("rowCount").GetInt64()).IsEqualTo(25);
        await Assert.That(root.GetProperty("pageCount").GetInt64()).IsEqualTo(5);
        await Assert.That(rows.GetArrayLength()).IsEqualTo(5);
        await Assert.That(rows[0].GetProperty("name").GetString()).IsEqualTo("Widget 9");
        await Assert.That(rows[1].GetProperty("name").GetString()).IsEqualTo("Widget 8");
        await Assert.That(rows[2].GetProperty("name").GetString()).IsEqualTo("Widget 7");
    }

    [Test]
    public async Task Second_Page_Uses_The_Absolute_Offset()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync(
            TanStackRoute,
            Json("""{"pagination":{"pageIndex":1,"pageSize":5},"sorting":[{"id":"Name","desc":true}]}"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = doc.RootElement.GetProperty("rows");

        await Assert.That(doc.RootElement.GetProperty("rowCount").GetInt64()).IsEqualTo(25);
        await Assert.That(rows.GetArrayLength()).IsEqualTo(5);
        await Assert.That(rows[0].GetProperty("name").GetString()).IsEqualTo("Widget 4");
    }

    [Test]
    public async Task Global_Filter_And_Column_Filter_Narrow_The_Page()
    {
        await using var app = await TestApp.StartAsync();

        var search = await app.Client.PostAsync(
            TanStackRoute,
            Json("""{"pagination":{"pageIndex":0,"pageSize":100},"globalFilter":"Widget 1"}"""));
        using var searchDoc = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        await Assert.That(search.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(searchDoc.RootElement.GetProperty("rowCount").GetInt64()).IsEqualTo(11);

        var price = await app.Client.PostAsync(
            TanStackRoute,
            Json("""{"pagination":{"pageIndex":0,"pageSize":100},"columnFilters":[{"id":"Price","value":{"op":"gte","value":100}}]}"""));
        using var priceDoc = JsonDocument.Parse(await price.Content.ReadAsStringAsync());
        await Assert.That(price.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(priceDoc.RootElement.GetProperty("rowCount").GetInt64()).IsEqualTo(16);
    }

    [Test]
    public async Task Unknown_Sort_Id_Is_Rejected_Like_An_Unknown_Filter()
    {
        await using var app = await TestApp.StartAsync();

        var sort = await app.Client.PostAsync(
            TanStackRoute,
            Json("""{"sorting":[{"id":"name","desc":false}]}"""));
        var filter = await app.Client.PostAsync(
            TanStackRoute,
            Json("""{"columnFilters":[{"id":"name","value":"Widget 1"}]}"""));

        await Assert.That(sort.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(filter.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        using var sortDoc = JsonDocument.Parse(await sort.Content.ReadAsStringAsync());
        using var filterDoc = JsonDocument.Parse(await filter.Content.ReadAsStringAsync());
        await Assert.That(sortDoc.RootElement.GetProperty("code").GetString()).IsEqualTo("filter-unknown-field");
        await Assert.That(sortDoc.RootElement.GetProperty("field").GetString()).IsEqualTo("name");
        await Assert.That(filterDoc.RootElement.GetProperty("code").GetString()).IsEqualTo("filter-unknown-field");
        await Assert.That(filterDoc.RootElement.GetProperty("field").GetString()).IsEqualTo("name");
    }

    [Test]
    public async Task Malformed_Filter_Is_Adapter_Bind_Failed()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync(
            TanStackRoute,
            Json("""{"columnFilters":[{"id":"Name","value":{"op":"regex","value":"a"}}]}"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(doc.RootElement.GetProperty("code").GetString()).IsEqualTo("adapter-bind-failed");
    }

    [Test]
    public async Task TanStack_Adapter_Package_Has_No_AspNetCore_Dependency()
    {
        var adapterAssembly = typeof(TanStackTableAdapter).Assembly;
        await Assert.That(adapterAssembly.GetName().Name).IsEqualTo("a2n.Vista.Adapters.TanStackTable");

        var references = adapterAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        var referencesAspNetCore = references.Any(name =>
            name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || string.Equals(name, "a2n.Vista.AspNetCore", StringComparison.Ordinal));

        await Assert.That(referencesAspNetCore).IsFalse();
        await Assert.That(references.Any(name => name == "a2n.Vista.Core")).IsTrue();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

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
                        services.AddVistaAdapter<TanStackTableAdapter>();
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
