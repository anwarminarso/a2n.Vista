// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Integration tests for persistence-failure atomicity and concurrency on the
/// <see cref="EfViewExecutor"/> write facet (write-path task 9.1). These are the fault-injection
/// complement of Property 9 (a rejected write preserves persisted state) and cannot be exercised without
/// a real provider, so they run against a SQLite-backed <see cref="DbContext"/> that surfaces genuine
/// provider persistence failures at <c>SaveChanges</c>:
/// <list type="bullet">
/// <item><b>Constraint violation</b> (Requirements R1.7, R3.8, R9.4, R11.3, R11.4) — a create that
/// violates a UNIQUE index throws a provider <see cref="DbUpdateException"/>. The executor translates it
/// to a <see cref="VistaWriteConflictException"/> (HTTP 409 <c>write-conflict</c>) whose message is
/// fixed, Vista-authored text carrying no SQL, schema, or connection detail; the implicit transaction
/// leaves no partial row.</item>
/// <item><b>Optimistic concurrency at save time</b> (Requirement R6.5) — a genuine
/// <see cref="DbUpdateConcurrencyException"/> raised by a real concurrency-token mismatch detected at
/// <c>SaveChanges</c> (not the pre-check) is translated to a <see cref="VistaConcurrencyConflictException"/>
/// (HTTP 409) and rolled back, leaving the row unchanged.</item>
/// </list>
/// </summary>
/// <remarks>
/// The write path is RUC (reflection mapper fallback), so IL2026 is suppressed at the class level —
/// trimming is not used for tests. Each test owns an isolated in-memory SQLite database on a single open
/// connection so the read-for-write load and the persisting <c>SaveChanges</c> share one context (R11.5).
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests drive the reflection-based write path (Register<TView> + reflection write mapper) by design; trimming is not used for tests.")]
public sealed class WritePersistenceFailureIntegrationTests
{
    private const string ConstraintViewName = "write-constraint-atomicity";
    private const string ConcurrencyViewName = "write-savechanges-concurrency";

    // ---- Constraint violation (R1.7, R3.8, R9.4, R11.3, R11.4) --------------------------------------

    /// <summary>
    /// R9.4/R1.7/R11.3/R11.4: a create whose whitelisted value collides with a UNIQUE index makes
    /// <c>SaveChanges</c> throw a provider <see cref="DbUpdateException"/>. The executor surfaces a
    /// <see cref="VistaWriteConflictException"/> (409 <c>write-conflict</c>), and no partial row is
    /// persisted — the row count is unchanged from the pre-write state.
    /// </summary>
    [Test]
    public async Task Create_Violating_Unique_Constraint_Throws_WriteConflict_And_Persists_No_Partial_Row()
    {
        using var harness = ConstraintHarness.Create();

        // Seed the row that owns the unique name; a second insert with the same name must collide.
        harness.Seed(new UniqueSource { Name = "dup" });

        var thrown = await CaptureAsync(() => harness.Executor.CreateAsync(
            harness.View,
            new UniqueCrud { Name = "dup" },
            new ViewScope(),
            CancellationToken.None));

        // The provider DbUpdateException is translated to the typed write-conflict (R9.4)...
        await Assert.That(thrown).IsTypeOf<VistaWriteConflictException>();
        var conflict = (VistaWriteConflictException)thrown!;
        await Assert.That(conflict.Code).IsEqualTo(WriteErrorCode.WriteConflict);

        // ...with a fixed, Vista-authored message that leaks no SQL/schema/connection/table detail (R9.6).
        await AssertMessageIsLeakFree(conflict.Message);

        // R1.7/R11.3/R11.4: the failed SaveChanges left no partial row — exactly the one seeded row
        // remains, read back on a fresh no-tracking query against the store.
        var rowCount = harness.Context.Set<UniqueSource>().AsNoTracking().Count(r => r.Name == "dup");
        await Assert.That(rowCount).IsEqualTo(1);
    }

    // ---- Genuine DbUpdateConcurrencyException at SaveChanges (R6.5) ----------------------------------

    /// <summary>
    /// R6.5: a genuine optimistic-concurrency violation detected by the provider at <c>SaveChanges</c>
    /// (the row's token was changed out-of-band after the entity was loaded, so the pre-check passes but
    /// the UPDATE matches zero rows) surfaces as a <see cref="VistaConcurrencyConflictException"/> (409)
    /// with the change rolled back — the persisted row is unchanged.
    /// </summary>
    [Test]
    public async Task Update_With_SaveChanges_Concurrency_Violation_Throws_ConcurrencyConflict_And_Rolls_Back()
    {
        using var harness = ConcurrencyHarness.Create();

        // Seed a token-carrying row; after seeding it stays tracked on the harness context with token "v1".
        var id = harness.Seed(new TokenSource { Name = "before", Token = "v1" });

        // Out-of-band: bump the stored token to "v2" WITHOUT going through the change tracker. The tracked
        // entity still holds "v1", so the pre-check (If-Match "v1" == tracked "v1") passes, but the UPDATE
        // ... WHERE Token = 'v1' will match zero rows and EF raises DbUpdateConcurrencyException at save.
        var affected = harness.Context.Database.ExecuteSqlRaw(
            "UPDATE TokenSources SET Token = 'v2' WHERE Id = {0}", id);
        await Assert.That(affected).IsEqualTo(1);

        var thrown = await CaptureAsync(() => harness.Executor.UpdateAsync(
            harness.View,
            id,
            new TokenCrud { Name = "after" },
            new ViewScope(),
            concurrencyToken: "v1",
            CancellationToken.None));

        // R6.5: the provider DbUpdateConcurrencyException is translated to the typed concurrency conflict.
        await Assert.That(thrown).IsTypeOf<VistaConcurrencyConflictException>();
        var conflict = (VistaConcurrencyConflictException)thrown!;
        await Assert.That(conflict.Code).IsEqualTo(WriteErrorCode.ConcurrencyConflict);
        await AssertMessageIsLeakFree(conflict.Message);

        // Rolled back: the persisted row's whitelisted field is untouched (still "before"), read back on a
        // fresh no-tracking query so the store — not the in-memory tracked entity — is inspected.
        var persistedName = harness.Context.Set<TokenSource>().AsNoTracking().Single(r => r.Id == id).Name;
        await Assert.That(persistedName).IsEqualTo("before");
    }

    // ---- Shared helpers ------------------------------------------------------------------------------

    /// <summary>Invokes <paramref name="action"/> and returns the thrown exception, or fails the test.</summary>
    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ex;
        }

        return null;
    }

    /// <summary>
    /// Asserts a client-facing write-failure message carries no provider internals (R9.6): no SQL verbs,
    /// no schema/object names used by the test schema, and no connection/data-source detail.
    /// </summary>
    private static async Task AssertMessageIsLeakFree(string message)
    {
        await Assert.That(message).IsNotNull();
        foreach (var forbidden in new[]
                 {
                     "SQL", "UNIQUE", "constraint failed", "UPDATE", "INSERT",
                     "TokenSources", "UniqueSources", "DataSource", "connection", "Sqlite",
                 })
        {
            await Assert.That(message.Contains(forbidden, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }
    }

    // ---- Constraint fixtures -------------------------------------------------------------------------

    /// <summary>EF source entity with a UNIQUE index on <see cref="Name"/> to force a constraint breach.</summary>
    private sealed class UniqueSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Projected read row for the constraint view.</summary>
    private sealed class UniqueRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract: one whitelisted scalar (<c>Name</c>).</summary>
    private sealed class UniqueCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>EF context that gives <see cref="UniqueSource.Name"/> a unique index.</summary>
    private sealed class UniqueContext : DbContext
    {
        public UniqueContext(DbContextOptions<UniqueContext> options)
            : base(options)
        {
        }

        public DbSet<UniqueSource> Sources => Set<UniqueSource>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UniqueSource>(e =>
            {
                e.ToTable("UniqueSources");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.Name).IsUnique();
            });
        }
    }

    /// <summary>A single-source writable Style B view over <see cref="UniqueSource"/> (no token).</summary>
    private sealed class ConstraintView : View<UniqueRow, UniqueCrud>
    {
        protected override void Configure(IViewBuilder<UniqueRow, UniqueCrud> builder)
        {
            builder
                .Named(ConstraintViewName)
                .From<UniqueSource>(s => new UniqueRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<UniqueSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }

    /// <summary>Disposable per-test harness for the constraint-violation scenario.</summary>
    private sealed class ConstraintHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        private ConstraintHarness(
            SqliteConnection connection,
            UniqueContext context,
            ServiceProvider provider,
            EfViewExecutor executor,
            ViewMetadata view)
        {
            _connection = connection;
            Context = context;
            _provider = provider;
            Executor = executor;
            View = view;
        }

        public UniqueContext Context { get; }

        public EfViewExecutor Executor { get; }

        public ViewMetadata View { get; }

        [RequiresUnreferencedCode("Builds the reflection write path (Register<TView> + reflection write mapper); trimming is not used for tests.")]
        public static ConstraintHarness Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<UniqueContext>()
                .UseSqlite(connection)
                .Options;

            var context = new UniqueContext(options);
            context.Database.EnsureCreated();

            var services = new ServiceCollection();
            services.AddVista(v => v.Register<ConstraintView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();
            var view = registry.Get(ConstraintViewName)
                ?? throw new InvalidOperationException($"View '{ConstraintViewName}' was not registered.");

            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler());
            return new ConstraintHarness(connection, context, provider, executor, view);
        }

        public int Seed(UniqueSource row)
        {
            Context.Sources.Add(row);
            Context.SaveChanges();
            return row.Id;
        }

        public void Dispose()
        {
            _provider.Dispose();
            Context.Dispose();
            _connection.Dispose();
        }
    }

    // ---- Concurrency fixtures ------------------------------------------------------------------------

    /// <summary>EF source entity with a string optimistic-concurrency token.</summary>
    private sealed class TokenSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;
    }

    /// <summary>Projected read row for the concurrency view.</summary>
    private sealed class TokenRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Token { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract: one whitelisted scalar (<c>Name</c>); the token is protected.</summary>
    private sealed class TokenCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>EF context that marks <see cref="TokenSource.Token"/> as a concurrency token.</summary>
    private sealed class TokenContext : DbContext
    {
        public TokenContext(DbContextOptions<TokenContext> options)
            : base(options)
        {
        }

        public DbSet<TokenSource> Sources => Set<TokenSource>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TokenSource>(e =>
            {
                e.ToTable("TokenSources");
                e.HasKey(x => x.Id);
                e.Property(x => x.Token).IsConcurrencyToken();
            });
        }
    }

    /// <summary>A single-source writable Style B view over <see cref="TokenSource"/> with a token.</summary>
    private sealed class ConcurrencyView : View<TokenRow, TokenCrud>
    {
        protected override void Configure(IViewBuilder<TokenRow, TokenCrud> builder)
        {
            builder
                .Named(ConcurrencyViewName)
                .From<TokenSource>(s => new TokenRow { Id = s.Id, Name = s.Name, Token = s.Token })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<TokenSource>()
                .MapWritable(c => c.Name, e => e.Name)
                .WithConcurrencyToken(e => e.Token);
        }
    }

    /// <summary>Disposable per-test harness for the SaveChanges-time concurrency scenario.</summary>
    private sealed class ConcurrencyHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        private ConcurrencyHarness(
            SqliteConnection connection,
            TokenContext context,
            ServiceProvider provider,
            EfViewExecutor executor,
            ViewMetadata view)
        {
            _connection = connection;
            Context = context;
            _provider = provider;
            Executor = executor;
            View = view;
        }

        public TokenContext Context { get; }

        public EfViewExecutor Executor { get; }

        public ViewMetadata View { get; }

        [RequiresUnreferencedCode("Builds the reflection write path (Register<TView> + reflection write mapper); trimming is not used for tests.")]
        public static ConcurrencyHarness Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<TokenContext>()
                .UseSqlite(connection)
                .Options;

            var context = new TokenContext(options);
            context.Database.EnsureCreated();

            var services = new ServiceCollection();
            services.AddVista(v => v.Register<ConcurrencyView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();
            var view = registry.Get(ConcurrencyViewName)
                ?? throw new InvalidOperationException($"View '{ConcurrencyViewName}' was not registered.");

            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler());
            return new ConcurrencyHarness(connection, context, provider, executor, view);
        }

        /// <summary>Seeds a row directly; the entity stays tracked on the context with its seeded token.</summary>
        public int Seed(TokenSource row)
        {
            Context.Sources.Add(row);
            Context.SaveChanges();
            return row.Id;
        }

        public void Dispose()
        {
            _provider.Dispose();
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
