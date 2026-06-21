using System.Diagnostics.CodeAnalysis;
using System.Linq;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.Tests;

/// <summary>
/// EF source entity backing the paging tests (task 12.5). Seeded into a real SQLite database so the
/// <see cref="EfViewExecutor"/> List path runs end to end against a relational provider.
/// </summary>
internal sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

/// <summary>
/// Named projection row for the widgets view, so <see cref="EfViewExecutor.ListAsync{TRow}"/> can be
/// called with a known compile-time <c>TRow</c> (no reflection on the caller side).
/// </summary>
/// <remarks>
/// Declared as a class with init-only auto-properties (not a positional record) and projected via
/// member-initialization (<c>new WidgetRow { Id = w.Id, ... }</c>). This mirrors how anonymous
/// projections translate: EF Core maps each assigned member back to its source column, so a later
/// <c>OrderBy(x =&gt; x.Id)</c> / <c>Where(...)</c> on the projected query pushes down to SQL. A
/// constructor projection of a named type (positional record) is <em>not</em> translatable that way
/// in EF Core and would force client evaluation, which is why it is intentionally avoided here.
/// </remarks>
internal sealed class WidgetRow
{
    /// <summary>Primary key — filterable/sortable, not searchable.</summary>
    public int Id { get; init; }

    /// <summary>String field — filterable/sortable/searchable.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Numeric field — filterable/sortable, not searchable.</summary>
    public decimal Price { get; init; }
}

/// <summary>
/// Minimal EF context exposing <see cref="Widget"/> rows for the SQLite-backed paging tests.
/// </summary>
internal sealed class WidgetContext : DbContext
{
    public WidgetContext(DbContextOptions<WidgetContext> options)
        : base(options)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();
}

/// <summary>
/// A test-only <see cref="EfViewExecutor"/> that bypasses the DI execution-plan path by overriding the
/// single source-resolution seam (<see cref="ResolveScopedQueryable{TRow}"/>) to return a pre-projected,
/// SQLite-backed <see cref="IQueryable{T}"/> of <see cref="WidgetRow"/>. This keeps the test focused and
/// deterministic while still exercising the real List/paging/cancellation logic, the
/// <see cref="DefaultQueryDialect"/> (SQLite <c>LIKE</c>), and EF's async pipeline.
/// </summary>
internal sealed class WidgetTestExecutor : EfViewExecutor
{
    private readonly IQueryable<WidgetRow> _widgets;

    // Use the default dialect so the filtered-totals test exercises real SQLite LIKE translation.
    public WidgetTestExecutor(IQueryable<WidgetRow> widgets)
        : base(new FilterCompiler(new DefaultQueryDialect())) =>
        _widgets = widgets;

    /// <inheritdoc />
    [RequiresUnreferencedCode("Test seam returns a pre-projected SQLite queryable; mirrors the base reflection contract.")]
    protected override IQueryable<TRow> ResolveScopedQueryable<TRow>(ViewMetadata view, IViewScope scope) =>
        // Tests always call with TRow == WidgetRow; the cast keeps the override single-purpose.
        (IQueryable<TRow>)(object)_widgets;
}

/// <summary>
/// Disposable test harness owning an open in-memory SQLite connection, a seeded <see cref="WidgetContext"/>,
/// and a <see cref="WidgetTestExecutor"/> over a <see cref="WidgetRow"/> projection. SQLite in-memory
/// databases live only as long as the connection is open, so the connection is kept alive for the
/// lifetime of the harness and disposed last.
/// </summary>
internal sealed class WidgetTestHarness : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WidgetContext _context;

    private WidgetTestHarness(SqliteConnection connection, WidgetContext context, WidgetTestExecutor executor)
    {
        _connection = connection;
        _context = context;
        Executor = executor;
    }

    /// <summary>The number of widgets seeded into the database.</summary>
    public const int SeededRowCount = 25;

    /// <summary>The executor under test, wired to the seeded SQLite-backed projection.</summary>
    public WidgetTestExecutor Executor { get; }

    /// <summary>
    /// Creates and seeds a harness with <see cref="SeededRowCount"/> widgets. Each widget has a unique
    /// 1-based <see cref="Widget.Id"/>, a <see cref="Widget.Name"/> of <c>"Widget {Id}"</c>, and a
    /// <see cref="Widget.Price"/> of <c>Id * 10</c>, giving deterministic ordering and filter counts.
    /// </summary>
    public static WidgetTestHarness Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<WidgetContext>()
            .UseSqlite(connection)
            .Options;

        var context = new WidgetContext(options);
        context.Database.EnsureCreated();

        var widgets = Enumerable.Range(1, SeededRowCount)
            .Select(i => new Widget { Id = i, Name = $"Widget {i}", Price = i * 10m });
        context.Widgets.AddRange(widgets);
        context.SaveChanges();

        // Projection captured here (member-init) is translated by EF and executed by SQLite, and lets
        // subsequent OrderBy/Where on the projected row push down to SQL.
        var projection = context.Widgets.Select(w => new WidgetRow { Id = w.Id, Name = w.Name, Price = w.Price });
        var executor = new WidgetTestExecutor(projection);

        return new WidgetTestHarness(connection, context, executor);
    }

    /// <summary>
    /// Builds read-only <see cref="ViewMetadata"/> over <see cref="WidgetRow"/>. The page-size hard
    /// limit is configurable so the clamp test (R10.3) can drive it directly.
    /// </summary>
    /// <param name="maxPageSize">The view's <see cref="HardLimits.MaxPageSize"/>.</param>
    public static ViewMetadata BuildView(int maxPageSize = HardLimits.DefaultMaxPageSize)
    {
        var fields = new[]
        {
            FieldMetadata.Create(
                name: nameof(WidgetRow.Id),
                clrType: typeof(int),
                isFilterable: true,
                isSortable: true,
                isSearchable: false,
                allowedOperators: FilterOperator.Equals
                    | FilterOperator.In
                    | FilterOperator.GreaterThanOrEqual
                    | FilterOperator.LessThanOrEqual
                    | FilterOperator.Between),

            FieldMetadata.Create(
                name: nameof(WidgetRow.Name),
                clrType: typeof(string),
                isFilterable: true,
                isSortable: true,
                isSearchable: true,
                allowedOperators: FilterOperator.Text | FilterOperator.In),

            FieldMetadata.Create(
                name: nameof(WidgetRow.Price),
                clrType: typeof(decimal),
                isFilterable: true,
                isSortable: true,
                isSearchable: false,
                allowedOperators: FilterOperator.Range | FilterOperator.Equals),
        };

        return new ViewMetadata(
            Name: "Widgets",
            Route: "/test/Widgets",
            QueryType: typeof(WidgetRow),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: new HardLimits(maxPageSize, HardLimits.DefaultMaxExportRows),
            IsReadOnly: true)
        {
            KeyFields = [nameof(WidgetRow.Id)],
        };
    }

    public void Dispose()
    {
        _context.Dispose();
        // Disposing the connection drops the in-memory database; do it last.
        _connection.Dispose();
    }
}
