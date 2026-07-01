// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.Authoring;
using a2n.Vista.Ports;
using CsCheck;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the not-found indistinguishability guarantee of the write path
/// (write-path task 6.5; Property 5; Decision Log D119/D120). This is an endpoint-level property, so it
/// runs through the full ASP.NET Core pipeline over an in-process <see cref="TestServer"/> backed by a
/// real SQLite <see cref="DbContext"/> and the real <c>EfViewExecutor</c> write facet (no fake executor).
/// </summary>
/// <remarks>
/// <para>
/// A single <c>IViewAuthorizer</c> shapes every write with a server-trusted row filter
/// (<c>TenantId == AuthorizedTenant</c>), so rows seeded under a different tenant exist in the store yet
/// fall outside the authorized <c>View_Scope</c>. The property drives update and delete against three
/// classes of "missing" target — an out-of-scope key that physically exists, a genuinely absent key, and
/// the write routes of a read-only view and of an unregistered view — and asserts every response is a
/// byte-identical <c>404</c> (same status, same body) so no signal discloses that the row or view
/// exists. It also re-reads the out-of-scope rows afterwards to confirm no stored row was modified.
/// </para>
/// <para>
/// The write facet is RUC-annotated (it resolves the write mapper/key from metadata at runtime); trimming
/// is not used for tests, so IL2026 is suppressed at the class level, matching the sibling write-path
/// tests.
/// </para>
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The test exercises the runtime reflection write path end-to-end by design; trimming is not used for tests.")]
public sealed class NotFoundIndistinguishabilityPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>The tenant the authorizer trusts; only rows with this tenant are in scope.</summary>
    private const int AuthorizedTenant = 1;

    /// <summary>A different tenant whose rows physically exist but are always out of the authorized scope.</summary>
    private const int OtherTenant = 2;

    private const string WritableView = "p5-writable";
    private const string ReadOnlyView = "p5-readonly";
    private const string UnregisteredView = "p5-unregistered";

    private const string WritableRoute = "/api/views/" + WritableView;
    private const string ReadOnlyRoute = "/api/views/" + ReadOnlyView;
    private const string UnregisteredRoute = "/api/views/" + UnregisteredView;

    // Seeded id ranges. Out-of-scope rows physically exist (OtherTenant); the absent range is never seeded.
    private const int OutOfScopeMinId = 100;
    private const int OutOfScopeMaxId = 120;
    private const int AbsentMinId = 500;
    private const int AbsentMaxId = 900;

    private static readonly string[] PostedNames = { "", "posted", "changed-name", "p5" };

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    /// <summary>
    /// A generated probe: which physically-present out-of-scope key to hit, which genuinely-absent key to
    /// hit, and the whitelisted name the write posts (irrelevant to the outcome — every target is a 404 —
    /// but varied so no accidental success could hide behind a fixed payload).
    /// </summary>
    private readonly record struct Probe(int OutOfScopeId, int AbsentId, string PostedName);

    private static readonly Gen<Probe> GenProbe =
        from outOfScopeId in Gen.Int[OutOfScopeMinId, OutOfScopeMaxId]
        from absentId in Gen.Int[AbsentMinId, AbsentMaxId]
        from postedName in Pick(PostedNames)
        select new Probe(outOfScopeId, absentId, postedName);

    // Feature: write-path, Property 5: For any update or delete whose target key exists but falls outside
    // the authorized View_Scope — and for any write request against a read-only or unregistered view — the
    // response is identical in status and body to the response for a genuinely nonexistent key of the same
    // shape (HTTP 404, same body), and no stored row is modified. No signal discloses that the row or view
    // exists.
    //
    // Validates: Requirements 8.1, 8.2, 8.3, 12.3
    [Test]
    public async Task OutOfScope_ReadOnly_And_Unregistered_Targets_Are_Indistinguishable_From_NotFound()
    {
        await using var app = await TestApp.StartAsync();

        GenProbe.Sample(
            probe =>
            {
                // The canonical "genuinely nonexistent key" response every other 404 must match exactly.
                var genuineUpdate = app.PostSync($"{WritableRoute}/update", UpdateBody(probe.AbsentId, probe.PostedName));
                RequireNotFound(genuineUpdate, "update against a genuinely absent key");

                var genuineDelete = app.PostSync($"{WritableRoute}/delete", DeleteBody(probe.AbsentId));
                RequireNotFound(genuineDelete, "delete against a genuinely absent key");

                // 1) Out-of-scope key that physically exists → must be indistinguishable from absent (R8.2/R8.3).
                var oosUpdate = app.PostSync($"{WritableRoute}/update", UpdateBody(probe.OutOfScopeId, probe.PostedName));
                RequireIdentical(genuineUpdate, oosUpdate, "out-of-scope update", "genuinely-absent update");

                var oosDelete = app.PostSync($"{WritableRoute}/delete", DeleteBody(probe.OutOfScopeId));
                RequireIdentical(genuineDelete, oosDelete, "out-of-scope delete", "genuinely-absent delete");

                // 2) Read-only view write routes are never mapped → routing 404, indistinguishable (R12.3).
                var readOnlyUpdate = app.PostSync($"{ReadOnlyRoute}/update", UpdateBody(probe.OutOfScopeId, probe.PostedName));
                RequireIdentical(genuineUpdate, readOnlyUpdate, "read-only view update route", "genuinely-absent update");

                var readOnlyDelete = app.PostSync($"{ReadOnlyRoute}/delete", DeleteBody(probe.OutOfScopeId));
                RequireIdentical(genuineDelete, readOnlyDelete, "read-only view delete route", "genuinely-absent delete");

                // 3) Unregistered view write routes do not exist → routing 404, indistinguishable (R12.3).
                var unregisteredUpdate = app.PostSync($"{UnregisteredRoute}/update", UpdateBody(probe.OutOfScopeId, probe.PostedName));
                RequireIdentical(genuineUpdate, unregisteredUpdate, "unregistered view update route", "genuinely-absent update");

                var unregisteredDelete = app.PostSync($"{UnregisteredRoute}/delete", DeleteBody(probe.OutOfScopeId));
                RequireIdentical(genuineDelete, unregisteredDelete, "unregistered view delete route", "genuinely-absent delete");

                // No stored row is modified: the targeted out-of-scope row is byte-identical to its seed.
                var expectedName = SeedName(probe.OutOfScopeId);
                var actualName = app.GetSourceName(probe.OutOfScopeId);
                if (actualName is null)
                {
                    throw new Exception(
                        $"Out-of-scope row id {probe.OutOfScopeId} vanished from the store; a 404 write must not delete it.");
                }

                if (!string.Equals(actualName, expectedName, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Out-of-scope row id {probe.OutOfScopeId} changed from '{expectedName}' to '{actualName}'; "
                        + "a not-found (out-of-scope) write must persist no change.");
                }
            },
            iter: Iterations);
    }

    /// <summary>Asserts a response is a <c>404</c> with an empty body (the shared not-found shape).</summary>
    private static void RequireNotFound((HttpStatusCode Status, string Body) response, string what)
    {
        if (response.Status != HttpStatusCode.NotFound)
        {
            throw new Exception($"Expected 404 for {what}, but got {(int)response.Status} {response.Status}.");
        }

        if (response.Body.Length != 0)
        {
            throw new Exception($"Expected an empty body for {what}, but got '{response.Body}'.");
        }
    }

    /// <summary>Asserts two responses are identical in status and body (indistinguishable, R8.3/R12.3).</summary>
    private static void RequireIdentical(
        (HttpStatusCode Status, string Body) reference,
        (HttpStatusCode Status, string Body) candidate,
        string candidateWhat,
        string referenceWhat)
    {
        if (candidate.Status != reference.Status)
        {
            throw new Exception(
                $"{candidateWhat} returned status {(int)candidate.Status} but {referenceWhat} returned "
                + $"{(int)reference.Status}; the two must be indistinguishable.");
        }

        if (!string.Equals(candidate.Body, reference.Body, StringComparison.Ordinal))
        {
            throw new Exception(
                $"{candidateWhat} returned body '{candidate.Body}' but {referenceWhat} returned "
                + $"'{reference.Body}'; the two must be byte-identical so existence cannot be probed.");
        }
    }

    private static string UpdateBody(int key, string name) =>
        $"{{\"key\":{key},\"model\":{{\"name\":{JsonString(name)}}}}}";

    private static string DeleteBody(int key) => $"{{\"key\":{key}}}";

    /// <summary>Minimal JSON string escaping for the generated (small, controlled) name pool.</summary>
    private static string JsonString(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>The deterministic seed name for an out-of-scope row, used to prove no mutation occurred.</summary>
    private static string SeedName(int id) => $"seed-out-{id}";

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>The EF source entity the views project from; tenant-scoped, single-source, Id-keyed.</summary>
    private sealed class ScopedSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int TenantId { get; set; }
    }

    /// <summary>The projected (read) row type sent to clients for the writable view.</summary>
    private sealed class ScopedRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>The typed write contract for the writable view (whitelists only <c>Name</c>).</summary>
    private sealed class ScopedCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>The projected (read) row type for the read-only view.</summary>
    private sealed class ReadOnlyRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Minimal EF context backing both views.</summary>
    private sealed class ScopedContext : DbContext
    {
        public ScopedContext(DbContextOptions<ScopedContext> options)
            : base(options)
        {
        }

        public DbSet<ScopedSource> Sources => Set<ScopedSource>();
    }

    /// <summary>
    /// A writable class-per-view (Style B) definition over <see cref="ScopedSource"/> with an explicit
    /// primary key and a CRUD facet whitelisting only the scalar <c>Name</c>. No concurrency token is
    /// declared, so update/delete take the clean not-found path without a 428 precondition gate.
    /// </summary>
    private sealed class ScopedWritableView : View<ScopedRow, ScopedCrud>
    {
        protected override void Configure(IViewBuilder<ScopedRow, ScopedCrud> builder)
        {
            builder
                .Named(WritableView)
                .From<ScopedSource>(s => new ScopedRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<ScopedSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }

    /// <summary>
    /// A read-only class-per-view (Style B) definition over <see cref="ScopedSource"/>: it declares no
    /// CRUD facet, so <c>IsReadOnly</c> is <see langword="true"/> and the write routes are never mapped —
    /// a hand-crafted write against them must collapse to the same indistinguishable 404.
    /// </summary>
    private sealed class ScopedReadOnlyView : View<ReadOnlyRow>
    {
        protected override void Configure(IViewBuilder<ReadOnlyRow> builder)
        {
            builder
                .Named(ReadOnlyView)
                .From<ScopedSource>(s => new ReadOnlyRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());
        }
    }

    /// <summary>
    /// The one-door authorizer: allows every request and shapes each write with a server-trusted tenant
    /// filter. Rows under <see cref="OtherTenant"/> exist in the store yet are excluded pre-projection, so
    /// their keys are indistinguishable from genuinely absent keys.
    /// </summary>
    private sealed class TenantScopeAuthorizer : IViewAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(ViewAuthContext context) => ValueTask.FromResult(true);

        public void ShapeQuery(ViewAuthContext context, IViewScope scope) =>
            scope.AddRowFilter<ScopedSource>(s => s.TenantId == AuthorizedTenant);
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
        public static async Task<TestApp> StartAsync()
        {
            // A private, shared-cache in-memory database: every request-scoped DbContext opens its OWN
            // connection to the same store, which avoids the single-shared-connection "active statements"
            // reentrancy under the many sequential write requests this property drives. A keep-alive
            // connection stays open for the app's lifetime so the in-memory database is not dropped.
            var connectionString = $"DataSource=file:p5-{Guid.NewGuid():N}?mode=memory&cache=shared";
            var connection = new SqliteConnection(connectionString);
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<ScopedContext>(o => o.UseSqlite(connectionString));

                        // Style B Register<TView>() does not capture the context type, so Vista's executor
                        // resolves the base DbContext — forward it to the concrete context (the same
                        // pattern the read-path integration/property tests use).
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ScopedContext>());
                        services.AddVista(v =>
                        {
                            v.Register<ScopedWritableView>();
                            v.Register<ScopedReadOnlyView>();
                        });

                        // AllowAnonymousAccess keeps startup from failing closed (no authorizer type is
                        // recorded on the options); the real authorizer below still runs because the glue
                        // resolves IViewAuthorizer from the request services directly.
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
                        services.AddScoped<IViewAuthorizer>(_ => new TenantScopeAuthorizer());
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
                var ctx = scope.ServiceProvider.GetRequiredService<ScopedContext>();
                ctx.Database.EnsureCreated();

                // In-scope rows (authorized tenant) — never targeted, present so the scope filter is real.
                for (var id = 1; id <= 5; id++)
                {
                    ctx.Sources.Add(new ScopedSource { Id = id, Name = $"seed-in-{id}", TenantId = AuthorizedTenant });
                }

                // Out-of-scope rows (other tenant) — physically present but always excluded by the scope.
                for (var id = OutOfScopeMinId; id <= OutOfScopeMaxId; id++)
                {
                    ctx.Sources.Add(new ScopedSource { Id = id, Name = SeedName(id), TenantId = OtherTenant });
                }

                ctx.SaveChanges();
            }

            return new TestApp(host, connection, host.GetTestClient());
        }

        /// <summary>Posts a JSON body synchronously and returns the status and raw body text.</summary>
        public (HttpStatusCode Status, string Body) PostSync(string url, string json)
        {
            var response = Client.PostAsync(url, JsonContent(json)).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return (response.StatusCode, body);
        }

        /// <summary>Reads a source row's name by key from a fresh scope, or <see langword="null"/> if absent.</summary>
        public string? GetSourceName(int id)
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ScopedContext>();
            return ctx.Sources.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).SingleOrDefault();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            // Disposing the connection drops the in-memory database; do it last.
            _connection.Dispose();
        }
    }
}
