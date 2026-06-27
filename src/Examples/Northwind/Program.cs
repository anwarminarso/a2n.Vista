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

// Vista core wiring (EF layer): register the Gaya A central template. A view's route is composed at
// registration (default root /api/views, or via RouteGroup(...)) and recorded in ViewMetadata.Route
// (Decision Log D101/D103); views are exposed under {root}/{viewName}.
builder.Services.AddVista(vista =>
    vista.RegisterTemplate<NorthwindViews, NorthwindDbContext>());

// Vista HTTP layer. This public read-only sample runs without an authorizer; in a non-Development
// environment that is a fail-closed startup error unless open access is opted into explicitly (D94),
// so we call AllowAnonymousAccess() to make the open posture a deliberate, documented choice. A real
// app gates access via UseAuthorizer<T>() instead.
builder.Services.AddVistaEndpoints(v => v.AllowAnonymousAccess());

var app = builder.Build();

// No seeding: the extracted Northwind database is the source of truth (read-only sample).

// RFC 7807 error mapping, then the generic view endpoints under {root}/{viewName}.
app.UseVistaExceptionHandling();
app.MapVistaViews();

// Guarded end-to-end self-test (R12): `dotnet run -- selftest`. Exercises List paging, filter/sort/
// search, and Detail-by-key through the real executor, prints the outcome, and exits without serving.
if (args.Contains("selftest", StringComparer.OrdinalIgnoreCase))
{
    var passed = await SelfTest.RunAsync(app.Services);
    Environment.ExitCode = passed ? 0 : 1;
    return;
}

app.Run();
