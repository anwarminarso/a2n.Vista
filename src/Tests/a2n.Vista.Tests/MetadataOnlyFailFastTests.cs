using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Metadata-only fail-fast on execution (Task 7.2, Requirement R4.4 / DR5). A typed Style B view that
/// has <em>no</em> generated execution plan stays metadata-only: it is discoverable but not executable.
/// Both <see cref="a2n.Vista.EntityFrameworkCore.Execution.EfViewExecutor.ListAsync{TRow}"/> and
/// <see cref="a2n.Vista.EntityFrameworkCore.Execution.EfViewExecutor.DetailAsync{TRow}"/> SHALL fail
/// fast — before any query work and with no partial result — when the
/// <see cref="IViewExecutionPlanRegistry"/> returns <see langword="null"/> for the view, with a message
/// that names the view, states no generated plan exists, and instructs referencing the source generator.
/// </summary>
// The executor List/Detail entry points are [RequiresUnreferencedCode]; this test exercises the
// reflection contract by design, so the trim/AOT diagnostic is suppressed here (as in PagingTests).
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Test exercises the runtime reflection path of EfViewExecutor by design; trimming is not used for tests.")]
public sealed class MetadataOnlyFailFastTests
{
    [Test]
    public async Task ListAsync_Metadata_Only_View_Fails_Fast_Before_Any_Query()
    {
        using var harness = FailFastHarness.Create();
        var view = FailFastHarness.BuildView();
        var request = new ViewQueryRequest(Filter: null, Sort: Array.Empty<SortSpec>(), Page: 0, PageSize: 10);

        var caught = await Capture(async () =>
            await harness.Executor.ListAsync<FailFastRow>(view, request, new ViewScope(), CancellationToken.None));

        await AssertFailFastMessage(caught);
    }

    [Test]
    public async Task DetailAsync_Metadata_Only_View_Fails_Fast_Before_Any_Query()
    {
        using var harness = FailFastHarness.Create();
        var view = FailFastHarness.BuildView();

        var caught = await Capture(async () =>
            await harness.Executor.DetailAsync<FailFastRow>(view, 1, new ViewScope(), CancellationToken.None));

        await AssertFailFastMessage(caught);
    }

    /// <summary>
    /// Asserts the captured exception is the R4.4-compliant fail-fast: an
    /// <see cref="InvalidOperationException"/> whose message names the view, states that no generated
    /// execution plan exists / the view is metadata-only, and instructs referencing the source generator.
    /// </summary>
    private static async Task AssertFailFastMessage(Exception? caught)
    {
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught).IsTypeOf<InvalidOperationException>();
        await Assert.That(caught!.Message).Contains(FailFastHarness.ViewName);
        await Assert.That(caught.Message).Contains("metadata-only");
        await Assert.That(caught.Message).Contains("source generator");
    }

    /// <summary>Runs <paramref name="action"/> and returns the thrown exception, or <see langword="null"/>.</summary>
    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}

/// <summary>EF source entity for the metadata-only fail-fast test (uniquely named to avoid collisions).</summary>
internal sealed class FailFastEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>Projected row type for the metadata-only fail-fast test.</summary>
internal sealed class FailFastRow
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

/// <summary>Minimal EF context backing the metadata-only fail-fast test.</summary>
internal sealed class FailFastContext : DbContext
{
    public FailFastContext(DbContextOptions<FailFastContext> options)
        : base(options)
    {
    }

    public DbSet<FailFastEntity> Entities => Set<FailFastEntity>();
}

/// <summary>
/// Disposable harness wiring a real <see cref="EfViewExecutor"/> via its DI constructor with an
/// <em>empty</em> <see cref="ViewExecutionPlanRegistry"/>, so resolving the view's plan returns
/// <see langword="null"/> and the executor takes the metadata-only fail-fast path.
/// </summary>
internal sealed class FailFastHarness : IDisposable
{
    /// <summary>The globally-unique name of the metadata-only view under test.</summary>
    public const string ViewName = "FailFastView";

    private readonly SqliteConnection _connection;
    private readonly FailFastContext _context;
    private readonly ServiceProvider _services;

    private FailFastHarness(SqliteConnection connection, FailFastContext context, ServiceProvider services, EfViewExecutor executor)
    {
        _connection = connection;
        _context = context;
        _services = services;
        Executor = executor;
    }

    /// <summary>The executor under test, wired with an empty plan registry.</summary>
    public EfViewExecutor Executor { get; }

    public static FailFastHarness Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<FailFastContext>()
            .UseSqlite(connection)
            .Options;

        var context = new FailFastContext(options);
        context.Database.EnsureCreated();

        var services = new ServiceCollection().BuildServiceProvider();

        // Empty registry → Get(ViewName) returns null → metadata-only fail-fast (R4.4 / DR5).
        var registry = new ViewExecutionPlanRegistry();

        // Default (ordinal/in-memory) FilterCompiler keeps the harness provider-agnostic; the fail-fast
        // throws before any filter compilation runs anyway.
        var executor = new EfViewExecutor(context, services, registry, new FilterCompiler());

        return new FailFastHarness(connection, context, services, executor);
    }

    /// <summary>
    /// Builds read-only metadata for a typed Style B view that is <em>not</em> registered in the plan
    /// registry, mirroring a DR5 metadata-only view (discoverable, not executable).
    /// </summary>
    public static ViewMetadata BuildView()
    {
        var fields = new[]
        {
            FieldMetadata.Create(
                name: nameof(FailFastRow.Id),
                clrType: typeof(int),
                isFilterable: true,
                isSortable: true,
                isSearchable: false,
                allowedOperators: FilterOperator.Equals),

            FieldMetadata.Create(
                name: nameof(FailFastRow.Name),
                clrType: typeof(string),
                isFilterable: true,
                isSortable: true,
                isSearchable: true,
                allowedOperators: FilterOperator.Text),
        };

        return new ViewMetadata(
            Name: ViewName,
            Route: $"/test/{ViewName}",
            QueryType: typeof(FailFastRow),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: new HardLimits(HardLimits.DefaultMaxPageSize, HardLimits.DefaultMaxExportRows),
            IsReadOnly: true)
        {
            KeyFields = [nameof(FailFastRow.Id)],
        };
    }

    public void Dispose()
    {
        _services.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }
}
