using a2n.Vista.Examples.Northwind;
using a2n.Vista.Examples.Northwind.Data;
using a2n.Vista.Examples.Northwind.Views;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQLite read-only Northwind sample, seeded at startup so List/Detail return data.
builder.Services.AddDbContext<NorthwindDbContext>(options =>
    options.UseSqlite("Data Source=northwind-sample.db"));

// Vista core wiring (EF layer): register the Gaya A central template. RouteRoot defaults to /api/views.
builder.Services.AddVista(vista =>
    vista.RegisterTemplate<NorthwindViews, NorthwindDbContext>());

// Vista HTTP layer. No UseAuthorizer<T>() here on purpose: the spec marks the authorizer optional, and
// leaving it off demonstrates the fail-open startup warning ("all views publicly accessible", R7.3)
// while keeping the app fully functional (default allow, R7.2).
builder.Services.AddVistaEndpoints();

var app = builder.Build();

// Create + seed the database.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NorthwindDbContext>();
    NorthwindSeeder.EnsureSeeded(db);
}

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
