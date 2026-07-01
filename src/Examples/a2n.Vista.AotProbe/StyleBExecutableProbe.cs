// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Phase 2 AOT verification (spec style-b-executable, Task 11.1, R5.1/R5.4/R5.5/R1.7; Decision Log D118).
//
// This probe drives the REAL generated Style B execution plan (CompiledViewExecutionPlan_ProbeWidgetView,
// emitted into this assembly by the source generator and registered into GeneratedExecutionPlanStore via
// a [ModuleInitializer]) through EfViewExecutor's NON-RUC compiled read path — ListCompiledAsync and
// DetailCompiledAsync. Those helpers (and the generated member-access map, strongly-typed sort appliers,
// and masked-field accessors they consume) carry no [RequiresUnreferencedCode], so the trim/AOT analyzer
// enabled by <IsAotCompatible>true</IsAotCompatible> treats this exact path as the analyzed surface.
//
// Keeping the analyzed surface honest:
//   * The compiled plan is read AOT-cleanly from GeneratedExecutionPlanStore.TryGet — NOT via the
//     reflection-based VistaBuilder.Register<TView>(), which is permanently [RequiresUnreferencedCode]
//     because Style B metadata is introspected at runtime (D96 AOT asymmetry). Registration/metadata is
//     not the read path R5 verifies, so the probe builds the ViewMetadata by hand (AOT-clean), exactly as
//     the Phase 1 export probe does.
//   * The List/Detail call sites below are then the ONLY Vista surface under the strict (warning-as-error)
//     analyzer — and they are clean. Any IL2026/IL3050 attributable to this path fails the build and names
//     the offending member (R5.5).
//   * EF Core's own infrastructure (provider wire-up, schema create, seeding) is documented as not
//     AOT-compatible; it is NOT the generated Style B path, so it is isolated in BuildSeededContext and the
//     ProbeDbContext constructor with narrowly-scoped suppressions. Nothing on the generated read path is
//     suppressed.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace a2n.Vista.AotProbe;

/// <summary>
/// Exercises the generated Style B List and Detail compiled read path for AOT verification (Task 11.1).
/// </summary>
internal static class StyleBExecutableProbe
{
    /// <summary>
    /// Seeds a tiny SQLite-backed dataset, obtains the generated compiled plan from
    /// <see cref="GeneratedExecutionPlanStore"/> (AOT-clean), and runs List (with a client filter and
    /// sort) and Detail-by-key through the NON-RUC compiled helpers — the path the analyzer must find
    /// free of IL2026/IL3050.
    /// </summary>
    public static async Task RunAsync()
    {
        // EF Core provider wire-up, schema creation, and seeding are framework concerns, NOT the generated
        // Style B path under verification — isolated in this helper. The open SQLite connection owns the
        // in-memory database, so it must outlive the context; the connection is disposed last.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = BuildSeededContext(connection);

        // The generated [ModuleInitializer] in this assembly registered the compiled plan into the store
        // at module load. Read it back AOT-cleanly (no reflection, no Register<TView> RUC path).
        if (!GeneratedExecutionPlanStore.TryGet(ProbeWidgetView.ViewName, out var plan))
        {
            throw new InvalidOperationException(
                $"No generated compiled plan was found for '{ProbeWidgetView.ViewName}'. Ensure the source " +
                "generator analyzer is referenced so the Phase 2 emitter produces the compiled plan and its " +
                "[ModuleInitializer] registers it into GeneratedExecutionPlanStore at module load.");
        }

        // Hand-built ViewMetadata (AOT-clean) describing the same projection/whitelist the generated plan
        // serves. Mirrors the Phase 1 export probe's BuildViewMetadata so no reflection metadata path runs.
        var view = BuildViewMetadata();

        // A minimal service provider + (unused-by-the-compiled-helpers) plan registry satisfy the executor's
        // production constructor; the compiled helpers take the plan as an explicit argument and never touch
        // the registry. Both constructions are AOT-clean.
        var provider = new ServiceCollection().BuildServiceProvider();
        var planRegistry = new ViewExecutionPlanRegistry();

        var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler(new DefaultQueryDialect()));
        var scope = new ViewScope();

        // --- List through the compiled path: a client equality filter (exercises the generated
        //     member-access map + FilterCompiler resolver) and a client sort (exercises the generated
        //     strongly-typed sort appliers), with the KeyFields tiebreaker (D106). ---
        var request = new ViewQueryRequest(
            Filter: new FilterLeaf(nameof(ProbeWidgetRow.Region), FilterOperator.Equals, "EU"),
            Sort: new[] { new SortSpec(nameof(ProbeWidgetRow.Name), Descending: false) },
            Page: 0,
            PageSize: 50,
            Search: null,
            Scope: null);

        var listResult = await executor
            .ListCompiledAsync<ProbeWidgetRow>(plan, view, request, scope, CancellationToken.None)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("AOT probe: generated Style B compiled List/Detail path exercised.");
        Console.WriteLine(
            $"List(\"{ProbeWidgetView.ViewName}\", Region=EU, sort=Name): " +
            $"{listResult.Page.Items.Count} row(s), unfiltered total {listResult.TotalRowsUnfiltered}.");

        // --- Detail-by-key through the compiled path: scalar single key (exercises the generated key
        //     member-access; at-most-one row; null without throwing when absent, R3.2/R3.3). ---
        var detail = await executor
            .DetailCompiledAsync<ProbeWidgetRow>(plan, view, 1, scope, CancellationToken.None)
            .ConfigureAwait(false);

        var missing = await executor
            .DetailCompiledAsync<ProbeWidgetRow>(plan, view, 999, scope, CancellationToken.None)
            .ConfigureAwait(false);

        Console.WriteLine(
            $"Detail(Id=1) => {(detail is null ? "(none)" : $"{detail.Name} [{detail.Region}]")}; " +
            $"Detail(Id=999) => {(missing is null ? "(none)" : missing.Name)}.");
    }

    /// <summary>
    /// Builds the view metadata the compiled read path consumes, by hand and AOT-clean (no reflection over
    /// the view type). The field whitelist matches the generated plan's member-access: <c>Region</c> is
    /// filterable for equality and <c>Name</c> is sortable; <c>Id</c> is the single key field used for
    /// Detail-by-key and the deterministic D106 sort tiebreaker.
    /// </summary>
    private static ViewMetadata BuildViewMetadata()
    {
        var fields = new[]
        {
            FieldMetadata.Create("Id", typeof(int), isPrimaryKey: true),
            FieldMetadata.Create("Name", typeof(string)),
            FieldMetadata.Create("Region", typeof(string), allowedOperators: FilterOperator.Equals),
            FieldMetadata.Create("Quantity", typeof(int)),
        };

        return new ViewMetadata(
            Name: ProbeWidgetView.ViewName,
            Route: "/api/views/" + ProbeWidgetView.ViewName,
            QueryType: typeof(ProbeWidgetRow),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: true)
        {
            KeyFields = new[] { "Id" },
        };
    }

    /// <summary>
    /// Builds a <see cref="ProbeDbContext"/> over the supplied open SQLite connection, creates the schema,
    /// and seeds a handful of widgets. Isolated from the generated-path call sites because EF Core's
    /// provider/options/migration surface is framework infrastructure documented as not trim/AOT
    /// compatible — it is not the generated Style B read path this probe verifies (R5.1/R5.4).
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification = "EF Core provider/schema setup is framework infrastructure, not the generated Style B read path under AOT verification (R5.1/R5.4).")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Members annotated with 'RequiresDynamicCodeAttribute' may break when AOT compiling",
        Justification = "EF Core provider/schema setup is framework infrastructure, not the generated Style B read path under AOT verification (R5.1/R5.4).")]
    private static ProbeDbContext BuildSeededContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ProbeDbContext(options);
        context.Database.EnsureCreated();

        context.Widgets.AddRange(
            new ProbeWidget { Id = 1, Name = "Anchor", Region = "EU", Quantity = 10 },
            new ProbeWidget { Id = 2, Name = "Bolt", Region = "EU", Quantity = 5 },
            new ProbeWidget { Id = 3, Name = "Cog", Region = "US", Quantity = 7 },
            new ProbeWidget { Id = 4, Name = "Dowel", Region = "EU", Quantity = 3 });
        context.SaveChanges();

        return context;
    }
}
