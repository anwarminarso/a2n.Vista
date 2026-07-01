// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Example-based unit tests for the single-<c>SaveChanges</c> persistence semantics of the
/// <see cref="EfViewExecutor"/> write facet (write-path task 4.8; Requirements R1.3, R2.4, R3.4, R11.1,
/// R11.5). A SQLite-backed <see cref="DbContext"/> carries an EF Core <see cref="ISaveChangesInterceptor"/>
/// that counts every persistence round-trip and captures the acting context instance and the
/// change-tracker entries at save time.
/// <list type="bullet">
/// <item>R1.3/R11.1 (Create), R2.4/R11.1 (Update), R3.4/R11.1 (Delete) — each operation persists with
/// <em>exactly one</em> <c>SaveChanges</c>, mutating exactly one change-tracker entry (Added / Modified /
/// Deleted respectively).</item>
/// <item>R11.5 — the read-for-write load and the persistence happen on the <em>same</em> request-scoped
/// <see cref="DbContext"/> instance: the interceptor's captured context is the one the executor was
/// given, the loaded entity is tracked on that instance, and the mutation is visible on it without a
/// reload.</item>
/// </list>
/// </summary>
/// <remarks>
/// The executor is constructed directly over the SQLite context (the same pattern the read-path
/// property/integration tests use), so a single <see cref="DbContext"/> instance backs both the
/// read-for-write query and the persisting <c>SaveChanges</c>. The interceptor is reset after seeding so
/// only the executor's own writes are counted. The write path is RUC (reflection mapper fallback), so
/// IL2026 is suppressed at the class level — trimming is not used for tests.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests drive the reflection-based write path (Register<TView> + reflection write mapper) by design; trimming is not used for tests.")]
public sealed class WriteSaveChangesSemanticsTests
{
    private const string ViewName = "write-savechanges-semantics";

    // ---- Create --------------------------------------------------------------------------------------

    /// <summary>
    /// R1.3/R11.1: a Create persists with exactly one <c>SaveChanges</c>, and that save carries exactly
    /// one <see cref="EntityState.Added"/> change-tracker entry (the new row). R11.5: the save runs on the
    /// same context instance the executor was constructed with.
    /// </summary>
    [Test]
    public async Task Create_Persists_With_Exactly_One_SaveChanges()
    {
        using var harness = WriteHarness.Create();

        var newKey = await harness.Executor.CreateAsync(
            harness.View,
            new WriteCrud { Name = "created" },
            new ViewScope(),
            CancellationToken.None);

        // Exactly one persistence round-trip for the whole operation.
        await Assert.That(harness.Interceptor.SaveChangesCount).IsEqualTo(1);

        // That single save wrote exactly one Added entry — no incidental extra work.
        await Assert.That(harness.Interceptor.LastMutatingEntryCount).IsEqualTo(1);
        await Assert.That(harness.Interceptor.LastAddedCount).IsEqualTo(1);

        // R11.5: the save executed on the very context instance handed to the executor.
        await Assert.That(ReferenceEquals(harness.Interceptor.LastContext, harness.Context)).IsTrue();

        // The store-assigned key round-trips and the row is visible on the same context.
        var id = Convert.ToInt32(newKey);
        var persisted = await harness.Context.Set<WriteSource>().FindAsync(id);
        await Assert.That(persisted).IsNotNull();
        await Assert.That(persisted!.Name).IsEqualTo("created");
    }

    // ---- Update --------------------------------------------------------------------------------------

    /// <summary>
    /// R2.4/R11.1: an Update persists with exactly one <c>SaveChanges</c> carrying exactly one
    /// <see cref="EntityState.Modified"/> entry. R11.5: the read-for-write load and the persistence share
    /// one context instance — the mutated entity is tracked on it and the change is visible without a
    /// reload.
    /// </summary>
    [Test]
    public async Task Update_Persists_With_Exactly_One_SaveChanges_On_The_Same_Context()
    {
        using var harness = WriteHarness.Create();
        var seededId = harness.Seed(new WriteSource { Name = "before" });

        var updated = await harness.Executor.UpdateAsync(
            harness.View,
            seededId,
            new WriteCrud { Name = "after" },
            new ViewScope(),
            concurrencyToken: null,
            CancellationToken.None);

        await Assert.That(updated).IsTrue();

        // Exactly one persistence round-trip, mutating exactly one Modified entry.
        await Assert.That(harness.Interceptor.SaveChangesCount).IsEqualTo(1);
        await Assert.That(harness.Interceptor.LastMutatingEntryCount).IsEqualTo(1);
        await Assert.That(harness.Interceptor.LastModifiedCount).IsEqualTo(1);

        // R11.5: the read-for-write and the SaveChanges ran on the same context the executor was given...
        await Assert.That(ReferenceEquals(harness.Interceptor.LastContext, harness.Context)).IsTrue();

        // ...so the mutation is visible on that same instance (the loaded entity was tracked there, not on
        // a second context), and the entry saved was that tracked row.
        var tracked = harness.Context.Set<WriteSource>().Local.Single(e => e.Id == seededId);
        await Assert.That(tracked.Name).IsEqualTo("after");
    }

    // ---- Delete --------------------------------------------------------------------------------------

    /// <summary>
    /// R3.4/R11.1: a Delete persists with exactly one <c>SaveChanges</c> carrying exactly one
    /// <see cref="EntityState.Deleted"/> entry. R11.5: the read-for-write load and the removal share one
    /// context instance, and the row is gone from that instance afterward.
    /// </summary>
    [Test]
    public async Task Delete_Persists_With_Exactly_One_SaveChanges_On_The_Same_Context()
    {
        using var harness = WriteHarness.Create();
        var seededId = harness.Seed(new WriteSource { Name = "doomed" });

        var deleted = await harness.Executor.DeleteAsync(
            harness.View,
            seededId,
            new ViewScope(),
            concurrencyToken: null,
            CancellationToken.None);

        await Assert.That(deleted).IsTrue();

        // Exactly one persistence round-trip, removing exactly one Deleted entry.
        await Assert.That(harness.Interceptor.SaveChangesCount).IsEqualTo(1);
        await Assert.That(harness.Interceptor.LastMutatingEntryCount).IsEqualTo(1);
        await Assert.That(harness.Interceptor.LastDeletedCount).IsEqualTo(1);

        // R11.5: the load and the removal ran on the same context the executor was given...
        await Assert.That(ReferenceEquals(harness.Interceptor.LastContext, harness.Context)).IsTrue();

        // ...and the row is gone from that same instance.
        var persisted = await harness.Context.Set<WriteSource>().FindAsync(seededId);
        await Assert.That(persisted).IsNull();
    }

    // ---- Fixtures ------------------------------------------------------------------------------------

    /// <summary>EF source entity the writable Style B view projects from (single-source, Id-keyed).</summary>
    private sealed class WriteSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Projected (read) row type sent to clients.</summary>
    private sealed class WriteRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract for the writable view (one whitelisted scalar, closes mass assignment).</summary>
    private sealed class WriteCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Minimal EF context backing the writable Style B view.</summary>
    private sealed class WriteContext : DbContext
    {
        public WriteContext(DbContextOptions<WriteContext> options)
            : base(options)
        {
        }

        public DbSet<WriteSource> Sources => Set<WriteSource>();
    }

    /// <summary>
    /// A single-source writable Style B view: <c>Id</c> primary key and one whitelisted scalar
    /// (<c>Name</c>) via <c>MapWritable</c>. No concurrency token — the single-<c>SaveChanges</c> semantics
    /// are independent of optimistic concurrency.
    /// </summary>
    private sealed class WriteSemanticsView : View<WriteRow, WriteCrud>
    {
        protected override void Configure(IViewBuilder<WriteRow, WriteCrud> builder)
        {
            builder
                .Named(ViewName)
                .From<WriteSource>(s => new WriteRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<WriteSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }

    /// <summary>
    /// A <see cref="SaveChangesInterceptor"/> that counts persistence round-trips and, for the most recent
    /// save, captures the acting <see cref="DbContext"/> instance and a snapshot of the change-tracker
    /// entries by state (taken at <c>SavingChanges</c>, before EF resets states post-save). Both the sync
    /// and async hooks are covered; the executor uses the async path, but counting both keeps the
    /// assertions honest regardless of the code path.
    /// </summary>
    private sealed class SaveChangesCountingInterceptor : SaveChangesInterceptor
    {
        public int SaveChangesCount { get; private set; }

        public DbContext? LastContext { get; private set; }

        public int LastMutatingEntryCount { get; private set; }

        public int LastAddedCount { get; private set; }

        public int LastModifiedCount { get; private set; }

        public int LastDeletedCount { get; private set; }

        /// <summary>Zeroes the counters so seeding writes are not counted against the executor.</summary>
        public void Reset()
        {
            SaveChangesCount = 0;
            LastContext = null;
            LastMutatingEntryCount = 0;
            LastAddedCount = 0;
            LastModifiedCount = 0;
            LastDeletedCount = 0;
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            Capture(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Capture(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Capture(DbContext? context)
        {
            SaveChangesCount++;
            LastContext = context;

            if (context is null)
            {
                return;
            }

            var entries = context.ChangeTracker.Entries().ToArray();
            LastAddedCount = entries.Count(e => e.State == EntityState.Added);
            LastModifiedCount = entries.Count(e => e.State == EntityState.Modified);
            LastDeletedCount = entries.Count(e => e.State == EntityState.Deleted);
            LastMutatingEntryCount = LastAddedCount + LastModifiedCount + LastDeletedCount;
        }
    }

    /// <summary>
    /// Disposable per-test harness: owns an open in-memory SQLite connection, a seeded
    /// <see cref="WriteContext"/> wired with the counting interceptor, the DI provider produced by
    /// <c>AddVista</c> (which supplies the write-facet registry and mapper resolver), and an
    /// <see cref="EfViewExecutor"/> constructed over that <em>single</em> context instance.
    /// </summary>
    private sealed class WriteHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        private WriteHarness(
            SqliteConnection connection,
            WriteContext context,
            ServiceProvider provider,
            EfViewExecutor executor,
            ViewMetadata view,
            SaveChangesCountingInterceptor interceptor)
        {
            _connection = connection;
            Context = context;
            _provider = provider;
            Executor = executor;
            View = view;
            Interceptor = interceptor;
        }

        public WriteContext Context { get; }

        public EfViewExecutor Executor { get; }

        public ViewMetadata View { get; }

        public SaveChangesCountingInterceptor Interceptor { get; }

        [RequiresUnreferencedCode("Builds the reflection write path (Register<TView> + reflection write mapper); trimming is not used for tests.")]
        public static WriteHarness Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var interceptor = new SaveChangesCountingInterceptor();
            var options = new DbContextOptionsBuilder<WriteContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptor)
                .Options;

            var context = new WriteContext(options);
            context.Database.EnsureCreated();

            // Register the writable view: AddVista populates the write-facet registry and the mapper
            // resolver the executor's write path resolves from the request IServiceProvider.
            var services = new ServiceCollection();
            services.AddVista(v => v.Register<WriteSemanticsView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();

            var view = registry.Get(ViewName)
                ?? throw new InvalidOperationException($"View '{ViewName}' was not registered.");

            // The executor is built over the single SQLite context instance, so the read-for-write load
            // and the persisting SaveChanges share one DbContext (R11.5). The base FilterCompiler is used
            // because SQLite key coercion needs no provider-specific dialect here.
            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler());

            return new WriteHarness(connection, context, provider, executor, view, interceptor);
        }

        /// <summary>Seeds a row directly and returns its store-assigned id, then zeroes the interceptor.</summary>
        public int Seed(WriteSource row)
        {
            Context.Sources.Add(row);
            Context.SaveChanges();
            Interceptor.Reset();
            return row.Id;
        }

        public void Dispose()
        {
            _provider.Dispose();
            Context.Dispose();
            // Disposing the connection drops the in-memory database; do it last.
            _connection.Dispose();
        }
    }
}
