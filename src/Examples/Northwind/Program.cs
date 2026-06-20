using a2n.Vista.Examples.Northwind;
using a2n.Vista.Examples.Northwind.Data;
using a2n.Vista.Examples.Northwind.Views;
using Microsoft.EntityFrameworkCore;

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

// Vista core wiring (EF layer): register the Gaya A central template. The global route root is owned by
// the AspNetCore layer (Decision Log D101); views are exposed under {RouteRoot}/{viewName}.
builder.Services.AddVista(vista =>
    vista.RegisterTemplate<NorthwindViews, NorthwindDbContext>());

// Vista HTTP layer. No UseAuthorizer<T>() here on purpose: the spec marks the authorizer optional, and
// leaving it off demonstrates the fail-open startup warning ("all views publicly accessible", R7.3)
// while keeping the app fully functional (default allow, R7.2).
builder.Services.AddVistaEndpoints();

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
