using a2n.Vista.Examples.AgGridNorthwind;
using a2n.Vista.Examples.AgGridNorthwind.Views;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Northwind.DataAccess;

// SQLite constraints are a host concern. Core classifies only SQLSTATE class 23.
a2n.Vista.Examples.SqliteConstraintProbe.Register();

var builder = WebApplication.CreateBuilder(args);

// The build extracts src/Examples/DB/Northwind SQLite.zip to the gitignored northwind.db template
// and checks src/Examples/DB/Northwind.sha256. A missing or mismatched template refuses to start.
// That file is read-only. Copy it, then create VistaMemos on the copy only.
const string DbRelativePath = "../DB/northwind.db";
const string ChecksumRelativePath = "../DB/Northwind.sha256";
var templatePath = Path.GetFullPath(DbRelativePath);
string templateHash;
try
{
    templateHash = NorthwindWorkingDatabase.RequirePinnedTemplate(
        templatePath,
        Path.GetFullPath(ChecksumRelativePath));
}
catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(
        """
        Run from the project directory so the relative path resolves:

          dotnet run --project src/Examples/a2n.Vista.Examples.AgGridNorthwind
        """);
    Environment.ExitCode = 1;
    return;
}
var working = NorthwindWorkingDatabase.CreateCopy(templatePath);
a2n.Vista.Examples.NorthwindMemoTable.EnsureOnCopy(templatePath, working.DatabasePath);
var workingConnectionString = new SqliteConnectionStringBuilder { DataSource = working.DatabasePath }.ToString();

builder.Services.AddDbContext<NorthwindDbContext>(options =>
    options.UseSqlite(workingConnectionString));

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

// Register the AG Grid server-side row model adapter (Decision Log D133). Each view then also exposes
// POST {route}/aggrid, driving the showcase's Simple Wiring and Custom Renderer AG Grid pages (R7.3).
// Additive only: this changes no existing view route, envelope, or error shape.
builder.Services.AddVistaAdapter<a2n.Vista.Adapters.AgGrid.AgGridAdapter>();

// Register the TanStack Table adapter (Decision Log D154). Each view then also exposes
// POST {route}/tanstack, driving the showcase's TanStack Table page. Additive only.
builder.Services.AddVistaAdapter<a2n.Vista.Adapters.TanStackTable.TanStackTableAdapter>();

// Register the jQuery-QueryBuilder metadata-schema emitter (Decision Log D116). Each view then also
// exposes GET {route}/querybuilder returning the metadataQB schema.
builder.Services.AddVistaMetadataAdapter<a2n.Vista.Adapters.DataTablesNet.QueryBuilderSchemaAdapter>();

var app = builder.Build();

// Serve the interactive demo UI from wwwroot (index.html): a jQuery DataTables grid plus a
// jQuery-QueryBuilder panel wired to the adapter endpoints (POST {route}/datatable and
// GET {route}/querybuilder). Static-file serving is independent of the API surface — MapVistaViews still
// owns everything under /api/views. UseDefaultFiles rewrites "/" to "/index.html" before UseStaticFiles.
app.UseDefaultFiles();
app.UseStaticFiles();

// RFC 7807 error mapping, then the generic view endpoints under {root}/{viewName}.
app.UseVistaExceptionHandling();
app.MapVistaViews();

// App-level view catalog for the View Browser page's selector (Decision D138). This minimal-API handler
// resolves the in-process IViewRegistry and projects it — exactly the explicitly-registered views,
// secure-by-default — into the browser-facing catalog. It is NOT a Vista package contract and adds no
// new view route, envelope, or error shape (R4.4). It sits inside the host's normal middleware pipeline
// and runs under the open-access posture opted into above (D94, R4.3).
app.MapGet(
    "/api/showcase/views",
    (a2n.Vista.Ports.IViewRegistry registry) =>
        Results.Json(a2n.Vista.Examples.AgGridNorthwind.Showcase.ShowcaseCatalog.Project(registry)));

// The opt-in OpenAPI Serve_Endpoint (default GET /openapi/v1.json). It returns the once-built, cached
// document as application/json. The endpoint is authorized by default; because this showcase opted into the
// reviewed open posture with AllowAnonymousAccess() (D94), that requirement is skipped and the document is
// public here exactly as the views are. An app with an authorizer serves it to authorized callers only.
app.MapVistaOpenApi();

// Guarded end-to-end self-tests (R12, R16.5): `dotnet run -- selftest`. The read self-test exercises
// List paging, filter/sort/search, and Detail-by-key against the working copy through the real executor.
// The write self-test exercises Create/Update/Delete on vWritableMemo twice: once on an isolated
// in-memory database, and once on the working copy (the inserted memo is deleted afterwards). The
// template's SHA-256 must be unchanged. The process exits 0 only when every pass succeeds.
if (args.Contains("selftest", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        var readPassed = await SelfTest.RunAsync(app.Services);
        var writePassed = await WriteSelfTest.RunAsync();
        var hostWritePassed = await WriteSelfTest.RunAgainstHostAsync(app.Services, templatePath, templateHash);
        // The OpenAPI self-test stands up an in-process test host (with and without the emitter) over the
        // same Vista wiring and the working copy, issues a real GET /openapi/v1.json, asserts the served
        // document is a valid OpenAPI 3.x document whose operation set equals the registered views' live
        // View_Operation_Set (endpoint parity), and asserts a representative existing endpoint response is
        // byte-for-byte unchanged by the emitter.
        var openApiPassed = await OpenApiSelfTest.RunAsync(working.DatabasePath);
        var templateUntouched = string.Equals(
            NorthwindWorkingDatabase.Sha256(templatePath),
            templateHash,
            StringComparison.Ordinal);
        if (!templateUntouched)
        {
            Console.Error.WriteLine("FAIL: the read-only northwind.db template changed during the self-test.");
        }

        Environment.ExitCode = readPassed && writePassed && hostWritePassed && openApiPassed && templateUntouched
            ? 0
            : 1;
    }
    finally
    {
        await app.DisposeAsync();
        ReleaseWorkingCopy(working);
    }

    return;
}

app.Lifetime.ApplicationStopped.Register(() => ReleaseWorkingCopy(working));
app.Run();

static void ReleaseWorkingCopy(NorthwindWorkingDatabase.WorkingCopy copy)
{
    SqliteConnection.ClearAllPools();
    NorthwindWorkingDatabase.Cleanup(copy);
}
