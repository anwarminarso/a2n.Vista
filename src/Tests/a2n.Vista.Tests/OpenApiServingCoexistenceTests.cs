// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Serving-integration examples for the opt-in Vista OpenAPI endpoint that fill the coverage <em>gaps</em>
/// left by <c>OpenApiServingTests</c> (spec openapi-emitter, task 7.3; Decision Log D128). Where task 7.1
/// asserts the endpoint exists, serves <c>application/json</c>, and reflects title/version/path, these
/// cases go further:
/// <list type="bullet">
/// <item>R11.1 — the served document is <em>structurally complete</em>: exact <c>application/json</c>
/// content type, a parseable body declaring OpenAPI 3.x with a populated <c>info</c>, and
/// <c>components.schemas</c> carrying the fixed envelopes (<c>VistaListRequestBody</c>,
/// <c>ProblemDetails</c>, <c>FilterNode</c>).</item>
/// <item>R11.2 — a custom <see cref="a2n.Vista.OpenApi.VistaSecurityScheme"/> is applied end to end: it
/// appears under <c>components.securitySchemes</c> and is referenced by every operation, overriding the
/// default bearer scheme (the gap 7.1 did not cover).</item>
/// <item>R11.3 — the endpoint honors host authorization: with <c>.RequireAuthorization()</c> an
/// unauthenticated request is rejected (401) and an authenticated one succeeds (200), proving the endpoint
/// does not bypass the host auth pipeline.</item>
/// <item>R10.1/R10.3 — coexistence: an existing view endpoint returns a byte-for-byte identical response
/// whether or not the emitter is registered (the emitter is additive-only).</item>
/// </list>
/// Each case drives the <em>real</em> ASP.NET Core pipeline over an in-process <see cref="TestServer"/> with
/// the production wiring, so the served document is built by the metadata-driven builder over the live
/// registry and the live serialization seam. The Vista endpoint mapping and the OpenAPI build are RUC
/// (reflection), so IL2026 is suppressed at the class level — trimming is not used for tests.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Test drives the reflection-based endpoint mapping and OpenAPI builder by design; trimming is not used for tests.")]
public sealed class OpenApiServingCoexistenceTests
{
    private const string ViewName = "coexistenceWidgets";

    // ---- R11.1: the served document is structurally complete (envelopes present) ------------------

    [Test]
    public async Task Served_Document_Is_Structurally_Complete_With_Fixed_Envelopes()
    {
        await using var app = await TestApp.StartAsync();

        using var response = await app.Client.GetAsync("/openapi/v1.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // The content type is exactly application/json (Results.Text sets it verbatim, no charset).
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/json");

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // Declares a supported OpenAPI 3.x version.
        var openapi = root.GetProperty("openapi").GetString();
        await Assert.That(openapi).IsNotNull();
        await Assert.That(openapi!.StartsWith("3.", StringComparison.Ordinal)).IsTrue();

        // A populated info object with a non-empty title and version (R8.1).
        var info = root.GetProperty("info");
        await Assert.That(info.GetProperty("title").GetString()).IsNotNullOrEmpty();
        await Assert.That(info.GetProperty("version").GetString()).IsNotNullOrEmpty();

        // components.schemas carries the fixed, hand-authored envelope/FilterNode/ProblemDetails schemas.
        var schemas = root.GetProperty("components").GetProperty("schemas");
        await Assert.That(schemas.TryGetProperty("VistaListRequestBody", out _)).IsTrue();
        await Assert.That(schemas.TryGetProperty("ProblemDetails", out _)).IsTrue();
        await Assert.That(schemas.TryGetProperty("FilterNode", out _)).IsTrue();
    }

    // ---- R11.2: a custom security scheme is applied end to end ------------------------------------

    [Test]
    public async Task Custom_Security_Scheme_Is_Applied_And_Referenced_By_Operations()
    {
        // A non-anonymous app (Development lets it start with no authorizer, allow-all + warning) so the
        // emitter attaches the one-door security requirement and emits the configured scheme (R7.1/R7.2).
        await using var app = await TestApp.StartAsync(
            configureOpenApi: o =>
            {
                o.DocumentTitle = "Secured Widget API";
                o.DocumentVersion = "4.5.6";
                o.EndpointPath = "/api/openapi.json";
                o.Security = new a2n.Vista.OpenApi.VistaSecurityScheme("vistaAuth", "http", "bearer", "JWT");

                // The subject here is the emitted document, not the endpoint's posture: this host configures
                // no authorization middleware, so the secure-by-default RequireAuthorization() is opted out
                // of explicitly. The default itself is covered by the two Document_Endpoint_* cases below.
                o.RequireAuthorization = false;
            },
            anonymous: false);

        // Served at the custom path...
        using var custom = await app.Client.GetAsync("/api/openapi.json");
        await Assert.That(custom.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // ...and not at the default one.
        using var missing = await app.Client.GetAsync("/openapi/v1.json");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        var body = await custom.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // Title + version options flowed through.
        var info = root.GetProperty("info");
        await Assert.That(info.GetProperty("title").GetString()).IsEqualTo("Secured Widget API");
        await Assert.That(info.GetProperty("version").GetString()).IsEqualTo("4.5.6");

        // The configured scheme is emitted under components.securitySchemes, keyed by its name, and the
        // default "bearer" key is NOT present (the custom scheme overrides it).
        var securitySchemes = root.GetProperty("components").GetProperty("securitySchemes");
        await Assert.That(securitySchemes.TryGetProperty("vistaAuth", out var scheme)).IsTrue();
        await Assert.That(scheme.GetProperty("type").GetString()).IsEqualTo("http");
        await Assert.That(scheme.GetProperty("scheme").GetString()).IsEqualTo("bearer");
        await Assert.That(scheme.GetProperty("bearerFormat").GetString()).IsEqualTo("JWT");
        await Assert.That(securitySchemes.TryGetProperty("bearer", out _)).IsFalse();

        // Every operation references that same scheme: check the view's list operation carries it.
        var listPath = app.RouteOf(ViewName) + "/list";
        var listSecurity = root.GetProperty("paths").GetProperty(listPath)
            .GetProperty("post").GetProperty("security");
        await Assert.That(listSecurity.GetArrayLength()).IsGreaterThan(0);
        await Assert.That(listSecurity[0].TryGetProperty("vistaAuth", out _)).IsTrue();
    }

    // ---- R11.3: the endpoint honors host authentication/authorization ----------------------------

    [Test]
    public async Task Endpoint_Honors_Host_Authorization_Unauthenticated_Is_Rejected()
    {
        await using var app = await TestApp.StartAsync(protectOpenApi: true);

        // No credentials -> the RequireAuthorization()-protected endpoint challenges (401); it did NOT
        // bypass the host auth pipeline (R11.3).
        using var response = await app.Client.GetAsync("/openapi/v1.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Endpoint_Honors_Host_Authorization_Authenticated_Succeeds()
    {
        await using var app = await TestApp.StartAsync(protectOpenApi: true);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        request.Headers.Add(TestAuthHandler.UserHeader, "alice");

        // With a valid authenticated principal, the same endpoint serves the document (200).
        using var response = await app.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/json");
    }

    // ---- Secure by default: the mapped document endpoint requires authorization unless opted out -----

    [Test]
    public async Task Document_Endpoint_Requires_Authorization_By_Default()
    {
        // The host wires authentication/authorization but attaches NO convention of its own: the default
        // must still refuse an unauthenticated caller. Before the default existed, an endpoint with no
        // authorization metadata served the document (route set, writability, and DTO schemas) anonymously.
        await using var app = await TestApp.StartAsync(anonymous: false, withAuthPipeline: true);

        using var anonymousRequest = await app.Client.GetAsync("/openapi/v1.json");
        await Assert.That(anonymousRequest.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        using var authenticated = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        authenticated.Headers.Add(TestAuthHandler.UserHeader, "alice");
        using var allowed = await app.Client.SendAsync(authenticated);
        await Assert.That(allowed.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Document_Endpoint_Is_Anonymous_When_Explicitly_Opted_Out()
    {
        await using var app = await TestApp.StartAsync(
            configureOpenApi: o => o.RequireAuthorization = false,
            anonymous: false,
            withAuthPipeline: true);

        // The reviewable opt-out publishes the document without a credential while the views stay authorized.
        using var response = await app.Client.GetAsync("/openapi/v1.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Document_Endpoint_Stays_Open_Under_The_D94_Anonymous_Opt_In()
    {
        // AllowAnonymousAccess() is the reviewed open posture: the views are public, there may be no
        // authentication scheme at all, so the document endpoint must not demand one.
        await using var app = await TestApp.StartAsync(anonymous: true);

        using var response = await app.Client.GetAsync("/openapi/v1.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // ---- R10.1 / R10.3: coexistence — existing endpoints unchanged with the emitter registered ----

    [Test]
    public async Task Existing_Metadata_Endpoint_Is_Byte_For_Byte_Identical_With_Or_Without_Emitter()
    {
        await using var withEmitter = await TestApp.StartAsync(registerOpenApi: true);
        await using var withoutEmitter = await TestApp.StartAsync(registerOpenApi: false);

        using var responseWith = await withEmitter.Client.GetAsync(withEmitter.RouteOf(ViewName) + "/metadata");
        using var responseWithout = await withoutEmitter.Client.GetAsync(withoutEmitter.RouteOf(ViewName) + "/metadata");

        // Same status and same body: the emitter added nothing to the existing view endpoint (R10.1).
        await Assert.That(responseWith.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(responseWithout.StatusCode).IsEqualTo(responseWith.StatusCode);

        var bodyWith = await responseWith.Content.ReadAsStringAsync();
        var bodyWithout = await responseWithout.Content.ReadAsStringAsync();
        await Assert.That(bodyWith).IsEqualTo(bodyWithout);
    }

    [Test]
    public async Task Off_By_Default_Existing_View_Endpoint_Works_Without_Any_Serve_Endpoint()
    {
        await using var app = await TestApp.StartAsync(registerOpenApi: false);

        // No serve endpoint exists when the emitter is not registered (R10.3)...
        using var missing = await app.Client.GetAsync("/openapi/v1.json");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        // ...and the existing view endpoint still serves normally (the app behaves as before the feature).
        using var metadata = await app.Client.GetAsync(app.RouteOf(ViewName) + "/metadata");
        await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(metadata.Content.Headers.ContentType!.MediaType).IsEqualTo("application/json");
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

    /// <summary>
    /// A minimal test authentication handler: it authenticates any request carrying a non-empty
    /// <see cref="UserHeader"/> and otherwise returns <see cref="AuthenticateResult.NoResult"/> so an
    /// unauthenticated request is challenged (401) by <c>RequireAuthorization()</c>.
    /// </summary>
    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";
        public const string UserHeader = "X-Test-User";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrEmpty(user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, user.ToString()) },
                SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class TestApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private TestApp(IHost host, SqliteConnection connection, HttpClient client) =>
            (_host, _connection, Client) = (host, connection, client);

        public HttpClient Client { get; }

        [RequiresUnreferencedCode("Vista endpoint mapping and the OpenAPI builder use reflection by design.")]
        public static async Task<TestApp> StartAsync(
            Action<a2n.Vista.OpenApi.VistaOpenApiOptions>? configureOpenApi = null,
            bool registerOpenApi = true,
            bool anonymous = true,
            bool protectOpenApi = false,
            bool withAuthPipeline = false)
        {
            // `protectOpenApi` attaches .RequireAuthorization() explicitly (the pre-existing R11.3 cases);
            // `withAuthPipeline` only wires authentication/authorization so the secure-by-default posture can
            // be observed without any host-attached convention.
            var authPipeline = protectOpenApi || withAuthPipeline;
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();

                    // A non-anonymous app has no authorizer here; Development keeps startup from failing
                    // closed (allow-all + a single warning, D94) so the emitter still emits the security
                    // requirement.
                    if (!anonymous)
                    {
                        web.UseEnvironment("Development");
                    }

                    web
                        .ConfigureServices(services =>
                        {
                            services.AddRouting();
                            services.AddDbContext<WidgetContext>(o => o.UseSqlite(connection));
                            services.AddScoped<DbContext>(sp => sp.GetRequiredService<WidgetContext>());
                            services.AddVista(v => v.Register<WidgetView>());

                            if (anonymous)
                            {
                                services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
                            }
                            else
                            {
                                services.AddVistaEndpoints();
                            }

                            if (authPipeline)
                            {
                                services
                                    .AddAuthentication(TestAuthHandler.SchemeName)
                                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                                        TestAuthHandler.SchemeName, _ => { });
                                services.AddAuthorization();
                            }

                            if (registerOpenApi)
                            {
                                services.AddVistaOpenApi(configureOpenApi);
                            }
                        })
                        .Configure(app =>
                        {
                            app.UseVistaExceptionHandling();
                            app.UseRouting();

                            if (authPipeline)
                            {
                                app.UseAuthentication();
                                app.UseAuthorization();
                            }

                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapVistaViews();
                                if (registerOpenApi)
                                {
                                    var openApi = endpoints.MapVistaOpenApi();
                                    if (protectOpenApi)
                                    {
                                        openApi.RequireAuthorization();
                                    }
                                }
                            });
                        });
                })
                .StartAsync();

            using (var scope = host.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<WidgetContext>().Database.EnsureCreated();
            }

            return new TestApp(host, connection, host.GetTestClient());
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
