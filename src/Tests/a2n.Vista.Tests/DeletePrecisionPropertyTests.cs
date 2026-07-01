// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore;
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
/// Property-based test for delete precision on the M12 write path (write-path task 4.6; Decision Log
/// D119, D120). This is an <em>executor-level</em> property: it drives the real
/// <see cref="EfViewExecutor"/> write facet (<see cref="IViewExecutor.DeleteAsync"/>) against a
/// SQLite-backed <see cref="DbContext"/>, so the whole scoped-resolution → concurrency-guard →
/// single-<c>SaveChanges</c> pipeline runs exactly as production would.
/// </summary>
/// <remarks>
/// Each generated case seeds a fresh in-memory SQLite database with a distinct-keyed set of rows,
/// snapshots the persisted state, deletes one randomly chosen present key through the executor, and
/// re-reads the database. A fresh connection + service provider is built per case so no state leaks
/// between iterations. The writable Gaya B view declares an explicit primary key and a single
/// <c>MapWritable</c> whitelist (delete carries no body, but the write-facet registry must still hold the
/// view's captured facet — <see cref="AddVista"/> populates it at registration). No concurrency token is
/// declared, so a <see langword="null"/> <c>If-Match</c> is accepted (Requirement R6.6).
/// </remarks>
public sealed class DeletePrecisionPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>The globally-unique name the writable view registers under.</summary>
    private const string ViewName = "delete-precision-view";

    /// <summary>A seeded row: a distinct key plus two non-key payload fields checked for survivor equality.</summary>
    private readonly record struct RowSeed(int Id, string Name, int Quantity);

    // Feature: write-path, Property 3: For any seeded set of rows within scope and any key present in
    // that set, a delete removes exactly the one row whose key matches and leaves every other row
    // unchanged (final row count is the initial count minus one, and every surviving row is identical to
    // its pre-delete state).
    //
    // Validates: Requirements 3.1
    [Test]
    public void Delete_Removes_Exactly_The_Keyed_Row_And_Leaves_Every_Other_Row_Unchanged()
    {
        // Distinct keys: dedupe by Id so each seeded key maps to exactly one row (the property's premise).
        var genRows =
            Gen.Select(Gen.Int[1, 1_000_000], Gen.Int[0, 5_000], Gen.Int[0, 10_000],
                    (id, nameSeed, qty) => new RowSeed(id, "row-" + nameSeed, qty))
                .Array[1, 12]
                .Select(static arr => arr
                    .GroupBy(static r => r.Id)
                    .Select(static g => g.First())
                    .ToArray());

        // Pick one present key to delete. Modulo keeps the index in range without a dependent generator.
        var genCase =
            from rows in genRows
            from pickSeed in Gen.Int[0, int.MaxValue]
            select (rows, pick: pickSeed % rows.Length);

        genCase.Sample(
            input =>
            {
                var (rows, pick) = input;
                using var harness = DeletePrecisionHarness.Create(rows);

                var target = rows[pick];
                var before = harness.SnapshotAll();

                // Delete exactly the keyed row within scope (empty scope = all rows eligible), no token.
                var deleted = harness.Delete(target.Id);
                if (!deleted)
                {
                    throw new Exception(
                        $"DeleteAsync returned false for the present key {target.Id} " +
                        $"(seeded {rows.Length} rows).");
                }

                var after = harness.SnapshotAll();

                // Final count is exactly one fewer than the initial count (R3.1).
                if (after.Count != before.Count - 1)
                {
                    throw new Exception(
                        $"Row count after delete was {after.Count}, expected {before.Count - 1} " +
                        $"(deleted key {target.Id} from {before.Count} rows).");
                }

                // The keyed row is gone (R3.1).
                if (after.ContainsKey(target.Id))
                {
                    throw new Exception($"The keyed row {target.Id} was still present after delete.");
                }

                // Every surviving row is byte-identical to its pre-delete state (R3.1).
                foreach (var kvp in before)
                {
                    if (kvp.Key == target.Id)
                    {
                        continue;
                    }

                    if (!after.TryGetValue(kvp.Key, out var survivor))
                    {
                        throw new Exception(
                            $"Survivor row {kvp.Key} disappeared; delete must remove only the keyed row {target.Id}.");
                    }

                    if (!string.Equals(survivor.Name, kvp.Value.Name, StringComparison.Ordinal) ||
                        survivor.Quantity != kvp.Value.Quantity)
                    {
                        throw new Exception(
                            $"Survivor row {kvp.Key} changed from (Name='{kvp.Value.Name}', " +
                            $"Quantity={kvp.Value.Quantity}) to (Name='{survivor.Name}', " +
                            $"Quantity={survivor.Quantity}) after deleting key {target.Id}.");
                    }
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Disposable per-case harness: owns an open in-memory SQLite connection, a seeded
    /// <see cref="DeleteContext"/>, and an <see cref="EfViewExecutor"/> wired to a service provider whose
    /// <c>AddVista</c> registration published the writable view's captured write facet (needed by the
    /// executor's concurrency guard). SQLite in-memory databases live only while the connection is open,
    /// so the connection is disposed last.
    /// </summary>
    private sealed class DeletePrecisionHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DeleteContext _context;
        private readonly ServiceProvider _provider;
        private readonly EfViewExecutor _executor;
        private readonly ViewMetadata _view;
        private readonly ViewScope _scope = new();

        private DeletePrecisionHarness(
            SqliteConnection connection,
            DeleteContext context,
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

        public static DeletePrecisionHarness Create(IReadOnlyCollection<RowSeed> rows)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<DeleteContext>()
                .UseSqlite(connection)
                .Options;

            var context = new DeleteContext(options);
            context.Database.EnsureCreated();

            context.Sources.AddRange(rows.Select(static r => new DeleteSource
            {
                Id = r.Id,
                Name = r.Name,
                Quantity = r.Quantity,
            }));
            context.SaveChanges();

            // Register the writable view so AddVista populates the write-facet registry the executor's
            // concurrency guard reads (RequireWriteFacet). No generated plan is needed: delete resolves
            // the entity via the reflection write path, and the explicit PK satisfies the D106 key gate.
            var services = new ServiceCollection();
            services.AddVista(v => v.Register<DeleteView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();

            var view = registry.Get(ViewName)
                ?? throw new InvalidOperationException($"View '{ViewName}' was not registered.");

            // Construct the executor over the SAME context used to seed/verify (R11.5), with the AddVista
            // provider so the write-facet registry resolves. The base FilterCompiler is used because the
            // EF Core SQLite provider needs no provider-aware LIKE for scalar key coercion.
            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler());

            return new DeletePrecisionHarness(connection, context, provider, executor, view);
        }

        /// <summary>Deletes the row with the scalar key <paramref name="id"/> within the empty scope.</summary>
        public bool Delete(int id) =>
            _executor.DeleteAsync(_view, id, _scope, concurrencyToken: null, CancellationToken.None)
                .GetAwaiter().GetResult();

        /// <summary>Reads the persisted rows straight from the database (no tracking), keyed by Id.</summary>
        public IReadOnlyDictionary<int, (string Name, int Quantity)> SnapshotAll() =>
            _context.Sources
                .AsNoTracking()
                .ToList()
                .ToDictionary(s => s.Id, s => (s.Name, s.Quantity));

        public void Dispose()
        {
            _provider.Dispose();
            _context.Dispose();
            // Disposing the connection drops the in-memory database; do it last.
            _connection.Dispose();
        }
    }

    /// <summary>EF source entity the writable Gaya B view projects from and deletes (single-source, Id-keyed).</summary>
    private sealed class DeleteSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Quantity { get; set; }
    }

    /// <summary>Projected (read) row type for the view.</summary>
    private sealed class DeleteRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public int Quantity { get; init; }
    }

    /// <summary>Typed write contract (delete carries no body; a whitelist is required for a writable view).</summary>
    private sealed class DeleteCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Minimal EF context backing the writable Gaya B view.</summary>
    private sealed class DeleteContext : DbContext
    {
        public DeleteContext(DbContextOptions<DeleteContext> options)
            : base(options)
        {
        }

        public DbSet<DeleteSource> Sources => Set<DeleteSource>();
    }

    /// <summary>
    /// A writable class-per-view (Gaya B) definition: a typed write facet (<c>CrudOn</c> +
    /// <c>MapWritable</c>) makes it non-read-only. It is single-source over <see cref="DeleteSource"/> and
    /// declares an explicit primary key so it registers without a generated plan (Decision Log D106).
    /// </summary>
    private sealed class DeleteView : View<DeleteRow, DeleteCrud>
    {
        protected override void Configure(IViewBuilder<DeleteRow, DeleteCrud> builder)
        {
            builder
                .Named(ViewName)
                .From<DeleteSource>(s => new DeleteRow { Id = s.Id, Name = s.Name, Quantity = s.Quantity })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<DeleteSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }
}
