using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text.Json;
using a2n.Vista.Examples.AgGridNorthwind.Views;
using a2n.Vista.Metadata;
using a2n.Vista.OpenApi;
using a2n.Vista.Ports;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Northwind.DataAccess;

namespace a2n.Vista.Examples.AgGridNorthwind;

/// <summary>
/// End-to-end verification harness for the opt-in Vista OpenAPI emitter (Decision Log D127/D128; spec
/// openapi-emitter, task 11.3). It stands up an in-process ASP.NET Core test host over the <b>same</b> Vista
/// wiring the real Northwind host uses, then drives the live <c>Serve_Endpoint</c> exactly as a browser or
/// codegen tool would, asserting that:
/// <list type="bullet">
///   <item><description><c>GET /openapi/v1.json</c> returns <c>200 application/json</c> whose body is a
///   well-formed OpenAPI 3.x document — a declared 3.x version, a populated <c>info</c> (title + version),
///   at least one path, and every <c>$ref</c> resolving to a schema under <c>components.schemas</c>
///   (Requirements 8.1, 8.2, 11.1).</description></item>
///   <item><description>the document's operation set — every <c>(method, path)</c> pair — equals the
///   registered views' live <c>View_Operation_Set</c> computed straight from <see cref="IViewRegistry"/>
///   and the fixed facet table (endpoint parity: no phantom operations, none missing —
///   Requirements 1.1–1.4, 2.1, 2.2).</description></item>
///   <item><description>adding the emitter changes no existing endpoint: a representative existing endpoint
///   (<c>GET {route}/metadata</c>) returns byte-for-byte identical bytes with and without the emitter
///   registered (Requirement 10.1).</description></item>
/// </list>
/// Run it with <c>dotnet run -- selftest</c>.
/// </summary>
/// <remarks>
/// The <see cref="IViewRegistry"/> is the endpoint-parity oracle: the expected operation set is derived from
/// the live registry, not hard-coded, so the assertion tracks whatever views the host registers. The
/// non-regression comparison uses the metadata facet because it is served purely from
/// <see cref="ViewMetadata"/> and never touches the database, so the check is robust regardless of the
/// backing store.
/// </remarks>
public static class OpenApiSelfTest
{
    private const string ServeEndpointPath = "/openapi/v1.json";
    private const string RegressionViewName = "vProductCategory";

    /// <summary>
    /// Runs the OpenAPI self-test against two in-process test hosts (one with the emitter, one without),
    /// both mirroring the real Northwind Vista wiring over the shipped read-only database.
    /// </summary>
    /// <param name="dbRelativePath">The relative path to the Northwind SQLite database (the host's source of truth).</param>
    /// <returns><see langword="true"/> when every check passed; otherwise <see langword="false"/>.</returns>
    [RequiresUnreferencedCode("Mirrors the reflection-based Vista wiring (RegisterTemplate/MapVistaViews) and the RUC OpenAPI emitter.")]
    public static async Task<bool> RunAsync(string dbRelativePath)
    {
        ArgumentNullException.ThrowIfNull(dbRelativePath);

        Console.WriteLine();
        Console.WriteLine("=== Vista Northwind OpenAPI self-test ===");

        await using var withEmitter = BuildApp(dbRelativePath, withOpenApi: true);
        await using var withoutEmitter = BuildApp(dbRelativePath, withOpenApi: false);

        await withEmitter.StartAsync().ConfigureAwait(false);
        await withoutEmitter.StartAsync().ConfigureAwait(false);

        using var withClient = withEmitter.GetTestClient();
        using var withoutClient = withoutEmitter.GetTestClient();

        var registry = withEmitter.Services.GetRequiredService<IViewRegistry>();
        Console.WriteLine($"Serve     : GET {ServeEndpointPath}");
        Console.WriteLine($"Views     : {string.Join(", ", registry.All.Select(v => $"{v.Name}{(v.IsReadOnly ? "" : " (writable)")}"))}");
        Console.WriteLine();

        var allPassed = true;

        // [1] Serve + validity: GET /openapi/v1.json -> 200 application/json, well-formed OpenAPI 3.x doc.
        var (servedOk, documentJson) = await ServeAndValidateAsync(withClient).ConfigureAwait(false);
        allPassed &= servedOk;

        // [2] Endpoint parity: the document's (method, path) set equals the live View_Operation_Set.
        if (documentJson is not null)
        {
            using var document = JsonDocument.Parse(documentJson);
            allPassed &= EndpointParityCheck(registry, document.RootElement);
        }
        else
        {
            allPassed = false;
        }

        // [3] Additive coexistence: an existing endpoint's response is unchanged by the emitter.
        allPassed &= await NoRegressionCheckAsync(registry, withClient, withoutClient).ConfigureAwait(false);

        await withEmitter.StopAsync().ConfigureAwait(false);
        await withoutEmitter.StopAsync().ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"OPENAPI SELF-TEST RESULT: {(allPassed ? "PASS" : "FAIL")}");
        return allPassed;
    }

    /// <summary>
    /// Builds a started-able in-process test host mirroring the real Northwind Vista registration (the Gaya
    /// A central template plus the writable Style B view), optionally opting into the OpenAPI emitter.
    /// </summary>
    [RequiresUnreferencedCode("RegisterTemplate/MapVistaViews and AddVistaOpenApi reflect over view/DTO types.")]
    private static WebApplication BuildApp(string dbRelativePath, bool withOpenApi)
    {
        var builder = WebApplication.CreateBuilder();

        // Route the host through the in-memory TestServer and keep the console quiet (this is a harness).
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddDbContext<NorthwindDbContext>(options =>
            options.UseSqlite($"Data Source={dbRelativePath}"));

        builder.Services.AddVista(vista =>
            vista
                .RegisterTemplate<NorthwindViews, NorthwindDbContext>()
                .Register<WritableMemoView>());

        builder.Services.AddVistaEndpoints(v => v.AllowAnonymousAccess());

        if (withOpenApi)
        {
            builder.Services.AddVistaOpenApi();
        }

        var app = builder.Build();

        app.UseVistaExceptionHandling();
        app.MapVistaViews();

        if (withOpenApi)
        {
            app.MapVistaOpenApi();
        }

        return app;
    }

    /// <summary>
    /// [1] Serves the document and validates it is a well-formed OpenAPI 3.x document (Requirements 8.1,
    /// 8.2, 11.1): a <c>200 application/json</c> response, a declared 3.x version, a populated <c>info</c>,
    /// at least one path, and no dangling <c>$ref</c>.
    /// </summary>
    private static async Task<(bool Ok, string? DocumentJson)> ServeAndValidateAsync(HttpClient client)
    {
        Console.WriteLine("[1] Serve + validity (GET /openapi/v1.json)");

        var response = await client.GetAsync(ServeEndpointPath).ConfigureAwait(false);
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Console.WriteLine($"    Status={(int)response.StatusCode} {response.StatusCode}  Content-Type={mediaType ?? "(none)"}  Bytes={body.Length}");

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Console.WriteLine("    -> FAIL: expected 200 OK.");
            return (false, null);
        }

        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("    -> FAIL: expected Content-Type application/json.");
            return (false, null);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"    -> FAIL: response body is not valid JSON ({ex.Message}).");
            return (false, null);
        }

        using (document)
        {
            var root = document.RootElement;
            var problems = new List<string>();

            var openApiVersion = root.TryGetProperty("openapi", out var v) ? v.GetString() : null;
            if (openApiVersion is null || !openApiVersion.StartsWith("3.", StringComparison.Ordinal))
            {
                problems.Add($"'openapi' is not a 3.x version (got '{openApiVersion ?? "(missing)"}').");
            }

            if (!root.TryGetProperty("info", out var info)
                || string.IsNullOrWhiteSpace(info.TryGetProperty("title", out var title) ? title.GetString() : null)
                || string.IsNullOrWhiteSpace(info.TryGetProperty("version", out var ver) ? ver.GetString() : null))
            {
                problems.Add("'info' must carry a non-empty 'title' and 'version'.");
            }

            var hasPaths = root.TryGetProperty("paths", out var paths)
                && paths.ValueKind == JsonValueKind.Object
                && paths.EnumerateObject().Any();
            if (!hasPaths)
            {
                problems.Add("'paths' must contain at least one path.");
            }

            var dangling = FindDanglingRefs(root);
            if (dangling.Count > 0)
            {
                problems.Add($"{dangling.Count} '$ref' value(s) do not resolve under components.schemas (e.g. '{dangling[0]}').");
            }

            Console.WriteLine($"    openapi={openApiVersion}  paths={(hasPaths ? paths.EnumerateObject().Count() : 0)}  danglingRefs={dangling.Count}");

            if (problems.Count > 0)
            {
                foreach (var problem in problems)
                {
                    Console.WriteLine($"    - {problem}");
                }

                Console.WriteLine("    -> FAIL");
                return (false, null);
            }

            Console.WriteLine("    -> PASS");
            return (true, body);
        }
    }

    /// <summary>
    /// [2] Endpoint parity (Requirements 1.1–1.4, 2.1, 2.2): the set of <c>(method, path)</c> operations in
    /// the document equals the union over every registered view of its <c>View_Operation_Set</c> (computed
    /// from <see cref="FacetOperations.ForView(bool)"/> and the view's <see cref="ViewMetadata.Route"/>).
    /// </summary>
    private static bool EndpointParityCheck(IViewRegistry registry, JsonElement root)
    {
        Console.WriteLine("[2] Endpoint parity (document operations == live View_Operation_Set)");

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var view in registry.All)
        {
            var route = view.Route.TrimEnd('/');
            foreach (var facet in FacetOperations.ForView(view.IsReadOnly))
            {
                expected.Add($"{facet.HttpMethod} {route}/{facet.PathSuffix}");
            }
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Object)
        {
            foreach (var path in paths.EnumerateObject())
            {
                if (path.Value.TryGetProperty("get", out _))
                {
                    actual.Add($"GET {path.Name}");
                }

                if (path.Value.TryGetProperty("post", out _))
                {
                    actual.Add($"POST {path.Name}");
                }
            }
        }

        var missing = expected.Except(actual).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var phantom = actual.Except(expected).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Console.WriteLine($"    Expected operations={expected.Count}  Document operations={actual.Count}");
        Console.WriteLine($"    Missing={missing.Count}  Phantom={phantom.Count}");

        foreach (var operation in missing)
        {
            Console.WriteLine($"    - MISSING : {operation}");
        }

        foreach (var operation in phantom)
        {
            Console.WriteLine($"    - PHANTOM : {operation}");
        }

        var ok = missing.Count == 0 && phantom.Count == 0;
        Console.WriteLine($"    -> {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    /// <summary>
    /// [3] Additive coexistence (Requirement 10.1): an existing endpoint response is byte-for-byte identical
    /// whether or not the emitter is registered. Uses the metadata facet (served purely from
    /// <see cref="ViewMetadata"/>, no database access) of a representative read-only view.
    /// </summary>
    private static async Task<bool> NoRegressionCheckAsync(IViewRegistry registry, HttpClient withClient, HttpClient withoutClient)
    {
        Console.WriteLine("[3] Additive coexistence (existing endpoint unchanged by the emitter)");

        var view = registry.Get(RegressionViewName);
        if (view is null)
        {
            Console.WriteLine($"    -> FAIL: reference view '{RegressionViewName}' is not registered.");
            return false;
        }

        var metadataPath = view.Route.TrimEnd('/') + "/metadata";

        var withResponse = await withClient.GetAsync(metadataPath).ConfigureAwait(false);
        var withBody = await withResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        var withoutResponse = await withoutClient.GetAsync(metadataPath).ConfigureAwait(false);
        var withoutBody = await withoutResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        Console.WriteLine($"    GET {metadataPath}");
        Console.WriteLine($"    with-emitter   : {(int)withResponse.StatusCode}  Content-Type={withResponse.Content.Headers.ContentType?.MediaType ?? "(none)"}  Bytes={withBody.Length}");
        Console.WriteLine($"    without-emitter: {(int)withoutResponse.StatusCode}  Content-Type={withoutResponse.Content.Headers.ContentType?.MediaType ?? "(none)"}  Bytes={withoutBody.Length}");

        var ok =
            withResponse.StatusCode == HttpStatusCode.OK &&
            withResponse.StatusCode == withoutResponse.StatusCode &&
            string.Equals(
                withResponse.Content.Headers.ContentType?.MediaType,
                withoutResponse.Content.Headers.ContentType?.MediaType,
                StringComparison.Ordinal) &&
            string.Equals(withBody, withoutBody, StringComparison.Ordinal);

        Console.WriteLine($"    -> {(ok ? "PASS (byte-for-byte identical)" : "FAIL (response differs)")}");
        return ok;
    }

    /// <summary>
    /// Walks the whole document collecting every <c>$ref</c> value and returns those that do not resolve to
    /// a schema present under <c>components.schemas</c> (referential integrity — part of a valid document).
    /// </summary>
    private static List<string> FindDanglingRefs(JsonElement root)
    {
        const string prefix = "#/components/schemas/";

        var componentNames = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("components", out var components)
            && components.TryGetProperty("schemas", out var schemas)
            && schemas.ValueKind == JsonValueKind.Object)
        {
            foreach (var schema in schemas.EnumerateObject())
            {
                componentNames.Add(schema.Name);
            }
        }

        var refs = new List<string>();
        CollectRefs(root, refs);

        return refs
            .Where(reference => !reference.StartsWith(prefix, StringComparison.Ordinal)
                || !componentNames.Contains(reference[prefix.Length..]))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Recursively collects every <c>$ref</c> string value anywhere in the JSON tree.</summary>
    private static void CollectRefs(JsonElement element, List<string> refs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("$ref") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var reference = property.Value.GetString();
                        if (reference is not null)
                        {
                            refs.Add(reference);
                        }
                    }
                    else
                    {
                        CollectRefs(property.Value, refs);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectRefs(item, refs);
                }

                break;
        }
    }
}
