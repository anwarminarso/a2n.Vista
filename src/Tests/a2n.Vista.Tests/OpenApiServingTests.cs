// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
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
/// Serving-integration examples for the opt-in Vista OpenAPI endpoint (spec openapi-emitter, task 7.1;
/// Decision Log D128). Each case drives the <em>real</em> ASP.NET Core pipeline over an in-process
/// <see cref="TestServer"/> with the production wiring (<c>AddVista</c> + <c>AddVistaEndpoints</c> +
/// <c>AddVistaOpenApi</c>, then <c>MapVistaViews</c> + <c>MapVistaOpenApi</c>), so the served document is
/// built by the metadata-driven builder over the live registry and the live serialization seam.
/// <list type="bullet">
/// <item>R11.1 — <c>GET /openapi/v1.json</c> → <c>200</c> <c>application/json</c> with a document carrying
/// <c>openapi</c> and <c>paths</c>.</item>
/// <item>R11.2 — configured title/version/endpoint path are reflected in the served document and its
/// route.</item>
/// <item>R10.3 — off by default: a host that does not call the emitter has no document endpoint (404) and
/// its existing view endpoints are unchanged.</item>
/// </list>
/// The Vista endpoint mapping and the OpenAPI document build are RUC (reflection), so IL2026 is suppressed
/// at the class level — trimming is not used for tests.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Test drives the reflection-based endpoint mapping and OpenAPI builder by design; trimming is not used for tests.")]
public sealed class OpenApiServingTests
{
    private const string ViewName = "openapiWidgets";

    // ---- R11.1: serves a valid document at the default path ---------------------------------------

    [Test]
    public async Task Serves_Valid_Document_At_Default_Path()
    {
        await using var app = await TestApp.StartAsync();

        using var response = await app.Client.GetAsync("/openapi/v1.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/json");

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        // A valid OpenAPI document carries an "openapi" version and a "paths" object (R11.1).
        await Assert.That(document.RootElement.TryGetProperty("openapi", out _)).IsTrue();
        await Assert.That(document.RootElement.TryGetProperty("paths", out var paths)).IsTrue();

        // Endpoint parity smoke check: the view's list operation is present on its route.
        var listPath = app.RouteOf(ViewName) + "/list";
        await Assert.That(paths.TryGetProperty(listPath, out _)).IsTrue();
    }

    // ---- R11.2: options applied to the document and the serve path --------------------------------

    [Test]
    public async Task Applies_Custom_Title_Version_And_Endpoint_Path()
    {
        await using var app = await TestApp.StartAsync(o =>
        {
            o.DocumentTitle = "Widget API";
            o.DocumentVersion = "9.9.9";
            o.EndpointPath = "/docs/openapi.json";
        });

        // The document is served at the custom path...
        using var custom = await app.Client.GetAsync("/docs/openapi.json");
        await Assert.That(custom.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // ...and not at the default one.
        using var defaultPath = await app.Client.GetAsync("/openapi/v1.json");
        await Assert.That(defaultPath.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        var body = await custom.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var info = document.RootElement.GetProperty("info");

        await Assert.That(info.GetProperty("title").GetString()).IsEqualTo("Widget API");
        await Assert.That(info.GetProperty("version").GetString()).IsEqualTo("9.9.9");
    }

    // ---- R10.3: off by default --------------------------------------------------------------------

    [Test]
    public async Task Off_By_Default_When_Emitter_Not_Registered()
    {
        await using var app = await TestApp.StartAsync(configureOpenApi: null, registerOpenApi: false);

        // No document endpoint exists when AddVistaOpenApi/MapVistaOpenApi were not called (R10.3).
        using var missing = await app.Client.GetAsync("/openapi/v1.json");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        // Existing view endpoints are unchanged (the metadata GET still serves).
        using var metadata = await app.Client.GetAsync(app.RouteOf(ViewName) + "/metadata");
        await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // ---- Fixtures ----------------------------------------------------------------------------------

    private sealed class WidgetSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class WidgetRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    private sealed class WidgetContext : DbContext
    {
        public WidgetContext(DbContextOptions<WidgetContext> options)
            : base(options)
        {
        }

        public DbSet<WidgetSource> Sources => Set<WidgetSource>();
    }

    private sealed class WidgetView : View<WidgetRow>
    {
        protected override void Configure(IViewBuilder<WidgetRow> builder) =>
            builder
                .Named(ViewName)
                .From<WidgetSource>(s => new WidgetRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());
    }

    private sealed class TestApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private TestApp(IHost host, SqliteConnection connection, HttpClient client) =>
            (_host, _connection, Client) = (host, connection, client);

        public HttpClient Client { get; }

        [RequiresUnreferencedCode("Vista endpoint mapping and the OpenAPI builder use reflection by design.")]
        public static async Task<TestApp> StartAsync(
            Action<a2n.Vista.OpenApi.VistaOpenApiOptions>? configureOpenApi = null,
            bool registerOpenApi = true)
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
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<WidgetContext>());
                        services.AddVista(v => v.Register<WidgetView>());
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());

                        if (registerOpenApi)
                        {
                            services.AddVistaOpenApi(configureOpenApi);
                        }
                    })
                    .Configure(app =>
                    {
                        app.UseVistaExceptionHandling();
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapVistaViews();
                            if (registerOpenApi)
                            {
                                endpoints.MapVistaOpenApi();
                            }
                        });
                    }))
                .StartAsync();

            using (var scope = host.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<WidgetContext>().Database.EnsureCreated();
            }

            var client = host.GetTestClient();
            return new TestApp(host, connection, client);
        }

        public string RouteOf(string viewName) =>
            _host.Services.GetRequiredService<a2n.Vista.Ports.IViewRegistry>().Get(viewName)?.Route
                ?? throw new InvalidOperationException($"View '{viewName}' was not registered.");

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            _connection.Dispose();
        }
    }
}
