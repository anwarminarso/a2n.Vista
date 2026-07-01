// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Write;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Unit tests for <see cref="WriteMapperResolver"/> — the fixed-signature write-path seam that turns a
/// <see cref="ViewMetadata"/> into exactly one resolved <see cref="WriteMapper"/> per write (Decision
/// Log D119). They pin the deterministic preference contract: a registered source-generated mapper is
/// chosen over the reflection fallback on <em>every</em> write; with none registered the reflection
/// whitelist mapper is used on every write; and the choice is resolved exactly once per write without
/// the executor ever branching on which implementation produced the mapper
/// (Requirements R13.1, R13.2, R13.3, R13.4).
/// </summary>
/// <remarks>
/// <see cref="GeneratedWriteMapperStore"/> is a process-wide, first-wins static store, so every test
/// uses a unique view name (<see cref="UniqueViewName"/>) to stay isolated from sibling tests and from
/// any module-initializer registrations present in the process.
/// </remarks>
public sealed class WriteMapperResolverTests
{
    private static string UniqueViewName(string hint) => $"{hint}-{Guid.NewGuid():N}";

    /// <summary>Sentinel written only by the fake generated mapper, never by the reflection fallback.</summary>
    private const string GeneratedSentinel = "written-by-generated-mapper";

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
    /// Builds the whitelisted write facet for the test view: <c>Name</c> and <c>Price</c> are writable;
    /// the key <c>Id</c> is never mapped.
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

    /// <summary>A fake source-generated mapper that stamps a sentinel so it is distinguishable from reflection.</summary>
    private static WriteMapper GeneratedMapper() =>
        (_, entity) => ((TestEntity)entity).Name = GeneratedSentinel;

    // R13.3, R13.2: a registered generated mapper is returned (and applied) on every write.
    [Test]
    public async Task Resolve_Returns_Generated_Mapper_When_Registered()
    {
        var view = BuildWritableView(UniqueViewName("generated-wins"));
        GeneratedWriteMapperStore.Add(view.Name, GeneratedMapper());

        // The registry has NO facet for this view: if the resolver ever tried the reflection fallback it
        // would throw, so a clean resolve additionally proves the generated branch was taken.
        var resolver = new WriteMapperResolver(new WriteFacetRegistry());

        var mapper = resolver.Resolve(view);

        var model = new TestCrud { Name = "client-name", Price = 42m };
        var entity = new TestEntity { Id = 7, Name = "original", Price = 1m };
        mapper(model, entity);

        // The generated delegate ran (sentinel), and it did not copy the client value like reflection would.
        await Assert.That(entity.Name).IsEqualTo(GeneratedSentinel);
    }

    // R13.4: with no generated mapper registered, the reflection whitelist mapper is used.
    [Test]
    public async Task Resolve_Falls_Back_To_Reflection_Mapper_When_None_Registered()
    {
        var view = BuildWritableView(UniqueViewName("reflection-fallback"));

        var registry = new WriteFacetRegistry();
        registry.Register(view.Name, BuildFacet());
        var resolver = new WriteMapperResolver(registry);

        var mapper = resolver.Resolve(view);

        var model = new TestCrud { Name = "client-name", Price = 42m };
        var entity = new TestEntity { Id = 7, Name = "original", Price = 1m };
        mapper(model, entity);

        // The reflection mapper copies the whitelisted client values (and never the sentinel)...
        await Assert.That(entity.Name).IsEqualTo("client-name");
        await Assert.That(entity.Price).IsEqualTo(42m);
        // ...while leaving the key field untouched (defense in depth).
        await Assert.That(entity.Id).IsEqualTo(7);
    }

    // R13.2, R13.3: the generated mapper wins on EVERY resolve, even when a facet is also registered.
    [Test]
    public async Task Resolve_Prefers_Generated_Over_Reflection_On_Every_Write()
    {
        var view = BuildWritableView(UniqueViewName("generated-over-reflection"));
        GeneratedWriteMapperStore.Add(view.Name, GeneratedMapper());

        // Both a generated mapper AND a reflection facet exist; the generated one must always win.
        var registry = new WriteFacetRegistry();
        registry.Register(view.Name, BuildFacet());
        var resolver = new WriteMapperResolver(registry);

        for (var write = 0; write < 5; write++)
        {
            var mapper = resolver.Resolve(view);
            var entity = new TestEntity { Id = 1, Name = "original", Price = 1m };
            mapper(new TestCrud { Name = "client-name", Price = 9m }, entity);

            // Generated stamped the sentinel; reflection (which would set "client-name"/9m) never ran.
            await Assert.That(entity.Name).IsEqualTo(GeneratedSentinel);
            await Assert.That(entity.Price).IsEqualTo(1m);
        }
    }

    // R13.1, R13.4: resolution is deterministic — repeated resolves make the same choice, and (with a
    // generated hit) the reflection fallback is never built, proven by an empty registry that would throw.
    [Test]
    public async Task Resolve_Is_Deterministic_And_Does_Not_Build_Reflection_When_Generated_Hits()
    {
        var view = BuildWritableView(UniqueViewName("deterministic-generated"));
        GeneratedWriteMapperStore.Add(view.Name, GeneratedMapper());

        // No facet registered: any reflection-fallback build attempt throws InvalidOperationException.
        var resolver = new WriteMapperResolver(new WriteFacetRegistry());

        // Repeated resolves must all succeed and yield the generated mapper (never touch the fallback).
        for (var write = 0; write < 3; write++)
        {
            var mapper = resolver.Resolve(view);
            var entity = new TestEntity { Id = 1, Name = "original", Price = 1m };
            mapper(new TestCrud { Name = "client-name", Price = 9m }, entity);
            await Assert.That(entity.Name).IsEqualTo(GeneratedSentinel);
        }
    }

    // R13.4: with no generated mapper, the fallback is likewise deterministic across repeated writes.
    [Test]
    public async Task Resolve_Reflection_Fallback_Is_Deterministic_Across_Writes()
    {
        var view = BuildWritableView(UniqueViewName("deterministic-reflection"));

        var registry = new WriteFacetRegistry();
        registry.Register(view.Name, BuildFacet());
        var resolver = new WriteMapperResolver(registry);

        for (var write = 0; write < 3; write++)
        {
            var mapper = resolver.Resolve(view);
            var entity = new TestEntity { Id = 3, Name = "original", Price = 1m };
            mapper(new TestCrud { Name = "client-name", Price = 5m }, entity);

            await Assert.That(entity.Name).IsEqualTo("client-name");
            await Assert.That(entity.Price).IsEqualTo(5m);
            await Assert.That(entity.Id).IsEqualTo(3);
        }
    }
}
