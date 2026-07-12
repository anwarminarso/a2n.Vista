// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
/// Requirement R1 (Decision Log D110) — the action-style endpoint surface, verified at the
/// route-registration level. Rather than round-tripping HTTP (where a defensive <c>404</c> from the
/// write handler is indistinguishable from an unmapped route), these tests inspect the composite
/// <see cref="EndpointDataSource"/> that <c>MapVistaViews()</c> populates, so they assert exactly which
/// routes <c>MapSingleView</c> produces:
/// <list type="bullet">
/// <item>R1.1 — a writable view exposes the full action set: <c>POST {route}/list|detail|export|
/// create|update|delete</c> and <c>GET {route}/metadata</c>.</item>
/// <item>R1.6 — a read-only view exposes ONLY the read actions (<c>list</c>, <c>detail</c>,
/// <c>metadata</c>, <c>export</c>); the write actions are not mapped at all (D38).</item>
/// <item>R1.7 — every action for a view sits under exactly one <c>{route}</c> prefix (D103,
/// one view = one route prefix).</item>
/// </list>
/// The Style B reflection authoring/mapping path is RUC, so IL2026 is suppressed at the class level —
/// trimming is not used for tests.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Test drives the reflection-based authoring/endpoint-mapping path by design; trimming is not used for tests.")]
public sealed class HttpSurfaceRouteMappingTests
{
    private const string WritableView = "rm-writable";
    private const string ReadOnlyView = "rm-readonly";

    /// <summary>R1.1: a writable view maps all seven action routes with the correct HTTP verbs.</summary>
    [Test]
    public async Task Writable_View_Maps_The_Full_Action_Set()
    {
        await using var app = await TestApp.StartAsync();
        var route = app.RouteOf(WritableView);
        var routes = app.RegisteredRoutes();

        await Assert.That(Has(routes, "POST", $"{route}/list")).IsTrue();
        await Assert.That(Has(routes, "POST", $"{route}/detail")).IsTrue();
        await Assert.That(Has(routes, "GET", $"{route}/metadata")).IsTrue();
        await Assert.That(Has(routes, "POST", $"{route}/export")).IsTrue();
        await Assert.That(Has(routes, "POST", $"{route}/create")).IsTrue();
        await Assert.That(Has(routes, "POST", $"{route}/update")).IsTrue();
        await Assert.That(Has(routes, "POST", $"{route}/delete")).IsTrue();
    }

    /// <summary>
    /// R1.6: a read-only view maps the four read actions and NONE of the write actions — the write
    /// routes are absent from the endpoint table, not merely gated by a runtime <c>404</c> (D38).
    /// </summary>
    [Test]
    public async Task ReadOnly_View_Maps_Only_Read_Actions()
    {
        await using var app = await TestApp.StartAsync();
        var route = app.RouteOf(ReadOnlyView);
        var routes = app.RegisteredRoutes();

        // Read actions are present.
        await Assert.That(Has(routes, "POST", $"{route}/list")).IsTrue();
        await Assert.That(Has(routes, "POST", $"{route}/detail")).IsTrue();
        await Assert.That(Has(routes, "GET", $"{route}/metadata")).IsTrue();
        await Assert.That(Has(routes, "POST", $"{route}/export")).IsTrue();

        // Write actions are not mapped at all for a read-only view.
        await Assert.That(HasPattern(routes, $"{route}/create")).IsFalse();
        await Assert.That(HasPattern(routes, $"{route}/update")).IsFalse();
        await Assert.That(HasPattern(routes, $"{route}/delete")).IsFalse();
    }

    /// <summary>
    /// R1.7: every action route for a view lives under a single <c>{route}</c> prefix (one view = one
    /// route prefix, D103). Asserted for both the writable and the read-only view.
    /// </summary>
    [Test]
    public async Task Each_View_Maps_Under_Exactly_One_Route_Prefix()
    {
        await using var app = await TestApp.StartAsync();
        var routes = app.RegisteredRoutes();

        foreach (var viewName in new[] { WritableView, ReadOnlyView })
        {
            var route = app.RouteOf(viewName);

            // All routes that belong to this view (its own prefix) must share exactly that prefix; the
            // action name is the only segment appended under it.
            var prefixes = routes
                .Where(r => r.Pattern.StartsWith($"{route}/", StringComparison.Ordinal))
                .Select(r => r.Pattern[..r.Pattern.LastIndexOf('/')])
                .Distinct()
                .ToArray();

            await Assert.That(prefixes).IsEquivalentTo(new[] { route });
        }
    }

    private static bool Has(IReadOnlyList<(string Method, string Pattern)> routes, string method, string pattern) =>
        routes.Any(r => r.Method == method && r.Pattern == pattern);

    private static bool HasPattern(IReadOnlyList<(string Method, string Pattern)> routes, string pattern) =>
        routes.Any(r => r.Pattern == pattern);

    // ---- Fixtures ------------------------------------------------------------------------------------

    private sealed class RouteMapSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class RouteMapRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract; only the non-key <c>Name</c> is writable.</summary>
    private sealed class RouteMapCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class RouteMapContext : DbContext
    {
        public RouteMapContext(DbContextOptions<RouteMapContext> options)
            : base(options)
        {
        }

        public DbSet<RouteMapSource> Sources => Set<RouteMapSource>();
    }

    /// <summary>A writable Style B view: it must expose the full action set (R1.1).</summary>
    private sealed class RouteMapWritableView : View<RouteMapRow, RouteMapCrud>
    {
        protected override void Configure(IViewBuilder<RouteMapRow, RouteMapCrud> builder)
        {
            builder
                .Named(WritableView)
                .From<RouteMapSource>(s => new RouteMapRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<RouteMapSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }

    /// <summary>A read-only Style B view: it must expose only the read actions (R1.6).</summary>
    private sealed class RouteMapReadOnlyView : View<RouteMapRow>
    {
        protected override void Configure(IViewBuilder<RouteMapRow> builder) =>
            builder
                .Named(ReadOnlyView)
                .From<RouteMapSource>(s => new RouteMapRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());
    }

    /// <summary>A started in-process host owning the in-memory SQLite connection; exposes the endpoint table.</summary>
    private sealed class TestApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private TestApp(IHost host, SqliteConnection connection) => (_host, _connection) = (host, connection);

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
                        services.AddDbContext<RouteMapContext>(o => o.UseSqlite(connection));
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<RouteMapContext>());
                        services.AddVista(v =>
                        {
                            v.Register<RouteMapWritableView>();
                            v.Register<RouteMapReadOnlyView>();
                        });
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
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
                scope.ServiceProvider.GetRequiredService<RouteMapContext>().Database.EnsureCreated();
            }

            return new TestApp(host, connection);
        }

        /// <summary>The full registered route of a view (composed at registration, D101/D103).</summary>
        public string RouteOf(string viewName) =>
            _host.Services.GetRequiredService<IViewRegistry>().Get(viewName)?.Route
                ?? throw new InvalidOperationException($"View '{viewName}' was not registered.");

        /// <summary>Flattens the composite endpoint table into (HTTP method, route pattern) pairs.</summary>
        public IReadOnlyList<(string Method, string Pattern)> RegisteredRoutes()
        {
            var source = _host.Services.GetRequiredService<EndpointDataSource>();
            var routes = new List<(string, string)>();

            foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
            {
                var pattern = endpoint.RoutePattern.RawText;
                if (pattern is null)
                {
                    continue;
                }

                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                    ?? (IReadOnlyList<string>)new[] { "*" };

                foreach (var method in methods)
                {
                    routes.Add((method, pattern));
                }
            }

            return routes;
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync();
            _host.Dispose();
            _connection.Dispose();
        }
    }
}
