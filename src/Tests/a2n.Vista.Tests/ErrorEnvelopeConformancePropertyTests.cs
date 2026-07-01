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
using a2n.Vista.Authoring;
using a2n.Vista.Write;
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
/// Endpoint-level property-based test for the M12 write path's error contract (write-path task 6.8;
/// Decision Log D120). It drives the <em>real</em> ASP.NET Core write pipeline
/// (<c>HandleWriteAsync</c> → <see cref="a2n.Vista.AspNetCore"/> glue → the real EF write executor) over
/// an in-process <see cref="TestServer"/> with a SQLite-backed <see cref="DbContext"/>, so every rejected
/// write is shaped by <c>VistaProblemResults</c> exactly as production would shape it.
/// </summary>
/// <remarks>
/// Two writable Style B views back the branches: a tokenless view (malformed body, bulk-not-enabled,
/// missing key, uncoercible key) and a concurrency-token view (precondition-required, concurrency
/// conflict). A single seeded row on the token view lets the concurrency pre-check run; every generated
/// case is a <em>rejection</em>, so no iteration mutates persisted state and the host/client are safely
/// reused across all iterations. The write path is RUC (reflection mapper + reflection endpoint bridge),
/// so IL2026 is suppressed at the class level — trimming is not used for tests.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration test drives the reflection-based endpoint/executor write path by design; trimming is not used for tests.")]
public sealed class ErrorEnvelopeConformancePropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 120;

    private const string PlainView = "err-env-plain";
    private const string TokenView = "err-env-token";
    private const string PlainRoute = "/api/views/" + PlainView;
    private const string TokenRoute = "/api/views/" + TokenView;

    /// <summary>The stored token on the seeded token-view row; every generated If-Match differs from it.</summary>
    private const string SeededToken = "seed-token-1";

    /// <summary>The non-projected, server-only entity value that must never surface in any error body.</summary>
    private const string SecretSentinel = "top-secret-internal-value";

    /// <summary>The full write-error wire vocabulary a rejected write may carry (design "Error Handling").</summary>
    private static readonly HashSet<string> WriteVocabulary = new(StringComparer.Ordinal)
    {
        WriteErrorCodes.MalformedBody,
        WriteErrorCodes.MissingKey,
        WriteErrorCodes.KeyTypeCoercion,
        WriteErrorCodes.IncompleteKey,
        WriteErrorCodes.ValidationFailed,
        WriteErrorCodes.PreconditionRequired,
        WriteErrorCodes.ConcurrencyConflict,
        WriteErrorCodes.WriteConflict,
        WriteErrorCodes.BulkNotEnabled,
    };

    /// <summary>Malformed JSON bodies (invalid JSON or non-object roots) that must classify as malformed-body.</summary>
    private static readonly string[] MalformedBodies =
    {
        "",
        "{",
        "not json at all",
        "{\"model\":",
        "]",
        "{ \"model\": {",
        "@@@",
        "{\"model\":{\"name\":\"x\"}",
        "\"just-a-string\"",
        "12345",
        "true",
    };

    /// <summary>Key strings that cannot be coerced to the integer key type (uncoercible-key branch).</summary>
    private static readonly string[] NonNumericKeys =
    {
        "abc",
        "xyz",
        "not-a-number",
        "1.2.3",
        "",
        "  ",
        "0x1F",
        "one",
    };

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    /// <summary>The error branch a generated case exercises. Each maps to one write-error wire code.</summary>
    private enum Branch
    {
        MalformedBody,
        BulkNotEnabled,
        MissingKey,
        KeyType,
        PreconditionRequired,
        ConcurrencyConflict,
    }

    /// <summary>A generated rejected-write request plus its expected status and wire code.</summary>
    private readonly record struct Case(
        string Route,
        string Action,
        string Body,
        string? IfMatch,
        HttpStatusCode ExpectedStatus,
        string ExpectedCode);

    // Feature: write-path, Property 8: For any rejected write (malformed body, missing/incomplete/
    // uncoercible key, validation failure, precondition-required, concurrency conflict, write conflict,
    // bulk-not-enabled), the error response uses the same RFC 7807 problem-details envelope and
    // machine-readable error-code vocabulary as the read endpoints, carries a code drawn from that
    // vocabulary, and its body contains no stack trace, exception type name, SQL text, schema or database
    // object name, connection string, masked field value, or any entity field absent from the read
    // projection.
    //
    // Validates: Requirements 9.5, 9.6, 10.5
    [Test]
    public async Task Every_Rejected_Write_Conforms_To_The_Shared_Envelope_And_Leaks_No_Internals()
    {
        await using var app = await TestApp.StartAsync();

        var genCase =
            from branch in Gen.Int[0, 5]
            from useUpdate in Gen.Bool
            from malformed in Pick(MalformedBodies)
            from nonNumeric in Pick(NonNumericKeys)
            from mismatchSeed in Gen.Int[0, 1_000_000]
            select BuildCase((Branch)branch, useUpdate, malformed, nonNumeric, "mismatch-" + mismatchSeed);

        genCase.Sample(
            testCase =>
            {
                var response = app.Send(testCase);
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                // The status matches the branch's contract (adds strength beyond the envelope check).
                if (response.StatusCode != testCase.ExpectedStatus)
                {
                    throw new Exception(
                        $"Branch expected HTTP {(int)testCase.ExpectedStatus} but got {(int)response.StatusCode} " +
                        $"for {testCase.Action} {testCase.Route}. Body: {body}");
                }

                // R9.5: every error rides the shared RFC 7807 envelope (application/problem+json), the
                // same media type the read endpoints use for FilterValidationException.
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!string.Equals(mediaType, "application/problem+json", StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Error response Content-Type was '{mediaType}', expected 'application/problem+json'. Body: {body}");
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // R9.5: a machine-readable `code` extension is present and drawn from the write vocabulary,
                // and it is exactly the code this branch must classify to.
                if (!root.TryGetProperty("code", out var codeElement) ||
                    codeElement.ValueKind != JsonValueKind.String)
                {
                    throw new Exception($"Error body carried no string `code` member. Body: {body}");
                }

                var code = codeElement.GetString()!;
                if (!WriteVocabulary.Contains(code))
                {
                    throw new Exception($"Error `code` '{code}' is not in the shared write vocabulary. Body: {body}");
                }

                if (!string.Equals(code, testCase.ExpectedCode, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Branch expected `code` '{testCase.ExpectedCode}' but got '{code}'. Body: {body}");
                }

                // The stable problem `type` URN pairs with the code (urn:a2n.vista:error:{code}).
                if (!root.TryGetProperty("type", out var typeElement) ||
                    typeElement.GetString() != "urn:a2n.vista:error:" + code)
                {
                    throw new Exception(
                        $"Problem `type` did not match 'urn:a2n.vista:error:{code}'. Body: {body}");
                }

                // R9.6/R10.5: the body leaks no internals — no stack trace, exception type name, SQL text,
                // schema/db-object name, connection string, or non-projected entity field value.
                foreach (var marker in ForbiddenMarkers)
                {
                    if (body.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception(
                            $"Error body leaked the forbidden marker '{marker}'. Body: {body}");
                    }

                }
            },
            iter: Iterations,
            // A single shared TestServer + one in-memory SQLite connection backs every iteration; CsCheck
            // samples in parallel by default, and concurrent requests over one SQLite connection race
            // ("active statements" reentrancy) and can surface a transient status. Pin to one thread —
            // matching the sibling endpoint property tests — so each rejected write is issued sequentially.
            threads: 1);
    }

    /// <summary>
    /// Substrings that must never appear in an error body: stack-trace frames, exception type-name
    /// conventions, SQL/provider text, connection-string fragments, the concrete DbContext/entity/table
    /// names, the internal concurrency-token member name, and the server-only non-projected sentinel.
    /// Matched case-insensitively so casing tricks cannot slip a leak through.
    /// </summary>
    private static readonly string[] ForbiddenMarkers =
    {
        "Exception",        // exception type names (e.g. DbUpdateException, JsonException)
        "   at ",           // stack-trace frame marker
        "SQLite",           // provider text
        "SELECT ",          // SQL text
        "Data Source",      // connection-string fragment (spaced form)
        "DataSource=",      // connection-string fragment (SqliteConnection form)
        "DbContext",        // EF infrastructure type name
        "ErrorEnvContext",  // the concrete DbContext type name
        "PlainSource",      // a backing entity/table name (absent from the read projection)
        "TokenSource",      // a backing entity/table name (absent from the read projection)
        "RowVersion",       // the internal concurrency-token member name
        SecretSentinel,     // a non-projected, server-only field value
    };

    /// <summary>Builds a rejected-write <see cref="Case"/> for the chosen branch from generated inputs.</summary>
    private static Case BuildCase(Branch branch, bool useUpdate, string malformed, string nonNumericKey, string mismatch)
    {
        // For key-bearing branches, delete/update both carry the key; create never needs a key.
        var keyAction = useUpdate ? "update" : "delete";

        return branch switch
        {
            // R9.1: invalid JSON or a non-object root → malformed-body (400). create/update both bind first.
            Branch.MalformedBody => new Case(
                PlainRoute,
                useUpdate ? "update" : "create",
                malformed,
                IfMatch: null,
                HttpStatusCode.BadRequest,
                WriteErrorCodes.MalformedBody),

            // R15.1: a JSON array body (or array model) is a bulk batch, not enabled → bulk-not-enabled (400).
            Branch.BulkNotEnabled => new Case(
                PlainRoute,
                useUpdate ? "update" : "create",
                useUpdate ? "{\"model\":[{\"name\":\"x\"}]}" : "[{\"name\":\"x\"}]",
                IfMatch: null,
                HttpStatusCode.BadRequest,
                WriteErrorCodes.BulkNotEnabled),

            // R2.8/R5.5/R9.2: update/delete without a key → missing-key (400).
            Branch.MissingKey => new Case(
                PlainRoute,
                keyAction,
                useUpdate ? "{\"model\":{\"name\":\"x\"}}" : "{}",
                IfMatch: null,
                HttpStatusCode.BadRequest,
                WriteErrorCodes.MissingKey),

            // R9.3: a key value that cannot be coerced to the integer key type → key-type (400).
            Branch.KeyType => new Case(
                PlainRoute,
                keyAction,
                useUpdate
                    ? $"{{\"key\":{JsonString(nonNumericKey)},\"model\":{{\"name\":\"x\"}}}}"
                    : $"{{\"key\":{JsonString(nonNumericKey)}}}",
                IfMatch: null,
                HttpStatusCode.BadRequest,
                WriteErrorCodes.KeyTypeCoercion),

            // R6.2: a token view with a missing/blank If-Match → precondition-required (428), before the executor.
            Branch.PreconditionRequired => new Case(
                TokenRoute,
                keyAction,
                useUpdate ? "{\"key\":1,\"model\":{\"name\":\"x\"}}" : "{\"key\":1}",
                IfMatch: null,
                HttpStatusCode.PreconditionRequired,
                WriteErrorCodes.PreconditionRequired),

            // R6.3: a token view whose If-Match differs from the stored token → concurrency conflict (409).
            Branch.ConcurrencyConflict => new Case(
                TokenRoute,
                keyAction,
                useUpdate ? "{\"key\":1,\"model\":{\"name\":\"x\"}}" : "{\"key\":1}",
                IfMatch: mismatch,
                HttpStatusCode.Conflict,
                WriteErrorCodes.ConcurrencyConflict),

            _ => throw new ArgumentOutOfRangeException(nameof(branch), branch, "Unknown branch."),
        };
    }

    /// <summary>Renders <paramref name="value"/> as a JSON string literal (quoted, escaped).</summary>
    private static string JsonString(string value) => JsonSerializer.Serialize(value);

    // ---- Fixtures ------------------------------------------------------------------------------------

    /// <summary>Tokenless backing entity: a projected key + name, plus a non-projected server-only secret.</summary>
    private sealed class PlainSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Secret { get; set; } = string.Empty;
    }

    /// <summary>Token backing entity: adds a concurrency-token member (absent from the read projection).</summary>
    private sealed class TokenSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string RowVersion { get; set; } = string.Empty;

        public string Secret { get; set; } = string.Empty;
    }

    /// <summary>Projected (read) row for the tokenless view — exposes only Id and Name.</summary>
    private sealed class PlainRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Projected (read) row for the token view — exposes only Id and Name.</summary>
    private sealed class TokenRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract for both views (one whitelisted scalar closes mass assignment).</summary>
    private sealed class NameCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Single EF context backing both writable Style B views.</summary>
    private sealed class ErrorEnvContext : DbContext
    {
        public ErrorEnvContext(DbContextOptions<ErrorEnvContext> options)
            : base(options)
        {
        }

        public DbSet<PlainSource> Plains => Set<PlainSource>();

        public DbSet<TokenSource> Tokens => Set<TokenSource>();
    }

    /// <summary>A single-source writable Style B view with an explicit key and one whitelisted scalar; no token.</summary>
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

    /// <summary>A writable Style B view declaring an optimistic-concurrency token via <c>WithConcurrencyToken</c>.</summary>
    private sealed class TokenWritableView : View<TokenRow, NameCrud>
    {
        protected override void Configure(IViewBuilder<TokenRow, NameCrud> builder)
        {
            builder
                .Named(TokenView)
                .From<TokenSource>(s => new TokenRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<TokenSource>()
                .MapWritable(c => c.Name, e => e.Name)
                .WithConcurrencyToken(e => e.RowVersion);
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
                        services.AddDbContext<ErrorEnvContext>(o => o.UseSqlite(connection));

                        // Style B Register<TView>() does not capture the context type, so Vista's real EF
                        // executor resolves the base DbContext — forward it to the concrete context (the
                        // same pattern the read-path integration/property tests use). No fake executor is
                        // registered: the real write facet shapes every rejection.
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ErrorEnvContext>());
                        services.AddVista(v =>
                        {
                            v.Register<PlainWritableView>();
                            v.Register<TokenWritableView>();
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
                var ctx = scope.ServiceProvider.GetRequiredService<ErrorEnvContext>();
                ctx.Database.EnsureCreated();

                // One token-view row so the concurrency pre-check has a target to compare against. Its
                // secret + token are server-only (absent from the read projection) and must never leak.
                ctx.Tokens.Add(new TokenSource
                {
                    Id = 1,
                    Name = "existing",
                    RowVersion = SeededToken,
                    Secret = SecretSentinel,
                });
                ctx.Plains.Add(new PlainSource { Id = 1, Name = "plain", Secret = SecretSentinel });
                ctx.SaveChanges();
            }

            return new TestApp(host, connection, host.GetTestClient());
        }

        /// <summary>Sends one generated rejected-write request synchronously (each case is independent).</summary>
        public HttpResponseMessage Send(Case testCase)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{testCase.Route}/{testCase.Action}")
            {
                Content = new StringContent(testCase.Body, Encoding.UTF8, "application/json"),
            };

            if (testCase.IfMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", testCase.IfMatch);
            }

            return Client.SendAsync(request).GetAwaiter().GetResult();
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
