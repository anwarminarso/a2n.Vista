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
/// Endpoint-level property-based test for state preservation on rejected writes on the M12 write path
/// (write-path task 6.9; Decision Log D119, D120). This drives the <em>real</em> ASP.NET Core write
/// pipeline (<c>HandleWriteAsync</c> → the Core <see cref="IViewExecutor"/> write facet implemented by
/// the real <c>EfViewExecutor</c>) over an in-process <see cref="TestServer"/> backed by SQLite, so the
/// whole bind → authorize → scope → precondition → concurrency-guard path runs exactly as production
/// would — and, crucially, so a rejection short-circuits <em>before any</em> <c>SaveChanges</c>.
/// </summary>
/// <remarks>
/// Three writable Style B views over one shared source table exercise the full rejected-write branch set:
/// <list type="bullet">
/// <item>a plain (tokenless) view — <b>missing key</b> and <b>uncoercible key</b> on update/delete → 400
/// (Requirements R2.8, R9.2, R9.3);</item>
/// <item>a deny view — an <see cref="IViewAuthorizer"/> that denies that view's write facet → 403
/// (Requirement R7.2);</item>
/// <item>a token view — a <b>missing/blank <c>If-Match</c></b> → 428 (Requirement R6.2) and a
/// <b>mismatched <c>If-Match</c></b> → 409 (Requirement R6.3).</item>
/// </list>
/// Each iteration reseeds the shared table with a fresh, distinct-keyed row set, snapshots the whole
/// table, issues one rejected write against a present target, then re-reads the whole table and asserts
/// it is byte-identical to the pre-request snapshot (row count and every field of every row unchanged) —
/// i.e. the rejection persisted nothing (Requirements R1.6, R2.7, R7.2, R9.7). A single host + SQLite
/// connection is built once and the rows are reset per iteration; the sample runs single-threaded
/// (<c>threads: 1</c>) because the reused host/database is shared mutable state across cases. Style B
/// registration and the reflection write/endpoint path are RUC-annotated (mappings/keys resolved from
/// metadata at runtime); trimming is not used for tests, so IL2026 is suppressed at the class level,
/// matching the sibling write-path integration tests.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The test drives the runtime reflection write/endpoint path by design; trimming is not used for tests.")]
public sealed class RejectedWriteStatePreservationPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    private const string PlainView = "rejected-write-plain-view";
    private const string TokenView = "rejected-write-token-view";
    private const string DenyView = "rejected-write-deny-view";

    /// <summary>A seeded row: a distinct key plus the non-key payload and token fields checked for equality.</summary>
    private readonly record struct RowSeed(int Id, string Name, int Quantity, string Version);

    /// <summary>The distinct rejected-write branches this property spans (see the class remarks).</summary>
    private enum Branch
    {
        MissingKeyUpdate,
        MissingKeyDelete,
        InvalidKeyUpdate,
        InvalidKeyDelete,
        AuthDenyUpdate,
        AuthDenyDelete,
        Precondition428Update,
        Precondition428Delete,
        Mismatch409Update,
        Mismatch409Delete,
    }

    private static readonly Branch[] Branches = Enum.GetValues<Branch>();

    // Feature: write-path, Property 9: For any write request rejected before or during execution for a
    // non-persistence reason (validation failure, missing/invalid key, authorization denial, missing/blank
    // precondition, or concurrency-token mismatch), zero rows change and every target row remains
    // byte-identical to its pre-request state.
    //
    // Validates: Requirements 1.6, 2.7, 7.2, 9.7
    [Test]
    public void Rejected_Write_Preserves_Every_Persisted_Row_Byte_Identical()
    {
        using var harness = RejectionHarness.Start();

        // Distinct keys: dedupe by Id so each seeded key maps to exactly one row and seeding never
        // violates the primary key.
        var genRows =
            Gen.Select(Gen.Int[1, 1_000_000], Gen.Int[0, 5_000], Gen.Int[0, 10_000], Gen.Int[0, 9_999],
                    (id, nameSeed, qty, verSeed) => new RowSeed(id, "row-" + nameSeed, qty, "v" + verSeed))
                .Array[2, 8]
                .Select(static arr => arr
                    .GroupBy(static r => r.Id)
                    .Select(static g => g.First())
                    .ToArray());

        var genCase =
            from rows in genRows
            from branchSeed in Gen.Int[0, Branches.Length - 1]
            from pickSeed in Gen.Int[0, int.MaxValue]
            select (rows, branch: Branches[branchSeed], pick: pickSeed % rows.Length);

        genCase.Sample(
            input =>
            {
                var (rows, branch, pick) = input;

                // Fresh, known starting state for this case.
                harness.Reset(rows);
                var before = harness.SnapshotAll();

                var target = rows[pick];
                var (status, expected) = harness.ExecuteRejection(branch, target.Id, target.Version);

                // The branch must actually be rejected with its expected status — a 2xx here would mean the
                // rejection path was not exercised (and would very likely have mutated state).
                if (status != expected)
                {
                    throw new Exception(
                        $"Branch {branch} (target key {target.Id}) returned {(int)status} {status}, " +
                        $"expected {(int)expected} {expected}.");
                }

                var after = harness.SnapshotAll();

                // Zero rows change: the row count is preserved (R9.7).
                if (after.Count != before.Count)
                {
                    throw new Exception(
                        $"Row count changed after a rejected {branch}: {before.Count} → {after.Count} " +
                        $"(target key {target.Id}).");
                }

                // Every target row remains byte-identical to its pre-request state (R1.6, R2.7, R7.2, R9.7).
                foreach (var kvp in before)
                {
                    if (!after.TryGetValue(kvp.Key, out var now))
                    {
                        throw new Exception(
                            $"Row {kvp.Key} disappeared after a rejected {branch}; a rejection must persist nothing.");
                    }

                    if (!string.Equals(now.Name, kvp.Value.Name, StringComparison.Ordinal)
                        || now.Quantity != kvp.Value.Quantity
                        || !string.Equals(now.Version, kvp.Value.Version, StringComparison.Ordinal))
                    {
                        throw new Exception(
                            $"Row {kvp.Key} changed after a rejected {branch}: " +
                            $"(Name='{kvp.Value.Name}', Quantity={kvp.Value.Quantity}, Version='{kvp.Value.Version}') → " +
                            $"(Name='{now.Name}', Quantity={now.Quantity}, Version='{now.Version}').");
                    }
                }
            },
            iter: Iterations,
            threads: 1);
    }

    /// <summary>
    /// A started in-process host + its test client, owning the in-memory SQLite connection. Exposes helpers
    /// to reset the seeded rows, snapshot the whole table, and issue a rejected write for a given branch
    /// (some carry a raw <c>If-Match</c> header sent unvalidated so the executor sees the exact token text).
    /// </summary>
    private sealed class RejectionHarness : IDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;
        private readonly HttpClient _client;

        private RejectionHarness(IHost host, SqliteConnection connection, HttpClient client)
        {
            _host = host;
            _connection = connection;
            _client = client;
        }

        [RequiresUnreferencedCode("Vista endpoint mapping uses the reflection bridge by design.")]
        public static RejectionHarness Start()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<RejectContext>(o => o.UseSqlite(connection));

                        // Style B Register<TView>() does not capture the context type, so the Vista executor
                        // resolves the base DbContext — forward it to the concrete context (the same pattern
                        // the sibling write-path integration tests use). No fake executor is registered, so
                        // the REAL EfViewExecutor implements the write facet end to end.
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<RejectContext>());
                        services.AddVista(v =>
                        {
                            v.Register<RejectPlainView>();
                            v.Register<RejectTokenView>();
                            v.Register<RejectDenyView>();
                        });

                        // AllowAnonymousAccess keeps startup from failing closed (no UseAuthorizer<T>()), while
                        // the SelectiveAuthorizer registered below is still resolved and enforced per request:
                        // it denies only the deny view's write facet, so the 403 branch is exercised without
                        // blocking the other branches (R7.2).
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
                        services.AddSingleton<IViewAuthorizer>(new SelectiveAuthorizer());
                    })
                    .Configure(app =>
                    {
                        app.UseVistaExceptionHandling();
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapVistaViews());
                    }))
                .Start();

            using (var scope = host.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<RejectContext>().Database.EnsureCreated();
            }

            return new RejectionHarness(host, connection, host.GetTestClient());
        }

        /// <summary>Clears the shared table and reseeds it with exactly the given distinct-keyed rows.</summary>
        public void Reset(IReadOnlyCollection<RowSeed> rows)
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<RejectContext>();
            ctx.Sources.RemoveRange(ctx.Sources.ToList());
            ctx.Sources.AddRange(rows.Select(static r => new RejectSource
            {
                Id = r.Id,
                Name = r.Name,
                Quantity = r.Quantity,
                Version = r.Version,
            }));
            ctx.SaveChanges();
        }

        /// <summary>Reads the persisted rows straight from the database (no tracking), keyed by Id.</summary>
        public IReadOnlyDictionary<int, (string Name, int Quantity, string Version)> SnapshotAll()
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<RejectContext>();
            return ctx.Sources
                .AsNoTracking()
                .ToList()
                .ToDictionary(s => s.Id, s => (s.Name, s.Quantity, s.Version));
        }

        /// <summary>
        /// Issues the rejected write for <paramref name="branch"/> against the present key
        /// <paramref name="targetId"/> (whose current token is <paramref name="storedVersion"/>), and
        /// returns the HTTP status together with the status this branch is expected to yield.
        /// </summary>
        public (HttpStatusCode Status, HttpStatusCode Expected) ExecuteRejection(
            Branch branch,
            int targetId,
            string storedVersion)
        {
            // A token guaranteed to differ from the stored one (for the 409 mismatch branches).
            var wrongToken = storedVersion + "-mismatch";

            return branch switch
            {
                // Plain (tokenless) view: no 'key' in the body → missing-key 400 (R2.8, R9.2).
                Branch.MissingKeyUpdate =>
                    (Send($"/api/views/{PlainView}/update", "{\"model\":{\"name\":\"x\"}}"), HttpStatusCode.BadRequest),
                Branch.MissingKeyDelete =>
                    (Send($"/api/views/{PlainView}/delete", "{}"), HttpStatusCode.BadRequest),

                // Plain view: a non-coercible key for an int PK → key-type 400 (R9.3).
                Branch.InvalidKeyUpdate =>
                    (Send($"/api/views/{PlainView}/update", "{\"key\":\"not-an-int\",\"model\":{\"name\":\"x\"}}"), HttpStatusCode.BadRequest),
                Branch.InvalidKeyDelete =>
                    (Send($"/api/views/{PlainView}/delete", "{\"key\":\"not-an-int\"}"), HttpStatusCode.BadRequest),

                // Deny view: the authorizer denies the write facet → 403 (R7.2). A present, valid key is
                // supplied so the request reaches authorization (not a prior 400/404).
                Branch.AuthDenyUpdate =>
                    (Send($"/api/views/{DenyView}/update", $"{{\"key\":{targetId},\"model\":{{\"name\":\"x\"}}}}"), HttpStatusCode.Forbidden),
                Branch.AuthDenyDelete =>
                    (Send($"/api/views/{DenyView}/delete", $"{{\"key\":{targetId}}}"), HttpStatusCode.Forbidden),

                // Token view: a present key but NO If-Match → precondition-required 428 (R6.2).
                Branch.Precondition428Update =>
                    (Send($"/api/views/{TokenView}/update", $"{{\"key\":{targetId},\"model\":{{\"name\":\"x\"}}}}"), HttpStatusCode.PreconditionRequired),
                Branch.Precondition428Delete =>
                    (Send($"/api/views/{TokenView}/delete", $"{{\"key\":{targetId}}}"), HttpStatusCode.PreconditionRequired),

                // Token view: a present key with a mismatching If-Match → concurrency-conflict 409 (R6.3).
                Branch.Mismatch409Update =>
                    (Send($"/api/views/{TokenView}/update", $"{{\"key\":{targetId},\"model\":{{\"name\":\"x\"}}}}", wrongToken), HttpStatusCode.Conflict),
                Branch.Mismatch409Delete =>
                    (Send($"/api/views/{TokenView}/delete", $"{{\"key\":{targetId}}}", wrongToken), HttpStatusCode.Conflict),

                _ => throw new InvalidOperationException($"Unhandled branch {branch}."),
            };
        }

        /// <summary>Sends a write POST with an optional raw <c>If-Match</c> header; returns the status.</summary>
        private HttpStatusCode Send(string url, string json, string? ifMatch = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            if (ifMatch is not null)
            {
                // Send the token verbatim (it is not a quoted entity-tag), so the executor compares the exact
                // stored-token text against the exact supplied value.
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }

            using var response = _client.SendAsync(request).GetAwaiter().GetResult();
            return response.StatusCode;
        }

        public void Dispose()
        {
            _client.Dispose();
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
            // Disposing the connection drops the in-memory database; do it last.
            _connection.Dispose();
        }
    }

    /// <summary>
    /// An <see cref="IViewAuthorizer"/> that denies only the deny view's write facet and allows everything
    /// else. Its <see cref="ShapeQuery"/> is a no-op (empty scope → all rows eligible), so the 428/409/400
    /// branches on the other views are not affected by scope.
    /// </summary>
    private sealed class SelectiveAuthorizer : IViewAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(ViewAuthContext context) =>
            ValueTask.FromResult(!string.Equals(context.ViewName, DenyView, StringComparison.Ordinal));

        public void ShapeQuery(ViewAuthContext context, IViewScope scope)
        {
            // No server-trusted filters: every row is in scope for these rejection tests.
        }
    }

    /// <summary>EF source entity the three writable Style B views project from; Id-keyed with a string token.</summary>
    private sealed class RejectSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public string Version { get; set; } = string.Empty;
    }

    /// <summary>Projected (read) row type shared by the three views.</summary>
    private sealed class RejectRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public int Quantity { get; init; }

        public string Version { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract; only the non-key, non-token <see cref="Name"/> is writable (D25).</summary>
    private sealed class RejectCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Minimal EF context backing the three writable Style B views over one shared table.</summary>
    private sealed class RejectContext : DbContext
    {
        public RejectContext(DbContextOptions<RejectContext> options)
            : base(options)
        {
        }

        public DbSet<RejectSource> Sources => Set<RejectSource>();

        // A declared Vista token must also be a model concurrency token (D146).
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<RejectSource>().Property(e => e.Version).IsConcurrencyToken();
    }

    /// <summary>A tokenless writable view: exercises the missing/invalid-key 400 branches.</summary>
    private sealed class RejectPlainView : View<RejectRow, RejectCrud>
    {
        protected override void Configure(IViewBuilder<RejectRow, RejectCrud> builder)
        {
            builder
                .Named(PlainView)
                .From<RejectSource>(s => new RejectRow { Id = s.Id, Name = s.Name, Quantity = s.Quantity, Version = s.Version })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<RejectSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }

    /// <summary>A writable view with a string concurrency token: exercises the 428 and 409 branches.</summary>
    private sealed class RejectTokenView : View<RejectRow, RejectCrud>
    {
        protected override void Configure(IViewBuilder<RejectRow, RejectCrud> builder)
        {
            builder
                .Named(TokenView)
                .From<RejectSource>(s => new RejectRow { Id = s.Id, Name = s.Name, Quantity = s.Quantity, Version = s.Version })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<RejectSource>()
                .MapWritable(c => c.Name, e => e.Name)
                .WithConcurrencyToken(e => e.Version);
        }
    }

    /// <summary>A tokenless writable view the authorizer always denies: exercises the 403 branch.</summary>
    private sealed class RejectDenyView : View<RejectRow, RejectCrud>
    {
        protected override void Configure(IViewBuilder<RejectRow, RejectCrud> builder)
        {
            builder
                .Named(DenyView)
                .From<RejectSource>(s => new RejectRow { Id = s.Id, Name = s.Name, Quantity = s.Quantity, Version = s.Version })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<RejectSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }
}
