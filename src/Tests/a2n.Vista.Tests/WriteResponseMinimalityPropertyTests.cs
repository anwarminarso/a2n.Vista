// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using a2n.Vista.Authoring;
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
/// Endpoint-level property-based test for write-response minimality on the M12 write path (write-path
/// task 6.7; Decision Log D119, D120). It drives the <em>real</em> ASP.NET Core create pipeline
/// (<c>HandleWriteAsync</c> → the Core <see cref="a2n.Vista.Ports.IViewExecutor"/> write facet implemented
/// by the real <c>EfViewExecutor</c>) over an in-process <see cref="TestServer"/> backed by SQLite, so
/// the whole bind → authorize → scope → single-<c>SaveChanges</c> → PK read-back path runs exactly as
/// production would.
/// </summary>
/// <remarks>
/// The writable Style B view projects <em>only</em> <c>Id</c> and <c>Name</c> (the read projection),
/// while the backing entity carries extra columns absent from that projection: a whitelisted, non-projected
/// <c>Secret</c> and <c>Quantity</c> (set from the write model), and a non-whitelisted, non-projected
/// <c>InternalNote</c> that every created row carries as a fixed sentinel. Each iteration posts a create
/// with freshly generated values, so the persisted row genuinely holds a distinctive secret that
/// <em>could</em> leak — the property asserts it never does: a successful create returns exactly the
/// primary key and nothing else.
/// <para>
/// A single host + SQLite connection is built once and reused; create is append-only, so no per-iteration
/// reset is needed. The sample runs single-threaded (<c>threads: 1</c>) because the reused SQLite
/// connection is shared across iterations. Style B registration and the reflection write path are
/// RUC-annotated (mappings/keys are resolved from metadata at runtime); trimming is not used for tests, so
/// IL2026 is suppressed at the class level, matching the sibling write-path integration tests.
/// </para>
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The test drives the runtime reflection write/endpoint path by design; trimming is not used for tests.")]
public sealed class WriteResponseMinimalityPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    private const string ViewName = "write-response-minimality-view";
    private const string Route = "/api/views/" + ViewName;

    /// <summary>
    /// The value every created row's non-projected, non-whitelisted <c>InternalNote</c> carries. It is
    /// distinctive on purpose: if the endpoint ever serialized the whole entity (or any non-projected
    /// field), this sentinel would surface in the response body and the property would fail.
    /// </summary>
    private const string InternalNoteSentinel = "INTERNAL-NOTE-DO-NOT-LEAK";

    // Feature: write-path, Property 7: For any successful write, the response body exposes only the
    // affected row's primary-key value(s) and no other field value; it never contains a masked field
    // value, the raw target entity, or any entity field absent from the view's read projection. Any field
    // value present beyond the primary key is derived solely from the view's read projection with masking
    // applied.
    //
    // Validates: Requirements 1.2, 10.1, 10.2, 10.3, 10.4
    [Test]
    public void Create_Response_Exposes_Only_The_Primary_Key_And_Never_A_Non_Projected_Field()
    {
        using var harness = MinimalityHarness.Start();

        // nameSeed/secretSeed/quantity vary the whitelisted model values every iteration so that a leak of
        // any of them (or of the non-projected InternalNote sentinel) would be caught. The strings are
        // prefixed and distinctive, so a substring probe over the response body is a reliable leak detector.
        var genCase =
            from nameSeed in Gen.Int[0, 1_000_000]
            from secretSeed in Gen.Int[0, 1_000_000]
            from quantity in Gen.Int[0, 1_000_000]
            select (nameSeed, secretSeed, quantity);

        genCase.Sample(
            input =>
            {
                var (nameSeed, secretSeed, quantity) = input;

                var name = "NAME-" + nameSeed.ToString(CultureInfo.InvariantCulture);
                var secret = "SECRET-" + secretSeed.ToString(CultureInfo.InvariantCulture);

                var (status, body) = harness.Create(name, secret, quantity);

                // R1.2 / R16.6: a successful, mapped create returns 200 (not 501, not an error).
                if (status != HttpStatusCode.OK)
                {
                    throw new Exception(
                        $"Expected 200 for a valid create, got {(int)status} {status}. Body: {body}");
                }

                // R1.2 / R10.1: the body is exactly the PK shape — a single scalar primary key, nothing
                // else. Parse it and assert there is precisely one property, named "key".
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new Exception($"Create response was not a JSON object. Body: {body}");
                }

                var properties = root.EnumerateObject().ToArray();
                if (properties.Length != 1)
                {
                    throw new Exception(
                        $"Create response exposed {properties.Length} properties; a minimal PK-only body has " +
                        $"exactly one. Body: {body}");
                }

                var only = properties[0];
                if (!string.Equals(only.Name, "key", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        $"Create response's only property was '{only.Name}', expected the primary-key property " +
                        $"'key'. Body: {body}");
                }

                if (only.Value.ValueKind != JsonValueKind.Number || !only.Value.TryGetInt32(out var pk) || pk <= 0)
                {
                    throw new Exception(
                        $"Create response key was not a positive store-assigned integer. Body: {body}");
                }

                // R10.2 / R10.3 / R10.4: no other field value or name appears — not the whitelisted-but-
                // non-projected Secret/Quantity the client just sent, not the projected Name, and not the
                // non-whitelisted, non-projected InternalNote sentinel the row carries.
                AssertBodyDoesNotContain(body, secret, "the generated secret value");
                AssertBodyDoesNotContain(body, name, "the generated name value");
                AssertBodyDoesNotContain(body, InternalNoteSentinel, "the non-projected InternalNote sentinel");
                AssertBodyDoesNotContain(body, "secret", "the 'secret' field name");
                AssertBodyDoesNotContain(body, "internalNote", "the 'internalNote' field name");
                AssertBodyDoesNotContain(body, "quantity", "the 'quantity' field name");
                AssertBodyDoesNotContain(body, "name", "the 'name' field name");

                // The write really happened and the non-projected fields really hold distinctive values in
                // storage (so they genuinely COULD have leaked): confirm the persisted row, by the returned
                // key, carries the generated secret/name/quantity and the InternalNote sentinel.
                var row = harness.ReadRow(pk)
                    ?? throw new Exception($"No row was persisted for the returned key {pk}. Body: {body}");

                if (!string.Equals(row.Name, name, StringComparison.Ordinal) ||
                    !string.Equals(row.Secret, secret, StringComparison.Ordinal) ||
                    row.Quantity != quantity ||
                    !string.Equals(row.InternalNote, InternalNoteSentinel, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Persisted row {pk} did not match the created values: " +
                        $"(Name='{row.Name}', Secret='{row.Secret}', Quantity={row.Quantity}, " +
                        $"InternalNote='{row.InternalNote}').");
                }
            },
            iter: Iterations,
            threads: 1);
    }

    private static void AssertBodyDoesNotContain(string body, string needle, string description)
    {
        if (body.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception(
                $"Create response leaked {description} ('{needle}'); a minimal write response exposes only " +
                $"the primary key. Body: {body}");
        }
    }

    /// <summary>
    /// A started in-process host + its test client, owning the in-memory SQLite connection. Exposes a
    /// create helper (returns the raw status + body) and a no-tracking read of a persisted row by key.
    /// No fake executor is registered, so the REAL <c>EfViewExecutor</c> implements the write facet end to
    /// end.
    /// </summary>
    private sealed class MinimalityHarness : IDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;
        private readonly HttpClient _client;

        private MinimalityHarness(IHost host, SqliteConnection connection, HttpClient client)
        {
            _host = host;
            _connection = connection;
            _client = client;
        }

        [RequiresUnreferencedCode("Vista endpoint mapping uses the reflection bridge by design.")]
        public static MinimalityHarness Start()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<MinimalityContext>(o => o.UseSqlite(connection));

                        // Style B Register<TView>() does not capture the context type, so the Vista executor
                        // resolves the base DbContext — forward it to the concrete context (the same pattern
                        // the sibling write-path integration tests use). No fake executor is registered, so
                        // the REAL EfViewExecutor implements the write facet end to end.
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MinimalityContext>());
                        services.AddVista(v => v.Register<MinimalityView>());
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
                scope.ServiceProvider.GetRequiredService<MinimalityContext>().Database.EnsureCreated();
            }

            return new MinimalityHarness(host, connection, host.GetTestClient());
        }

        /// <summary>Issues <c>POST {route}/create</c> with the whitelisted model; returns status + body.</summary>
        public (HttpStatusCode Status, string Body) Create(string name, string secret, int quantity)
        {
            var json =
                $"{{\"model\":{{\"name\":\"{name}\",\"secret\":\"{secret}\"," +
                $"\"quantity\":{quantity.ToString(CultureInfo.InvariantCulture)}}}}}";

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Route}/create")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            var response = _client.SendAsync(request).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var status = response.StatusCode;
            response.Dispose();
            return (status, body);
        }

        /// <summary>Reads the row by key straight from the database (no tracking), or null when absent.</summary>
        public (string Name, string Secret, int Quantity, string InternalNote)? ReadRow(int id)
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MinimalityContext>();
            var row = ctx.Sources.AsNoTracking().SingleOrDefault(s => s.Id == id);
            return row is null ? null : (row.Name, row.Secret, row.Quantity, row.InternalNote);
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
    /// EF source entity the writable Style B view projects from. Beyond the projected <c>Id</c>/<c>Name</c>
    /// it carries columns absent from the read projection: a whitelisted <c>Secret</c> and <c>Quantity</c>
    /// (set from the write model) and a non-whitelisted <c>InternalNote</c> that defaults to a fixed
    /// sentinel on every created row.
    /// </summary>
    private sealed class MinimalitySource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Secret { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public string InternalNote { get; set; } = InternalNoteSentinel;
    }

    /// <summary>Projected (read) row type — deliberately narrower than the entity: only Id and Name.</summary>
    private sealed class MinimalityRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract whitelisting a couple of scalars (Name, Secret, Quantity).</summary>
    private sealed class MinimalityCrud
    {
        public string Name { get; init; } = string.Empty;

        public string Secret { get; init; } = string.Empty;

        public int Quantity { get; init; }
    }

    /// <summary>Minimal EF context backing the writable Style B view.</summary>
    private sealed class MinimalityContext : DbContext
    {
        public MinimalityContext(DbContextOptions<MinimalityContext> options)
            : base(options)
        {
        }

        public DbSet<MinimalitySource> Sources => Set<MinimalitySource>();
    }

    /// <summary>
    /// A writable class-per-view (Style B) definition over <see cref="MinimalitySource"/>. The read
    /// projection exposes only <c>Id</c> and <c>Name</c>; the write whitelist maps <c>Name</c>,
    /// <c>Secret</c>, and <c>Quantity</c> — the latter two are non-projected columns, so a created row
    /// holds values that must never appear in the PK-only create response. The typed write facet makes the
    /// view non-read-only, so the create route is mapped and reaches the real executor.
    /// </summary>
    private sealed class MinimalityView : View<MinimalityRow, MinimalityCrud>
    {
        protected override void Configure(IViewBuilder<MinimalityRow, MinimalityCrud> builder)
        {
            builder
                .Named(ViewName)
                .From<MinimalitySource>(s => new MinimalityRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<MinimalitySource>()
                .MapWritable(c => c.Name, e => e.Name)
                .MapWritable(c => c.Secret, e => e.Secret)
                .MapWritable(c => c.Quantity, e => e.Quantity);
        }
    }
}
