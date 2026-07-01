// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.StyleBExecSample;
using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the source-generator Phase 2 compiled List path's page bound and unfiltered
/// total (spec style-b-executable; Decision Log D118).
///
/// Feature: style-b-executable, Property 2: For any seeded data, page size, and page index, a List served
/// by the generated plan SHALL return a page whose row count is at most the (clamped) requested page size,
/// and whose TotalRowsUnfiltered equals the count of the server-trusted + client-Scope baseline ignoring
/// the page window (DR6), independent of the page index.
///
/// Validates: Requirements 3.1
///
/// The view under test (<see cref="P1CustomerView"/>) lives in the EF-aware consumer assembly
/// <c>a2n.Vista.StyleBExecSample</c>, where the source generator emits a REAL
/// <see cref="ICompiledViewExecutionPlan"/> and registers it into <see cref="GeneratedExecutionPlanStore"/>
/// at module load. The view declares no server-trusted row filters, so its unfiltered baseline is the set
/// of rows matching the per-request client <c>Scope</c> only — independent of any client <c>Filter</c> /
/// <c>Search</c> and of the page window. Each generated case seeds a fresh SQLite database, registers the
/// view through <c>AddVista</c> (so the compiled plan is adopted into the execution-plan registry), and
/// drives <see cref="EfViewExecutor.ListCompiledAsync{TRow}"/> via the public
/// <see cref="IViewExecutor.ListAsync{TRow}"/> entry point across several page indices of the SAME request.
/// </summary>
public sealed class ListPageBoundPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>
    /// <see cref="P1CustomerView"/> declares no <c>MaxPageSize</c>, so the effective clamp is the default
    /// hard limit. Page item count must never exceed <c>min(requested, MaxPageSize)</c> (R10.3 / R3.1).
    /// </summary>
    private static readonly int MaxPageSize = HardLimits.DefaultMaxPageSize;

    /// <summary>City pool — also the <c>Scope</c> value pool, so a scope frequently matches seeded rows.</summary>
    private static readonly string[] Cities = { "Paris", "Portland", "London", "Lyon", "Berlin" };

    /// <summary>Name pool with overlapping substrings so a client filter/search prunes the page meaningfully.</summary>
    private static readonly string[] Names = { "Alice", "Alicia", "Bob", "Bobby", "Carol", "Dave", "" };

    /// <summary>A generated source row (Id is assigned positionally during seeding).</summary>
    private readonly record struct RowSpec(string Name, string City, double Balance, int Age);

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    private static readonly Gen<RowSpec> GenRow =
        from name in Pick(Names)
        from city in Pick(Cities)
        from balTenths in Gen.Int[0, 5000]
        from age in Gen.Int[0, 90]
        select new RowSpec(name, city, balTenths / 10d, age);

    /// <summary>
    /// Touches a type in the consumer assembly so its module — and thus the generated
    /// <c>[ModuleInitializer]</c> that calls <see cref="GeneratedExecutionPlanStore.Add"/> — is loaded
    /// before any case runs. Instantiating the view is a safe, side-effect-free trigger.
    /// </summary>
    private static void EnsureFixtureModuleLoaded() => _ = new P1CustomerView().Name;

    [Test]
    public void List_Page_Is_Bounded_And_Unfiltered_Total_Is_Scope_Baseline_Independent_Of_Page_Index()
    {
        EnsureFixtureModuleLoaded();

        // A full generated case: the dataset, the channel inputs that must NOT affect the unfiltered total
        // (client filter/search), the optional scope that DOES define the baseline, a requested page size
        // (deliberately allowed to exceed MaxPageSize so the clamp is exercised), and a set of distinct page
        // indices over which TotalRowsUnfiltered must stay constant.
        var genCase =
            from rowCount in Gen.Int[0, 30]
            from rows in GenRow.Array[rowCount]
            from pageSize in Gen.Int[1, 150]
            from hasScope in Gen.Bool
            from scopeCity in Pick(Cities)
            from hasFilter in Gen.Bool
            from filterName in Pick(Names)
            from hasSearch in Gen.Bool
            from searchTerm in Pick(new[] { "Al", "Bob", "o", "i", "" })
            from pageIndices in Gen.Int[0, 6].Array[1, 5]
            select (rows, pageSize, hasScope, scopeCity, hasFilter, filterName, hasSearch, searchTerm, pageIndices);

        genCase.Sample(
            input =>
            {
                var (rows, pageSize, hasScope, scopeCity, hasFilter, filterName, hasSearch, searchTerm, pageIndices) = input;

                using var harness = P2ListHarness.Create(rows);

                // The view has no server-trusted row filters, so the unfiltered baseline is the rows that
                // match the client Scope only (ignoring client Filter/Search and the page window, DR6).
                var expectedUnfiltered = hasScope
                    ? rows.Count(r => string.Equals(r.City, scopeCity, StringComparison.Ordinal))
                    : rows.Length;

                FilterNode? scope = hasScope
                    ? new FilterLeaf(nameof(P1CustomerRow.City), FilterOperator.Equals, scopeCity)
                    : null;
                FilterNode? filter = hasFilter
                    ? new FilterLeaf(nameof(P1CustomerRow.Name), FilterOperator.Contains, filterName)
                    : null;
                FilterNode? search = hasSearch
                    ? new FilterLeaf(nameof(P1CustomerRow.Name), FilterOperator.Contains, searchTerm)
                    : null;

                var clampedPageSize = Math.Min(pageSize, MaxPageSize);

                // Run the SAME request across distinct page indices; the page bound must hold on each page
                // and the unfiltered total must be identical (independent of the page index, R3.1).
                foreach (var pageIndex in pageIndices.Distinct())
                {
                    var request = new ViewQueryRequest(
                        Filter: filter,
                        Sort: Array.Empty<SortSpec>(),
                        Page: pageIndex,
                        PageSize: pageSize,
                        Search: search,
                        Scope: scope);

                    var result = harness.List(request);

                    // (1) Page row count is at most the clamped requested page size.
                    if (result.Page.Items.Count > clampedPageSize)
                    {
                        throw new Exception(
                            $"Page {pageIndex} returned {result.Page.Items.Count} rows, exceeding the clamped " +
                            $"page size {clampedPageSize} (requested {pageSize}, MaxPageSize {MaxPageSize}).");
                    }

                    // (2) The effective page size echoed by the result equals the clamp.
                    if (result.Page.PageSize != clampedPageSize)
                    {
                        throw new Exception(
                            $"Page {pageIndex} reported PageSize {result.Page.PageSize}, expected the clamp " +
                            $"{clampedPageSize} (requested {pageSize}).");
                    }

                    // (3) TotalRowsUnfiltered equals the scope baseline, ignoring client filter/search and the
                    //     page window — and is independent of the page index.
                    if (result.TotalRowsUnfiltered != expectedUnfiltered)
                    {
                        throw new Exception(
                            $"Page {pageIndex} reported TotalRowsUnfiltered {result.TotalRowsUnfiltered}, expected " +
                            $"the server-trusted + Scope baseline {expectedUnfiltered} " +
                            $"(rows={rows.Length}, scope={(hasScope ? scopeCity : "(none)")}, " +
                            $"filter={(hasFilter ? "Name~'" + filterName + "'" : "(none)")}, " +
                            $"search={(hasSearch ? "'" + searchTerm + "'" : "(none)")}).");
                    }
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Test-only EF context exposing <see cref="P1Customer"/> over SQLite so the generated compiled plan
    /// can root its queryable on <c>DbContext.Set&lt;P1Customer&gt;()</c>.
    /// </summary>
    private sealed class P2ListDbContext : DbContext
    {
        public P2ListDbContext(DbContextOptions<P2ListDbContext> options)
            : base(options)
        {
        }

        public DbSet<P1Customer> Customers => Set<P1Customer>();
    }

    /// <summary>
    /// Disposable per-case harness: owns an open in-memory SQLite connection, a seeded
    /// <see cref="P2ListDbContext"/>, and an <see cref="EfViewExecutor"/> wired to the REAL generated
    /// compiled plan (adopted into the execution-plan registry by <c>AddVista</c>), so List runs through
    /// the compiled (non-RUC) path.
    /// </summary>
    private sealed class P2ListHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly P2ListDbContext _context;
        private readonly ServiceProvider _provider;
        private readonly EfViewExecutor _executor;
        private readonly ViewMetadata _view;
        private readonly ViewScope _scope = new();

        private P2ListHarness(
            SqliteConnection connection,
            P2ListDbContext context,
            ServiceProvider provider,
            EfViewExecutor executor,
            ViewMetadata view)
        {
            _connection = connection;
            _context = context;
            _provider = provider;
            _executor = executor;
            _view = view;
        }

        public static P2ListHarness Create(IReadOnlyList<RowSpec> rows)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<P2ListDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new P2ListDbContext(options);
            context.Database.EnsureCreated();

            for (var i = 0; i < rows.Count; i++)
            {
                var spec = rows[i];
                context.Customers.Add(new P1Customer
                {
                    Id = i + 1,
                    Name = spec.Name,
                    City = spec.City,
                    Balance = spec.Balance,
                    Age = spec.Age,
                });
            }

            context.SaveChanges();

            // Register the view: AddVista drains GeneratedExecutionPlanStore and adopts the real generated
            // compiled plan, making List run through the compiled (non-RUC) path.
            var services = new ServiceCollection();
            services.AddVista(v => v.Register<P1CustomerView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();

            var view = registry.Get(P1CustomerView.ViewName)
                ?? throw new InvalidOperationException($"View '{P1CustomerView.ViewName}' was not registered.");

            // Sanity: the adopted plan must be the compiled facet, otherwise this would silently exercise
            // the reflection path instead of the generated compiled path (Property 2).
            if (planRegistry.Get(P1CustomerView.ViewName) is not ICompiledViewExecutionPlan)
            {
                throw new InvalidOperationException(
                    $"No generated compiled plan was adopted for '{P1CustomerView.ViewName}'; ensure the " +
                    "a2n.Vista.StyleBExecSample fixture assembly (with the generator analyzer) is referenced " +
                    "and loaded.");
            }

            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler(new DefaultQueryDialect()));

            return new P2ListHarness(connection, context, provider, executor, view);
        }

        /// <summary>Runs the compiled List path for <paramref name="request"/> (synchronously awaited).</summary>
        public ViewListResult<P1CustomerRow> List(ViewQueryRequest request) =>
            _executor.ListAsync<P1CustomerRow>(_view, request, _scope, CancellationToken.None)
                .GetAwaiter().GetResult();

        public void Dispose()
        {
            _provider.Dispose();
            _context.Dispose();
            // Disposing the connection drops the in-memory database; do it last.
            _connection.Dispose();
        }
    }
}
