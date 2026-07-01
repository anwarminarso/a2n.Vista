// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.GeneratorExecSampleP5;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the source-generator Phase 2 masking runtime applied at materialization
/// (spec style-b-executable; Decision Log D118).
///
/// Feature: style-b-executable, Property 5: For any materialized row of a view with a masked field, when
/// that field's shouldMask predicate returns true for the request, the value leaving the executor on
/// List, Detail, and export SHALL equal masker(originalValue) applied to the pre-mask value; when
/// shouldMask returns false, the value SHALL equal the unchanged original. Masking SHALL apply
/// post-projection in memory without altering the SQL query.
///
/// Validates: Requirements 7.2, 7.3, 7.4, 7.5, 8.3
///
/// The view under test (<see cref="P5AccountView"/>) lives in the EF-aware consumer assembly
/// <c>a2n.Vista.GeneratorExecSampleP5</c>, where the source generator emits a REAL
/// <see cref="ICompiledViewExecutionPlan"/> (with a generated <see cref="MaskAccessor"/> for the masked
/// field) and registers it into <see cref="GeneratedExecutionPlanStore"/> at module load. Its row type is
/// a record, so the generated mask setter is a <c>with</c>-style rebuild; its masker embeds the ORIGINAL
/// value's length (so the masked output is derived from the pre-mask value, R7.3); and its shouldMask
/// predicate reads an <see cref="IP5MaskToggle"/> from request services, letting each case flip masking
/// on/off on the SAME request. A <see cref="DbCommandInterceptor"/> SQL spy proves the captured SQL is
/// byte-identical whether masking is on or off (masking is in-memory, post-projection — R7.5).
///
/// "Export" is the List path with the export page window (Page 0, PageSize = MaxExportRows): masking is
/// applied at the same materialization seam, so exercising it through that request shape covers R8.3.
/// </summary>
public sealed class ConditionalMaskingPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>Owner pool — an unmasked field that must always pass through masking unchanged.</summary>
    private static readonly string[] Owners = { "Ada", "Linus", "Grace", "Hedy", "" };

    /// <summary>Secret pool with varied lengths so the original-derived masker output varies per row.</summary>
    private static readonly string[] Secrets = { "", "x", "abcd", "p@ssw0rd", "0123456789", "secret-value" };

    /// <summary>A generated source row (Id is assigned positionally during seeding).</summary>
    private readonly record struct RowSpec(string Owner, string Secret);

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    private static readonly Gen<RowSpec> GenRow =
        from owner in Pick(Owners)
        from secret in Pick(Secrets)
        select new RowSpec(owner, secret);

    /// <summary>
    /// Touches a type in the consumer assembly so its module — and thus the generated
    /// <c>[ModuleInitializer]</c> that calls <see cref="GeneratedExecutionPlanStore.Add"/> — is loaded
    /// before any case runs. Instantiating the view is a safe, side-effect-free trigger.
    /// </summary>
    private static void EnsureFixtureModuleLoaded() => _ = new P5AccountView().Name;

    [Test]
    public void Masking_Is_Conditional_And_Leaves_The_Sql_Unchanged_On_List_Detail_And_Export()
    {
        EnsureFixtureModuleLoaded();

        var genCase =
            from rowCount in Gen.Int[0, 25]
            from rows in GenRow.Array[rowCount]
            select rows;

        genCase.Sample(
            rows =>
            {
                using var harness = P5MaskingHarness.Create(rows);

                // Expected unmasked / masked Secret values, keyed by the positional Id assigned at seeding.
                var expectedOriginal = new Dictionary<int, string>();
                var expectedMasked = new Dictionary<int, string>();
                var expectedOwner = new Dictionary<int, string>();
                for (var i = 0; i < rows.Length; i++)
                {
                    var id = i + 1;
                    expectedOriginal[id] = rows[i].Secret;
                    expectedMasked[id] = P5AccountView.Mask(rows[i].Secret);
                    expectedOwner[id] = rows[i].Owner;
                }

                var listRequest = new ViewQueryRequest(
                    Filter: null,
                    Sort: Array.Empty<SortSpec>(),
                    Page: 0,
                    PageSize: 100);

                var exportRequest = new ViewQueryRequest(
                    Filter: null,
                    Sort: Array.Empty<SortSpec>(),
                    Page: 0,
                    PageSize: harness.MaxExportRows);

                // ---- masking OFF: every value leaves the executor unchanged (R7.4). ----
                harness.Toggle.Enabled = false;

                harness.Spy.Clear();
                var listOff = harness.List(listRequest);
                var listSqlOff = harness.Spy.Snapshot();
                AssertRows(listOff, expectedOwner, expectedOriginal, "List", masked: false);

                harness.Spy.Clear();
                var exportOff = harness.List(exportRequest);
                var exportSqlOff = harness.Spy.Snapshot();
                AssertRows(exportOff, expectedOwner, expectedOriginal, "Export", masked: false);

                foreach (var id in expectedOriginal.Keys)
                {
                    var detail = harness.Detail(id) ?? throw new Exception($"Detail returned null for id {id}.");
                    AssertSecret(detail, expectedOriginal[id], "Detail (off)", masked: false);
                }

                // ---- masking ON: the masked field becomes masker(original); others unchanged (R7.2/R7.3). ----
                harness.Toggle.Enabled = true;

                harness.Spy.Clear();
                var listOn = harness.List(listRequest);
                var listSqlOn = harness.Spy.Snapshot();
                AssertRows(listOn, expectedOwner, expectedMasked, "List", masked: true);

                harness.Spy.Clear();
                var exportOn = harness.List(exportRequest);
                var exportSqlOn = harness.Spy.Snapshot();
                AssertRows(exportOn, expectedOwner, expectedMasked, "Export", masked: true);

                int detailObservations = 0;
                harness.Spy.Clear();
                foreach (var id in expectedOriginal.Keys)
                {
                    var detail = harness.Detail(id) ?? throw new Exception($"Detail returned null for id {id}.");
                    AssertSecret(detail, expectedMasked[id], "Detail (on)", masked: true);
                    detailObservations++;
                }
                var detailSqlOn = harness.Spy.Snapshot();

                // Re-capture Detail SQL with masking off for the on/off comparison.
                harness.Toggle.Enabled = false;
                harness.Spy.Clear();
                foreach (var id in expectedOriginal.Keys)
                {
                    _ = harness.Detail(id);
                }
                var detailSqlOff = harness.Spy.Snapshot();

                // ---- R7.5 / R8.3: masking is in-memory and post-projection, so the SQL is identical
                //      whether masking is on or off, on every read path. ----
                AssertSameSql(listSqlOff, listSqlOn, "List");
                AssertSameSql(exportSqlOff, exportSqlOn, "Export");
                AssertSameSql(detailSqlOff, detailSqlOn, "Detail");
            },
            iter: Iterations);
    }

    /// <summary>
    /// Asserts every returned row's <c>Owner</c> is unchanged and its <c>Secret</c> equals the expected
    /// value for the row's id (the original when <paramref name="masked"/> is false, the masked token when
    /// true), and that exactly the seeded ids are present.
    /// </summary>
    private static void AssertRows(
        ViewListResult<P5AccountRow> result,
        IReadOnlyDictionary<int, string> expectedOwner,
        IReadOnlyDictionary<int, string> expectedSecret,
        string path,
        bool masked)
    {
        var seen = new HashSet<int>();
        foreach (var row in result.Page.Items)
        {
            if (!expectedSecret.TryGetValue(row.Id, out var secret))
            {
                throw new Exception($"{path} returned an unexpected row id {row.Id}.");
            }

            seen.Add(row.Id);

            if (!string.Equals(row.Owner, expectedOwner[row.Id], StringComparison.Ordinal))
            {
                throw new Exception(
                    $"{path} row {row.Id}: unmasked field Owner changed — got '{row.Owner}', " +
                    $"expected '{expectedOwner[row.Id]}'.");
            }

            AssertSecret(row, secret, path, masked);
        }

        if (seen.Count != expectedSecret.Count)
        {
            throw new Exception(
                $"{path} returned {seen.Count} of {expectedSecret.Count} seeded rows; masking must not " +
                "change result-set membership.");
        }
    }

    /// <summary>Asserts a single row's masked field equals the expected (masked or original) value.</summary>
    private static void AssertSecret(P5AccountRow row, string expected, string path, bool masked)
    {
        if (!string.Equals(row.Secret, expected, StringComparison.Ordinal))
        {
            var kind = masked ? "masked" : "unmasked";
            throw new Exception(
                $"{path} row {row.Id}: {kind} Secret was '{row.Secret}', expected '{expected}'.");
        }
    }

    /// <summary>Asserts two captured SQL command sequences are identical (order and text).</summary>
    private static void AssertSameSql(IReadOnlyList<string> off, IReadOnlyList<string> on, string path)
    {
        if (!off.SequenceEqual(on, StringComparer.Ordinal))
        {
            throw new Exception(
                $"{path} emitted different SQL with masking on vs off — masking must be in-memory and " +
                $"post-projection (R7.5).{Environment.NewLine}" +
                $"off ({off.Count}): [{string.Join(" | ", off)}]{Environment.NewLine}" +
                $"on  ({on.Count}): [{string.Join(" | ", on)}]");
        }
    }

    /// <summary>The DI-resolvable masking toggle the view's predicate reads; flipped per request.</summary>
    private sealed class P5MaskToggle : IP5MaskToggle
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// A <see cref="DbCommandInterceptor"/> recording the text of every executed command (reader / scalar
    /// / non-query, sync and async). Used to prove the SQL is identical whether masking is on or off.
    /// </summary>
    private sealed class P5SqlSpyInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _executed = new();

        public void Clear() => _executed.Clear();

        /// <summary>Returns a snapshot copy of the commands executed since the last <see cref="Clear"/>.</summary>
        public IReadOnlyList<string> Snapshot() => _executed.ToArray();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _executed.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _executed.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            _executed.Add(command.CommandText);
            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            _executed.Add(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            _executed.Add(command.CommandText);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _executed.Add(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Test-only EF context exposing <see cref="P5AccountSource"/> over SQLite with the SQL spy.</summary>
    private sealed class P5MaskingDbContext : DbContext
    {
        private readonly P5SqlSpyInterceptor _spy;

        public P5MaskingDbContext(DbContextOptions<P5MaskingDbContext> options, P5SqlSpyInterceptor spy)
            : base(options) => _spy = spy;

        public DbSet<P5AccountSource> Accounts => Set<P5AccountSource>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.AddInterceptors(_spy);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<P5AccountSource>().HasKey(a => a.Id);
    }

    /// <summary>
    /// Disposable per-case harness: owns an open in-memory SQLite connection, a seeded
    /// <see cref="P5MaskingDbContext"/> with the SQL spy attached, the masking toggle (registered into the
    /// provider so the view predicate resolves it), and an <see cref="EfViewExecutor"/> wired to the REAL
    /// generated compiled plan (adopted into the execution-plan registry by <c>AddVista</c>).
    /// </summary>
    private sealed class P5MaskingHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly P5MaskingDbContext _context;
        private readonly ServiceProvider _provider;
        private readonly EfViewExecutor _executor;
        private readonly ViewMetadata _view;
        private readonly ViewScope _scope = new();

        private P5MaskingHarness(
            SqliteConnection connection,
            P5MaskingDbContext context,
            ServiceProvider provider,
            EfViewExecutor executor,
            ViewMetadata view,
            P5SqlSpyInterceptor spy,
            P5MaskToggle toggle)
        {
            _connection = connection;
            _context = context;
            _provider = provider;
            _executor = executor;
            _view = view;
            Spy = spy;
            Toggle = toggle;
        }

        /// <summary>The SQL spy attached to the context.</summary>
        public P5SqlSpyInterceptor Spy { get; }

        /// <summary>The masking toggle the view predicate reads; flip <c>Enabled</c> to mask on/off.</summary>
        public P5MaskToggle Toggle { get; }

        /// <summary>The view's configured export-row cap, used to shape the export request.</summary>
        public int MaxExportRows => _view.Limits.MaxExportRows;

        public static P5MaskingHarness Create(IReadOnlyList<RowSpec> rows)
        {
            var spy = new P5SqlSpyInterceptor();

            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<P5MaskingDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new P5MaskingDbContext(options, spy);
            context.Database.EnsureCreated();

            for (var i = 0; i < rows.Count; i++)
            {
                var spec = rows[i];
                context.Accounts.Add(new P5AccountSource
                {
                    Id = i + 1,
                    Owner = spec.Owner,
                    Secret = spec.Secret,
                });
            }

            context.SaveChanges();

            // The masking toggle is a singleton the view's shouldMask predicate resolves from the executor's
            // request services. AddVista drains GeneratedExecutionPlanStore and adopts the real generated
            // compiled plan, making List/Detail run through the compiled (non-RUC) path with the generated
            // MaskAccessor.
            var toggle = new P5MaskToggle { Enabled = false };
            var services = new ServiceCollection();
            services.AddSingleton<IP5MaskToggle>(toggle);
            services.AddVista(v => v.Register<P5AccountView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();

            var view = registry.Get(P5AccountView.ViewName)
                ?? throw new InvalidOperationException($"View '{P5AccountView.ViewName}' was not registered.");

            // Sanity: the adopted plan must be the compiled facet (otherwise this would exercise the
            // reflection path and the generated MaskAccessor would not be tested).
            if (planRegistry.Get(P5AccountView.ViewName) is not ICompiledViewExecutionPlan)
            {
                throw new InvalidOperationException(
                    $"No generated compiled plan was adopted for '{P5AccountView.ViewName}'; ensure the " +
                    "a2n.Vista.GeneratorExecSampleP5 fixture assembly (with the generator analyzer) is " +
                    "referenced and loaded.");
            }

            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler());

            return new P5MaskingHarness(connection, context, provider, executor, view, spy, toggle);
        }

        /// <summary>Runs the compiled List path for <paramref name="request"/> (synchronously awaited).</summary>
        public ViewListResult<P5AccountRow> List(ViewQueryRequest request) =>
            _executor.ListAsync<P5AccountRow>(_view, request, _scope, CancellationToken.None)
                .GetAwaiter().GetResult();

        /// <summary>Runs the compiled Detail-by-key path for the single-key view.</summary>
        public P5AccountRow? Detail(int id) =>
            _executor.DetailAsync<P5AccountRow>(_view, id, _scope, CancellationToken.None)
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
