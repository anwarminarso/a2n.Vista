// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
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
/// Property-based test for the whitelisted-only assignment guarantee of the reflection write mapper
/// (write-path task 3.4; Decision Log D119). The mapper is a near-pure function over
/// <c>(model, entity)</c>, so this property runs against pure in-memory <c>TCrud</c>/<c>TEntity</c>
/// objects (no database), generating models that deliberately carry values for non-whitelisted members,
/// key fields, and the concurrency token to prove none of them leak onto the entity.
/// </summary>
/// <remarks>
/// The write facet is built directly (Style A shape) and published through the concrete
/// <see cref="WriteFacetRegistry"/>. To exercise the mapper's runtime defense-in-depth (Requirements
/// R5.1, R5.3, R4.5) the whitelist intentionally also includes a key-field mapping, a
/// concurrency-token mapping, and a navigation (non-scalar) mapping — all of which the compiled mapper
/// must skip even though they were "authored", leaving those entity members byte-identical to their
/// pre-write values. <see cref="ReflectionWriteMapper"/> is RUC-annotated (it compiles the captured
/// selectors at runtime); trimming is not used for tests, so IL2026 is suppressed at the class level,
/// matching the sibling write-path tests.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The test exercises the runtime reflection write mapper by design; trimming is not used for tests.")]
public sealed class WhitelistedOnlyAssignmentPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>Model-side name pool (disjoint from the entity seed pool so an assignment is observable).</summary>
    private static readonly string[] ModelNames = { "", "n1", "name-two", "product-x" };

    /// <summary>Entity-seed name pool (disjoint from the model pool).</summary>
    private static readonly string[] EntityNames = { "E-a", "E-name1", "E-name2", "E-xyz" };

    /// <summary>Model-side secret pool for the non-whitelisted string member.</summary>
    private static readonly string[] ModelSecrets = { "", "s-x", "s-y", "s-z" };

    /// <summary>Entity-seed secret pool (disjoint from the model pool).</summary>
    private static readonly string[] EntitySecrets = { "E-s1", "E-s2", "E-s3", "E-s4" };

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    /// <summary>
    /// Generates a write model carrying whitelisted members (Name, Price), non-whitelisted members
    /// (Secret, Quantity, Related), the key member (Id), and the concurrency-token member (Version). The
    /// protected/non-whitelisted integer members are drawn from a low range disjoint from the entity
    /// seed's high range, so any leak would produce a value different from the pre-write value.
    /// </summary>
    private static readonly Gen<TestCrud> GenModel =
        from name in Pick(ModelNames)
        from price in Gen.Int[0, 500]
        from secret in Pick(ModelSecrets)
        from quantity in Gen.Int[0, 500]
        from id in Gen.Int[1, 500]
        from version in Gen.Int[1, 500]
        select new TestCrud
        {
            Name = name,
            Price = price,
            Secret = secret,
            Quantity = quantity,
            Id = id,
            Version = version,
            Related = new RelatedThing { Tag = "model-" + id },
        };

    /// <summary>
    /// Generates the entity's pre-write state from ranges/pools disjoint from <see cref="GenModel"/>, so
    /// every protected member's pre-write value differs from any value the model could carry.
    /// </summary>
    private static readonly Gen<EntitySeed> GenSeed =
        from name in Pick(EntityNames)
        from price in Gen.Int[600, 1000]
        from secret in Pick(EntitySecrets)
        from quantity in Gen.Int[600, 1000]
        from id in Gen.Int[600, 1000]
        from version in Gen.Int[600, 1000]
        select new EntitySeed(id, name, price, version, secret, quantity);

    // Feature: write-path, Property 1: For any writable view and any generated write model — including
    // models that carry values for non-whitelisted members, key fields, and the concurrency token —
    // after applying the write mapper to a target entity, every entity member named by a MapWritable
    // target holds the model's value, and every other entity member is byte-identical to its pre-write
    // value (key fields and the concurrency token included), with no error raised for the ignored members.
    //
    // Validates: Requirements 1.1, 2.1, 4.1, 4.2, 4.5, 5.1, 5.3
    [Test]
    public void Mapper_Assigns_Only_Whitelisted_Members_And_Leaves_Every_Other_Member_Untouched()
    {
        // The mapper is built once and cached by view name; the same delegate is exercised per case.
        var mapper = BuildMapper();

        var genCase =
            from model in GenModel
            from seed in GenSeed
            select (model, seed);

        genCase.Sample(
            tuple =>
            {
                var (model, seed) = tuple;

                var relatedSeed = new RelatedThing { Tag = "entity-" + seed.Id };
                var entity = new TestSourceEntity
                {
                    Id = seed.Id,
                    Name = seed.Name,
                    Price = seed.Price,
                    Version = seed.Version,
                    Secret = seed.Secret,
                    Quantity = seed.Quantity,
                    Related = relatedSeed,
                };

                // Apply the whitelisted mapping. No error may be raised for ignored members (R4.2, R5.1, R5.3).
                mapper(model, entity);

                // Whitelisted scalar members hold the model's value (R4.1, R1.1, R2.1). Pools are disjoint,
                // so a missing assignment would leave the pre-write value and be detected.
                if (!string.Equals(entity.Name, model.Name, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Whitelisted member Name was '{entity.Name}', expected the model's '{model.Name}'.");
                }

                if (entity.Price != model.Price)
                {
                    throw new Exception(
                        $"Whitelisted member Price was {entity.Price}, expected the model's {model.Price}.");
                }

                // Key field is never assigned even though the whitelist named it (R5.1, defense in depth).
                if (entity.Id != seed.Id)
                {
                    throw new Exception(
                        $"Key member Id changed from {seed.Id} to {entity.Id}; the mapper must never assign a key field.");
                }

                // Concurrency token is never assigned even though the whitelist named it (R5.3).
                if (entity.Version != seed.Version)
                {
                    throw new Exception(
                        $"Concurrency-token member Version changed from {seed.Version} to {entity.Version}; " +
                        "the mapper must never assign the token.");
                }

                // Non-whitelisted scalar members are byte-identical to their pre-write value (R4.2).
                if (!string.Equals(entity.Secret, seed.Secret, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Non-whitelisted member Secret changed from '{seed.Secret}' to '{entity.Secret}'.");
                }

                if (entity.Quantity != seed.Quantity)
                {
                    throw new Exception(
                        $"Non-whitelisted member Quantity changed from {seed.Quantity} to {entity.Quantity}.");
                }

                // Navigation (non-scalar) target is skipped, so the reference is unchanged (R4.5).
                if (!ReferenceEquals(entity.Related, relatedSeed))
                {
                    throw new Exception(
                        "Navigation member Related was reassigned; the mapper must skip non-scalar targets.");
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Builds a <see cref="ViewMetadata"/> and a captured <see cref="CrudFacetDefinition"/>, publishes the
    /// facet into a concrete <see cref="WriteFacetRegistry"/>, and resolves the compiled
    /// <see cref="WriteMapper"/> from <see cref="ReflectionWriteMapper"/>. The whitelist deliberately
    /// includes the key, the token, and a navigation mapping to exercise the mapper's runtime skips.
    /// </summary>
    private static WriteMapper BuildMapper()
    {
        const string viewName = "write-whitelist-property";

        var view = new ViewMetadata(
            Name: viewName,
            Route: $"/test/{viewName}",
            QueryType: typeof(TestSourceEntity),
            CrudType: typeof(TestCrud),
            CrudEntityType: typeof(TestSourceEntity),
            Fields: Array.Empty<FieldMetadata>(),
            Authorization: null,
            Limits: new HardLimits(HardLimits.DefaultMaxPageSize, HardLimits.DefaultMaxExportRows),
            IsReadOnly: false)
        {
            KeyFields = new[] { nameof(TestSourceEntity.Id) },
        };

        // Ordered whitelist: two legitimate scalar mappings, plus a key mapping, a token mapping, and a
        // navigation mapping the compiled mapper must skip (defense in depth on top of the build-time guards).
        var writable = new[]
        {
            Map<string>(nameof(TestCrud.Name), nameof(TestSourceEntity.Name), c => c.Name, e => e.Name),
            Map<decimal>(nameof(TestCrud.Price), nameof(TestSourceEntity.Price), c => c.Price, e => e.Price),
            Map<int>(nameof(TestCrud.Id), nameof(TestSourceEntity.Id), c => c.Id, e => e.Id),
            Map<int>(nameof(TestCrud.Version), nameof(TestSourceEntity.Version), c => c.Version, e => e.Version),
            Map<RelatedThing?>(
                nameof(TestCrud.Related), nameof(TestSourceEntity.Related), c => c.Related, e => e.Related),
        };

        var facet = new CrudFacetDefinition(
            CrudType: typeof(TestCrud),
            EntityType: typeof(TestSourceEntity),
            WritableFields: writable,
            ConcurrencyToken: (Expression<Func<TestSourceEntity, int>>)(e => e.Version),
            AllowsBulk: false);

        var registry = new WriteFacetRegistry();
        registry.Register(viewName, facet);

        return new ReflectionWriteMapper(registry).GetOrCreate(view);
    }

    /// <summary>Builds a single <see cref="WritableFieldMapping"/> from strongly-typed selectors.</summary>
    private static WritableFieldMapping Map<TProp>(
        string crudMember,
        string entityMember,
        Expression<Func<TestCrud, TProp>> from,
        Expression<Func<TestSourceEntity, TProp>> to) =>
        new(crudMember, entityMember, from, to);

    /// <summary>Pre-write entity state, drawn from ranges/pools disjoint from the generated model.</summary>
    private readonly record struct EntitySeed(
        int Id,
        string Name,
        decimal Price,
        int Version,
        string Secret,
        int Quantity);

    /// <summary>A related entity, used only to give the source a navigation (non-scalar) member.</summary>
    private sealed class RelatedThing
    {
        public string Tag { get; set; } = string.Empty;
    }

    /// <summary>
    /// The typed write contract. It carries the whitelisted members plus the key, the token, and
    /// non-whitelisted members, so a generated model can attempt to assign every one of them.
    /// </summary>
    private sealed class TestCrud
    {
        public string Name { get; init; } = string.Empty;

        public decimal Price { get; init; }

        public string Secret { get; init; } = string.Empty;

        public int Quantity { get; init; }

        public int Id { get; init; }

        public int Version { get; init; }

        public RelatedThing? Related { get; init; }
    }

    /// <summary>The EF-shaped source entity a write is applied to (single-source, Id-keyed).</summary>
    private sealed class TestSourceEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public int Version { get; set; }

        public string Secret { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public RelatedThing? Related { get; set; }
    }
}
