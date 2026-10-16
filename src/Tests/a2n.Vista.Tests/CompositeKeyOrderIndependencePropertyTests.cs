// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for composite-key order independence on the M12 write path (write-path task 4.7;
/// Decision Log D119). A composite key arrives on the wire as a field-name→value map; the executor
/// resolves the target row by matching each <see cref="ViewMetadata.KeyFields"/> entry <em>by name</em>,
/// so the order the fields appear in the map must never change which row is resolved. This is an
/// executor-level property, so it runs against a SQLite-backed <see cref="DbContext"/> seeded per case
/// and drives the real <see cref="EfViewExecutor"/> write facet through
/// <see cref="IViewExecutor.DeleteAsync"/>.
/// </summary>
/// <remarks>
/// Each case seeds two identical SQLite databases with the same rows, then deletes the same target row
/// from one using the canonical <c>KeyFields</c> order and from the other using a randomly permuted map
/// order. Delete (rather than update) is used because it needs no write model or mapper — it exercises
/// pure composite-key resolution. The property holds when the two databases end in identical states:
/// the shuffled-order delete removed exactly the row the canonical-order delete removed, which is exactly
/// the keyed target. <see cref="EfViewExecutor.DeleteAsync"/> and Style B registration are RUC-annotated
/// (they resolve the key/facet from metadata at runtime); trimming is not used for tests, so IL2026 is
/// suppressed at the class level, matching the sibling write-path tests.
/// </remarks>
/// <remarks>
/// CsCheck runs each iteration on a worker thread, and every iteration calls <c>EnsureCreated</c> on a
/// fresh <see cref="CompositeKeyContext"/>. That first touch is serialized by
/// <see cref="RelationalModelWarmup"/> before any test method runs. The property itself is unchanged:
/// 100 iterations, both map orders, same deleted row.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The test drives the runtime reflection write/registration path by design; trimming is not used for tests.")]
public sealed class CompositeKeyOrderIndependencePropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    private const string ViewName = "composite-key-order-independence";

    /// <summary>The view's canonical (declaration) key order: A, then B, then C.</summary>
    private static readonly string[] CanonicalOrder = { "A", "B", "C" };

    /// <summary>The six permutations of the three-field composite key, indexed 0..5.</summary>
    private static readonly string[][] Permutations =
    {
        new[] { "A", "B", "C" },
        new[] { "A", "C", "B" },
        new[] { "B", "A", "C" },
        new[] { "B", "C", "A" },
        new[] { "C", "A", "B" },
        new[] { "C", "B", "A" },
    };

    /// <summary>A seeded composite-key row; the key is (<see cref="A"/>, <see cref="B"/>, <see cref="C"/>).</summary>
    private readonly record struct RowSeed(int A, string B, int C, string Payload);

    // Feature: write-path, Property 4: For any composite-key view and any name→value map that supplies a
    // value for every field in the view's ordered KeyFields, the row resolved is the same regardless of
    // the order the fields appear in the map, and equals the row resolved from the canonical KeyFields
    // order.
    //
    // Validates: Requirements 3.6
    [Test]
    public void Composite_Key_Resolves_The_Same_Row_Regardless_Of_Map_Field_Order()
    {
        // Distinct composite keys: dedupe by (A, B, C) so each seeded key maps to exactly one row and
        // seeding never violates the composite primary key.
        var genRows =
            Gen.Select(Gen.Int[1, 40], Gen.Int[0, 6], Gen.Int[1, 40], Gen.Int[0, 6],
                    (a, bSeed, c, pSeed) => new RowSeed(a, "b" + bSeed, c, "p" + pSeed))
                .Array[1, 10]
                .Select(static arr => arr
                    .GroupBy(static r => (r.A, r.B, r.C))
                    .Select(static g => g.First())
                    .ToArray());

        // Build the DI graph once: it holds the write-facet registry (populated at registration with the
        // captured composite key) and the write-mapper resolver the executor reads per write. Fresh SQLite
        // databases are created per case; the registration is invariant across cases.
        using var provider = new ServiceCollection()
            .AddVista(v => v.Register<CompositeKeyOrderView>())
            .BuildServiceProvider();

        var viewRegistry = provider.GetRequiredService<IViewRegistry>();
        var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();
        var view = viewRegistry.Get(ViewName)
            ?? throw new InvalidOperationException($"View '{ViewName}' was not registered.");

        // Sanity: the view must expose the ordered three-field composite key this property is about.
        if (!view.KeyFields.SequenceEqual(CanonicalOrder, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"View '{ViewName}' has KeyFields [{string.Join(", ", view.KeyFields)}], " +
                $"expected the canonical composite key [{string.Join(", ", CanonicalOrder)}].");
        }

        Gen.Select(genRows, Gen.Int[0, int.MaxValue], Gen.Int[0, Permutations.Length - 1]).Sample(
            data =>
            {
                var (rows, targetSeed, permIndex) = data;
                AssertOrderIndependentDelete(provider, planRegistry, view, rows, targetSeed, permIndex);
            },
            iter: Iterations);
    }

    /// <summary>
    /// The exact sample CI run 38877351961 reported for seed <c>9CKbg4_346s7</c>
    /// (0 shrinks): five rows, target selector 388739900, permutation index 1
    /// (<c>A, C, B</c>). CsCheck threw from <c>EnsureCreated</c> before either delete ran, so this
    /// input never demonstrated a wrong row. Replaying it checks that this key map still deletes the
    /// same row in both field orders.
    /// </summary>
    [Test]
    public void Reported_Seed_9CKbg4_346s7_Deletes_The_Same_Row_For_Both_Key_Orders()
    {
        using var provider = new ServiceCollection()
            .AddVista(v => v.Register<CompositeKeyOrderView>())
            .BuildServiceProvider();

        var viewRegistry = provider.GetRequiredService<IViewRegistry>();
        var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();
        var view = viewRegistry.Get(ViewName)
            ?? throw new InvalidOperationException($"View '{ViewName}' was not registered.");

        RowSeed[] rows =
        {
            new(9, "b5", 19, "p5"),
            new(27, "b5", 13, "p4"),
            new(39, "b3", 24, "p4"),
            new(1, "b1", 8, "p2"),
            new(33, "b1", 5, "p1"),
        };

        AssertOrderIndependentDelete(provider, planRegistry, view, rows, targetSeed: 388739900, permIndex: 1);
    }

    /// <summary>
    /// Checks that concurrent <c>EnsureCreated</c> on <see cref="CompositeKeyContext"/> completes after
    /// <see cref="RelationalModelWarmup"/> has already finalized that context's relational model.
    /// </summary>
    /// <remarks>
    /// This is a check of the post-warmup path only. It is not a regression test that the
    /// <c>GetRelationalModel</c> race is closed: that race was not reproduced locally (the failing
    /// trace is CI run 38877351961). A burst that passes after warmup does not prove a burst before
    /// warmup would have thrown.
    /// </remarks>
    [Test]
    public void Concurrent_EnsureCreated_After_Warmup_Completes()
    {
        Exception? failure = null;
        var burst = new Thread[32];
        var gate = new Barrier(burst.Length);
        for (var i = 0; i < burst.Length; i++)
        {
            burst[i] = new Thread(() =>
            {
                gate.SignalAndWait();
                try
                {
                    EnsureCreatedOnFreshDatabase();
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref failure, ex, null);
                }
            });
            burst[i].Start();
        }

        foreach (var thread in burst)
        {
            thread.Join();
        }

        if (failure is not null)
        {
            throw new Exception(
                "Concurrent EnsureCreated threw after RelationalModelWarmup had already run.",
                failure);
        }
    }

    /// <summary>
    /// Deletes <paramref name="rows"/>'s selected target from two identical databases, one with the
    /// canonical key-field order and one with <see cref="Permutations"/>[<paramref name="permIndex"/>],
    /// and throws when the two deletes do not remove that same row.
    /// </summary>
    private static void AssertOrderIndependentDelete(
        IServiceProvider provider,
        IViewExecutionPlanRegistry planRegistry,
        ViewMetadata view,
        IReadOnlyList<RowSeed> rows,
        int targetSeed,
        int permIndex)
    {
        var target = rows[targetSeed % rows.Count];
        var shuffledOrder = Permutations[permIndex];

        var canonicalKey = BuildKeyMap(target, CanonicalOrder);
        var shuffledKey = BuildKeyMap(target, shuffledOrder);

        // Two identically seeded databases: delete the same target from each, one by canonical
        // order and one by the permuted order.
        using var canonical = WriteHarness.Create(provider, planRegistry, view, rows);
        using var shuffled = WriteHarness.Create(provider, planRegistry, view, rows);

        var deletedCanonical = canonical.Delete(canonicalKey);
        var deletedShuffled = shuffled.Delete(shuffledKey);

        // Both orders resolve the seeded target: each delete matches exactly one in-scope row.
        if (!deletedCanonical)
        {
            throw new Exception(
                $"Canonical-order delete resolved no row for present key ({target.A}, '{target.B}', {target.C}).");
        }

        if (!deletedShuffled)
        {
            throw new Exception(
                $"Shuffled-order delete (order [{string.Join(", ", shuffledOrder)}]) resolved no row " +
                $"for present key ({target.A}, '{target.B}', {target.C}); resolution is order-dependent.");
        }

        var remainingCanonical = canonical.SnapshotKeys();
        var remainingShuffled = shuffled.SnapshotKeys();

        // The canonical-order delete removed exactly the target: every other row survives.
        var expectedRemaining = rows
            .Where(r => (r.A, r.B, r.C) != (target.A, target.B, target.C))
            .Select(r => (r.A, r.B, r.C))
            .OrderBy(static k => k)
            .ToArray();

        if (!remainingCanonical.SequenceEqual(expectedRemaining))
        {
            throw new Exception(
                "Canonical-order delete did not remove exactly the keyed row; " +
                $"remaining [{FormatKeys(remainingCanonical)}], expected [{FormatKeys(expectedRemaining)}].");
        }

        // Order independence: the shuffled-order delete produced the identical final state, i.e. it
        // resolved and removed the very same row the canonical order did (Requirement R3.6).
        if (!remainingShuffled.SequenceEqual(remainingCanonical))
        {
            throw new Exception(
                $"Shuffled-order delete (order [{string.Join(", ", shuffledOrder)}]) removed a different " +
                $"row than the canonical order: remaining [{FormatKeys(remainingShuffled)}] vs " +
                $"[{FormatKeys(remainingCanonical)}].");
        }
    }

    /// <summary>Opens a private in-memory database and creates the composite-key schema.</summary>
    private static void EnsureCreatedOnFreshDatabase()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CompositeKeyContext>()
            .UseSqlite(connection)
            .Options;
        using var context = new CompositeKeyContext(options);
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// Builds a composite-key name→value map, inserting the fields in the given <paramref name="order"/>
    /// so the map's enumeration (insertion) order reflects the order under test.
    /// </summary>
    private static Dictionary<string, object?> BuildKeyMap(RowSeed target, IReadOnlyList<string> order)
    {
        var map = new Dictionary<string, object?>(order.Count, StringComparer.Ordinal);
        foreach (var name in order)
        {
            map[name] = name switch
            {
                "A" => target.A,
                "B" => target.B,
                "C" => target.C,
                _ => throw new InvalidOperationException($"Unexpected key field '{name}'."),
            };
        }

        return map;
    }

    private static string FormatKeys(IEnumerable<(int A, string B, int C)> keys) =>
        string.Join("; ", keys.Select(k => $"({k.A}, '{k.B}', {k.C})"));

    /// <summary>
    /// Disposable per-case harness: owns an open in-memory SQLite connection, a seeded
    /// <see cref="CompositeKeyContext"/>, and an <see cref="EfViewExecutor"/> built over that context and
    /// the shared DI graph (write-facet registry + write-mapper resolver). SQLite in-memory databases
    /// live only while the connection is open, so the connection is disposed last.
    /// </summary>
    private sealed class WriteHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly CompositeKeyContext _context;
        private readonly EfViewExecutor _executor;
        private readonly ViewMetadata _view;
        private readonly ViewScope _scope = new();

        private WriteHarness(
            SqliteConnection connection,
            CompositeKeyContext context,
            EfViewExecutor executor,
            ViewMetadata view)
        {
            _connection = connection;
            _context = context;
            _executor = executor;
            _view = view;
        }

        public static WriteHarness Create(
            IServiceProvider provider,
            IViewExecutionPlanRegistry planRegistry,
            ViewMetadata view,
            IReadOnlyCollection<RowSeed> rows)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<CompositeKeyContext>()
                .UseSqlite(connection)
                .Options;

            var context = new CompositeKeyContext(options);
            context.Database.EnsureCreated();

            context.Sources.AddRange(rows.Select(static r => new CompositeKeySource
            {
                A = r.A,
                B = r.B,
                C = r.C,
                Payload = r.Payload,
            }));
            context.SaveChanges();

            // The base (ordinal) FilterCompiler matches the SQLite/in-memory sibling tests; key resolution
            // uses equality predicates, so no provider-specific text translation is required.
            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler());

            return new WriteHarness(connection, context, executor, view);
        }

        /// <summary>Deletes by the supplied composite key map; <see langword="true"/> when a row matched.</summary>
        public bool Delete(IReadOnlyDictionary<string, object?> key) =>
            _executor.DeleteAsync(_view, key, _scope, concurrencyToken: null, CancellationToken.None)
                .GetAwaiter().GetResult();

        /// <summary>Returns the surviving rows' composite keys, ordered, read fresh from the database.</summary>
        public (int A, string B, int C)[] SnapshotKeys() =>
            _context.Sources
                .AsNoTracking()
                .Select(s => new { s.A, s.B, s.C })
                .AsEnumerable()
                .Select(s => (s.A, s.B, s.C))
                .OrderBy(static k => k)
                .ToArray();

        public void Dispose()
        {
            _context.Dispose();
            // Disposing the connection drops the in-memory database; do it last.
            _connection.Dispose();
        }
    }

    /// <summary>EF source entity for the composite-key writable view; keyed by (A, B, C).</summary>
    internal sealed class CompositeKeySource
    {
        public int A { get; set; }

        public string B { get; set; } = string.Empty;

        public int C { get; set; }

        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>Projected (read) row exposing the composite key and the writable payload.</summary>
    private sealed class CompositeKeyRow
    {
        public int A { get; init; }

        public string B { get; init; } = string.Empty;

        public int C { get; init; }

        public string Payload { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract; only the non-key <see cref="Payload"/> is writable (D25).</summary>
    private sealed class CompositeKeyCrud
    {
        public string Payload { get; init; } = string.Empty;
    }

    /// <summary>Minimal EF context backing the composite-key writable view, with a three-field key.</summary>
    internal sealed class CompositeKeyContext : DbContext
    {
        public CompositeKeyContext(DbContextOptions<CompositeKeyContext> options)
            : base(options)
        {
        }

        internal DbSet<CompositeKeySource> Sources => Set<CompositeKeySource>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<CompositeKeySource>().HasKey(e => new { e.A, e.B, e.C });
    }

    /// <summary>
    /// A writable class-per-view (Style B) definition over <see cref="CompositeKeySource"/> with a
    /// three-field composite key (A, B, C) marked in declaration order. The typed write facet
    /// (<c>CrudOn</c> + a single non-key <c>MapWritable</c>) makes it writable, so the delete facet is
    /// reachable; only composite-key <em>resolution</em> is exercised by this property.
    /// </summary>
    private sealed class CompositeKeyOrderView : View<CompositeKeyRow, CompositeKeyCrud>
    {
        protected override void Configure(IViewBuilder<CompositeKeyRow, CompositeKeyCrud> builder)
        {
            builder
                .Named(ViewName)
                .From<CompositeKeySource>(s => new CompositeKeyRow
                {
                    A = s.A,
                    B = s.B,
                    C = s.C,
                    Payload = s.Payload,
                })
                .Field(x => x.A, f => f.PrimaryKey())
                .Field(x => x.B, f => f.PrimaryKey())
                .Field(x => x.C, f => f.PrimaryKey());

            builder
                .CrudOn<CompositeKeySource>()
                .MapWritable(c => c.Payload, e => e.Payload);
        }
    }
}
