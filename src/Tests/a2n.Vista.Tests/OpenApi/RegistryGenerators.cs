// Licensed to the a2n.Vista project. Published artifact — English only.
//
// CsCheck generators of random view registries for the OpenAPI emitter STRUCTURAL property tests
// (spec openapi-emitter, task 8.1; Requirements 1.1, 4.1, 14.1). The registry is the endpoint-parity
// oracle: Properties 1, 2, 5, 6, 7, 8, 9, 10 quantify over arbitrary registries and assert a structural
// invariant on the built document (design "Testing Strategy → Registry generators").
//
// What is randomized (the STRUCTURE the structural properties quantify over): the view Name (unique,
// identifier-safe), Route ("/api/views/{name}", unique), IsReadOnly, key arity (single vs composite ->
// KeyFields), a small random FieldMetadata set, whether the view declares a concurrency TOKEN, and whether
// the view has grid ADAPTERS registered. Because the emitter derives TRow/TCrud from real CLR types
// (QueryType/CrudType) that cannot be generated at runtime, QueryType is drawn from a SMALL fixed pool of
// the compile-once representative rows (EmitterFixtures) and, for writable views, CrudType is the
// representative record CRUD — keeping DTO schema generation REAL while randomizing the structure.
//
// Uniqueness invariant (D101/D103): view names are made globally unique WITHIN a generated registry by
// suffixing a per-registry index, so registry.Add never throws on a duplicate; unique names imply unique
// routes.
//
// Tokens: a writable view's concurrency token lives only on the write-facet registry, so the generator
// produces a matching WriteFacetRegistry alongside the IViewRegistry — writable views are registered with
// a CrudFacetDefinition whose ConcurrencyToken is set iff the view was flagged token-bearing.
//
// Adapters: the emitter's builder ONLY ever iterates the seven core facets (FacetOperations.ForView) and
// has no adapter input on ViewMetadata / the builder surface, so grid-adapter endpoints (D111–D116) are not
// representable through the current registry+builder inputs. The "has adapters" flag is therefore carried
// as a no-op MARKER on the generated registry (AdapterViewNames) so Property 10 (task 9.6) can assert that
// adapter endpoints are absent from the emitted document REGARDLESS of the flag — the builder emits only
// core facets by construction. If a future phase adds an adapter input, this marker is where P10 would wire
// the "adapters present" precondition.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using CsCheck;

namespace a2n.Vista.Tests;

/// <summary>
/// The compile-once CLR-type pool a generated view's <c>TRow</c> is drawn from. Randomizing the STRUCTURE
/// (name/route/readonly/keys/fields/token/adapters) while binding <c>TRow</c>/<c>TCrud</c> to real
/// representative types keeps the RUC DTO schema generation real.
/// </summary>
public enum RowKind
{
    /// <summary><see cref="EmitterFixtures.CatalogItemRow"/> — the read-DTO shape spectrum.</summary>
    CatalogItem,

    /// <summary><see cref="EmitterFixtures.GeoZoneRow"/> — a composite-key row.</summary>
    GeoZone,

    /// <summary><see cref="EmitterFixtures.SubscriptionRow"/> — the writable view's row.</summary>
    Subscription,
}

/// <summary>
/// A randomly-generated view registry plus its matching write-facet registry and the set of views flagged
/// as adapter-bearing. Everything a structural emitter property test needs to build the document and assert
/// its invariant against the registry oracle.
/// </summary>
/// <param name="Registry">The generated <see cref="IViewRegistry"/> (unique names/routes).</param>
/// <param name="WriteFacets">
/// The matching write-facet registry: every writable view is registered, with a concurrency token iff it
/// was flagged token-bearing (the only place a token is expressed, R6.4).
/// </param>
/// <param name="Views">The generated views, in generation order.</param>
/// <param name="AdapterViewNames">
/// The names of views flagged as having grid adapters registered — a no-op marker (see file header): the
/// builder emits only core facets regardless, which is exactly what Property 10 asserts.
/// </param>
public sealed record GeneratedRegistry(
    IViewRegistry Registry,
    WriteFacetRegistry WriteFacets,
    IReadOnlyList<ViewMetadata> Views,
    IReadOnlySet<string> AdapterViewNames);

/// <summary>
/// CsCheck generators of random <see cref="ViewMetadata"/> and <see cref="GeneratedRegistry"/> for the
/// OpenAPI emitter structural property tests (spec openapi-emitter, task 8.1). Uses the same CsCheck-via-
/// TUnit idiom as the sibling property suites (<c>Gen.Int[..]</c>/<c>Gen.Bool</c>/LINQ query composition,
/// consumed by <c>Gen&lt;T&gt;.Sample(action, iter: 100)</c> at ≥100 iterations).
/// </summary>
public static class RegistryGenerators
{
    /// <summary>The pool of identifier-safe name stems a generated view name is built from.</summary>
    private static readonly string[] NameStems =
    {
        "widgets", "orders", "customers", "invoices", "products", "regions", "tenants", "audits",
    };

    /// <summary>
    /// The pool of candidate fields a view's random <see cref="FieldMetadata"/> set is drawn from (distinct
    /// names, so any subset stays valid). Kept small for cost control.
    /// </summary>
    private static readonly (string Name, Type ClrType)[] FieldPool =
    {
        ("code", typeof(string)),
        ("amount", typeof(int)),
        ("active", typeof(bool)),
        ("createdAt", typeof(DateTime)),
        ("score", typeof(double)),
    };

    // ---- Intermediate randomized shape (name assigned per-registry for uniqueness) -------------------

    private readonly record struct ViewShape(
        string Stem,
        bool IsReadOnly,
        bool CompositeKey,
        RowKind Row,
        bool HasToken,
        bool HasAdapter,
        bool[] IncludedFields);

    /// <summary>Maps a <see cref="RowKind"/> to its representative compile-once CLR row type.</summary>
    private static Type QueryTypeFor(RowKind kind) => kind switch
    {
        RowKind.CatalogItem => typeof(EmitterFixtures.CatalogItemRow),
        RowKind.GeoZone => typeof(EmitterFixtures.GeoZoneRow),
        _ => typeof(EmitterFixtures.SubscriptionRow),
    };

    private static Gen<T> Pick<T>(IReadOnlyList<T> values) =>
        Gen.Int[0, values.Count - 1].Select(i => values[i]);

    /// <summary>A single randomized view shape (its final unique name is assigned when the registry is built).</summary>
    private static readonly Gen<ViewShape> Shape =
        from stem in Pick(NameStems)
        from isReadOnly in Gen.Bool
        from compositeKey in Gen.Bool
        from row in Gen.Int[0, 2].Select(i => (RowKind)i)
        from hasToken in Gen.Bool
        from hasAdapter in Gen.Bool
        from includedFields in Gen.Bool.Array[FieldPool.Length]
        select new ViewShape(stem, isReadOnly, compositeKey, row, hasToken, hasAdapter, includedFields);

    /// <summary>
    /// Generates a set of <paramref name="minViews"/>..<paramref name="maxViews"/> random views as a
    /// <see cref="GeneratedRegistry"/>. Names/routes are made unique within the registry, writable views
    /// are registered into the write-facet registry (with a token iff flagged), and adapter-flagged view
    /// names are collected as a marker for Property 10.
    /// </summary>
    /// <param name="minViews">The minimum number of views (default 1).</param>
    /// <param name="maxViews">The maximum number of views (default 6).</param>
    /// <returns>A generator of populated <see cref="GeneratedRegistry"/> instances.</returns>
    public static Gen<GeneratedRegistry> Registry(int minViews = 1, int maxViews = 6) =>
        from count in Gen.Int[minViews, maxViews]
        from shapes in Shape.Array[count]
        select Build(shapes);

    /// <summary>
    /// Generates a single random <see cref="ViewMetadata"/> (unique-enough name via a random numeric suffix)
    /// for tests that operate on one view at a time.
    /// </summary>
    /// <returns>A generator of standalone <see cref="ViewMetadata"/>.</returns>
    public static Gen<ViewMetadata> View() =>
        from shape in Shape
        from suffix in Gen.Int[0, 999_999]
        select BuildView(shape, shape.Stem + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture));

    // ---- Builders ------------------------------------------------------------------------------------

    private static GeneratedRegistry Build(IReadOnlyList<ViewShape> shapes)
    {
        var registry = new ViewRegistry();
        var writeFacets = new WriteFacetRegistry();
        var views = new List<ViewMetadata>(shapes.Count);
        var adapterNames = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < shapes.Count; i++)
        {
            var shape = shapes[i];

            // Per-registry index guarantees a globally-unique, identifier-safe name (and hence route).
            var name = shape.Stem + "V" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var view = BuildView(shape, name);

            registry.Add(view);
            views.Add(view);

            if (!view.IsReadOnly)
            {
                // Every writable view is registered on the write-facet registry; the concurrency token is
                // set iff the shape was flagged token-bearing (R6.4). The token selector is over the
                // representative entity — the builder only checks token presence, not its target type.
                Expression<Func<EmitterFixtures.SubscriptionEntity, int>>? token =
                    shape.HasToken ? e => e.Version : null;

                writeFacets.Register(name, new CrudFacetDefinition(
                    CrudType: typeof(EmitterFixtures.SubscriptionCrud),
                    EntityType: typeof(EmitterFixtures.SubscriptionEntity),
                    WritableFields: Array.Empty<WritableFieldMapping>(),
                    ConcurrencyToken: token,
                    AllowsBulk: false));
            }

            if (shape.HasAdapter)
            {
                adapterNames.Add(name);
            }
        }

        return new GeneratedRegistry(registry, writeFacets, views, adapterNames);
    }

    private static ViewMetadata BuildView(ViewShape shape, string name)
    {
        var route = "/api/views/" + name;

        // A writable view always carries a CrudType so the write body specializes to the real record CRUD.
        var crudType = shape.IsReadOnly ? null : typeof(EmitterFixtures.SubscriptionCrud);
        var crudEntityType = shape.IsReadOnly ? null : typeof(EmitterFixtures.SubscriptionEntity);

        var keyFields = shape.CompositeKey
            ? new[] { name + "KeyA", name + "KeyB" }
            : new[] { name + "Key" };

        return new ViewMetadata(
            Name: name,
            Route: route,
            QueryType: QueryTypeFor(shape.Row),
            CrudType: crudType,
            CrudEntityType: crudEntityType,
            Fields: BuildFields(shape.IncludedFields),
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: shape.IsReadOnly)
        {
            KeyFields = keyFields,
        };
    }

    private static IReadOnlyList<FieldMetadata> BuildFields(bool[] included)
    {
        var fields = new List<FieldMetadata>();
        for (var i = 0; i < FieldPool.Length; i++)
        {
            if (included[i])
            {
                var (fieldName, clrType) = FieldPool[i];
                fields.Add(FieldMetadata.Create(
                    fieldName,
                    clrType,
                    allowedOperators: FilterOperator.Equals));
            }
        }

        return fields;
    }
}
