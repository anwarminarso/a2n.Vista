// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Write;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the deterministic preference and coexistence contract of
/// <see cref="WriteMapperResolver"/> (source-generator-write-mapper task 8.2; Decision Log D121). The
/// resolver is exercised directly (no compilation/generator) against the runtime seams that already
/// exist: <see cref="GeneratedWriteMapperStore"/> (source-generated mapper sink) and
/// <see cref="ReflectionWriteMapper"/> (the fallback oracle). Each generated case builds a random set of
/// views — some with a registered generated mapper, some without — and asserts that
/// <see cref="WriteMapperResolver.Resolve"/> returns the generated mapper for every registered view and
/// the reflection mapper for every unregistered one, consistently across repeated and reordered
/// resolutions.
/// </summary>
/// <remarks>
/// <see cref="GeneratedWriteMapperStore"/> is a process-wide, first-wins static store, so every view in
/// every case uses a unique name (<see cref="UniqueViewName"/>) to stay isolated from sibling tests and
/// from any module-initializer registrations present in the process. A write facet is registered for
/// every view so the reflection fallback can build for the unregistered ones (and so the generated
/// mapper is proven to win even when a facet coexists). Origin is observed behaviorally: the fake
/// generated mapper stamps a sentinel a reflection mapper would never produce.
/// <see cref="ReflectionWriteMapper"/> is RUC-annotated; trimming is not used for tests, so IL2026 is
/// suppressed at the class level, matching the sibling write-path tests.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The test exercises the runtime reflection write mapper by design; trimming is not used for tests.")]
public sealed class ResolverPreferenceCoexistencePropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>Sentinel written only by the fake generated mapper, never by the reflection fallback.</summary>
    private const string GeneratedSentinel = "written-by-generated-mapper";

    /// <summary>Origin label for a view resolved to a source-generated mapper.</summary>
    private const string GeneratedOrigin = "generated";

    /// <summary>Origin label for a view resolved to the reflection fallback mapper.</summary>
    private const string ReflectionOrigin = "reflection";

    // Feature: source-generator-write-mapper, Property 5: For any view, WriteMapperResolver.Resolve
    // returns the generated mapper on every call when one is registered for that view and the reflection
    // mapper on every call when none is, and returns a mapper of the same origin regardless of how many
    // times or in what order the view is resolved.
    //
    // Validates: Requirements 7.1, 7.2, 7.5, 8.4
    [Test]
    public void Resolve_Prefers_Generated_When_Registered_And_Reflection_Otherwise_Consistently()
    {
        // Feature: source-generator-write-mapper, Property 5: For any view, WriteMapperResolver.Resolve
        // returns the generated mapper on every call when one is registered for that view and the
        // reflection mapper on every call when none is, and returns a mapper of the same origin
        // regardless of how many times or in what order the view is resolved.
        var genCase =
            // A random, non-empty set of views; each flag says whether the view has a generated mapper.
            from hasGenerated in Gen.Bool.List[1, 6]
            // A random extra resolution order over the same view set, to vary count and order.
            from extraOrder in Gen.Int[0, hasGenerated.Count - 1].List[0, 16]
            select (hasGenerated, extraOrder);

        genCase.Sample(
            tuple =>
            {
                var (hasGenerated, extraOrder) = tuple;
                var count = hasGenerated.Count;

                // Each case gets its own resolver and its own unique, isolated view names.
                var registry = new WriteFacetRegistry();
                var views = new ViewMetadata[count];
                var expectedOrigin = new string[count];

                for (var i = 0; i < count; i++)
                {
                    var view = BuildWritableView(UniqueViewName($"resolver-coexistence-{i}"));
                    views[i] = view;

                    // A facet is registered for EVERY view, so the reflection fallback can build for the
                    // unregistered ones and the generated mapper is proven to win when both coexist.
                    registry.Register(view.Name, BuildFacet());

                    if (hasGenerated[i])
                    {
                        GeneratedWriteMapperStore.Add(view.Name, GeneratedMapper());
                        expectedOrigin[i] = GeneratedOrigin;
                    }
                    else
                    {
                        expectedOrigin[i] = ReflectionOrigin;
                    }
                }

                var resolver = new WriteMapperResolver(registry);

                // Build a resolution schedule that touches each view many times, in several orders:
                // forward, reversed, forward again, then a random extra order. Every touch must yield the
                // expected origin, proving determinism across count and order (R7.1, R7.2, R7.5).
                var schedule = new List<int>(3 * count + extraOrder.Count);
                for (var i = 0; i < count; i++)
                {
                    schedule.Add(i);
                }

                for (var i = count - 1; i >= 0; i--)
                {
                    schedule.Add(i);
                }

                for (var i = 0; i < count; i++)
                {
                    schedule.Add(i);
                }

                schedule.AddRange(extraOrder);

                // Track the first observed origin per view; every later resolution must match it (R7.5)
                // and must equal the registration-derived expectation (R7.1, R7.2).
                var firstObserved = new string?[count];

                foreach (var index in schedule)
                {
                    var mapper = resolver.Resolve(views[index]);
                    var origin = ObserveOrigin(mapper);

                    if (!string.Equals(origin, expectedOrigin[index], StringComparison.Ordinal))
                    {
                        throw new Exception(
                            $"View #{index} ('{views[index].Name}') resolved to the {origin} mapper, " +
                            $"expected the {expectedOrigin[index]} mapper " +
                            (expectedOrigin[index] == GeneratedOrigin
                                ? "(a generated mapper is registered, so it must win on every call)."
                                : "(no generated mapper is registered, so the reflection fallback must be used)."));
                    }

                    var seen = firstObserved[index];
                    if (seen is null)
                    {
                        firstObserved[index] = origin;
                    }
                    else if (!string.Equals(seen, origin, StringComparison.Ordinal))
                    {
                        throw new Exception(
                            $"View #{index} ('{views[index].Name}') resolved to the {origin} mapper this " +
                            $"time but the {seen} mapper earlier; the origin must be identical across " +
                            "repeated and reordered resolutions.");
                    }
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Applies the resolved mapper to a fixed probe pair and reports whether it was the generated mapper
    /// (which stamps <see cref="GeneratedSentinel"/>) or the reflection mapper (which copies the model's
    /// whitelisted value). The two outcomes are disjoint, so the observed entity state uniquely
    /// identifies the mapper's origin.
    /// </summary>
    private static string ObserveOrigin(WriteMapper mapper)
    {
        var entity = new TestEntity { Id = 7, Name = "original", Price = 1m };
        mapper(new TestCrud { Name = "client-name", Price = 42m }, entity);
        return string.Equals(entity.Name, GeneratedSentinel, StringComparison.Ordinal)
            ? GeneratedOrigin
            : ReflectionOrigin;
    }

    private static string UniqueViewName(string hint) => $"{hint}-{Guid.NewGuid():N}";

    /// <summary>A fake source-generated mapper that stamps a sentinel so it is distinguishable from reflection.</summary>
    private static WriteMapper GeneratedMapper() =>
        (_, entity) => ((TestEntity)entity).Name = GeneratedSentinel;

    /// <summary>
    /// Builds a minimal writable <see cref="ViewMetadata"/> with <c>Id</c> as its key. Fields are empty
    /// because the resolver and the reflection mapper consume only <see cref="ViewMetadata.KeyFields"/>
    /// and the registered <see cref="CrudFacetDefinition"/>.
    /// </summary>
    private static ViewMetadata BuildWritableView(string name) =>
        new(
            Name: name,
            Route: $"/test/{name}",
            QueryType: typeof(TestCrud),
            CrudType: typeof(TestCrud),
            CrudEntityType: typeof(TestEntity),
            Fields: Array.Empty<FieldMetadata>(),
            Authorization: null,
            Limits: new HardLimits(HardLimits.DefaultMaxPageSize, HardLimits.DefaultMaxExportRows),
            IsReadOnly: false)
        {
            KeyFields = [nameof(TestEntity.Id)],
        };

    /// <summary>
    /// Builds the whitelisted write facet for a test view: <c>Name</c> and <c>Price</c> are writable; the
    /// key <c>Id</c> is never mapped.
    /// </summary>
    private static CrudFacetDefinition BuildFacet()
    {
        Expression<Func<TestCrud, string>> nameFrom = c => c.Name;
        Expression<Func<TestEntity, string>> nameTo = e => e.Name;
        Expression<Func<TestCrud, decimal>> priceFrom = c => c.Price;
        Expression<Func<TestEntity, decimal>> priceTo = e => e.Price;

        return new CrudFacetDefinition(
            CrudType: typeof(TestCrud),
            EntityType: typeof(TestEntity),
            WritableFields:
            [
                new WritableFieldMapping(nameof(TestCrud.Name), nameof(TestEntity.Name), nameFrom, nameTo),
                new WritableFieldMapping(nameof(TestCrud.Price), nameof(TestEntity.Price), priceFrom, priceTo),
            ],
            ConcurrencyToken: null,
            AllowsBulk: false);
    }

    /// <summary>Representative typed write contract (<c>TCrud</c>) clients post against.</summary>
    private sealed class TestCrud
    {
        public string Name { get; init; } = string.Empty;

        public decimal Price { get; init; }
    }

    /// <summary>Representative underlying entity (<c>TEntity</c>) writes are applied to.</summary>
    private sealed class TestEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }
    }
}
