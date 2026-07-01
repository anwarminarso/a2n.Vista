// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.Authoring;
using a2n.Vista.Ports;
using a2n.Vista.Write;
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
/// Example-based endpoint tests for the M12 write path (write-path task 6.10; Decision Log D119, D120).
/// Every case drives the <em>real</em> ASP.NET Core write pipeline (<c>HandleWriteAsync</c> → the
/// AspNetCore glue → the real <c>EfViewExecutor</c> write facet) over an in-process
/// <see cref="TestServer"/> backed by a fresh, isolated SQLite database, so the whole
/// bind → authorize → scope → concurrency-guard → single-<c>SaveChanges</c> path runs exactly as
/// production would. These pin the concrete status codes and error <c>code</c> values the property tests
/// state as universal invariants.
/// </summary>
/// <remarks>
/// <para>
/// Three writable Style B views back the branches: a tokenless view (happy paths, no-net-change,
/// If-Match-is-harmless, malformed/missing/uncoercible inputs, array body), a concurrency-token view
/// (428 gate, correct-token round-trip), and a bulk-flagged view (<c>AllowBulk()</c> that still rejects
/// an array body in M12, R15.2). Each test starts its own host + in-memory SQLite connection, so state
/// never leaks between cases. The write path is RUC (reflection mapper + reflection endpoint bridge), so
/// IL2026 is suppressed at the class level — trimming is not used for tests.
/// </para>
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration test drives the reflection-based endpoint/executor write path by design; trimming is not used for tests.")]
public sealed class WriteEndpointExampleTests
{
    private const string PlainView = "we-plain";
    private const string TokenView = "we-token";
    private const string BulkView = "we-bulk";

    private const string PlainRoute = "/api/views/" + PlainView;
    private const string TokenRoute = "/api/views/" + TokenView;
    private const string BulkRoute = "/api/views/" + BulkView;

    // ---- Happy paths ---------------------------------------------------------------------------------

    /// <summary>R1.1/R1.2/R10.1: a create returns 200 with a body carrying ONLY the new primary key.</summary>
    [Test]
    public async Task Create_HappyPath_Returns_200_With_PrimaryKey_Only()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{PlainRoute}/create", Json("{\"model\":{\"name\":\"created\"}}"));
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        // Only the primary-key member is present, and it is the newly generated id.
        await Assert.That(root.TryGetProperty("key", out var keyElement)).IsTrue();
        await Assert.That(keyElement.GetInt32()).IsEqualTo(1);
        await Assert.That(root.EnumerateObject().Count()).IsEqualTo(1);

        // The row was actually inserted with the whitelisted field applied.
        await Assert.That(app.ReadPlainName(1)).IsEqualTo("created");
    }

    /// <summary>R2.1/R2.2: an update of an in-scope row applies the whitelisted field and returns 200.</summary>
    [Test]
    public async Task Update_HappyPath_Returns_200_And_Applies_Whitelisted_Field()
    {
        await using var app = await TestApp.StartAsync();
        app.SeedPlain(1, "before");

        var response = await app.Client.PostAsync($"{PlainRoute}/update", Json("{\"key\":1,\"model\":{\"name\":\"after\"}}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.ReadPlainName(1)).IsEqualTo("after");
    }

    /// <summary>R3.1/R3.2: a delete of an in-scope keyed row removes exactly that row and returns 200.</summary>
    [Test]
    public async Task Delete_HappyPath_Returns_200_And_Removes_The_Row()
    {
        await using var app = await TestApp.StartAsync();
        app.SeedPlain(1, "doomed");
        app.SeedPlain(2, "survivor");

        var response = await app.Client.PostAsync($"{PlainRoute}/delete", Json("{\"key\":1}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.ReadPlainName(1)).IsNull();
        await Assert.That(app.ReadPlainName(2)).IsEqualTo("survivor");
    }

    /// <summary>R2.2: an update that produces no net change to the whitelisted field still returns 200.</summary>
    [Test]
    public async Task Update_With_No_Net_Change_Returns_200()
    {
        await using var app = await TestApp.StartAsync();
        app.SeedPlain(1, "same");

        var response = await app.Client.PostAsync($"{PlainRoute}/update", Json("{\"key\":1,\"model\":{\"name\":\"same\"}}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.ReadPlainName(1)).IsEqualTo("same");
    }

    /// <summary>R16.6: a mapped write returns a response other than 501 — the write facet is implemented.</summary>
    [Test]
    public async Task Mapped_Write_Is_Not_501()
    {
        await using var app = await TestApp.StartAsync();

        var create = await app.Client.PostAsync($"{PlainRoute}/create", Json("{\"model\":{\"name\":\"x\"}}"));
        var update = await app.Client.PostAsync($"{PlainRoute}/update", Json("{\"key\":1,\"model\":{\"name\":\"y\"}}"));
        var delete = await app.Client.PostAsync($"{PlainRoute}/delete", Json("{\"key\":1}"));

        await Assert.That(create.StatusCode).IsNotEqualTo(HttpStatusCode.NotImplemented);
        await Assert.That(update.StatusCode).IsNotEqualTo(HttpStatusCode.NotImplemented);
        await Assert.That(delete.StatusCode).IsNotEqualTo(HttpStatusCode.NotImplemented);
    }

    // ---- Optimistic concurrency (token views) --------------------------------------------------------

    /// <summary>R6.2: a token view with a MISSING If-Match on update → 428 Precondition Required.</summary>
    [Test]
    public async Task TokenView_Update_Without_IfMatch_Returns_428()
    {
        await using var app = await TestApp.StartAsync();
        app.SeedToken(1, "orig", 7);

        var response = await app.Client.PostAsync($"{TokenRoute}/update", Json("{\"key\":1,\"model\":{\"name\":\"new\"}}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.PreconditionRequired);
        await Assert.That(await CodeOf(response)).IsEqualTo(WriteErrorCodes.PreconditionRequired);
        // No change persisted.
        await Assert.That(app.ReadTokenName(1)).IsEqualTo("orig");
    }

    /// <summary>R6.2: a token view with a BLANK (whitespace) If-Match on delete → 428.</summary>
    [Test]
    public async Task TokenView_Delete_With_Blank_IfMatch_Returns_428()
    {
        await using var app = await TestApp.StartAsync();
        app.SeedToken(1, "orig", 7);

        var response = app.SendWithIfMatch($"{TokenRoute}/delete", "{\"key\":1}", "   ");

        await Assert.That(response.Status).IsEqualTo(HttpStatusCode.PreconditionRequired);
        // Row still present.
        await Assert.That(app.ReadTokenName(1)).IsEqualTo("orig");
    }

    /// <summary>
    /// R6.1/R6.4: a non-empty If-Match matching the stored token is used as the expected token — the
    /// update succeeds (200) and the ETag round-trips the token.
    /// </summary>
    [Test]
    public async Task TokenView_Update_With_Correct_IfMatch_Returns_200_And_RoundTrips_ETag()
    {
        await using var app = await TestApp.StartAsync();
        app.SeedToken(1, "orig", 42);

        var response = app.SendWithIfMatch($"{TokenRoute}/update", "{\"key\":1,\"model\":{\"name\":\"changed\"}}", "42");

        await Assert.That(response.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.ETag).IsEqualTo("42");
        await Assert.That(app.ReadTokenName(1)).IsEqualTo("changed");
    }

    /// <summary>R6.6: a tokenless view ignores a present If-Match (harmless) — the update still returns 200.</summary>
    [Test]
    public async Task TokenlessView_Ignores_Present_IfMatch_Returns_200()
    {
        await using var app = await TestApp.StartAsync();
        app.SeedPlain(1, "before");

        var response = app.SendWithIfMatch($"{PlainRoute}/update", "{\"key\":1,\"model\":{\"name\":\"after\"}}", "any-token-value");

        await Assert.That(response.Status).IsEqualTo(HttpStatusCode.OK);
        // No ETag is emitted for a tokenless view.
        await Assert.That(response.ETag).IsNull();
        await Assert.That(app.ReadPlainName(1)).IsEqualTo("after");
    }

    // ---- Bulk (not enabled in M12) -------------------------------------------------------------------

    /// <summary>R15.1: an array body on a plain writable view → 400 write-bulk-not-enabled; nothing created.</summary>
    [Test]
    public async Task Array_Body_Returns_400_BulkNotEnabled()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{PlainRoute}/create", Json("[{\"name\":\"a\"},{\"name\":\"b\"}]"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await CodeOf(response)).IsEqualTo(WriteErrorCodes.BulkNotEnabled);
        await Assert.That(app.PlainRowCount()).IsEqualTo(0);
    }

    /// <summary>
    /// R15.2: <c>AllowBulk()</c> is a build-time opt-in that enables NO execution path in M12 — an array
    /// body against a bulk-flagged view still returns 400 write-bulk-not-enabled and creates nothing.
    /// </summary>
    [Test]
    public async Task AllowBulk_View_Still_Returns_400_BulkNotEnabled_For_Array_Body()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{BulkRoute}/create", Json("[{\"name\":\"a\"},{\"name\":\"b\"}]"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await CodeOf(response)).IsEqualTo(WriteErrorCodes.BulkNotEnabled);
        await Assert.That(app.BulkRowCount()).IsEqualTo(0);
    }

    // ---- Client-error inputs (correct 400 `code`) ----------------------------------------------------

    /// <summary>R2.8/R5.5/R9.2: an update with no primary key → 400 write-missing-key; nothing changes.</summary>
    [Test]
    public async Task Update_Without_Key_Returns_400_MissingKey()
    {
        await using var app = await TestApp.StartAsync();
        app.SeedPlain(1, "before");

        var response = await app.Client.PostAsync($"{PlainRoute}/update", Json("{\"model\":{\"name\":\"after\"}}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await CodeOf(response)).IsEqualTo(WriteErrorCodes.MissingKey);
        await Assert.That(app.ReadPlainName(1)).IsEqualTo("before");
    }

    /// <summary>R9.3: a key value that cannot be coerced to the int key type → 400 write-key-type.</summary>
    [Test]
    public async Task Delete_With_Uncoercible_Key_Returns_400_KeyType()
    {
        await using var app = await TestApp.StartAsync();
        app.SeedPlain(1, "before");

        var response = await app.Client.PostAsync($"{PlainRoute}/delete", Json("{\"key\":\"not-a-number\"}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await CodeOf(response)).IsEqualTo(WriteErrorCodes.KeyTypeCoercion);
        await Assert.That(app.ReadPlainName(1)).IsEqualTo("before");
    }

    /// <summary>R9.1: an invalid/non-object JSON body → 400 write-malformed-body.</summary>
    [Test]
    public async Task Create_With_Malformed_Body_Returns_400_MalformedBody()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{PlainRoute}/create", Json("{ this is not json"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await CodeOf(response)).IsEqualTo(WriteErrorCodes.MalformedBody);
        await Assert.That(app.PlainRowCount()).IsEqualTo(0);
    }

    // ---- Helpers -------------------------------------------------------------------------------------

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>Reads the RFC 7807 <c>code</c> extension member from a problem response body.</summary>
    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    // ---- Fixtures ------------------------------------------------------------------------------------

    private sealed class PlainSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class TokenSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Version { get; set; }
    }

    private sealed class BulkSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class PlainRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    private sealed class TokenRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public int Version { get; init; }
    }

    private sealed class BulkRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract; only the non-key, non-token <c>Name</c> is writable (D25).</summary>
    private sealed class NameCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class ExampleWriteContext : DbContext
    {
        public ExampleWriteContext(DbContextOptions<ExampleWriteContext> options)
            : base(options)
        {
        }

        public DbSet<PlainSource> Plains => Set<PlainSource>();

        public DbSet<TokenSource> Tokens => Set<TokenSource>();

        public DbSet<BulkSource> Bulks => Set<BulkSource>();
    }

    /// <summary>A tokenless single-source writable Style B view whitelisting only the scalar <c>Name</c>.</summary>
    private sealed class PlainWritableView : View<PlainRow, NameCrud>
    {
        protected override void Configure(IViewBuilder<PlainRow, NameCrud> builder)
        {
            builder
                .Named(PlainView)
                .From<PlainSource>(s => new PlainRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<PlainSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }

    /// <summary>A writable Style B view declaring an int optimistic-concurrency token via <c>WithConcurrencyToken</c>.</summary>
    private sealed class TokenWritableView : View<TokenRow, NameCrud>
    {
        protected override void Configure(IViewBuilder<TokenRow, NameCrud> builder)
        {
            builder
                .Named(TokenView)
                .From<TokenSource>(s => new TokenRow { Id = s.Id, Name = s.Name, Version = s.Version })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<TokenSource>()
                .MapWritable(c => c.Name, e => e.Name)
                .WithConcurrencyToken(e => e.Version);
        }
    }

    /// <summary>A writable Style B view with <c>AllowBulk()</c> set — the flag must enable no path in M12 (R15.2).</summary>
    private sealed class BulkWritableView : View<BulkRow, NameCrud>
    {
        protected override void Configure(IViewBuilder<BulkRow, NameCrud> builder)
        {
            builder
                .Named(BulkView)
                .From<BulkSource>(s => new BulkRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<BulkSource>()
                .MapWritable(c => c.Name, e => e.Name)
                .AllowBulk();
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
                        services.AddDbContext<ExampleWriteContext>(o => o.UseSqlite(connection));

                        // Style B Register<TView>() does not capture the context type, so Vista's real EF
                        // executor resolves the base DbContext — forward it to the concrete context (the
                        // same pattern the sibling write-path integration/property tests use). No fake
                        // executor is registered: the real write facet runs end to end.
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ExampleWriteContext>());
                        services.AddVista(v =>
                        {
                            v.Register<PlainWritableView>();
                            v.Register<TokenWritableView>();
                            v.Register<BulkWritableView>();
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
                scope.ServiceProvider.GetRequiredService<ExampleWriteContext>().Database.EnsureCreated();
            }

            return new TestApp(host, connection, host.GetTestClient());
        }

        public void SeedPlain(int id, string name) => Mutate(ctx => ctx.Plains.Add(new PlainSource { Id = id, Name = name }));

        public void SeedToken(int id, string name, int version) =>
            Mutate(ctx => ctx.Tokens.Add(new TokenSource { Id = id, Name = name, Version = version }));

        public string? ReadPlainName(int id) => Read(ctx => ctx.Plains.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).SingleOrDefault());

        public string? ReadTokenName(int id) => Read(ctx => ctx.Tokens.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).SingleOrDefault());

        public int PlainRowCount() => Read(ctx => ctx.Plains.AsNoTracking().Count());

        public int BulkRowCount() => Read(ctx => ctx.Bulks.AsNoTracking().Count());

        /// <summary>Posts a body with a raw (unvalidated) <c>If-Match</c> header; returns the status + ETag.</summary>
        public (HttpStatusCode Status, string? ETag) SendWithIfMatch(string url, string json, string ifMatch)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            // Send the token verbatim (an int's invariant text is not a quoted entity-tag), so the executor
            // compares the exact stored-token text against the exact supplied value.
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

            var response = Client.SendAsync(request).GetAwaiter().GetResult();

            string? etag = null;
            if (response.Headers.TryGetValues("ETag", out var values))
            {
                etag = values.FirstOrDefault();
            }

            var status = response.StatusCode;
            response.Dispose();
            return (status, etag);
        }

        private void Mutate(Action<ExampleWriteContext> mutate)
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ExampleWriteContext>();
            mutate(ctx);
            ctx.SaveChanges();
        }

        private T Read<T>(Func<ExampleWriteContext, T> read)
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ExampleWriteContext>();
            return read(ctx);
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
