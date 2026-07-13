using a2n.Vista.Examples.Northwind;
using a2n.Vista.Examples.Northwind.Views;
using Microsoft.EntityFrameworkCore;
using Northwind.DataAccess;

var builder = WebApplication.CreateBuilder(args);

// Real Microsoft Northwind sample database (SQLite). It is shipped zipped under ../DB and must be
// extracted before first run; we never recreate or seed it, so it stays the single source of truth.
const string DbRelativePath = "../DB/northwind.db";
var dbFullPath = Path.GetFullPath(DbRelativePath);

if (!File.Exists(dbFullPath))
{
    Console.Error.WriteLine(
        $"""
        Northwind database not found at: {dbFullPath}

        Extract the bundled "Northwind SQLite.zip" so the database file exists, for example:
          1. Open the folder next to the project: src/Examples/DB
          2. Extract "Northwind SQLite.zip" into that folder
          3. Make sure the extracted file is named "northwind.db" (rename it if needed)

        Then run this example again from the project directory:
          dotnet run --project src/Examples/Northwind
        """);
    Environment.ExitCode = 1;
    return;
}

// Read-only SQLite connection against the extracted database file.
builder.Services.AddDbContext<NorthwindDbContext>(options =>
    options.UseSqlite($"Data Source={DbRelativePath}"));

// Vista core wiring (EF layer): register the Gaya A central template plus a class-per-view (Style B)
// *writable* view. A view's route is composed at registration (default root /api/views, or via
// RouteGroup(...)) and recorded in ViewMetadata.Route (Decision Log D101/D103); views are exposed under
// {root}/{viewName}. The writable Style B view (vWritableMemo) declares a MapWritable whitelist and a
// concurrency token, so its Create/Update/Delete endpoints are enabled (Requirement R16.4).
builder.Services.AddVista(vista =>
    vista
        .RegisterTemplate<NorthwindViews, NorthwindDbContext>()
        .Register<WritableMemoView>());

// Vista HTTP layer. This public read-only sample runs without an authorizer; in a non-Development
// environment that is a fail-closed startup error unless open access is opted into explicitly (D94),
// so we call AllowAnonymousAccess() to make the open posture a deliberate, documented choice. A real
// app gates access via UseAuthorizer<T>() instead.
//
// No developer App_Json_Context is registered: the ViewJsonContextGenerator emits a reflection-free
// per-view JsonTypeInfo set for the typed Style B view (vWritableMemo) — covering MemoRow,
// ViewListResult<MemoRow>, PagedResult<MemoRow>, and MemoWriteModel — and Vista auto-chains it into the
// serialization seam ahead of the reflection fallback (Decision Log D125/D126). Combined with the
// generated dispatch invoker (ViewInvokerStore, D123) that closes List/Detail/Create/Update over those
// types at compile time, the vWritableMemo HTTP path runs reflection-free with no hand-authored context.
// The Style A views (vProductCategory, vOrderDetail) project anonymous rows and stay on the reflection
// serialization fallback by design (D96).
builder.Services.AddVistaEndpoints(v => v
    .AllowAnonymousAccess());

// Opt in to the Vista OpenAPI emitter (Decision Log D127/D128). AddVistaOpenApi() registers the
// metadata-driven document builder + the build-once cache (off by default; nothing is added unless this is
// called), and MapVistaOpenApi() below exposes GET /openapi/v1.json serving the document as
// application/json. The emitted document's operation set is, by construction, exactly the live
// View_Operation_Set for every registered view (endpoint parity), and adding it changes no existing
// endpoint response — both asserted by the OpenAPI self-test (dotnet run -- selftest).
builder.Services.AddVistaOpenApi();

// Register the jQuery DataTables.NET adapter (Decision Log D112). Each view then also exposes
// POST {route}/datatable for DataTables server-side requests.
builder.Services.AddVistaAdapter<a2n.Vista.Adapters.DataTablesNet.DataTablesAdapter>();

// Register the jQuery-QueryBuilder metadata-schema emitter (Decision Log D116). Each view then also
// exposes GET {route}/querybuilder returning the metadataQB schema.
builder.Services.AddVistaMetadataAdapter<a2n.Vista.Adapters.DataTablesNet.QueryBuilderSchemaAdapter>();

var app = builder.Build();

// No seeding: the extracted Northwind database is the source of truth (read-only sample).

// Serve the interactive demo UI from wwwroot (index.html): a jQuery DataTables grid plus a
// jQuery-QueryBuilder panel wired to the adapter endpoints (POST {route}/datatable and
// GET {route}/querybuilder). Static-file serving is independent of the API surface — MapVistaViews still
// owns everything under /api/views. UseDefaultFiles rewrites "/" to "/index.html" before UseStaticFiles.
app.UseDefaultFiles();
app.UseStaticFiles();

// RFC 7807 error mapping, then the generic view endpoints under {root}/{viewName}.
app.UseVistaExceptionHandling();
app.MapVistaViews();

// The opt-in OpenAPI Serve_Endpoint (default GET /openapi/v1.json). It sits inside the host's normal
// middleware pipeline (it bypasses no authentication/authorization) and returns the once-built, cached
// OpenAPI document as application/json.
app.MapVistaOpenApi();

// Guarded end-to-end self-tests (R12, R16.5): `dotnet run -- selftest`. The read self-test exercises
// List paging, filter/sort/search, and Detail-by-key against the shipped read-only northwind.db through
// the real executor. The write self-test exercises Create/Update/Delete on the writable vWritableMemo
// view against an isolated in-memory database (the read-only sample has no VistaMemos table and is never
// mutated). Both run, print their outcomes, and the process exits 0 only when BOTH pass.
if (args.Contains("selftest", StringComparer.OrdinalIgnoreCase))
{
    var readPassed = await SelfTest.RunAsync(app.Services);
    var writePassed = await WriteSelfTest.RunAsync();
    // The OpenAPI self-test stands up an in-process test host (with and without the emitter) over the same
    // Vista wiring, issues a real GET /openapi/v1.json, asserts the served document is a valid OpenAPI 3.x
    // document whose operation set equals the registered views' live View_Operation_Set (endpoint parity),
    // and asserts a representative existing endpoint response is byte-for-byte unchanged by the emitter.
    var openApiPassed = await OpenApiSelfTest.RunAsync(DbRelativePath);
    Environment.ExitCode = readPassed && writePassed && openApiPassed ? 0 : 1;
    return;
}

app.Run();
