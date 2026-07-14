using a2n.Vista.Examples.AgGridNorthwind;
using a2n.Vista.Examples.AgGridNorthwind.Views;
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
          dotnet run --project src/Examples/a2n.Vista.Examples.AgGridNorthwind
        """);
    Environment.ExitCode = 1;
    return;
}

// Read-only SQLite connection against the extracted database file.
builder.Services.AddDbContext<NorthwindDbContext>(options =>
    options.UseSqlite($"Data Source={DbRelativePath}"));

// Vista core wiring (EF layer): register the Northwind central template (Gaya A / Style A). It exposes the
// read-only vProductCategory view (Product joined to Category/Supplier — string, numeric, and FK fields),
// so the AG Grid front-end can drive text/number/set filters, multi-sort, and quick-filter search against
// a real view (D136, R7.1).
builder.Services.AddVista(vista =>
    vista.RegisterTemplate<AgGridNorthwindViews, NorthwindDbContext>());

// Vista HTTP layer. This public read-only sample runs without an authorizer; in a non-Development
// environment that is a fail-closed startup error unless open access is opted into explicitly (D94),
// so we call AllowAnonymousAccess() to make the open posture a deliberate, documented choice. A real
// app gates access via UseAuthorizer<T>() instead.
builder.Services.AddVistaEndpoints(v => v
    .AllowAnonymousAccess());

// Register the AG Grid server-side row model adapter (D133/D134/D135, R6.1). Each view then also exposes
// POST {route}/aggrid for AG Grid IServerSideGetRowsRequest payloads; the quick-filter text rides
// out-of-band as ?q= folded into AdapterRequest.Values["q"].
builder.Services.AddVistaAdapter<a2n.Vista.Adapters.AgGrid.AgGridAdapter>();

var app = builder.Build();

// No seeding: the extracted Northwind database is the source of truth (read-only sample).

// Serve the interactive demo UI from wwwroot (index.html). Static-file serving is independent of the API
// surface — MapVistaViews still owns everything under /api/views. UseDefaultFiles rewrites "/" to
// "/index.html" before UseStaticFiles.
//
// TODO (task 9): the TypeScript client under client/ builds the AG Grid front-end (vistaAgGridDatasource.ts
// + main.ts) and emits its JS into wwwroot/js; the placeholder index.html here is replaced/extended by the
// real front-end then.
app.UseDefaultFiles();
app.UseStaticFiles();

// RFC 7807 error mapping, then the generic view endpoints under {root}/{viewName}.
app.UseVistaExceptionHandling();
app.MapVistaViews();

// Guarded end-to-end self-test (R8.2, R8.6): `dotnet run -- selftest`. It drives an AG Grid
// IServerSideGetRowsRequest (startRow/endRow block paging, two sortModel keys, a combined two-condition
// filterModel, and a quick filter) through the same path the POST {route}/aggrid endpoint uses — the
// AgGridAdapter (BindRequest → ToQuery) + the real Core executor + ToResponse — and asserts the
// { rowData, rowCount } shape (rowCount = total matching before paging; rowData = the exact rows within
// [startRow, endRow) in the requested sort order), then that the response serializes to camelCase
// rowData/rowCount. The process exits 0 only when the self-test passes.
if (args.Contains("selftest", StringComparer.OrdinalIgnoreCase))
{
    var passed = await AgGridSelfTest.RunAsync(app.Services);
    Environment.ExitCode = passed ? 0 : 1;
    return;
}

app.Run();
