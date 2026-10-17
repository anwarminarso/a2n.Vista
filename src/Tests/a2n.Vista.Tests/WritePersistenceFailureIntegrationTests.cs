// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Diagnostics;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Examples;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
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
/// <item><b>Optimistic concurrency at save time</b> (Requirement R6.5, D158) — a genuine
/// <see cref="DbUpdateConcurrencyException"/> is translated to a
/// <see cref="VistaConcurrencyConflictException"/> (HTTP 409 <c>write-concurrency-conflict</c>) and rolled back.</item>
/// <item><b>Constraint violation</b> (D158) — a unique or foreign-key breach is a
/// <see cref="VistaConstraintViolationException"/> (HTTP 422 <c>write-constraint-violation</c>), not a
/// 409. The message carries no SQL or schema detail, and no partial row is persisted.</item>
/// <item><b>Schema failure</b> (D158) — a missing table is a <see cref="DbUpdateException"/> that is not
/// a constraint. It becomes a <see cref="VistaPersistenceException"/> (HTTP 500
/// <c>write-persistence-failed</c>), never a 409.</item>
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
    static WritePersistenceFailureIntegrationTests()
    {
        // The EF library does not reference SQLite. These tests are a host: they register the same
        // probe the Northwind samples register, then a real UNIQUE/FK failure is 422.
        SqliteConstraintProbe.Register();
    }

    private const string ConstraintViewName = "write-constraint-atomicity";
    private const string ForeignKeyViewName = "write-foreign-key-constraint";
    private const string ConcurrencyViewName = "write-savechanges-concurrency";

    // ---- Constraint violation (R1.7, R3.8, R11.3, R11.4, D158) ----------------------------------------

    /// <summary>
    /// D158: a create whose whitelisted value collides with a UNIQUE index makes <c>SaveChanges</c>
    /// throw a provider <see cref="DbUpdateException"/>. That constraint is not a concurrency conflict:
    /// the executor surfaces a <see cref="VistaConstraintViolationException"/> (422
    /// <c>write-constraint-violation</c>), and no partial row is persisted.
    /// </summary>
    [Test]
    public async Task Create_Violating_Unique_Constraint_Throws_ConstraintViolation_Not_409()
    {
        using var harness = ConstraintHarness.Create();

        // Seed the row that owns the unique name; a second insert with the same name must collide.
        harness.Seed(new UniqueSource { Name = "dup" });

        var thrown = await CaptureAsync(() => harness.Executor.CreateAsync(
            harness.View,
            new UniqueCrud { Name = "dup" },
            new ViewScope(),
            CancellationToken.None));

        await Assert.That(thrown).IsTypeOf<VistaConstraintViolationException>();
        var violation = (VistaConstraintViolationException)thrown!;
        await Assert.That(violation.Code).IsEqualTo(WriteErrorCode.ConstraintViolation);
        await Assert.That(violation.Code.ToWireCode()).IsEqualTo(WriteErrorCodes.ConstraintViolation);
        await Assert.That(violation.Code.ToWireCode()).IsNotEqualTo("write-conflict");

        var sqlite = FindSqliteException(violation);
        await Assert.That(sqlite).IsNotNull();
        await Assert.That(sqlite!.SqliteErrorCode).IsEqualTo(19);
        // Observed on this provider: DbException.ErrorCode is -2147467259 (HResult 0x80004005, E_FAIL),
        // not 19. Classification does not read ErrorCode.

        // ...with a fixed, Vista-authored message that leaks no SQL/schema/connection/table detail (R9.6).
        await AssertMessageIsLeakFree(violation.Message);

        // R1.7/R11.3/R11.4: the failed SaveChanges left no partial row — exactly the one seeded row
        // remains, read back on a fresh no-tracking query against the store.
        var rowCount = harness.Context.Set<UniqueSource>().AsNoTracking().Count(r => r.Name == "dup");
        await Assert.That(rowCount).IsEqualTo(1);
    }

    /// <summary>
    /// D158: a create whose target table is missing makes <c>SaveChanges</c> throw a provider
    /// <see cref="DbUpdateException"/> that is not a constraint and not a concurrency conflict. The
    /// executor surfaces a <see cref="VistaPersistenceException"/> (500), never 409 <c>write-conflict</c>.
    /// </summary>
    [Test]
    public async Task Create_When_Target_Table_Is_Missing_Throws_PersistenceFailed_Not_WriteConflict()
    {
        using var harness = ConstraintHarness.Create();
        harness.DropSourcesTable();

        var thrown = await CaptureAsync(() => harness.Executor.CreateAsync(
            harness.View,
            new UniqueCrud { Name = "orphan" },
            new ViewScope(),
            CancellationToken.None));

        await Assert.That(thrown).IsTypeOf<VistaPersistenceException>();
        var failure = (VistaPersistenceException)thrown!;
        await Assert.That(failure.Code).IsEqualTo(WriteErrorCode.PersistenceFailed);
        await Assert.That(failure.Code.ToWireCode()).IsEqualTo(WriteErrorCodes.PersistenceFailed);
        await Assert.That(failure.Code).IsNotEqualTo(WriteErrorCode.ConcurrencyConflict);
        await Assert.That(failure.Code).IsNotEqualTo(WriteErrorCode.ConstraintViolation);
        await Assert.That(failure.InnerException).IsTypeOf<DbUpdateException>();
        await AssertMessageIsLeakFree(failure.Message);
        await Assert.That(failure.Message.Contains("UniqueSources", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(failure.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)).IsFalse();
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

        // R6.5/D158: DbUpdateConcurrencyException is 409 write-concurrency-conflict, not a constraint or a 500.
        await Assert.That(thrown).IsTypeOf<VistaConcurrencyConflictException>();
        var conflict = (VistaConcurrencyConflictException)thrown!;
        await Assert.That(conflict.Code).IsEqualTo(WriteErrorCode.ConcurrencyConflict);
        await Assert.That(conflict.Code.ToWireCode()).IsEqualTo(WriteErrorCodes.ConcurrencyConflict);
        await Assert.That(conflict.Code.ToWireCode()).IsNotEqualTo("write-conflict");
        await Assert.That(conflict.Code).IsNotEqualTo(WriteErrorCode.ConstraintViolation);
        await Assert.That(conflict.Code).IsNotEqualTo(WriteErrorCode.PersistenceFailed);
        await AssertMessageIsLeakFree(conflict.Message);

        // Rolled back: the persisted row's whitelisted field is untouched (still "before"), read back on a
        // fresh no-tracking query so the store — not the in-memory tracked entity — is inspected.
        var persistedName = harness.Context.Set<TokenSource>().AsNoTracking().Single(r => r.Id == id).Name;
        await Assert.That(persistedName).IsEqualTo("before");
    }

    /// <summary>
    /// D158: a foreign-key breach is the same non-conflict constraint response as a unique breach, not a
    /// 409 <c>write-conflict</c> and not a 500.
    /// </summary>
    [Test]
    public async Task Create_Violating_Foreign_Key_Throws_ConstraintViolation_Not_409()
    {
        using var harness = ForeignKeyHarness.Create();

        var thrown = await CaptureAsync(() => harness.Executor.CreateAsync(
            harness.View,
            new FkCrud { Name = "orphan" },
            new ViewScope(),
            CancellationToken.None));

        await Assert.That(thrown).IsTypeOf<VistaConstraintViolationException>();
        var violation = (VistaConstraintViolationException)thrown!;
        await Assert.That(violation.Code).IsEqualTo(WriteErrorCode.ConstraintViolation);
        await Assert.That(violation.Code.ToWireCode()).IsEqualTo("write-constraint-violation");
        await Assert.That(violation.Code.ToWireCode()).IsNotEqualTo("write-conflict");
        await Assert.That(violation.Code).IsNotEqualTo(WriteErrorCode.PersistenceFailed);
        await AssertMessageIsLeakFree(violation.Message);
    }

    /// <summary>
    /// D158: the three <c>DbUpdateException</c> cases leave the executor as three different exceptions and
    /// the HTTP mapper as three different status/code pairs — 409 <c>write-concurrency-conflict</c> (concurrency),
    /// 422 <c>write-constraint-violation</c> (unique constraint), 500 <c>write-persistence-failed</c>
    /// (missing table).
    /// </summary>
    [Test]
    public async Task SaveChanges_Failures_Map_To_Three_Distinct_Outcomes()
    {
        Exception? concurrency;
        using (var harness = ConcurrencyHarness.Create())
        {
            var id = harness.Seed(new TokenSource { Name = "before", Token = "v1" });
            harness.Context.Database.ExecuteSqlRaw("UPDATE TokenSources SET Token = 'v2' WHERE Id = {0}", id);
            concurrency = await CaptureAsync(() => harness.Executor.UpdateAsync(
                harness.View,
                id,
                new TokenCrud { Name = "after" },
                new ViewScope(),
                concurrencyToken: "v1",
                CancellationToken.None));
        }

        Exception? constraint;
        using (var harness = ConstraintHarness.Create())
        {
            harness.Seed(new UniqueSource { Name = "dup" });
            constraint = await CaptureAsync(() => harness.Executor.CreateAsync(
                harness.View,
                new UniqueCrud { Name = "dup" },
                new ViewScope(),
                CancellationToken.None));
        }

        Exception? schema;
        using (var harness = ConstraintHarness.Create())
        {
            harness.DropSourcesTable();
            schema = await CaptureAsync(() => harness.Executor.CreateAsync(
                harness.View,
                new UniqueCrud { Name = "orphan" },
                new ViewScope(),
                CancellationToken.None));
        }

        var concurrencyHttp = await MapProblemAsync(concurrency!);
        var constraintHttp = await MapProblemAsync(constraint!);
        var schemaHttp = await MapProblemAsync(schema!);

        // (1) concurrency conflict → 409 write-concurrency-conflict
        await Assert.That(concurrency).IsTypeOf<VistaConcurrencyConflictException>();
        await Assert.That(concurrencyHttp.Status).IsEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(concurrencyHttp.Code).IsEqualTo(WriteErrorCodes.ConcurrencyConflict);
        await Assert.That(concurrencyHttp.Code).IsNotEqualTo("write-conflict");

        // (2) constraint violation → its own non-conflict response
        await Assert.That(constraint).IsTypeOf<VistaConstraintViolationException>();
        await Assert.That(constraintHttp.Status).IsEqualTo(StatusCodes.Status422UnprocessableEntity);
        await Assert.That(constraintHttp.Code).IsEqualTo("write-constraint-violation");
        await Assert.That(constraintHttp.Status).IsNotEqualTo(StatusCodes.Status409Conflict);

        // (3) schema failure (missing table) → server error, not 409
        await Assert.That(schema).IsTypeOf<VistaPersistenceException>();
        await Assert.That(schemaHttp.Status).IsEqualTo(StatusCodes.Status500InternalServerError);
        await Assert.That(schemaHttp.Code).IsEqualTo("write-persistence-failed");
        await Assert.That(schemaHttp.Status).IsNotEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(schemaHttp.Code).IsNotEqualTo("write-conflict");

        await Assert.That(new[] { concurrencyHttp.Status, constraintHttp.Status, schemaHttp.Status }.Distinct().Count())
            .IsEqualTo(3);
        await Assert.That(new[] { concurrencyHttp.Code, constraintHttp.Code, schemaHttp.Code }.Distinct().Count())
            .IsEqualTo(3);
    }

    // ---- Shared helpers ------------------------------------------------------------------------------

    /// <summary>Invokes <paramref name="action"/> and returns the thrown exception, or fails the test.</summary>
    private static SqliteException? FindSqliteException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqlite)
            {
                return sqlite;
            }
        }

        return null;
    }

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

    /// <summary>Runs a typed write failure through the real problem-details mapper and reads status + code.</summary>
    private static async Task<(int Status, string? Code)> MapProblemAsync(Exception exception)
    {
        var mapped = VistaProblemResults.TryCreate(exception, out var result);
        if (!mapped || result is null)
        {
            throw new InvalidOperationException(
                $"VistaProblemResults did not map {exception.GetType().Name}.");
        }

        // ProblemHttpResult requires ILoggerFactory from the request services.
        var services = new ServiceCollection();
        services.AddLogging();
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();
        await result.ExecuteAsync(http);
        http.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(http.Response.Body);
        var code = document.RootElement.TryGetProperty("code", out var codeElement)
            ? codeElement.GetString()
            : null;
        return (http.Response.StatusCode, code);
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
    internal sealed class UniqueSource
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
    internal sealed class UniqueContext : DbContext
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

        /// <summary>Drops the mapped table so the next save fails with "no such table", not a constraint.</summary>
        public void DropSourcesTable()
        {
            var table = Context.Model.FindEntityType(typeof(UniqueSource))?.GetTableName()
                ?? throw new InvalidOperationException("UniqueSource has no mapped table.");
            // The identifier comes from the EF model for this fixture, not from a request.
#pragma warning disable EF1003
            Context.Database.ExecuteSqlRaw("DROP TABLE \"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\";");
#pragma warning restore EF1003
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
    internal sealed class TokenSource
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
    internal sealed class TokenContext : DbContext
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

    // ---- Foreign-key fixtures ------------------------------------------------------------------------

    /// <summary>Parent row referenced by <see cref="ChildSource"/>.</summary>
    private sealed class ParentSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Child row whose <see cref="ParentId"/> is a foreign key. The view never sets it.</summary>
    private sealed class ChildSource
    {
        public int Id { get; set; }

        public int ParentId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Projected read row for the foreign-key view.</summary>
    private sealed class FkRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract: one whitelisted scalar (<c>Name</c>). <c>ParentId</c> stays 0.</summary>
    private sealed class FkCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>EF context with a required foreign key from child to parent.</summary>
    private sealed class ForeignKeyContext : DbContext
    {
        public ForeignKeyContext(DbContextOptions<ForeignKeyContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ParentSource>(e =>
            {
                e.ToTable("ParentSources");
                e.HasKey(x => x.Id);
            });

            modelBuilder.Entity<ChildSource>(e =>
            {
                e.ToTable("ChildSources");
                e.HasKey(x => x.Id);
                e.HasOne<ParentSource>()
                    .WithMany()
                    .HasForeignKey(x => x.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    /// <summary>
    /// A writable view over <see cref="ChildSource"/> that maps only <c>Name</c>, so a create inserts
    /// <c>ParentId = 0</c> and breaches the foreign key when enforcement is on.
    /// </summary>
    private sealed class ForeignKeyView : View<FkRow, FkCrud>
    {
        protected override void Configure(IViewBuilder<FkRow, FkCrud> builder)
        {
            builder
                .Named(ForeignKeyViewName)
                .From<ChildSource>(s => new FkRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<ChildSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }

    /// <summary>Disposable per-test harness for the foreign-key constraint scenario.</summary>
    private sealed class ForeignKeyHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        private ForeignKeyHarness(
            SqliteConnection connection,
            ForeignKeyContext context,
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

        public ForeignKeyContext Context { get; }

        public EfViewExecutor Executor { get; }

        public ViewMetadata View { get; }

        [RequiresUnreferencedCode("Builds the reflection write path (Register<TView> + reflection write mapper); trimming is not used for tests.")]
        public static ForeignKeyHarness Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            // SQLite leaves foreign keys off unless the connection asks. The pragma is per connection.
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_keys = ON;";
                command.ExecuteNonQuery();
            }

            var options = new DbContextOptionsBuilder<ForeignKeyContext>()
                .UseSqlite(connection)
                .Options;

            var context = new ForeignKeyContext(options);
            context.Database.EnsureCreated();

            var services = new ServiceCollection();
            services.AddVista(v => v.Register<ForeignKeyView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();
            var view = registry.Get(ForeignKeyViewName)
                ?? throw new InvalidOperationException($"View '{ForeignKeyViewName}' was not registered.");

            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler());
            return new ForeignKeyHarness(connection, context, provider, executor, view);
        }

        public void Dispose()
        {
            _provider.Dispose();
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
