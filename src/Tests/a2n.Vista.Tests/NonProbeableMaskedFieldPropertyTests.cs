// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.GeneratorExecSampleP6;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the source-generator Phase 2 masking security guarantee — masked-without-opt-in
/// fields are non-probeable (spec style-b-executable; Decision Log D118 / D95).
///
/// Feature: style-b-executable, Property 6: For any sequence of filter/search requests and any two datasets
/// that differ only in the value of a masked-without-opt-in field, the result-set membership SHALL be
/// identical across the whole sequence, and the rejection outcome for a disallowed filter/search on that
/// field SHALL be identical regardless of the masked value — so neither the result set nor the error path
/// leaks information about the masked value (no binary-search probing channel). A masked-without-opt-in
/// string field SHALL never contribute to global search membership.
///
/// Validates: Requirements 8.2, 8.5, 8.6
///
/// The view under test (<see cref="P6PersonView"/>) lives in the EF-aware consumer assembly
/// <c>a2n.Vista.GeneratorExecSampleP6</c>, where the source generator emits a REAL
/// <see cref="ICompiledViewExecutionPlan"/> and registers it into <see cref="GeneratedExecutionPlanStore"/>
/// at module load. It projects three fields: <c>Id</c> (key), <c>Name</c> (an ordinary
/// filterable/sortable/searchable string), and <c>Secret</c> (a masked-without-opt-in string that D95
/// makes non-filterable and non-searchable). Each generated case seeds TWO SQLite databases that are
/// byte-for-byte identical except for the <c>Secret</c> values, then runs a generated sequence of
/// filter/search requests through both and asserts:
/// <list type="number">
/// <item>allowed requests return identical result-set membership (by key) across the two datasets (R8.5);</item>
/// <item>a global search built from the view's searchable fields never matches on <c>Secret</c> (R8.2) —
/// enforced by the same membership invariance, since the only difference between the datasets is the masked
/// value;</item>
/// <item>a disallowed filter/search on <c>Secret</c> is rejected with a byte-identical error regardless of
/// the masked value (R8.6).</item>
/// </list>
/// </summary>
public sealed class NonProbeableMaskedFieldPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>Name pool with overlapping substrings so a filter/search prunes membership meaningfully.</summary>
    private static readonly string[] Names = { "Ada", "Adam", "Linus", "Grace", "Gregory", "Bob", "" };

    /// <summary>
    /// Secret value pool. Includes tokens that share substrings with <see cref="Names"/> ("Ada", "Bob")
    /// so that, were the masked field ever folded into a filter/search, a probe term would match a
    /// different row set across the two datasets — the very leak this property forbids.
    /// </summary>
    private static readonly string[] Secrets = { "SSN-001", "SSN-Ada", "SSN-Bob", "tax-42", "pin-7", "" };

    /// <summary>Free-text probe terms drawn from both the name and secret pools, so secret-only terms are exercised.</summary>
    private static readonly string[] ProbeTerms = { "Ad", "Gr", "Bob", "o", "", "SSN", "Ada", "tax", "pin", "001" };

    /// <summary>A generated source row: Id is positional; the two secret values differ across the datasets.</summary>
    private readonly record struct RowSpec(string Name, string SecretA, string SecretB);

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    /// <summary>
    /// Generates a row whose <c>SecretA</c> and <c>SecretB</c> are guaranteed to differ (the datasets must
    /// differ ONLY in the masked value, and they must actually differ for the property to bite).
    /// </summary>
    private static readonly Gen<RowSpec> GenRow =
        from name in Pick(Names)
        from secretA in Pick(Secrets)
        from secretB in Pick(Secrets)
        select new RowSpec(
            name,
            secretA,
            string.Equals(secretA, secretB, StringComparison.Ordinal) ? secretB + "-x" : secretB);

    /// <summary>The kinds of probe a generated request sequence is built from.</summary>
    private enum ProbeKind
    {
        /// <summary>Allowed: filter <c>Name</c> with Contains over a probe term.</summary>
        FilterNameContains,

        /// <summary>Allowed: filter <c>Name</c> with Equals over a name value.</summary>
        FilterNameEquals,

        /// <summary>Allowed: filter <c>Id</c> with Equals over a key value.</summary>
        FilterIdEquals,

        /// <summary>Allowed: global search (built from the view's searchable fields) over a probe term.</summary>
        GlobalSearch,

        /// <summary>Disallowed: filter the masked-without-opt-in <c>Secret</c> field (must be rejected).</summary>
        FilterSecret,

        /// <summary>Disallowed: search the masked-without-opt-in <c>Secret</c> field (must be rejected).</summary>
        SearchSecret,
    }

    /// <summary>A single probe in a generated sequence (term/key are interpreted per <see cref="Kind"/>).</summary>
    private readonly record struct Probe(ProbeKind Kind, string Term, int Key);

    private static readonly Gen<Probe> GenProbe =
        from kind in Gen.Int[0, 5].Select(i => (ProbeKind)i)
        from term in Pick(ProbeTerms)
        from name in Pick(Names)
        from key in Gen.Int[0, 20]
        select new Probe(
            kind,
            kind == ProbeKind.FilterNameEquals ? name : term,
            key);

    /// <summary>
    /// Touches a type in the consumer assembly so its module — and thus the generated
    /// <c>[ModuleInitializer]</c> that calls <see cref="GeneratedExecutionPlanStore.Add"/> — is loaded
    /// before any case runs. Instantiating the view is a safe, side-effect-free trigger.
    /// </summary>
    private static void EnsureFixtureModuleLoaded() => _ = new P6PersonView().Name;

    [Test]
    public void Masked_Field_Is_NonProbeable_Across_Filter_And_Search_Sequences()
    {
        EnsureFixtureModuleLoaded();

        var genCase =
            from rowCount in Gen.Int[1, 15]
            from rows in GenRow.Array[rowCount]
            from probes in GenProbe.Array[1, 8]
            select (rows, probes);

        genCase.Sample(
            input =>
            {
                var (rows, probes) = input;

                // Two datasets identical in every projected field EXCEPT the masked Secret value.
                var datasetA = rows.Select((r, i) => new P6PersonSource { Id = i + 1, Name = r.Name, Secret = r.SecretA }).ToArray();
                var datasetB = rows.Select((r, i) => new P6PersonSource { Id = i + 1, Name = r.Name, Secret = r.SecretB }).ToArray();

                using var harnessA = P6Harness.Create(datasetA);
                using var harnessB = P6Harness.Create(datasetB);

                // The searchable string fields the view exposes for global search. D95 must keep Secret OUT
                // of this set, so a global search can never match against the masked value (R8.2).
                var searchableFields = harnessA.View.Fields
                    .Where(f => f.IsSearchable && f.ClrType == typeof(string))
                    .Select(f => f.Name)
                    .ToArray();

                // Self-check (not the property): D95 defaults must hold, otherwise the fixture is wrong.
                var secret = harnessA.View.Fields.Single(f => f.Name == nameof(P6PersonRow.Secret));
                if (secret.IsFilterable || secret.IsSearchable || searchableFields.Contains(nameof(P6PersonRow.Secret)))
                {
                    throw new Exception(
                        "Fixture invariant broken: the masked 'Secret' field must default to non-filterable " +
                        "and excluded from global search (D95).");
                }

                foreach (var probe in probes)
                {
                    switch (probe.Kind)
                    {
                        case ProbeKind.FilterNameContains:
                            AssertSameMembership(
                                harnessA, harnessB,
                                new ViewQueryRequest(
                                    Filter: new FilterLeaf(nameof(P6PersonRow.Name), FilterOperator.Contains, probe.Term),
                                    Sort: Array.Empty<SortSpec>(), Page: 0, PageSize: 100),
                                probe);
                            break;

                        case ProbeKind.FilterNameEquals:
                            AssertSameMembership(
                                harnessA, harnessB,
                                new ViewQueryRequest(
                                    Filter: new FilterLeaf(nameof(P6PersonRow.Name), FilterOperator.Equals, probe.Term),
                                    Sort: Array.Empty<SortSpec>(), Page: 0, PageSize: 100),
                                probe);
                            break;

                        case ProbeKind.FilterIdEquals:
                            AssertSameMembership(
                                harnessA, harnessB,
                                new ViewQueryRequest(
                                    Filter: new FilterLeaf(nameof(P6PersonRow.Id), FilterOperator.Equals, probe.Key),
                                    Sort: Array.Empty<SortSpec>(), Page: 0, PageSize: 100),
                                probe);
                            break;

                        case ProbeKind.GlobalSearch:
                            // A faithful global search: OR(Contains) over the view's searchable fields only.
                            // Because Secret is excluded by D95, a probe term equal to a Secret value can
                            // never match through it — and any such leak would diverge the A/B membership.
                            AssertSameMembership(
                                harnessA, harnessB,
                                new ViewQueryRequest(
                                    Filter: null,
                                    Sort: Array.Empty<SortSpec>(), Page: 0, PageSize: 100,
                                    Search: BuildGlobalSearch(searchableFields, probe.Term)),
                                probe);
                            break;

                        case ProbeKind.FilterSecret:
                            // Disallowed: filtering the masked-without-opt-in field. Must be rejected with a
                            // byte-identical error regardless of the underlying (differing) masked value.
                            AssertIdenticalRejection(
                                harnessA, harnessB,
                                new ViewQueryRequest(
                                    Filter: new FilterLeaf(nameof(P6PersonRow.Secret), FilterOperator.Equals, probe.Term),
                                    Sort: Array.Empty<SortSpec>(), Page: 0, PageSize: 100),
                                probe);
                            break;

                        case ProbeKind.SearchSecret:
                            // Disallowed: searching the masked-without-opt-in field via the Search channel.
                            AssertIdenticalRejection(
                                harnessA, harnessB,
                                new ViewQueryRequest(
                                    Filter: null,
                                    Sort: Array.Empty<SortSpec>(), Page: 0, PageSize: 100,
                                    Search: new FilterLeaf(nameof(P6PersonRow.Secret), FilterOperator.Contains, probe.Term)),
                                probe);
                            break;

                        default:
                            throw new Exception($"Unhandled probe kind '{probe.Kind}'.");
                    }
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Expands a free-text term into the global-search sub-tree an adapter would build: <c>Contains</c>
    /// over each searchable string field, OR-ed together (D111). The masked field is absent from
    /// <paramref name="searchableFields"/>, so it never participates.
    /// </summary>
    private static FilterNode? BuildGlobalSearch(IReadOnlyList<string> searchableFields, string term)
    {
        if (searchableFields.Count == 0)
        {
            return null;
        }

        if (searchableFields.Count == 1)
        {
            return new FilterLeaf(searchableFields[0], FilterOperator.Contains, term);
        }

        return new FilterOr(searchableFields
            .Select(f => (FilterNode)new FilterLeaf(f, FilterOperator.Contains, term))
            .ToArray());
    }

    /// <summary>
    /// Runs <paramref name="request"/> through both datasets and asserts the returned key set is identical —
    /// the masked value cannot change which rows are returned (R8.5 / R8.2).
    /// </summary>
    private static void AssertSameMembership(P6Harness a, P6Harness b, ViewQueryRequest request, Probe probe)
    {
        var membershipA = Membership(a, request);
        var membershipB = Membership(b, request);

        if (!membershipA.SetEquals(membershipB))
        {
            throw new Exception(
                $"Result-set membership leaked the masked value: probe {Describe(probe)} returned keys " +
                $"[{string.Join(",", membershipA.OrderBy(x => x))}] on dataset A but " +
                $"[{string.Join(",", membershipB.OrderBy(x => x))}] on dataset B. The two datasets differ " +
                "only in the masked 'Secret' value, so membership must be identical (Property 6 / R8.5).");
        }
    }

    /// <summary>
    /// Runs a disallowed <paramref name="request"/> through both datasets and asserts BOTH are rejected with
    /// a byte-identical <see cref="FilterValidationException"/> (code, field, operator, message) — the error
    /// path leaks nothing about the masked value (R8.6).
    /// </summary>
    private static void AssertIdenticalRejection(P6Harness a, P6Harness b, ViewQueryRequest request, Probe probe)
    {
        var rejectionA = CaptureRejection(a, request, probe);
        var rejectionB = CaptureRejection(b, request, probe);

        if (rejectionA.Code != rejectionB.Code
            || !string.Equals(rejectionA.Field, rejectionB.Field, StringComparison.Ordinal)
            || rejectionA.Operator != rejectionB.Operator
            || !string.Equals(rejectionA.Message, rejectionB.Message, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Rejection of disallowed probe {Describe(probe)} differed across datasets — the error path " +
                "leaked the masked value (Property 6 / R8.6).\n" +
                $"  A: code={rejectionA.Code}, field='{rejectionA.Field}', op={rejectionA.Operator}, msg='{rejectionA.Message}'\n" +
                $"  B: code={rejectionB.Code}, field='{rejectionB.Field}', op={rejectionB.Operator}, msg='{rejectionB.Message}'");
        }

        // The rejection must come from the field-whitelist path (masked → not allowed for filter/search).
        if (rejectionA.Code != FilterErrorCode.FieldNotAllowed)
        {
            throw new Exception(
                $"Disallowed probe {Describe(probe)} was rejected with code '{rejectionA.Code}', but a masked " +
                "field rejection must be FieldNotAllowed (the field-whitelist path).");
        }
    }

    /// <summary>Lists the view for <paramref name="request"/> and returns the set of returned keys (Id).</summary>
    private static HashSet<int> Membership(P6Harness harness, ViewQueryRequest request)
    {
        var result = harness.List(request);
        return result.Page.Items.Select(r => r.Id).ToHashSet();
    }

    /// <summary>Runs a request expected to be rejected and returns the captured exception (fails if none).</summary>
    private static FilterValidationException CaptureRejection(P6Harness harness, ViewQueryRequest request, Probe probe)
    {
        try
        {
            _ = harness.List(request);
        }
        catch (FilterValidationException ex)
        {
            return ex;
        }

        throw new Exception(
            $"Disallowed probe {Describe(probe)} on the masked 'Secret' field was NOT rejected — a masked " +
            "field with no opt-in must be refused before any query executes (Property 6 / R8.6).");
    }

    private static string Describe(Probe probe) => $"({probe.Kind}, term='{probe.Term}', key={probe.Key})";

    /// <summary>Test-only EF context exposing <see cref="P6PersonSource"/> over SQLite.</summary>
    private sealed class P6Db : DbContext
    {
        public P6Db(DbContextOptions<P6Db> options)
            : base(options)
        {
        }

        public DbSet<P6PersonSource> People => Set<P6PersonSource>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<P6PersonSource>().HasKey(p => p.Id);
    }

    /// <summary>
    /// Disposable per-dataset harness: owns an open in-memory SQLite connection, a seeded
    /// <see cref="P6Db"/>, and an <see cref="EfViewExecutor"/> wired to the REAL generated compiled plan
    /// (adopted into the execution-plan registry by <c>AddVista</c>), so List runs through the compiled
    /// (non-RUC) path.
    /// </summary>
    private sealed class P6Harness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly P6Db _context;
        private readonly ServiceProvider _provider;
        private readonly EfViewExecutor _executor;
        private readonly ViewScope _scope = new();

        private P6Harness(
            SqliteConnection connection,
            P6Db context,
            ServiceProvider provider,
            EfViewExecutor executor,
            ViewMetadata view)
        {
            _connection = connection;
            _context = context;
            _provider = provider;
            _executor = executor;
            View = view;
        }

        /// <summary>The registered view metadata (used to enumerate the searchable-field whitelist).</summary>
        public ViewMetadata View { get; }

        public static P6Harness Create(IReadOnlyList<P6PersonSource> rows)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<P6Db>()
                .UseSqlite(connection)
                .Options;

            var context = new P6Db(options);
            context.Database.EnsureCreated();

            foreach (var row in rows)
            {
                context.People.Add(new P6PersonSource { Id = row.Id, Name = row.Name, Secret = row.Secret });
            }

            context.SaveChanges();

            // Register the view: AddVista drains GeneratedExecutionPlanStore and adopts the real generated
            // compiled plan, making List run through the compiled (non-RUC) path.
            var services = new ServiceCollection();
            services.AddVista(v => v.Register<P6PersonView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();

            var view = registry.Get(P6PersonView.ViewName)
                ?? throw new InvalidOperationException($"View '{P6PersonView.ViewName}' was not registered.");

            // Sanity: the adopted plan must be the compiled facet, otherwise this would silently exercise
            // the reflection path instead of the generated compiled path (Property 6).
            if (planRegistry.Get(P6PersonView.ViewName) is not ICompiledViewExecutionPlan)
            {
                throw new InvalidOperationException(
                    $"No generated compiled plan was adopted for '{P6PersonView.ViewName}'; ensure the " +
                    "a2n.Vista.GeneratorExecSampleP6 fixture assembly (with the generator analyzer) is " +
                    "referenced and loaded.");
            }

            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler(new DefaultQueryDialect()));

            return new P6Harness(connection, context, provider, executor, view);
        }

        /// <summary>Runs the compiled List path for <paramref name="request"/> (synchronously awaited).</summary>
        public ViewListResult<P6PersonRow> List(ViewQueryRequest request) =>
            _executor.ListAsync<P6PersonRow>(View, request, _scope, CancellationToken.None)
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
