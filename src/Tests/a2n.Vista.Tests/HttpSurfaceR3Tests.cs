// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.Authoring;
using a2n.Vista.Ports;
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
/// Requirement R3 (Decision Log D110) — the action-style HTTP surface honours the same one-door
/// <see cref="IViewAuthorizer"/> as the pre-redesign REST surface, verified end to end over an
/// in-process <see cref="TestServer"/> with a real SQLite-backed, writable Style B view (so the full
/// facet set is mapped: <c>list</c>, <c>detail</c>, <c>metadata</c>, <c>export</c>, <c>create</c>,
/// <c>update</c>, <c>delete</c>):
/// <list type="bullet">
/// <item>R3.1 — EACH action endpoint maps to its <see cref="ViewFacet"/> and passes it to the one-door
/// authorizer exactly as today; a denying authorizer therefore turns every action into HTTP 403.</item>
/// <item>R3.3 — the <c>metadata</c> facet is authorized like any other facet: a deny on
/// <c>GET {route}/metadata</c> yields 403 (there is NO implicit anonymous metadata), and an allow yields
/// 200 — confirming the 403 is caused by the deny decision, not by a wiring fault.</item>
/// </list>
/// A real authorizer is registered through the public <c>UseAuthorizer&lt;T&gt;()</c> path (not
/// <c>AllowAnonymousAccess()</c>), so the D94 fail-safe posture (R3.2) is exercised in its natural,
/// authorizer-present form. R3.2's startup matrix (no authorizer → warn in Development / fail-closed
/// otherwise / explicit opt-in) is covered directly by <see cref="AuthorizationTests"/> and is not
/// duplicated here.
/// The Style B reflection authoring/mapping path is RUC, so IL2026 is suppressed at the class level —
/// trimming is not used for tests.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration tests drive the reflection-based endpoint/executor path by design; trimming is not used for tests.")]
public sealed class HttpSurfaceR3Tests
{
    private const string ViewName = "r3-widgets";
    private const string Route = "/api/views/" + ViewName;

    // ---- R3.1: a denying authorizer turns EACH action endpoint into HTTP 403 ------------------------

    /// <summary>R3.1: <c>POST {route}/list</c> denied by the authorizer → 403.</summary>
    [Test]
    public async Task Deny_List_Returns_403()
    {
        await using var app = await TestApp.StartAsync(deny: true);

        var response = await app.Client.PostAsync($"{Route}/list", Json("""{ "page": 0, "pageSize": 10 }"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>R3.1: <c>POST {route}/detail</c> denied by the authorizer → 403 (before any row lookup).</summary>
    [Test]
    public async Task Deny_Detail_Returns_403()
    {
        await using var app = await TestApp.StartAsync(deny: true);

        var response = await app.Client.PostAsync($"{Route}/detail", Json("""{ "key": 1 }"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// R3.3: <c>GET {route}/metadata</c> denied by the authorizer → 403. Metadata is authorized like any
    /// other facet; there is no implicit anonymous metadata disclosure.
    /// </summary>
    [Test]
    public async Task Deny_Metadata_Returns_403()
    {
        await using var app = await TestApp.StartAsync(deny: true);

        var response = await app.Client.GetAsync($"{Route}/metadata");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>R3.1: <c>POST {route}/export</c> denied by the authorizer → 403.</summary>
    [Test]
    public async Task Deny_Export_Returns_403()
    {
        await using var app = await TestApp.StartAsync(deny: true);

        var response = await app.Client.PostAsync($"{Route}/export", Json("{}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>R3.1: <c>POST {route}/create</c> denied by the authorizer → 403.</summary>
    [Test]
    public async Task Deny_Create_Returns_403()
    {
        await using var app = await TestApp.StartAsync(deny: true);

        var response = await app.Client.PostAsync($"{Route}/create", Json("""{ "model": { "name": "x" } }"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>R3.1: <c>POST {route}/update</c> denied by the authorizer → 403.</summary>
    [Test]
    public async Task Deny_Update_Returns_403()
    {
        await using var app = await TestApp.StartAsync(deny: true);

        var response = await app.Client.PostAsync(
            $"{Route}/update",
            Json("""{ "key": 1, "model": { "name": "y" } }"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>R3.1: <c>POST {route}/delete</c> denied by the authorizer → 403.</summary>
    [Test]
    public async Task Deny_Delete_Returns_403()
    {
        await using var app = await TestApp.StartAsync(deny: true);

        var response = await app.Client.PostAsync($"{Route}/delete", Json("""{ "key": 1 }"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    // ---- R3.3 positive control: an allowing authorizer lets metadata (and list) through -------------

    /// <summary>
    /// R3.3 (positive control): with an ALLOWING authorizer, <c>GET {route}/metadata</c> returns 200 —
    /// proving the deny case above is the discriminating factor, not a mapping/wiring fault.
    /// </summary>
    [Test]
    public async Task Allow_Metadata_Returns_200()
    {
        await using var app = await TestApp.StartAsync(deny: false);

        var response = await app.Client.GetAsync($"{Route}/metadata");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    // ---- Fixtures -----------------------------------------------------------------------------------

    private sealed class R3Source
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class R3Row
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    private sealed class R3Crud
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class R3Context : DbContext
    {
        public R3Context(DbContextOptions<R3Context> options)
            : base(options)
        {
        }

        public DbSet<R3Source> Sources => Set<R3Source>();
    }

    /// <summary>A writable Style B view over <see cref="R3Source"/> so the full action facet set is mapped.</summary>
    private sealed class R3WritableView : View<R3Row, R3Crud>
    {
        protected override void Configure(IViewBuilder<R3Row, R3Crud> builder)
        {
            builder
                .Named(ViewName)
                .From<R3Source>(s => new R3Row { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<R3Source>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }

    /// <summary>The one-door authorizer under test: denies every facet.</summary>
    private sealed class DenyingAuthorizer : IViewAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(ViewAuthContext context) => ValueTask.FromResult(false);

        public void ShapeQuery(ViewAuthContext context, IViewScope scope)
        {
            // No server-trusted filters needed for these tests.
        }
    }

    /// <summary>The one-door authorizer under test: allows every facet (positive control).</summary>
    private sealed class AllowingAuthorizer : IViewAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(ViewAuthContext context) => ValueTask.FromResult(true);

        public void ShapeQuery(ViewAuthContext context, IViewScope scope)
        {
            // Empty scope → all rows eligible.
        }
    }

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
        public static async Task<TestApp> StartAsync(bool deny)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<R3Context>(o => o.UseSqlite(connection));
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<R3Context>());
                        services.AddVista(v => v.Register<R3WritableView>());

                        // Register a REAL authorizer via UseAuthorizer<T>() (not AllowAnonymousAccess()):
                        // this both drives the 403s and keeps the D94 posture in its natural,
                        // authorizer-present form (HasAuthorizer = true), so startup does not fail closed.
                        services.AddVistaEndpoints(e =>
                        {
                            if (deny)
                            {
                                e.UseAuthorizer<DenyingAuthorizer>();
                            }
                            else
                            {
                                e.UseAuthorizer<AllowingAuthorizer>();
                            }
                        });
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
                scope.ServiceProvider.GetRequiredService<R3Context>().Database.EnsureCreated();
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
