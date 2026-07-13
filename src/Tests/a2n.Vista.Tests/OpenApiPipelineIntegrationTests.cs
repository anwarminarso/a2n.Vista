// Licensed to the a2n.Vista project. Published artifact — English only.

// The built-in ASP.NET Core OpenAPI pipeline (Microsoft.AspNetCore.OpenApi) is a net9.0+ package, so the
// pipeline-integration surface (VistaOpenApiDocumentTransformer + AddVistaOpenApiPipelineIntegration) only
// exists there. The whole test is TFM-guarded to net9.0+ so the net8.0 test run is unaffected (spec
// openapi-emitter, task 7.2; Requirement 11.4).
#if NET9_0_OR_GREATER

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.OpenApi.AspNetCorePipeline;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
#if NET10_0_OR_GREATER
using OA = Microsoft.OpenApi;
#else
using OA = Microsoft.OpenApi.Models;
#endif

namespace a2n.Vista.Tests;

/// <summary>
/// Integration and direct examples for the optional built-in ASP.NET Core OpenAPI pipeline integration
/// (spec openapi-emitter, task 7.2; Requirement 11.4; Decision Log D128).
/// <list type="bullet">
/// <item>R11.4 (end-to-end) — a host using <c>AddOpenApi</c> + <c>AddVistaOpenApiPipelineIntegration</c> +
/// <c>AddVistaOpenApi</c> and mapping the built-in <c>MapOpenApi()</c> endpoint sees the Vista view paths
/// and component schemas merged into its <c>/openapi/v1.json</c> document.</item>
/// <item>R11.4 (direct) — invoking <see cref="VistaOpenApiDocumentTransformer"/> on a bare
/// <c>Microsoft.OpenApi</c> document merges the Vista paths/components/security, adds absent entries, and
/// never clobbers an entry the target already declares (skip-if-exists / add-if-absent).</item>
/// </list>
/// The Vista endpoint mapping and the OpenAPI document build are RUC (reflection); IL2026 is suppressed at
/// the class level — trimming is not used for tests.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Test drives the reflection-based endpoint mapping and OpenAPI builder/transformer by design; trimming is not used for tests.")]
public sealed class OpenApiPipelineIntegrationTests
{
    private const string ViewName = "pipelineWidgets";

    // ---- R11.4: end-to-end merge into the built-in pipeline document ------------------------------

    [Test]
    public async Task Merges_Vista_Views_Into_BuiltIn_Pipeline_Document()
    {
        await using var app = await TestApp.StartAsync();

        // The built-in pipeline serves at /openapi/{documentName}.json; the default document is "v1".
        using var response = await app.Client.GetAsync("/openapi/v1.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        // The Vista list operation was merged into the built-in document's paths...
        await Assert.That(document.RootElement.TryGetProperty("paths", out var paths)).IsTrue();
        var listPath = app.RouteOf(ViewName) + "/list";
        await Assert.That(paths.TryGetProperty(listPath, out var listItem)).IsTrue();
        await Assert.That(listItem.TryGetProperty("post", out _)).IsTrue();

        // ...and the Vista envelope component schema came along with it.
        await Assert.That(document.RootElement.TryGetProperty("components", out var components)).IsTrue();
        await Assert.That(components.TryGetProperty("schemas", out var schemas)).IsTrue();
        await Assert.That(schemas.TryGetProperty("VistaListRequestBody", out _)).IsTrue();
    }

    // ---- R11.4: direct transformer merge semantics (add-if-absent / skip-if-exists / security) ----

    [Test]
    public async Task Transformer_Merges_Paths_Components_Security_And_Skips_Collisions()
    {
        // A non-anonymous posture so the builder emits the default HTTP bearer security scheme (R7.1); the
        // document build reads VistaEndpointOptions only, so no live auth pipeline / fail-closed startup is
        // involved here.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<WidgetContext>(o => o.UseSqlite(connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<WidgetContext>());
        services.AddVista(v => v.Register<WidgetView>());
        services.AddVistaEndpoints(_ => { }); // not anonymous
        services.AddVistaOpenApi();

        await using var provider = services.BuildServiceProvider();
        var builder = provider.GetRequiredService<a2n.Vista.OpenApi.VistaOpenApiDocumentBuilder>();
        var route = provider.GetRequiredService<a2n.Vista.Ports.IViewRegistry>().Get(ViewName)!.Route;
        var listPath = route + "/list";
        var metadataPath = route + "/metadata";

        // Pre-seed the target document with a colliding path and a colliding component schema; the
        // transformer must NOT overwrite either (skip-if-exists / add-if-absent).
        var target = new OA.OpenApiDocument
        {
            Paths = new OA.OpenApiPaths(),
            Components = new OA.OpenApiComponents(),
        };
        var minePathItem = new OA.OpenApiPathItem();
        target.Paths[listPath] = minePathItem;
        var mineSchema = new OA.OpenApiSchema();
#if NET10_0_OR_GREATER
        target.Components.Schemas = new System.Collections.Generic.Dictionary<string, OA.IOpenApiSchema>
        {
            ["VistaListRequestBody"] = mineSchema,
        };
#else
        target.Components.Schemas = new System.Collections.Generic.Dictionary<string, OA.OpenApiSchema>
        {
            ["VistaListRequestBody"] = mineSchema,
        };
#endif

        var transformer = new VistaOpenApiDocumentTransformer(builder);
        var context = new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            ApplicationServices = provider,
            DescriptionGroups = Array.Empty<Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescriptionGroup>(),
        };

        await transformer.TransformAsync(target, context, CancellationToken.None);

        // Skip-if-exists: the pre-seeded path item and schema are the exact instances we inserted.
        await Assert.That(ReferenceEquals(target.Paths[listPath], minePathItem)).IsTrue();
        await Assert.That(ReferenceEquals(target.Components!.Schemas!["VistaListRequestBody"], mineSchema)).IsTrue();

        // Add-if-absent: a Vista path the target did not declare was added fresh, with its operation.
        await Assert.That(target.Paths.ContainsKey(metadataPath)).IsTrue();
        await Assert.That(target.Paths[metadataPath].Operations!.Count > 0).IsTrue();

        // Components merged: another Vista envelope schema is present.
        await Assert.That(target.Components!.Schemas!.ContainsKey("VistaMetadataResponse")).IsTrue();

        // Security merged (not anonymous): the default bearer scheme is under components.securitySchemes...
        await Assert.That(target.Components!.SecuritySchemes is not null).IsTrue();
        await Assert.That(target.Components!.SecuritySchemes!.ContainsKey("bearer")).IsTrue();

        // ...and the freshly added operation carries a security requirement.
        var metadataGet = FirstOperation(target.Paths[metadataPath]);
        await Assert.That(metadataGet.Security is { Count: > 0 }).IsTrue();
    }

#if NET10_0_OR_GREATER
    private static OA.OpenApiOperation FirstOperation(OA.IOpenApiPathItem item)
#else
    private static OA.OpenApiOperation FirstOperation(OA.OpenApiPathItem item)
#endif
    {
        foreach (var operation in item.Operations!.Values)
        {
            return operation;
        }

        throw new InvalidOperationException("The path item declares no operation.");
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

        [RequiresUnreferencedCode("Vista endpoint mapping and the OpenAPI builder/transformer use reflection by design.")]
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
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<WidgetContext>());
                        services.AddVista(v => v.Register<WidgetView>());
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());

                        // The Vista document builder (resolved by the transformer)...
                        services.AddVistaOpenApi();

                        // ...and the built-in pipeline wired with the Vista document transformer (R11.4).
                        services.AddVistaOpenApiPipelineIntegration();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapVistaViews();

                            // The built-in pipeline serve endpoint (NOT MapVistaOpenApi): /openapi/v1.json.
                            endpoints.MapOpenApi();
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

#endif
