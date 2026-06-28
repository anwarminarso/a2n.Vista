using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using a2n.Vista.Adapters.DataTablesNet;
using a2n.Vista.Authoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
/// End-to-end HTTP integration tests over an in-process <see cref="TestServer"/> with a real
/// SQLite-backed Gaya A view: exercises the action-style surface (D110), the DataTables adapter endpoint
/// (D111/D112), and the page-size 400 contract through the actual ASP.NET Core pipeline.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration tests drive the reflection-based endpoint/executor path by design.")]
public sealed class HttpEndpointIntegrationTests
{
    private const string Route = "/api/views/Widgets";

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

    [Test]
    public async Task List_Returns_Paged_Result()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync(
            $"{Route}/list",
            JsonContent("{\"page\":0,\"pageSize\":5,\"sort\":[{\"field\":\"Name\",\"desc\":false}]}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("totalRowsUnfiltered").GetInt64()).IsEqualTo(25L);
        await Assert.That(root.GetProperty("page").GetProperty("items").GetArrayLength()).IsEqualTo(5);
    }

    [Test]
    public async Task Metadata_Returns_View_Shape()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.GetAsync($"{Route}/metadata");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Widgets");
    }

    [Test]
    public async Task DataTables_Endpoint_Round_Trips()
    {
        await using var app = await TestApp.StartAsync();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["draw"] = "5",
            ["start"] = "0",
            ["length"] = "3",
            ["columns[0][data]"] = "Name",
            ["order[0][column]"] = "0",
            ["order[0][dir]"] = "asc",
        });

        var response = await app.Client.PostAsync($"{Route}/datatable", form);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("draw").GetInt32()).IsEqualTo(5);
        await Assert.That(root.GetProperty("recordsTotal").GetInt64()).IsEqualTo(25L);
        await Assert.That(root.GetProperty("recordsFiltered").GetInt64()).IsEqualTo(25L);
        await Assert.That(root.GetProperty("data").GetArrayLength()).IsEqualTo(3);
    }

    [Test]
    public async Task List_Negative_PageSize_Is_400()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync(
            $"{Route}/list",
            JsonContent("{\"page\":0,\"pageSize\":-1}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Metadata_Caching_Off_By_Default_No_ETag()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.GetAsync($"{Route}/metadata");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.ETag).IsNull();
    }

    [Test]
    public async Task Metadata_Caching_Enabled_Emits_ETag_And_Honors_If_None_Match()
    {
        await using var app = await TestApp.StartAsync(enableMetadataCache: true);

        var first = await app.Client.GetAsync($"{Route}/metadata");
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(first.Headers.ETag).IsNotNull();

        var etag = first.Headers.ETag!.ToString();
        var conditional = new HttpRequestMessage(HttpMethod.Get, $"{Route}/metadata");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await app.Client.SendAsync(conditional);

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
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

        [RequiresUnreferencedCode("Vista endpoint mapping uses the reflection bridge by design.")]
        public static async Task<TestApp> StartAsync(bool enableMetadataCache = false)
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
                        services.AddVistaEndpoints(e =>
                        {
                            e.AllowAnonymousAccess();
                            if (enableMetadataCache)
                            {
                                e.EnableMetadataCaching();
                            }
                        });
                        services.AddVistaAdapter<DataTablesAdapter>();
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
