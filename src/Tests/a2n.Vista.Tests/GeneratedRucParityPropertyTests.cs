// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 2 (M10, D118) compiled execution-plan read path — the CENTRAL guard
// of the feature.
//
// Feature: style-b-executable, Property 1: For any registered typed Style B view, any valid
// ViewQueryRequest (filter, search, scope, sort, paging), and any seeded data, the compiled execution
// path (generated plan) and the runtime (RUC) execution path SHALL produce identical List and Detail
// results — identical row set, identical row order, and identical unfiltered total. The RUC path is the
// reference model; the generated path is the optimized implementation under test.
//
// Validates: Requirements 3.6, 1.4, 2.2, 2.3, 3.4, 3.5
//
// Strategy (design Testing Strategy P1, model-based): a REAL source-generated Style B compiled plan
// (a2n.Vista.StyleBExecSample.P1CustomerView -> CompiledViewExecutionPlan_P1CustomerView, registered at
// module load) is run through EfViewExecutor's compiled read path, and a hand-built
// SplitViewExecutionPlan over the SAME source entity + projection is run through the RUC reflection path.
// Both execute over the same seeded SQLite dataset. A CsCheck generator produces both the seeded data and
// the request shape (filter / search / scope / sort / paging); the property asserts the two paths return
// identical List rows (set + order), identical unfiltered total, and identical Detail-by-key results.
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
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
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Test-only EF context exposing <see cref="P1Customer"/> over SQLite so both execution plans — the
/// generated compiled plan and the hand-built <see cref="SplitViewExecutionPlan{TSource, TRow}"/> — can
/// root their queryable on <c>DbContext.Set&lt;P1Customer&gt;()</c> (Decision Log D11).
/// </summary>
internal sealed class P1ParityDbContext : DbContext
{
    public P1ParityDbContext(DbContextOptions<P1ParityDbContext> options)
        : base(options)
    {
    }

    public DbSet<P1Customer> Customers => Set<P1Customer>();
}

/// <summary>
/// Property 1 — generated/RUC behavioral parity (task 7.6). The generated compiled plan is the
/// implementation under test; the <see cref="SplitViewExecutionPlan{TSource, TRow}"/> is the reference
/// model. See the file header for the full strategy.
/// </summary>
// EfViewExecutor's RUC List/Detail path and SplitViewExecutionPlan.CreateScopedQueryable are
// [RequiresUnreferencedCode]; this test drives the RUC reference model on purpose, so the trim/AOT
// diagnostic is suppressed here (tests are never trimmed).
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The parity test drives the RUC reference model by design; trimming is not used for tests.")]
public sealed class GeneratedRucParityPropertyTests
{
    private const string PropertyTag =
        "Feature: style-b-executable, Property 1: generated and RUC execution paths produce identical List/Detail results";

    // ---- candidate value pools (overlapping names/substrings make Contains/StartsWith meaningful) ----
    private static readonly string[] Names =
        { "Alice", "Alicia", "Bob", "Bobby", "Carol", "Dave", "Eve", "" };

    private static readonly string[] Cities =
        { "Paris", "Portland", "London", "Lyon", "Berlin" };

    private static readonly string[] StringFilterValues =
        { "Al", "Bob", "i", "o", "", "Alice", "xyz" };

    private static readonly string[] SortFields =
        { "Id", "Name", "City", "Balance", "Age" };

    /// <summary>A generated source row (Id is assigned positionally during seeding).</summary>
    private sealed record RowSpec(string Name, string City, double Balance, int Age);

    /// <summary>A full generated case: the dataset plus the request to run through both paths.</summary>
    private sealed record Case(RowSpec[] Rows, ViewQueryRequest Request);

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    private static readonly Gen<RowSpec> GenRow =
        from name in Pick(Names)
        from city in Pick(Cities)
        from balTenths in Gen.Int[0, 5000]
        from age in Gen.Int[0, 90]
        select new RowSpec(name, city, balTenths / 10d, age);

    private static Gen<FilterNode> GenIntLeaf(string field, int maxValue) =>
        from opIndex in Gen.Int[0, 2]
        from value in Gen.Int[0, maxValue]
        select (FilterNode)new FilterLeaf(
            field,
            opIndex switch
            {
                0 => FilterOperator.Equals,
                1 => FilterOperator.GreaterThanOrEqual,
                _ => FilterOperator.LessThanOrEqual,
            },
            value);

    private static readonly Gen<FilterNode> GenBalanceLeaf =
        from opIndex in Gen.Int[0, 2]
        from valueTenths in Gen.Int[0, 5000]
        select (FilterNode)new FilterLeaf(
            nameof(P1CustomerRow.Balance),
            opIndex switch
            {
                0 => FilterOperator.Equals,
                1 => FilterOperator.GreaterThanOrEqual,
                _ => FilterOperator.LessThanOrEqual,
            },
            valueTenths / 10d);

    private static Gen<FilterNode> GenStringLeaf(string field) =>
        from opIndex in Gen.Int[0, 3]
        from value in Pick(StringFilterValues)
        select (FilterNode)new FilterLeaf(
            field,
            opIndex switch
            {
                0 => FilterOperator.Contains,
                1 => FilterOperator.StartsWith,
                2 => FilterOperator.EndsWith,
                _ => FilterOperator.Equals,
            },
            value);

    private static readonly Gen<FilterNode> GenLeaf =
        from which in Gen.Int[0, 4]
        from leaf in which switch
        {
            0 => GenIntLeaf(nameof(P1CustomerRow.Id), 31),
            1 => GenIntLeaf(nameof(P1CustomerRow.Age), 90),
            2 => GenBalanceLeaf,
            3 => GenStringLeaf(nameof(P1CustomerRow.Name)),
            _ => GenStringLeaf(nameof(P1CustomerRow.City)),
        }
        select leaf;

    // Filter channel: null | single leaf | And(2) | Or(2) | Not(single) — exercises the closed node set.
    private static readonly Gen<FilterNode?> GenFilter =
        from shape in Gen.Int[0, 4]
        from a in GenLeaf
        from b in GenLeaf
        select shape switch
        {
            0 => (FilterNode?)null,
            1 => a,
            2 => new FilterAnd(new[] { a, b }),
            3 => new FilterOr(new[] { a, b }),
            _ => new FilterNot(a),
        };

    // Search channel: Contains over a searchable string field (Name/City), or absent.
    private static readonly Gen<FilterNode?> GenSearch =
        from present in Gen.Bool
        from field in Pick(new[] { nameof(P1CustomerRow.Name), nameof(P1CustomerRow.City) })
        from value in Pick(StringFilterValues)
        select present ? (FilterNode?)new FilterLeaf(field, FilterOperator.Contains, value) : null;

    // Scope channel: Equals over the scopable City field, or absent.
    private static readonly Gen<FilterNode?> GenScope =
        from present in Gen.Bool
        from city in Pick(Cities)
        select present ? (FilterNode?)new FilterLeaf(nameof(P1CustomerRow.City), FilterOperator.Equals, city) : null;

    private static readonly Gen<SortSpec> GenSort =
        from fieldIndex in Gen.Int[0, SortFields.Length - 1]
        from desc in Gen.Bool
        select new SortSpec(SortFields[fieldIndex], desc);

    private static readonly Gen<Case> GenCase =
        from rowCount in Gen.Int[1, 30]
        from rows in GenRow.Array[rowCount]
        from sortCount in Gen.Int[0, 2]
        from sorts in GenSort.Array[sortCount]
        from filter in GenFilter
        from search in GenSearch
        from scope in GenScope
        from page in Gen.Int[0, 3]
        from pageSize in Gen.Int[1, 12]
        select new Case(
            rows,
            new ViewQueryRequest(
                Filter: filter,
                Sort: sorts,
                Page: page,
                PageSize: pageSize,
                Search: search,
                Scope: scope));

    [Test]
    public async Task Generated_And_Ruc_Paths_Produce_Identical_List_And_Detail()
    {
        // Feature: style-b-executable, Property 1: For any registered typed Style B view, any valid
        // ViewQueryRequest (filter, search, scope, sort, paging), and any seeded data, the compiled
        // (generated) path and the runtime (RUC) path SHALL produce identical List and Detail results —
        // identical row set, identical order, identical unfiltered total. RUC is the reference model.
        var infra = ParityInfrastructure.Build();

        // Guard: the parity test is only meaningful when a REAL generated compiled plan was registered.
        await Assert.That(infra.GeneratedPlanIsCompiled)
            .IsTrue()
            .Because("the source generator must have emitted a compiled execution plan for P1CustomerView");

        GenCase.Sample(
            testCase => RunCaseParity(infra, testCase),
            iter: 200,
            print: Describe);
    }

    /// <summary>
    /// Seeds a fresh SQLite dataset from the case, runs the request through both execution paths, and
    /// returns <see langword="true"/> when their List (rows + order + unfiltered total) and Detail-by-key
    /// results are identical.
    /// </summary>
    private static bool RunCaseParity(ParityInfrastructure infra, Case testCase)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            var options = new DbContextOptionsBuilder<P1ParityDbContext>()
                .UseSqlite(connection)
                .Options;

            using var context = new P1ParityDbContext(options);
            context.Database.EnsureCreated();

            for (var i = 0; i < testCase.Rows.Length; i++)
            {
                var spec = testCase.Rows[i];
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

            // Both executors share the same context and the same metadata; only the plan registry differs,
            // which is exactly what routes one through the compiled path and the other through RUC.
            var generated = new EfViewExecutor(context, infra.Services, infra.GeneratedRegistry, new FilterCompiler(new DefaultQueryDialect()));
            var reference = new EfViewExecutor(context, infra.Services, infra.ReferenceRegistry, new FilterCompiler(new DefaultQueryDialect()));

            var scope = new ViewScope();

            // ---- List parity (rows, order, filtered + unfiltered totals) ----
            var generatedList = generated
                .ListAsync<P1CustomerRow>(infra.Metadata, testCase.Request, scope, CancellationToken.None)
                .GetAwaiter().GetResult();
            var referenceList = reference
                .ListAsync<P1CustomerRow>(infra.Metadata, testCase.Request, scope, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (generatedList.TotalRowsUnfiltered != referenceList.TotalRowsUnfiltered)
            {
                return false;
            }

            if (generatedList.Page.TotalRows != referenceList.Page.TotalRows)
            {
                return false;
            }

            if (!RowsEqualInOrder(generatedList.Page.Items, referenceList.Page.Items))
            {
                return false;
            }

            // ---- Detail-by-key parity (existing keys + fabricated absent keys) ----
            foreach (var key in DetailProbeKeys(testCase.Rows.Length))
            {
                var generatedDetail = generated
                    .DetailAsync<P1CustomerRow>(infra.Metadata, key, scope, CancellationToken.None)
                    .GetAwaiter().GetResult();
                var referenceDetail = reference
                    .DetailAsync<P1CustomerRow>(infra.Metadata, key, scope, CancellationToken.None)
                    .GetAwaiter().GetResult();

                if (!DetailEqual(generatedDetail, referenceDetail))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>Probe a representative set of keys: first, middle, last (existing) plus two absent ones.</summary>
    private static IEnumerable<int> DetailProbeKeys(int rowCount)
    {
        yield return 1;
        if (rowCount > 1)
        {
            yield return (rowCount / 2) + 1;
            yield return rowCount;
        }

        // Absent keys: 0 and one past the end. Detail must return null on both paths (R3.3).
        yield return 0;
        yield return rowCount + 5;
    }

    private static bool RowsEqualInOrder(IReadOnlyList<P1CustomerRow> a, IReadOnlyList<P1CustomerRow> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!RowEqual(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DetailEqual(P1CustomerRow? a, P1CustomerRow? b)
    {
        if (a is null)
        {
            return b is null;
        }

        return b is not null && RowEqual(a, b);
    }

    private static bool RowEqual(P1CustomerRow a, P1CustomerRow b) =>
        a.Id == b.Id
        && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
        && string.Equals(a.City, b.City, StringComparison.Ordinal)
        && a.Balance == b.Balance
        && a.Age == b.Age;

    /// <summary>Renders the failing case for a reproducible counterexample.</summary>
    private static string Describe(Case testCase)
    {
        var sb = new StringBuilder();
        sb.Append(PropertyTag).Append('\n');
        sb.Append("Rows (Id: Name, City, Balance, Age):\n");
        for (var i = 0; i < testCase.Rows.Length; i++)
        {
            var r = testCase.Rows[i];
            sb.Append("  ").Append(i + 1).Append(": '").Append(r.Name).Append("', '")
              .Append(r.City).Append("', ").Append(r.Balance).Append(", ").Append(r.Age).Append('\n');
        }

        var req = testCase.Request;
        sb.Append("Request: Page=").Append(req.Page).Append(", PageSize=").Append(req.PageSize).Append('\n');
        sb.Append("  Filter=").Append(DescribeNode(req.Filter)).Append('\n');
        sb.Append("  Search=").Append(DescribeNode(req.Search)).Append('\n');
        sb.Append("  Scope=").Append(DescribeNode(req.Scope)).Append('\n');
        sb.Append("  Sort=[").Append(string.Join(", ", req.Sort.Select(s => $"{s.Field}{(s.Descending ? " desc" : " asc")}"))).Append("]\n");
        return sb.ToString();
    }

    private static string DescribeNode(FilterNode? node) =>
        node switch
        {
            null => "(none)",
            FilterLeaf leaf => $"{leaf.Field} {leaf.Op} '{leaf.Value}'",
            FilterAnd and => $"AND({string.Join(", ", and.Children.Select(DescribeNode))})",
            FilterOr or => $"OR({string.Join(", ", or.Children.Select(DescribeNode))})",
            FilterNot not => $"NOT({DescribeNode(not.Child)})",
            _ => node.GetType().Name,
        };

    /// <summary>
    /// Builds the data-independent parity infrastructure once: forces the sample module to load (so the
    /// generated compiled plan is registered), wires the generated plan registry + metadata via AddVista,
    /// and builds a parallel registry holding the hand-built RUC reference plan over the same projection.
    /// </summary>
    private sealed class ParityInfrastructure
    {
        public required ServiceProvider Services { get; init; }

        public required IViewExecutionPlanRegistry GeneratedRegistry { get; init; }

        public required IViewExecutionPlanRegistry ReferenceRegistry { get; init; }

        public required ViewMetadata Metadata { get; init; }

        public required bool GeneratedPlanIsCompiled { get; init; }

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
            Justification = "AddVista/Register<TView> and the RUC plan are exercised on purpose; tests are not trimmed.")]
        public static ParityInfrastructure Build()
        {
            // Force the sample assembly's module to load so the generated [ModuleInitializer] registers
            // CompiledViewExecutionPlan_P1CustomerView into GeneratedExecutionPlanStore before AddVista
            // drains it.
            _ = new P1CustomerView().Name;

            var services = new ServiceCollection();
            services.AddVista(v => v.Register<P1CustomerView>());
            var provider = services.BuildServiceProvider();

            var generatedRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();
            var metadata = provider.GetRequiredService<IViewRegistry>().Get(P1CustomerView.ViewName)
                ?? throw new InvalidOperationException(
                    $"View '{P1CustomerView.ViewName}' was not registered; cannot run the parity property.");

            var generatedPlanIsCompiled = generatedRegistry.Get(P1CustomerView.ViewName) is ICompiledViewExecutionPlan;

            // The RUC reference model: a hand-built SplitViewExecutionPlan over the SAME source entity and
            // the SAME member-init projection, in its own registry so the executor routes it through the
            // reflection path (it is a plain IViewExecutionPlan, not an ICompiledViewExecutionPlan).
            var referenceRegistry = new ViewExecutionPlanRegistry();
            referenceRegistry.Add(new SplitViewExecutionPlan<P1Customer, P1CustomerRow>(
                P1CustomerView.ViewName,
                P1CustomerView.Projection));

            return new ParityInfrastructure
            {
                Services = provider,
                GeneratedRegistry = generatedRegistry,
                ReferenceRegistry = referenceRegistry,
                Metadata = metadata,
                GeneratedPlanIsCompiled = generatedPlanIsCompiled,
            };
        }
    }
}
