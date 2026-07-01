// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using a2n.Vista.EntityFrameworkCore;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.EntityFrameworkCore.Hosting;
using a2n.Vista.Examples.StyleBExecP7;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the source-generator Phase 2 single-source primary-key auto-derivation at
/// startup (spec style-b-executable; Decision Log D118 / D105 / M11).
///
/// Feature: style-b-executable, Property 7: For any single-source executable view with no explicitly
/// declared key, after startup its ViewMetadata.KeyFields SHALL equal the primary-key column names of its
/// source entity in DbContext.Model, listed in the model's declared key order (composite keys included);
/// and for any view that declares an explicit key, the declared key SHALL be used unchanged — never
/// overridden, merged, or supplemented from the model.
///
/// Validates: Requirements 6.1, 6.2, 6.3
///
/// The views under test live in the EF-aware consumer assembly <c>a2n.Vista.Examples.StyleBExecP7</c>,
/// where the source generator emits a REAL <see cref="ICompiledViewExecutionPlan"/> per view and
/// registers it into <see cref="GeneratedExecutionPlanStore"/> at module load. Two views are KEYLESS
/// (single + composite PK source) so <see cref="VistaModelKeyDerivationService"/> derives their key from
/// the model; two declare an EXPLICIT key (one in REVERSED model order) so the hook must leave them
/// untouched. Each generated case registers a random ordered subset of the views through <c>AddVista</c>
/// over a fresh SQLite-backed <see cref="P7TestDbContext"/>, runs the startup derivation hook once, and
/// asserts every registered view's resolved <see cref="ViewMetadata.KeyFields"/>.
/// </summary>
public sealed class SingleSourcePkDerivationPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>The fixture views, identified for selection/registration by the generated case.</summary>
    private enum P7ViewKind
    {
        OrderDerived,
        LineDerived,
        OrderExplicit,
        LineExplicit,
    }

    private static readonly P7ViewKind[] AllKinds =
    {
        P7ViewKind.OrderDerived,
        P7ViewKind.LineDerived,
        P7ViewKind.OrderExplicit,
        P7ViewKind.LineExplicit,
    };

    /// <summary>The globally-unique view name each kind registers under.</summary>
    private static string ViewNameOf(P7ViewKind kind) => kind switch
    {
        P7ViewKind.OrderDerived => P7OrderDerivedView.ViewName,
        P7ViewKind.LineDerived => P7LineDerivedView.ViewName,
        P7ViewKind.OrderExplicit => P7OrderExplicitView.ViewName,
        P7ViewKind.LineExplicit => P7LineExplicitView.ViewName,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The key the view must expose after startup. Derived views take the model's declared key order;
    /// the explicit composite view keeps its author-declared REVERSED order (proving no override/reorder).
    /// </summary>
    private static string[] ExpectedKeysOf(P7ViewKind kind) => kind switch
    {
        P7ViewKind.OrderDerived => new[] { nameof(P7Order.Id) },                       // derived: model single PK
        P7ViewKind.LineDerived => new[] { nameof(P7LineItem.OrderId), nameof(P7LineItem.LineNo) }, // derived: model order
        P7ViewKind.OrderExplicit => new[] { nameof(P7Order.Id) },                      // explicit: untouched
        P7ViewKind.LineExplicit => new[] { nameof(P7LineItem.LineNo), nameof(P7LineItem.OrderId) }, // explicit: reversed, untouched
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Touches a type in the consumer assembly so its module — and thus the generated
    /// <c>[ModuleInitializer]</c> that calls <see cref="GeneratedExecutionPlanStore.Add"/> — is loaded
    /// before any case runs. Instantiating a view is a safe, side-effect-free trigger.
    /// </summary>
    private static void EnsureFixtureModuleLoaded()
    {
        _ = new P7OrderDerivedView().Name;
        _ = new P7LineDerivedView().Name;
        _ = new P7OrderExplicitView().Name;
        _ = new P7LineExplicitView().Name;
    }

    [Test]
    public void Single_Source_Keys_Are_Derived_From_Model_And_Explicit_Keys_Are_Untouched()
    {
        EnsureFixtureModuleLoaded();

        // A generated case picks a non-empty subset of the four views (4-bit mask), a rotation so the
        // registration order varies, and small row counts so the property is shown invariant to seeded
        // data (PK derivation reads the model schema, never the rows).
        var genCase =
            from mask in Gen.Int[1, 15]
            from rotation in Gen.Int[0, 3]
            from orderRows in Gen.Int[0, 8]
            from lineRows in Gen.Int[0, 8]
            select (mask, rotation, orderRows, lineRows);

        genCase.Sample(
            input =>
            {
                var (mask, rotation, orderRows, lineRows) = input;

                // Selected kinds (bit order), rotated to vary registration order across cases.
                var selected = new List<P7ViewKind>();
                for (var i = 0; i < AllKinds.Length; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        selected.Add(AllKinds[i]);
                    }
                }

                var shift = rotation % selected.Count;
                var ordered = selected.Skip(shift).Concat(selected.Take(shift)).ToList();

                using var harness = P7DerivationHarness.Create(ordered, orderRows, lineRows);

                foreach (var kind in ordered)
                {
                    var name = ViewNameOf(kind);
                    var expected = ExpectedKeysOf(kind);
                    var actual = harness.KeyFieldsOf(name);

                    if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
                    {
                        throw new Exception(
                            $"View '{name}' resolved KeyFields [{string.Join(", ", actual)}], expected " +
                            $"[{string.Join(", ", expected)}] " +
                            $"(registration order: {string.Join(", ", ordered)}; " +
                            $"orderRows={orderRows}, lineRows={lineRows}).");
                    }
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Disposable per-case harness: owns an open in-memory SQLite connection and a DI container wired with
    /// <c>AddVista</c> (which adopts the REAL generated compiled plans) and the application
    /// <see cref="P7TestDbContext"/>. It runs <see cref="VistaModelKeyDerivationService"/> once — exactly
    /// as the host would at startup — and then exposes each view's completed <see cref="ViewMetadata.KeyFields"/>.
    /// </summary>
    private sealed class P7DerivationHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly IViewRegistry _registry;

        private P7DerivationHarness(SqliteConnection connection, ServiceProvider provider, IViewRegistry registry)
        {
            _connection = connection;
            _provider = provider;
            _registry = registry;
        }

        public static P7DerivationHarness Create(IReadOnlyList<P7ViewKind> views, int orderRows, int lineRows)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var services = new ServiceCollection();

            // The application DbContext. AddVista's captured-context type is null here (Style B only), so
            // the derivation hook resolves the DbContext base — forward it to the concrete context.
            services.AddDbContext<P7TestDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<P7TestDbContext>());

            // Register the selected views: AddVista drains GeneratedExecutionPlanStore and adopts each
            // REAL generated compiled plan, so a KEYLESS single-source view registers without the D106
            // fail-fast and reaches the derivation hook.
            services.AddVista(v =>
            {
                foreach (var kind in views)
                {
                    switch (kind)
                    {
                        case P7ViewKind.OrderDerived:
                            v.Register<P7OrderDerivedView>();
                            break;
                        case P7ViewKind.LineDerived:
                            v.Register<P7LineDerivedView>();
                            break;
                        case P7ViewKind.OrderExplicit:
                            v.Register<P7OrderExplicitView>();
                            break;
                        case P7ViewKind.LineExplicit:
                            v.Register<P7LineExplicitView>();
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(views), kind, null);
                    }
                }
            });

            var provider = services.BuildServiceProvider();

            // Realize the schema and seed data so the property is exercised against a non-empty database;
            // derivation must still read the model, not the rows.
            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<P7TestDbContext>();
                context.Database.EnsureCreated();

                for (var i = 0; i < orderRows; i++)
                {
                    context.Orders.Add(new P7Order { Id = i + 1, Name = "order-" + i, Quantity = i });
                }

                for (var i = 0; i < lineRows; i++)
                {
                    context.OrderLines.Add(new P7LineItem { OrderId = 1, LineNo = i + 1, Sku = "sku-" + i });
                }

                context.SaveChanges();
            }

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();
            var contextAccessor = provider.GetRequiredService<VistaDbContextAccessor>();

            // Sanity: every KEYLESS view must have adopted a generated compiled plan, otherwise its key
            // could never be derived and this would silently test nothing.
            foreach (var name in new[] { P7OrderDerivedView.ViewName, P7LineDerivedView.ViewName })
            {
                if (views.Any(k => ViewNameOf(k) == name) &&
                    planRegistry.Get(name) is not ICompiledViewExecutionPlan)
                {
                    throw new InvalidOperationException(
                        $"No generated compiled plan was adopted for '{name}'; ensure the StyleBExecP7 " +
                        "fixture assembly (with the generator analyzer) is referenced and loaded.");
                }
            }

            // Run the startup model-derivation hook exactly once, as the host would (R6.7).
            var derivation = new VistaModelKeyDerivationService(provider, registry, planRegistry, contextAccessor);
            derivation.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

            return new P7DerivationHarness(connection, provider, registry);
        }

        /// <summary>Returns the resolved key fields of the registered view named <paramref name="name"/>.</summary>
        public IReadOnlyList<string> KeyFieldsOf(string name)
        {
            var view = _registry.Get(name)
                ?? throw new InvalidOperationException($"View '{name}' was not registered.");
            return view.KeyFields;
        }

        public void Dispose()
        {
            _provider.Dispose();
            // Disposing the connection drops the in-memory database; do it last.
            _connection.Dispose();
        }
    }
}
