// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using a2n.Vista.Authoring;
using CsCheck;
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
/// Endpoint-level property-based test for optimistic concurrency on the M12 write path (write-path task
/// 6.6; Decision Log D119, D120). This drives the <em>real</em> ASP.NET Core write pipeline
/// (<c>HandleWriteAsync</c> → the Core <see cref="a2n.Vista.Ports.IViewExecutor"/> write facet implemented
/// by the real <c>EfViewExecutor</c>) over an in-process <see cref="TestServer"/> backed by SQLite, so
/// the whole bind → authorize → scope → concurrency-guard → single-<c>SaveChanges</c> path runs exactly
/// as production would.
/// </summary>
/// <remarks>
/// The writable Style B view declares an explicit primary key, a single <c>MapWritable</c> whitelist, and
/// an <c>int</c> optimistic-concurrency token via <c>WithConcurrencyToken(e =&gt; e.Version)</c>. An
/// <c>int</c> token is rendered deterministically by the executor's <c>FormatToken</c> (invariant-culture
/// text), so the wire token equals the value seeded into the row. The token is <em>not</em> in the
/// writable whitelist and is not auto-bumped by the provider, so a successful update leaves the row's
/// current token unchanged: the round-tripped <c>ETag</c> therefore equals the row's current token value
/// after the write.
/// <para>
/// A single host + SQLite connection is built once and the single seeded row is reset per iteration
/// (fresh version, name reset), which is far cheaper than rebuilding the pipeline 100+ times while still
/// giving each case an independent, known starting state. Style B registration and the reflection write
/// path are RUC-annotated (mappings/keys are resolved from metadata at runtime); trimming is not used for
/// tests, so IL2026 is suppressed at the class level, matching the sibling write-path integration tests.
/// </para>
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The test drives the runtime reflection write/endpoint path by design; trimming is not used for tests.")]
public sealed class ConcurrencyAbortPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    private const string ViewName = "concurrency-abort-view";
    private const string Route = "/api/views/" + ViewName;

    /// <summary>The constant primary key of the single seeded row every iteration resets and targets.</summary>
    private const int RowKey = 1;

    // Feature: write-path, Property 6: For any view that declares a concurrency token: when the supplied
    // If-Match value does not exactly equal the stored row's current token, the update/delete aborts with
    // HTTP 409 and the row is unchanged; when the operation succeeds, the ETag response header equals the
    // row's current token value after the write.
    //
    // Validates: Requirements 6.3, 6.4
    [Test]
    public void Concurrency_Mismatch_Aborts_Unchanged_And_Success_Round_Trips_The_Token()
    {
        using var harness = ConcurrencyHarness.Start();

        // version    : the seeded row's concurrency token for this case.
        // shouldMatch : true → supply the correct If-Match (success); false → supply a wrong one (409).
        // isDelete    : exercise the delete facet as well as update (both enforce the token).
        // delta       : a non-zero offset guaranteeing a distinct mismatching token when shouldMatch=false.
        var genCase =
            from version in Gen.Int[0, 1_000_000]
            from shouldMatch in Gen.Bool
            from isDelete in Gen.Bool
            from delta in Gen.Int[1, 1_000]
            select (version, shouldMatch, isDelete, delta);

        genCase.Sample(
            input =>
            {
                var (version, shouldMatch, isDelete, delta) = input;

                harness.SeedSingleRow(RowKey, "orig", version);

                var storedToken = version.ToString(CultureInfo.InvariantCulture);
                var ifMatch = shouldMatch
                    ? storedToken
                    : (version + delta).ToString(CultureInfo.InvariantCulture);

                var (status, etag) = isDelete
                    ? harness.Delete(RowKey, ifMatch)
                    : harness.Update(RowKey, "updated", ifMatch);

                if (shouldMatch)
                {
                    // Success path (R6.4): 200 and the ETag round-trips the token.
                    if (status != HttpStatusCode.OK)
                    {
                        throw new Exception(
                            $"Expected 200 for a matching If-Match on {(isDelete ? "delete" : "update")} " +
                            $"(token '{storedToken}'), got {(int)status} {status}.");
                    }

                    if (isDelete)
                    {
                        // The keyed row is gone, and the ETag equals the token the row carried.
                        if (harness.RowExists(RowKey))
                        {
                            throw new Exception($"Row {RowKey} still present after a successful delete.");
                        }

                        if (!string.Equals(etag, storedToken, StringComparison.Ordinal))
                        {
                            throw new Exception(
                                $"Delete ETag '{etag}' did not equal the deleted row's token '{storedToken}'.");
                        }
                    }
                    else
                    {
                        // The row is updated; its current token is unchanged (the token is not writable and
                        // is not auto-bumped), so the ETag equals the row's current token AFTER the write.
                        var row = harness.ReadRow(RowKey)
                            ?? throw new Exception($"Row {RowKey} vanished after a successful update.");

                        if (!string.Equals(row.Name, "updated", StringComparison.Ordinal))
                        {
                            throw new Exception(
                                $"Update did not apply the whitelisted field: Name='{row.Name}', expected 'updated'.");
                        }

                        var currentToken = row.Version.ToString(CultureInfo.InvariantCulture);
                        if (!string.Equals(etag, currentToken, StringComparison.Ordinal))
                        {
                            throw new Exception(
                                $"Update ETag '{etag}' did not equal the row's current token '{currentToken}' after the write.");
                        }
                    }
                }
                else
                {
                    // Mismatch path (R6.3): 409 and the row is unchanged (no persisted effect).
                    if (status != HttpStatusCode.Conflict)
                    {
                        throw new Exception(
                            $"Expected 409 for a mismatching If-Match ('{ifMatch}' vs stored '{storedToken}') on " +
                            $"{(isDelete ? "delete" : "update")}, got {(int)status} {status}.");
                    }

                    var row = harness.ReadRow(RowKey)
                        ?? throw new Exception(
                            $"Row {RowKey} was removed by a {(isDelete ? "delete" : "update")} that should have aborted with 409.");

                    if (!string.Equals(row.Name, "orig", StringComparison.Ordinal) || row.Version != version)
                    {
                        throw new Exception(
                            $"Row changed after a 409-aborted write: (Name='{row.Name}', Version={row.Version}), " +
                            $"expected (Name='orig', Version={version}).");
                    }
                }
            },
            iter: Iterations,
            // The harness (host + single SQLite row) is shared and reset per case, so cases must run
            // sequentially: CsCheck samples in parallel by default, which would let concurrent seeds/reads
            // cross-contaminate the one shared row.
            threads: 1);
    }

    /// <summary>
    /// A started in-process host + its test client, owning the in-memory SQLite connection. Exposes helpers
    /// to reset the single seeded row and to issue write requests carrying a raw <c>If-Match</c> header
    /// (sent unvalidated so the executor sees the exact token text, matching the wire contract).
    /// </summary>
    private sealed class ConcurrencyHarness : IDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;
        private readonly HttpClient _client;

        private ConcurrencyHarness(IHost host, SqliteConnection connection, HttpClient client)
        {
            _host = host;
            _connection = connection;
            _client = client;
        }

        [RequiresUnreferencedCode("Vista endpoint mapping uses the reflection bridge by design.")]
        public static ConcurrencyHarness Start()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<ConcurrencyContext>(o => o.UseSqlite(connection));

                        // Style B Register<TView>() does not capture the context type, so the Vista executor
                        // resolves the base DbContext — forward it to the concrete context (the same pattern
                        // the sibling write-path integration tests use). No fake executor is registered, so
                        // the REAL EfViewExecutor implements the write facet end to end.
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ConcurrencyContext>());
                        services.AddVista(v => v.Register<ConcurrencyView>());
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
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
                scope.ServiceProvider.GetRequiredService<ConcurrencyContext>().Database.EnsureCreated();
            }

            return new ConcurrencyHarness(host, connection, host.GetTestClient());
        }

        /// <summary>Resets the table to a single row with the given key, name, and concurrency token.</summary>
        public void SeedSingleRow(int id, string name, int version)
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyContext>();
            ctx.Sources.RemoveRange(ctx.Sources.ToList());
            ctx.Sources.Add(new ConcurrencySource { Id = id, Name = name, Version = version });
            ctx.SaveChanges();
        }

        /// <summary>Issues <c>POST {route}/update</c> with a raw <c>If-Match</c>; returns status + ETag.</summary>
        public (HttpStatusCode Status, string? ETag) Update(int key, string name, string ifMatch)
        {
            var json = $"{{\"key\":{key},\"model\":{{\"name\":\"{name}\"}}}}";
            return Send($"{Route}/update", json, ifMatch);
        }

        /// <summary>Issues <c>POST {route}/delete</c> with a raw <c>If-Match</c>; returns status + ETag.</summary>
        public (HttpStatusCode Status, string? ETag) Delete(int key, string ifMatch)
        {
            var json = $"{{\"key\":{key}}}";
            return Send($"{Route}/delete", json, ifMatch);
        }

        /// <summary>Reads the row by key straight from the database (no tracking), or null when absent.</summary>
        public (string Name, int Version)? ReadRow(int id)
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyContext>();
            var row = ctx.Sources.AsNoTracking().SingleOrDefault(s => s.Id == id);
            return row is null ? null : (row.Name, row.Version);
        }

        /// <summary>True when a row with the given key is still persisted.</summary>
        public bool RowExists(int id) => ReadRow(id) is not null;

        private (HttpStatusCode Status, string? ETag) Send(string url, string json, string ifMatch)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            // Send the token verbatim (an int's invariant text is not a quoted entity-tag), so the executor
            // compares the exact stored-token text against the exact supplied value.
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

            var response = _client.SendAsync(request).GetAwaiter().GetResult();

            string? etag = null;
            if (response.Headers.TryGetValues("ETag", out var values))
            {
                etag = values.FirstOrDefault();
            }

            var status = response.StatusCode;
            response.Dispose();
            return (status, etag);
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

    /// <summary>EF source entity the writable Style B view projects from; Id-keyed with an int token.</summary>
    private sealed class ConcurrencySource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Version { get; set; }
    }

    /// <summary>Projected (read) row type for the view.</summary>
    private sealed class ConcurrencyRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public int Version { get; init; }
    }

    /// <summary>Typed write contract; only the non-key, non-token <see cref="Name"/> is writable (D25).</summary>
    private sealed class ConcurrencyCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Minimal EF context backing the writable Style B view.</summary>
    private sealed class ConcurrencyContext : DbContext
    {
        public ConcurrencyContext(DbContextOptions<ConcurrencyContext> options)
            : base(options)
        {
        }

        public DbSet<ConcurrencySource> Sources => Set<ConcurrencySource>();
    }

    /// <summary>
    /// A writable class-per-view (Style B) definition over <see cref="ConcurrencySource"/> with an explicit
    /// primary key, a single non-key writable field, and an <c>int</c> optimistic-concurrency token. The
    /// typed write facet makes it non-read-only, so create/update/delete routes are mapped; the token
    /// drives the 428 gate, the 409 mismatch abort, and the ETag round-trip.
    /// </summary>
    private sealed class ConcurrencyView : View<ConcurrencyRow, ConcurrencyCrud>
    {
        protected override void Configure(IViewBuilder<ConcurrencyRow, ConcurrencyCrud> builder)
        {
            builder
                .Named(ViewName)
                .From<ConcurrencySource>(s => new ConcurrencyRow { Id = s.Id, Name = s.Name, Version = s.Version })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<ConcurrencySource>()
                .MapWritable(c => c.Name, e => e.Name)
                .WithConcurrencyToken(e => e.Version);
        }
    }
}
