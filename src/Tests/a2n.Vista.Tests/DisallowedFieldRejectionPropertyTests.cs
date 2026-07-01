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
using a2n.Vista.GeneratorExecSampleP4;
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
/// Property-based test for the source-generator Phase 2 compiled read path's field-whitelist guard
/// (spec style-b-executable; Decision Log D118).
///
/// Feature: style-b-executable, Property 4: For any filter or sort request that references a field which
/// is not a projected field, or which is masked without an explicit Filterable(true)/Operators(...)
/// opt-in, the System SHALL reject the whole request through the existing FilterCompiler field-whitelist
/// error path before any query executes — returning a not-permitted/not-filterable error, no result
/// rows, and no partial result, emitting and executing no SQL.
///
/// Validates: Requirements 2.4, 8.1
///
/// The view under test (<see cref="P4PersonView"/>) lives in the EF-aware consumer assembly
/// <c>a2n.Vista.GeneratorExecSampleP4</c>, where the source generator emits a REAL
/// <see cref="ICompiledViewExecutionPlan"/> and registers it into
/// <see cref="GeneratedExecutionPlanStore"/> at module load. It carries a masked-without-opt-in string
/// field (<c>Secret</c>), so D95 makes that field non-filterable — one of the two rejection vectors the
/// property exercises (the other being a non-projected field name). A
/// <see cref="DbCommandInterceptor"/> SQL spy is wired into the SQLite context and asserts that no
/// command was executed for the rejected request.
/// </summary>
public sealed class DisallowedFieldRejectionPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>The projected field names of <see cref="P4PersonView"/> — anything else is non-projected.</summary>
    private static readonly HashSet<string> ProjectedFields =
        new(StringComparer.Ordinal) { "Id", "Name", "Secret" };

    /// <summary>
    /// Touches a type in the consumer assembly so its module — and thus the generated
    /// <c>[ModuleInitializer]</c> that calls <see cref="GeneratedExecutionPlanStore.Add"/> — is loaded
    /// before any case runs. Instantiating the view is a safe, side-effect-free trigger.
    /// </summary>
    private static void EnsureFixtureModuleLoaded() => _ = new P4PersonView().Name;

    [Test]
    public void Disallowed_Filter_Or_Sort_Field_Is_Rejected_Before_Any_Sql()
    {
        EnsureFixtureModuleLoaded();

        // A pool of clearly non-projected field names (none of Id/Name/Secret). Each is guaranteed to be
        // absent from the view projection, so it must be refused as an unknown field.
        var genNonProjected = Gen.OneOf(
            Gen.Const("Ghost"),
            Gen.Const("Phantom"),
            Gen.Const("Unknown"),
            Gen.Const("Xyzzy"),
            Gen.Const("Missing"),
            Gen.Const("Email"),
            Gen.Const("Password"),
            Gen.Const("id"),    // case-sensitive whitelist: 'id' != projected 'Id'
            Gen.Const("name"),  // case-sensitive whitelist: 'name' != projected 'Name'
            Gen.Const("secret"));

        // Bad FILTER field: either the masked-without-opt-in projected field (rejected as not-filterable)
        // or a non-projected field (rejected as unknown). Both must be refused before any SQL (R2.4/R8.1).
        var genBadFilterField = Gen.OneOf(Gen.Const("Secret"), genNonProjected);

        // Bad SORT field: a non-projected field name (the masked field stays sortable by default, so it is
        // NOT a sort-rejection vector). A non-projected sort field is refused as unknown before any SQL.
        var genBadSortField = genNonProjected;

        // Each case picks one bad filter field, one bad sort field, a sort direction, and a filter value
        // seed (only ever used to build the leaf — validation throws before the value is read).
        Gen.Select(genBadFilterField, genBadSortField, Gen.Bool, Gen.Int[0, 9999]).Sample(
            input =>
            {
                var (badFilterField, badSortField, descending, valueSeed) = input;

                // Guard the generators' contract: a non-projected name must truly be non-projected, and the
                // masked field must be a projected, non-filterable field. (Self-check, not the property.)
                if (badFilterField != "Secret" && ProjectedFields.Contains(badFilterField))
                {
                    throw new Exception($"Generator produced a projected name '{badFilterField}' as a bad filter field.");
                }

                if (ProjectedFields.Contains(badSortField))
                {
                    throw new Exception($"Generator produced a projected name '{badSortField}' as a bad sort field.");
                }

                using var harness = P4RejectionHarness.Create();

                // --- Disallowed FILTER field: rejected via the whitelist, no SQL executed. ---
                var filterRequest = new ViewQueryRequest(
                    Filter: new FilterLeaf(badFilterField, FilterOperator.Equals, "v" + valueSeed),
                    Sort: Array.Empty<SortSpec>(),
                    Page: 0,
                    PageSize: 10);

                AssertRejectedWithoutSql(harness, filterRequest, "filter", badFilterField);

                // --- Disallowed SORT field: rejected via the whitelist, no SQL executed. ---
                var sortRequest = new ViewQueryRequest(
                    Filter: null,
                    Sort: new[] { new SortSpec(badSortField, descending) },
                    Page: 0,
                    PageSize: 10);

                AssertRejectedWithoutSql(harness, sortRequest, "sort", badSortField);
            },
            iter: Iterations);
    }

    /// <summary>
    /// Drives the compiled List path for <paramref name="request"/> and asserts the whole request is
    /// rejected with a <see cref="FilterValidationException"/> (a not-permitted/not-filterable error) and
    /// that the SQL spy recorded <b>no</b> executed command and the executor produced no result.
    /// </summary>
    private static void AssertRejectedWithoutSql(
        P4RejectionHarness harness,
        ViewQueryRequest request,
        string channel,
        string field)
    {
        harness.Spy.Clear();

        FilterValidationException? rejection = null;
        object? result = null;
        try
        {
            result = harness.List(request);
        }
        catch (FilterValidationException ex)
        {
            rejection = ex;
        }

        if (rejection is null)
        {
            throw new Exception(
                $"Disallowed {channel} field '{field}' was NOT rejected: the request returned a result " +
                $"({(result is null ? "null" : "non-null")}) instead of throwing FilterValidationException.");
        }

        // The rejection must come from the field-whitelist path: unknown field (non-projected) or
        // field-not-allowed (masked-without-opt-in). Anything else means a different code path ran.
        if (rejection.Code is not (FilterErrorCode.UnknownField or FilterErrorCode.FieldNotAllowed))
        {
            throw new Exception(
                $"Disallowed {channel} field '{field}' was rejected with code '{rejection.Code}', " +
                "but the field-whitelist path yields UnknownField or FieldNotAllowed.");
        }

        // The crux of Property 4 / R2.4 / R8.1: no SQL is emitted or executed for the rejected request.
        if (harness.Spy.ExecutedCommands.Count != 0)
        {
            throw new Exception(
                $"Disallowed {channel} field '{field}' was rejected, but {harness.Spy.ExecutedCommands.Count} " +
                $"SQL command(s) executed before the rejection: [{string.Join(" | ", harness.Spy.ExecutedCommands)}]. " +
                "Property 4 requires the request be refused before any query executes.");
        }
    }

    /// <summary>
    /// A <see cref="DbCommandInterceptor"/> that records the command text of every command the provider
    /// executes (reader / scalar / non-query, sync and async). The disallowed-field property asserts this
    /// list is empty for a rejected request.
    /// </summary>
    private sealed class P4SqlSpyInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _executed = new();

        /// <summary>The command texts executed since the last <see cref="Clear"/>.</summary>
        public IReadOnlyList<string> ExecutedCommands => _executed;

        /// <summary>Forgets all recorded commands (called after setup and before each probed request).</summary>
        public void Clear() => _executed.Clear();

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

    /// <summary>The EF source entity for <see cref="P4PersonView"/>, exposed by <see cref="P4TestDbContext"/>.</summary>
    private sealed class P4TestDbContext : DbContext
    {
        private readonly P4SqlSpyInterceptor _spy;

        public P4TestDbContext(DbContextOptions<P4TestDbContext> options, P4SqlSpyInterceptor spy)
            : base(options) => _spy = spy;

        public DbSet<P4PersonSource> People => Set<P4PersonSource>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.AddInterceptors(_spy);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<P4PersonSource>().HasKey(p => p.Id);
    }

    /// <summary>
    /// Disposable per-case harness: owns an open in-memory SQLite connection, a seeded
    /// <see cref="P4TestDbContext"/> with the SQL spy attached, and an <see cref="EfViewExecutor"/> wired
    /// to the REAL generated compiled plan (adopted into the execution-plan registry by <c>AddVista</c>).
    /// </summary>
    private sealed class P4RejectionHarness : IDisposable
    {
        private const string ViewName = "p4-disallowed-field-person";

        private readonly SqliteConnection _connection;
        private readonly P4TestDbContext _context;
        private readonly ServiceProvider _provider;
        private readonly EfViewExecutor _executor;
        private readonly ViewMetadata _view;
        private readonly ViewScope _scope = new();

        private P4RejectionHarness(
            SqliteConnection connection,
            P4TestDbContext context,
            ServiceProvider provider,
            EfViewExecutor executor,
            ViewMetadata view,
            P4SqlSpyInterceptor spy)
        {
            _connection = connection;
            _context = context;
            _provider = provider;
            _executor = executor;
            _view = view;
            Spy = spy;
        }

        /// <summary>The SQL spy attached to the context; asserted empty after a rejected request.</summary>
        public P4SqlSpyInterceptor Spy { get; }

        public static P4RejectionHarness Create()
        {
            var spy = new P4SqlSpyInterceptor();

            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<P4TestDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new P4TestDbContext(options, spy);
            context.Database.EnsureCreated();

            // Seed a few rows so a non-rejected query WOULD hit the database — proving the spy is wired and
            // that the empty-command assertion is meaningful (the rejection, not an empty table, is why no
            // SQL runs).
            context.People.AddRange(
                new P4PersonSource { Id = 1, Name = "Ada", Secret = "s1" },
                new P4PersonSource { Id = 2, Name = "Linus", Secret = "s2" },
                new P4PersonSource { Id = 3, Name = "Grace", Secret = "s3" });
            context.SaveChanges();

            // Register the view: AddVista drains GeneratedExecutionPlanStore and adopts the real generated
            // compiled plan, making List run through the compiled (non-RUC) path.
            var services = new ServiceCollection();
            services.AddVista(v => v.Register<P4PersonView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();

            var view = registry.Get(ViewName)
                ?? throw new InvalidOperationException($"View '{ViewName}' was not registered.");

            // Sanity: the adopted plan must be the compiled facet, otherwise this would silently exercise
            // the reflection path instead of the generated compiled path (Property 4).
            if (planRegistry.Get(ViewName) is not ICompiledViewExecutionPlan)
            {
                throw new InvalidOperationException(
                    $"No generated compiled plan was adopted for '{ViewName}'; ensure the " +
                    "a2n.Vista.GeneratorExecSampleP4 fixture assembly (with the generator analyzer) is " +
                    "referenced and loaded.");
            }

            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler());

            return new P4RejectionHarness(connection, context, provider, executor, view, spy);
        }

        /// <summary>Runs the compiled List path for <paramref name="request"/> (synchronously awaited).</summary>
        public object List(ViewQueryRequest request) =>
            _executor.ListAsync<P4PersonRow>(_view, request, _scope, CancellationToken.None)
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
