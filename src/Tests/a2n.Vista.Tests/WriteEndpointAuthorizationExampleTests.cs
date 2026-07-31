// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
/// Example-based endpoint tests for the M12 write path's authorization facet (write-path task 6.10;
/// Requirements R7.1, R7.2, R7.3). Each case drives the real ASP.NET Core write pipeline over an
/// in-process <see cref="TestServer"/> backed by a fresh, isolated SQLite database, with a one-door
/// <see cref="IViewAuthorizer"/> that either denies or throws. The write facet is authorized
/// independently and fail-closed: a deny decision and a throwing authorizer both map to HTTP 403 and
/// mutate no stored data.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration test drives the reflection-based endpoint/executor write path by design; trimming is not used for tests.")]
public sealed class WriteEndpointAuthorizationExampleTests
{
    private const string ViewName = "we-auth";
    private const string Route = "/api/views/" + ViewName;

    /// <summary>R7.2: a create denied by the authorizer → 403; no row is inserted.</summary>
    [Test]
    public async Task Authorizer_Deny_Create_Returns_403_And_Inserts_Nothing()
    {
        await using var app = await TestApp.StartAsync(AuthorizerMode.Deny);

        var response = await app.Client.PostAsync($"{Route}/create", Json("{\"model\":{\"name\":\"x\"}}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(app.RowCount()).IsEqualTo(0);
    }

    /// <summary>R7.2: an update denied by the authorizer → 403; the target row is left in its pre-request state.</summary>
    [Test]
    public async Task Authorizer_Deny_Update_Returns_403_And_Leaves_Row_Unchanged()
    {
        await using var app = await TestApp.StartAsync(AuthorizerMode.Deny);
        app.Seed(1, "before");

        var response = await app.Client.PostAsync($"{Route}/update", Json("{\"key\":1,\"model\":{\"name\":\"after\"}}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(app.ReadName(1)).IsEqualTo("before");
    }

    /// <summary>R7.3: an authorizer that throws is treated as a deny → 403; no row is deleted.</summary>
    [Test]
    public async Task Authorizer_Throw_Delete_Returns_403_And_Leaves_Row_Unchanged()
    {
        await using var app = await TestApp.StartAsync(AuthorizerMode.Throw);
        app.Seed(1, "keep");

        var response = await app.Client.PostAsync($"{Route}/delete", Json("{\"key\":1}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(app.ReadName(1)).IsEqualTo("keep");
    }

    /// <summary>
    /// Audit `BUG-03` / Decision Log D145: a malformed body from an unauthorized caller must still be
    /// <b>403</b>, not the <c>400</c> bind error. The bind error would confirm the view exists and is
    /// writable, and it made the server parse an unauthorized caller's payload first.
    /// </summary>
    [Test]
    public async Task Denied_Caller_With_Malformed_Body_Gets_403_Not_400()
    {
        await using var app = await TestApp.StartAsync(AuthorizerMode.Deny);

        var response = await app.Client.PostAsync($"{Route}/create", Json("{ this is not json"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Audit `BUG-03` / D145: an update with no <c>key</c> from an unauthorized caller is <b>403</b>, not the
    /// <c>400</c> missing-key error — the request never reaches the binder.
    /// </summary>
    [Test]
    public async Task Denied_Caller_With_Missing_Key_Gets_403_Not_400()
    {
        await using var app = await TestApp.StartAsync(AuthorizerMode.Deny);
        app.Seed(1, "before");

        var response = await app.Client.PostAsync($"{Route}/update", Json("{\"model\":{\"name\":\"after\"}}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(app.ReadName(1)).IsEqualTo("before");
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private enum AuthorizerMode
    {
        Deny,
        Throw,
    }

    // ---- Fixtures ------------------------------------------------------------------------------------

    private sealed class AuthSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class AuthRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    private sealed class NameCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class AuthContext : DbContext
    {
        public AuthContext(DbContextOptions<AuthContext> options)
            : base(options)
        {
        }

        public DbSet<AuthSource> Sources => Set<AuthSource>();
    }

    private sealed class AuthWritableView : View<AuthRow, NameCrud>
    {
        protected override void Configure(IViewBuilder<AuthRow, NameCrud> builder)
        {
            builder
                .Named(ViewName)
                .From<AuthSource>(s => new AuthRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<AuthSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }

    /// <summary>An authorizer that either denies every request or throws while deciding (fail-closed).</summary>
    private sealed class ConfigurableAuthorizer : IViewAuthorizer
    {
        private readonly AuthorizerMode _mode;

        public ConfigurableAuthorizer(AuthorizerMode mode) => _mode = mode;

        public ValueTask<bool> IsAllowedAsync(ViewAuthContext context)
        {
            if (_mode == AuthorizerMode.Throw)
            {
                throw new InvalidOperationException("Authorizer failed to reach a decision.");
            }

            return ValueTask.FromResult(false);
        }

        public void ShapeQuery(ViewAuthContext context, IViewScope scope)
        {
            // No server-trusted filters needed for these tests.
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

        [RequiresUnreferencedCode("Vista endpoint mapping + reflection write path are used by design.")]
        public static async Task<TestApp> StartAsync(AuthorizerMode mode)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<AuthContext>(o => o.UseSqlite(connection));
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<AuthContext>());
                        services.AddVista(v => v.Register<AuthWritableView>());

                        // AllowAnonymousAccess keeps startup from failing closed (no authorizer type is
                        // recorded on the options); the real authorizer below still runs because the glue
                        // resolves IViewAuthorizer from the request services directly.
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
                        services.AddScoped<IViewAuthorizer>(_ => new ConfigurableAuthorizer(mode));
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
                scope.ServiceProvider.GetRequiredService<AuthContext>().Database.EnsureCreated();
            }

            return new TestApp(host, connection, host.GetTestClient());
        }

        public void Seed(int id, string name)
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AuthContext>();
            ctx.Sources.Add(new AuthSource { Id = id, Name = name });
            ctx.SaveChanges();
        }

        public string? ReadName(int id)
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AuthContext>();
            return ctx.Sources.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).SingleOrDefault();
        }

        public int RowCount()
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AuthContext>();
            return ctx.Sources.AsNoTracking().Count();
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
