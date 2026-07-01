// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the request-key authority guarantee of the update write facet
/// (write-path task 4.5; Decision Log D119/D120). This is an executor-level property, so it runs against
/// a SQLite-backed <see cref="DbContext"/> and the real <see cref="EfViewExecutor"/> write path
/// (<see cref="EfViewExecutor.UpdateAsync{TCrud}"/>), seeding two rows per iteration and driving an update
/// whose write model deliberately carries a spoofed primary-key value that differs from the request key.
/// </summary>
/// <remarks>
/// The writable Style B view (<see cref="RequestKeyAuthorityView"/>) whitelists only the scalar
/// <c>Name</c> (the guard forbids mapping the key, so <c>Id</c> is never mapped) while its <c>TCrud</c>
/// still carries an <c>Id</c> member the client can fill — the spoof channel this property closes. The
/// executor must resolve the target row solely from the request key channel: the request-keyed row is
/// updated, its key column stays unchanged, and the row whose key equals the model's spoofed value is
/// never touched. <see cref="EfViewExecutor.UpdateAsync{TCrud}"/> is RUC-annotated (it resolves the
/// write mapper/key from metadata at runtime); trimming is not used for tests, so IL2026 is suppressed at
/// the class level, matching the sibling write-path tests.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The test exercises the runtime reflection write path by design; trimming is not used for tests.")]
public sealed class RequestKeyAuthorityPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    private const string ViewName = "request-key-authority-property";

    /// <summary>Model-side name pool (disjoint from the entity seed pool so an assignment is observable).</summary>
    private static readonly string[] ModelNames = { "", "m-alpha", "model-two", "posted-name" };

    /// <summary>Entity-seed name pool (disjoint from the model pool).</summary>
    private static readonly string[] EntityNames = { "E-seed1", "E-seed2", "E-seed3", "E-seed4" };

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    /// <summary>A generated update case over two rows whose keys are drawn from disjoint ranges.</summary>
    private readonly record struct SpoofCase(
        int RequestKeyId,   // row A: the request key channel (authoritative)
        string RequestName, // row A's pre-write name
        int SpoofKeyId,     // row B: the value the model spoofs into its Id member
        string SpoofName,   // row B's pre-write name
        string PostedName); // the whitelisted Name the update posts for row A

    /// <summary>
    /// Generates two distinct rows (request-key row A from the low range, spoof row B from the high range,
    /// so their ids never collide) plus a posted name drawn from a pool disjoint from the seeded names, so
    /// a successful whitelisted assignment is always observable and any accidental change to row B is
    /// caught.
    /// </summary>
    private static readonly Gen<SpoofCase> GenCase =
        from requestKeyId in Gen.Int[1, 500]
        from spoofKeyId in Gen.Int[501, 1000]
        from requestName in Pick(EntityNames)
        from spoofName in Pick(EntityNames)
        from postedName in Pick(ModelNames)
        select new SpoofCase(requestKeyId, requestName, spoofKeyId, spoofName, postedName);

    // Feature: write-path, Property 2: For any update request whose write model also carries a value for
    // a primary-key member that differs from the request key, the executor resolves the target row solely
    // from the request key channel: the row identified by the request key is the one modified, its key
    // column is unchanged, and no other row (in particular, the row whose key equals the model's spoofed
    // value) is modified.
    //
    // Validates: Requirements 2.5, 5.2
    [Test]
    public void Update_Resolves_Target_From_Request_Key_And_Ignores_The_Model_Key()
    {
        GenCase.Sample(
            spoof =>
            {
                using var harness = UpdateHarness.Create();

                // Seed row A (the request-key target) and row B (whose key equals the spoofed model value).
                harness.Seed(spoof.RequestKeyId, spoof.RequestName);
                harness.Seed(spoof.SpoofKeyId, spoof.SpoofName);

                // Update by the request key (row A) while the model spoofs Id = row B's key. The whitelist
                // maps only Name, so the model's Id is carried but never assignable (R2.5, R5.2).
                var model = new SpoofCrud { Id = spoof.SpoofKeyId, Name = spoof.PostedName };
                var updated = harness.Update(spoof.RequestKeyId, model);

                if (!updated)
                {
                    throw new Exception(
                        $"Update by request key {spoof.RequestKeyId} reported no matching row, but row A was seeded.");
                }

                // Row A (request key) is the one modified: its whitelisted Name holds the posted value and
                // its key column is unchanged (R2.5).
                var rowA = harness.Get(spoof.RequestKeyId)
                    ?? throw new Exception($"Row A (id {spoof.RequestKeyId}) disappeared after the update.");

                if (!string.Equals(rowA.Name, spoof.PostedName, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Request-keyed row {spoof.RequestKeyId} has Name '{rowA.Name}', expected the posted " +
                        $"'{spoof.PostedName}'.");
                }

                if (rowA.Id != spoof.RequestKeyId)
                {
                    throw new Exception(
                        $"Request-keyed row's key changed from {spoof.RequestKeyId} to {rowA.Id}; the key column " +
                        "must never be modified by a write.");
                }

                // Row B (the model's spoofed key) is never touched: neither its Name nor its key changed
                // (R5.2 — model key is ignored; only the request key selects the target).
                var rowB = harness.Get(spoof.SpoofKeyId)
                    ?? throw new Exception($"Spoof row B (id {spoof.SpoofKeyId}) disappeared after the update.");

                if (!string.Equals(rowB.Name, spoof.SpoofName, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Spoof row {spoof.SpoofKeyId} was modified: Name changed from '{spoof.SpoofName}' to " +
                        $"'{rowB.Name}'. The model key must not select or mutate a different row.");
                }

                if (rowB.Id != spoof.SpoofKeyId)
                {
                    throw new Exception(
                        $"Spoof row's key changed from {spoof.SpoofKeyId} to {rowB.Id}; no row other than the " +
                        "request-keyed target may be modified.");
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Disposable per-case harness: owns an open in-memory SQLite connection, a
    /// <see cref="SpoofContext"/>, a DI container wired via <c>AddVista</c> (which populates the write
    /// facet registry and the write-mapper resolver for the writable Style B view), and a real
    /// <see cref="EfViewExecutor"/> over the same request-scoped context. SQLite in-memory databases live
    /// only while the connection is open, so the connection is disposed last.
    /// </summary>
    private sealed class UpdateHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly SpoofContext _context;
        private readonly ServiceProvider _provider;
        private readonly EfViewExecutor _executor;
        private readonly ViewMetadata _view;
        private readonly ViewScope _scope = new();

        private UpdateHarness(
            SqliteConnection connection,
            SpoofContext context,
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

        public static UpdateHarness Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<SpoofContext>()
                .UseSqlite(connection)
                .Options;

            var context = new SpoofContext(options);
            context.Database.EnsureCreated();

            // Register the writable Style B view: AddVista publishes its captured CRUD facet into the
            // IWriteFacetRegistry and wires the WriteMapperResolver the executor resolves its mapper from.
            var services = new ServiceCollection();
            services.AddVista(v => v.Register<RequestKeyAuthorityView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();

            var view = registry.Get(ViewName)
                ?? throw new InvalidOperationException($"View '{ViewName}' was not registered.");

            // The write path resolves Set<TEntity> off the constructor-supplied context and its
            // WriteMapperResolver/IWriteFacetRegistry off the provider.
            var executor = new EfViewExecutor(context, provider, planRegistry);

            return new UpdateHarness(connection, context, provider, executor, view);
        }

        /// <summary>Inserts one source row and persists it.</summary>
        public void Seed(int id, string name)
        {
            _context.Sources.Add(new SpoofSource { Id = id, Name = name });
            _context.SaveChanges();
        }

        /// <summary>Drives <see cref="EfViewExecutor.UpdateAsync{TCrud}"/> by the scalar request key.</summary>
        public bool Update(int requestKey, SpoofCrud model) =>
            _executor.UpdateAsync(_view, requestKey, model, _scope, concurrencyToken: null, CancellationToken.None)
                .GetAwaiter().GetResult();

        /// <summary>Reads a source row by key from the store (no tracking), or <see langword="null"/>.</summary>
        public SpoofSource? Get(int id) =>
            _context.Sources.AsNoTracking().SingleOrDefault(s => s.Id == id);

        public void Dispose()
        {
            _provider.Dispose();
            _context.Dispose();
            // Disposing the connection drops the in-memory database; do it last.
            _connection.Dispose();
        }
    }

    /// <summary>The EF source entity the writable view projects from (single-source, Id-keyed).</summary>
    private sealed class SpoofSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>The projected (read) row type sent to clients.</summary>
    private sealed class SpoofRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>
    /// The typed write contract. It carries the whitelisted <c>Name</c> AND a key member <c>Id</c> that is
    /// deliberately NOT whitelisted, so a client can attempt to spoof the target row's identity through
    /// the body — the spoof channel this property proves is ignored.
    /// </summary>
    private sealed class SpoofCrud
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Minimal EF context backing the writable Style B view.</summary>
    private sealed class SpoofContext : DbContext
    {
        public SpoofContext(DbContextOptions<SpoofContext> options)
            : base(options)
        {
        }

        public DbSet<SpoofSource> Sources => Set<SpoofSource>();
    }

    /// <summary>
    /// A writable class-per-view (Style B) definition over <see cref="SpoofSource"/>. It declares an
    /// explicit primary key (Id) and a CRUD facet that whitelists only the scalar <c>Name</c>; the key is
    /// intentionally never mapped (the build-time guard forbids mapping a key field), so the executor's
    /// request-key authority is the only channel that selects the target row.
    /// </summary>
    private sealed class RequestKeyAuthorityView : View<SpoofRow, SpoofCrud>
    {
        protected override void Configure(IViewBuilder<SpoofRow, SpoofCrud> builder)
        {
            builder
                .Named(ViewName)
                .From<SpoofSource>(s => new SpoofRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<SpoofSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }
}
