// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Examples.StyleBExecP3;
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
/// Property-based test for the source-generator Phase 2 compiled Detail-by-key path
/// (spec style-b-executable; Decision Log D118).
///
/// Feature: style-b-executable, Property 3: For any row produced by a generated view's projection,
/// supplying that row's KeyFields values to Detail SHALL return exactly that one row (single and
/// composite keys), and for any key value matching no row Detail SHALL return an absent/null result
/// without throwing.
///
/// Validates: Requirements 3.2, 3.3
///
/// The views under test (<see cref="P3OrderView"/>, <see cref="P3CompositeView"/>) live in the
/// EF-aware consumer assembly <c>a2n.Vista.Examples.StyleBExecP3</c>, where the source generator emits
/// a REAL <c>ICompiledViewExecutionPlan</c> and registers it into
/// <see cref="GeneratedExecutionPlanStore"/> at module load. Each generated case seeds a fresh SQLite
/// database, registers the views through <c>AddVista</c> (so the compiled plan is adopted into the
/// execution-plan registry), and drives <see cref="EfViewExecutor.DetailCompiledAsync{TRow}"/> via the
/// public <see cref="IViewExecutor.DetailAsync{TRow}"/> entry point.
/// </summary>
public sealed class DetailByKeyRoundTripPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>A seeded single-key order row.</summary>
    private readonly record struct P3OrderSeed(int Id, string Name, int Quantity);

    /// <summary>A seeded composite-key order-line row.</summary>
    private readonly record struct P3LineSeed(int OrderId, int LineNo, string Sku);

    /// <summary>
    /// Touches a type in the consumer assembly so its module — and thus the generated
    /// <c>[ModuleInitializer]</c> that calls <see cref="GeneratedExecutionPlanStore.Add"/> — is loaded
    /// before any case runs. Instantiating a view is a safe, side-effect-free trigger.
    /// </summary>
    private static void EnsureFixtureModuleLoaded()
    {
        _ = new P3OrderView().Name;
        _ = new P3CompositeView().Name;
    }

    [Test]
    public void Detail_By_Key_Round_Trips_For_Single_And_Composite_Keys()
    {
        EnsureFixtureModuleLoaded();

        // Distinct ids: dedupe by id so each seeded key maps to exactly one row (the property's premise).
        var genOrders =
            Gen.Select(Gen.Int[1, 1_000_000], Gen.Int[0, 5_000], Gen.Int[0, 10_000],
                    (id, nameSeed, qty) => new P3OrderSeed(id, "order-" + nameSeed, qty))
                .Array[1, 12]
                .Select(static arr => arr
                    .GroupBy(static o => o.Id)
                    .Select(static g => g.First())
                    .ToArray());

        // Distinct composite keys: dedupe by (OrderId, LineNo).
        var genLines =
            Gen.Select(Gen.Int[1, 100_000], Gen.Int[1, 1_000], Gen.Int[0, 10_000],
                    (oid, ln, skuSeed) => new P3LineSeed(oid, ln, "sku-" + skuSeed))
                .Array[1, 12]
                .Select(static arr => arr
                    .GroupBy(static x => (x.OrderId, x.LineNo))
                    .Select(static g => g.First())
                    .ToArray());

        Gen.Select(genOrders, genLines).Sample(
            data =>
            {
                var (orders, lines) = data;
                using var harness = P3DetailHarness.Create(orders, lines);

                // --- Single-key round-trip: each seeded row is returned exactly by its key (R3.2). ---
                foreach (var order in orders)
                {
                    var row = harness.DetailOrder(order.Id);
                    if (row is null)
                    {
                        throw new Exception($"Single-key Detail returned null for present id {order.Id}.");
                    }

                    if (row.Id != order.Id || row.Name != order.Name || row.Quantity != order.Quantity)
                    {
                        throw new Exception(
                            $"Single-key Detail returned a mismatched row for id {order.Id}: " +
                            $"got (Id={row.Id}, Name='{row.Name}', Quantity={row.Quantity}), " +
                            $"expected (Id={order.Id}, Name='{order.Name}', Quantity={order.Quantity}).");
                    }
                }

                // --- Absent single key returns null without throwing (R3.3). ---
                var absentId = orders.Max(static o => o.Id) + 1;
                if (harness.DetailOrder(absentId) is not null)
                {
                    throw new Exception($"Single-key Detail should return null for absent id {absentId}.");
                }

                // --- Composite-key round-trip: each seeded row returned exactly by its (OrderId, LineNo). ---
                foreach (var line in lines)
                {
                    var row = harness.DetailLine(line.OrderId, line.LineNo);
                    if (row is null)
                    {
                        throw new Exception(
                            $"Composite-key Detail returned null for present key ({line.OrderId}, {line.LineNo}).");
                    }

                    if (row.OrderId != line.OrderId || row.LineNo != line.LineNo || row.Sku != line.Sku)
                    {
                        throw new Exception(
                            $"Composite-key Detail returned a mismatched row for ({line.OrderId}, {line.LineNo}): " +
                            $"got (OrderId={row.OrderId}, LineNo={row.LineNo}, Sku='{row.Sku}'), " +
                            $"expected (OrderId={line.OrderId}, LineNo={line.LineNo}, Sku='{line.Sku}').");
                    }
                }

                // --- Absent composite key returns null without throwing (R3.3). ---
                var absentOrderId = lines.Max(static l => l.OrderId) + 1;
                if (harness.DetailLine(absentOrderId, 1) is not null)
                {
                    throw new Exception(
                        $"Composite-key Detail should return null for absent key ({absentOrderId}, 1).");
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Disposable per-case harness: owns an open in-memory SQLite connection, a seeded
    /// <see cref="P3TestDbContext"/>, and an <see cref="EfViewExecutor"/> wired to the real generated
    /// compiled plans (adopted into the execution-plan registry by <c>AddVista</c>). SQLite in-memory
    /// databases live only while the connection is open, so the connection is disposed last.
    /// </summary>
    private sealed class P3DetailHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly P3TestDbContext _context;
        private readonly ServiceProvider _provider;
        private readonly EfViewExecutor _executor;
        private readonly ViewScope _scope = new();
        private readonly ViewMetadata _orderView;
        private readonly ViewMetadata _lineView;

        private P3DetailHarness(
            SqliteConnection connection,
            P3TestDbContext context,
            ServiceProvider provider,
            EfViewExecutor executor,
            ViewMetadata orderView,
            ViewMetadata lineView)
        {
            _connection = connection;
            _context = context;
            _provider = provider;
            _executor = executor;
            _orderView = orderView;
            _lineView = lineView;
        }

        public static P3DetailHarness Create(
            IReadOnlyCollection<P3OrderSeed> orders,
            IReadOnlyCollection<P3LineSeed> lines)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<P3TestDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new P3TestDbContext(options);
            context.Database.EnsureCreated();

            context.Orders.AddRange(orders.Select(static o => new P3Order
            {
                Id = o.Id,
                Name = o.Name,
                Quantity = o.Quantity,
            }));
            context.OrderLines.AddRange(lines.Select(static l => new P3LineItem
            {
                OrderId = l.OrderId,
                LineNo = l.LineNo,
                Sku = l.Sku,
            }));
            context.SaveChanges();

            // Register the views: AddVista drains GeneratedExecutionPlanStore and adopts the real
            // generated compiled plan for each, making Detail run through the compiled (non-RUC) path.
            var services = new ServiceCollection();
            services.AddVista(v =>
            {
                v.Register<P3OrderView>();
                v.Register<P3CompositeView>();
            });
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();

            var orderView = registry.Get("p3-orders")
                ?? throw new InvalidOperationException("View 'p3-orders' was not registered.");
            var lineView = registry.Get("p3-order-lines")
                ?? throw new InvalidOperationException("View 'p3-order-lines' was not registered.");

            // Sanity: the adopted plans must be the compiled facet, otherwise this would silently fall
            // back to the reflection path and not exercise the generated Detail-by-key (Property 3).
            if (planRegistry.Get("p3-orders") is not ICompiledViewExecutionPlan)
            {
                throw new InvalidOperationException(
                    "No generated compiled plan was adopted for 'p3-orders'; ensure the StyleBExecP3 " +
                    "fixture assembly (with the generator analyzer) is referenced and loaded.");
            }

            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler());

            return new P3DetailHarness(connection, context, provider, executor, orderView, lineView);
        }

        /// <summary>Detail-by-key for the single-key view; <paramref name="id"/> is the scalar key.</summary>
        public P3OrderRow? DetailOrder(int id) =>
            _executor.DetailAsync<P3OrderRow>(_orderView, id, _scope, CancellationToken.None)
                .GetAwaiter().GetResult();

        /// <summary>
        /// Detail-by-key for the composite-key view; a composite key arrives as a name→value map
        /// (Decision Log D109).
        /// </summary>
        public P3LineItemRow? DetailLine(int orderId, int lineNo)
        {
            var key = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["OrderId"] = orderId,
                ["LineNo"] = lineNo,
            };

            return _executor.DetailAsync<P3LineItemRow>(_lineView, key, _scope, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _provider.Dispose();
            _context.Dispose();
            // Disposing the connection drops the in-memory database; do it last.
            _connection.Dispose();
        }
    }
}
