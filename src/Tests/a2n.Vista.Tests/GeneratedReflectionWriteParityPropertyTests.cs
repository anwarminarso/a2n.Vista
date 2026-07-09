// Licensed to the a2n.Vista project. Published artifact — English only.
//
// MASTER oracle-parity property test for the Phase 3 generated WRITE MAPPER
// (spec source-generator-write-mapper, task 7.2; Decision Log D121/D122). This is the backbone guard of
// the feature: the source-generated WriteMapper must be observationally identical to the reflection
// oracle for every (model, entity) value pair.
//
// Feature: source-generator-write-mapper, Property 1: For any typed Style B writable view that has a
// generated write mapper, and for any (model, entity) pair, applying the generated WriteMapper leaves the
// entity byte-identical — member by member, including keys, the concurrency token, navigations, and every
// non-whitelisted member — to the entity produced by the ReflectionWriteMapper built from the same
// CrudFacetDefinition applied to an equal copy of the pair. Empty or fully-omitted whitelists yield an
// unchanged entity and raise no error.
//
// Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 8.5
//
// Strategy (design "Cost control for the master parity property", model-based): the representative typed
// Style B writable views in a2n.Vista.GeneratorWriteMapperSample are compiled ONCE; their generated write
// mappers register into GeneratedWriteMapperStore at module load. Per view the test resolves the GENERATED
// mapper from the store and builds the ReflectionWriteMapper ORACLE from the same captured
// CrudFacetDefinition (read back from the composition root's IWriteFacetRegistry). A CsCheck generator then
// quantifies over random (model, entity) VALUES — including nulls, empty strings, extreme numerics, and
// byte[] contents — applying each mapper to an equal copy of the pair and asserting member-by-member
// equality of the mutated entities. Minimum 100 generated cases (CsCheck default iter). PBT library: CsCheck.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.GeneratorWriteMapperSample;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using CsCheck;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property 1 — generated/reflection write parity (the master, model-based guard, task 7.2). The
/// source-generated <see cref="WriteMapper"/> is the implementation under test; the
/// <see cref="ReflectionWriteMapper"/> is the behavioral oracle. See the file header for the full
/// strategy.
/// </summary>
/// <remarks>
/// Both <c>AddVista</c>/<c>Register&lt;TView&gt;</c> (runtime reflection authoring) and
/// <see cref="ReflectionWriteMapper"/> (compiles the captured selectors at runtime) are RUC-annotated;
/// this test drives the reflection oracle on purpose, so the trim/AOT diagnostic is suppressed at the
/// class level (tests are never trimmed), matching the sibling write-path property tests.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The parity test drives the RUC reflection oracle by design; trimming is not used for tests.")]
public sealed class GeneratedReflectionWriteParityPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy, CsCheck default).</summary>
    private const int Iterations = 100;

    // ---- shared value generators (nulls, empties, extremes, byte[] contents) ------------------------

    private static readonly string[] StringPool =
        { "", "alpha", "beta-2", "gamma value", "  spaced  ", "δ-unicode-Ω" };

    private static Gen<string> PickString => Gen.Int[0, StringPool.Length - 1].Select(i => StringPool[i]);

    private static Gen<string?> GenNullableString =>
        from present in Gen.Int[0, 3]
        from s in PickString
        select present == 0 ? (string?)null : s;

    private static Gen<int> GenInt => Gen.Int[-100_000, 100_000];

    private static Gen<int?> GenNullableInt =>
        from present in Gen.Int[0, 3]
        from v in GenInt
        select present == 0 ? (int?)null : v;

    private static Gen<long> GenLong =>
        from hi in Gen.Int
        from lo in Gen.Int
        select ((long)hi << 32) ^ (uint)lo;

    // Finite, deterministic doubles/decimals (avoid NaN so byte-identity comparison is well defined).
    private static Gen<double> GenDouble =>
        from n in Gen.Int[-1_000_000, 1_000_000]
        from d in Gen.Int[1, 1_000]
        select (double)n / d;

    private static Gen<decimal> GenDecimal =>
        from n in Gen.Int[-1_000_000, 1_000_000]
        from d in Gen.Int[1, 1_000]
        select (decimal)n / d;

    private static Gen<DateTime> GenDateTime =>
        from days in Gen.Int[0, 40_000]
        from secs in Gen.Int[0, 86_399]
        select new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(days).AddSeconds(secs);

    private static Gen<DateTime?> GenNullableDateTime =>
        from present in Gen.Int[0, 3]
        from d in GenDateTime
        select present == 0 ? (DateTime?)null : d;

    private static Gen<byte> GenByte => Gen.Int[0, 255].Select(i => (byte)i);

    private static Gen<byte[]> GenBytes =>
        from n in Gen.Int[0, 8]
        from arr in GenByte.Array[n]
        select arr;

    private static Gen<byte[]?> GenNullableBytes =>
        from present in Gen.Int[0, 3]
        from arr in GenBytes
        select present == 0 ? (byte[]?)null : arr;

    private static Gen<Guid> GenGuid => GenByte.Array[16].Select(b => new Guid(b));

    private static Gen<WmGrade> GenGrade => Gen.Int[0, 2].Select(i => (WmGrade)i);

    // ---- Case 1: ONE mapping (minimal generated body) -----------------------------------------------

    [Test]
    public void OneMapping_Generated_Matches_Reflection_Oracle()
    {
        // Feature: source-generator-write-mapper, Property 1: the generated mapper leaves the entity
        // byte-identical, member by member, to the reflection oracle for any (model, entity) pair.
        var models =
            from text in PickString
            select new OneMappingCrud { Text = text };

        var entities =
            from id in GenInt
            from text in PickString
            select new OneMappingEntity { Id = id, Text = text };

        AssertParity<OneMappingView, OneMappingCrud, OneMappingEntity>(
            OneMappingView.ViewName,
            models,
            entities,
            e => new OneMappingEntity { Id = e.Id, Text = e.Text },
            (g, r) =>
            {
                Expect(g.Id == r.Id, "OneMapping.Id", g.Id, r.Id);
                Expect(string.Equals(g.Text, r.Text, StringComparison.Ordinal), "OneMapping.Text", g.Text, r.Text);
            });
    }

    // ---- Case 2: MANY ordered mappings --------------------------------------------------------------

    [Test]
    public void ManyMappings_Generated_Matches_Reflection_Oracle()
    {
        // Feature: source-generator-write-mapper, Property 1: several ordered scalar assignments match the
        // reflection oracle member for member.
        var models =
            from title in PickString
            from body in PickString
            from priority in GenInt
            from weight in GenInt
            from pinned in Gen.Bool
            select new ManyMappingsCrud
            {
                Title = title,
                Body = body,
                Priority = priority,
                Weight = weight,
                Pinned = pinned,
            };

        var entities =
            from id in GenInt
            from title in PickString
            from body in PickString
            from priority in GenInt
            from weight in GenInt
            from pinned in Gen.Bool
            select new ManyMappingsEntity
            {
                Id = id,
                Title = title,
                Body = body,
                Priority = priority,
                Weight = weight,
                Pinned = pinned,
            };

        AssertParity<ManyMappingsView, ManyMappingsCrud, ManyMappingsEntity>(
            ManyMappingsView.ViewName,
            models,
            entities,
            e => new ManyMappingsEntity
            {
                Id = e.Id,
                Title = e.Title,
                Body = e.Body,
                Priority = e.Priority,
                Weight = e.Weight,
                Pinned = e.Pinned,
            },
            (g, r) =>
            {
                Expect(g.Id == r.Id, "ManyMappings.Id", g.Id, r.Id);
                Expect(string.Equals(g.Title, r.Title, StringComparison.Ordinal), "ManyMappings.Title", g.Title, r.Title);
                Expect(string.Equals(g.Body, r.Body, StringComparison.Ordinal), "ManyMappings.Body", g.Body, r.Body);
                Expect(g.Priority == r.Priority, "ManyMappings.Priority", g.Priority, r.Priority);
                Expect(g.Weight == r.Weight, "ManyMappings.Weight", g.Weight, r.Weight);
                Expect(g.Pinned == r.Pinned, "ManyMappings.Pinned", g.Pinned, r.Pinned);
            });
    }

    // ---- Case 3: ALIASING (two source members → one entity member; order is observable, R4.6) -------

    [Test]
    public void Aliasing_Generated_Matches_Reflection_Oracle()
    {
        // Feature: source-generator-write-mapper, Property 1: two ordered assignments to the same target
        // apply in the same relative order in both mappers (last write wins), R4.6.
        var models =
            from primary in PickString
            from secondary in PickString
            select new AliasingCrud { Primary = primary, Secondary = secondary };

        var entities =
            from id in GenInt
            from note in PickString
            select new AliasingEntity { Id = id, Note = note };

        AssertParity<AliasingView, AliasingCrud, AliasingEntity>(
            AliasingView.ViewName,
            models,
            entities,
            e => new AliasingEntity { Id = e.Id, Note = e.Note },
            (g, r) =>
            {
                Expect(g.Id == r.Id, "Aliasing.Id", g.Id, r.Id);
                Expect(string.Equals(g.Note, r.Note, StringComparison.Ordinal), "Aliasing.Note", g.Note, r.Note);
            });
    }

    // ---- Case 4: NULLABLE and byte[] scalars (nulls + array references) ------------------------------

    [Test]
    public void NullableAndBinary_Generated_Matches_Reflection_Oracle()
    {
        // Feature: source-generator-write-mapper, Property 1: nullable value-type and byte[] scalar
        // assignments (including nulls) match the reflection oracle.
        var models =
            from count in GenNullableInt
            from when in GenNullableDateTime
            from blob in GenNullableBytes
            from signature in GenBytes
            from note in GenNullableString
            select new NullableAndBinaryCrud
            {
                Count = count,
                When = when,
                Blob = blob,
                Signature = signature,
                Note = note,
            };

        var entities =
            from id in GenInt
            from count in GenNullableInt
            from when in GenNullableDateTime
            from blob in GenNullableBytes
            from signature in GenBytes
            from note in GenNullableString
            select new NullableAndBinaryEntity
            {
                Id = id,
                Count = count,
                When = when,
                Blob = blob,
                Signature = signature,
                Note = note,
            };

        AssertParity<NullableAndBinaryView, NullableAndBinaryCrud, NullableAndBinaryEntity>(
            NullableAndBinaryView.ViewName,
            models,
            entities,
            e => new NullableAndBinaryEntity
            {
                Id = e.Id,
                Count = e.Count,
                When = e.When,
                Blob = CloneBytes(e.Blob),
                Signature = CloneBytes(e.Signature)!,
                Note = e.Note,
            },
            (g, r) =>
            {
                Expect(g.Id == r.Id, "NullableAndBinary.Id", g.Id, r.Id);
                Expect(Nullable.Equals(g.Count, r.Count), "NullableAndBinary.Count", g.Count, r.Count);
                Expect(Nullable.Equals(g.When, r.When), "NullableAndBinary.When", g.When, r.When);
                Expect(BytesEqual(g.Blob, r.Blob), "NullableAndBinary.Blob", Describe(g.Blob), Describe(r.Blob));
                Expect(BytesEqual(g.Signature, r.Signature), "NullableAndBinary.Signature", Describe(g.Signature), Describe(r.Signature));
                Expect(string.Equals(g.Note, r.Note, StringComparison.Ordinal), "NullableAndBinary.Note", g.Note, r.Note);
            });
    }

    // ---- Case 5: MIXED scalar member types ----------------------------------------------------------

    [Test]
    public void MixedTypes_Generated_Matches_Reflection_Oracle()
    {
        // Feature: source-generator-write-mapper, Property 1: parity holds across the value-type and
        // reference-scalar spectrum (string, int, long, double, decimal, bool, DateTime, Guid, enum).
        var models =
            from name in PickString
            from quantity in GenInt
            from ticks in GenLong
            from ratio in GenDouble
            from amount in GenDecimal
            from active in Gen.Bool
            from timestamp in GenDateTime
            from reference in GenGuid
            from grade in GenGrade
            select new MixedTypesCrud
            {
                Name = name,
                Quantity = quantity,
                Ticks = ticks,
                Ratio = ratio,
                Amount = amount,
                Active = active,
                Timestamp = timestamp,
                Reference = reference,
                Grade = grade,
            };

        var entities =
            from id in GenInt
            from name in PickString
            from quantity in GenInt
            from ticks in GenLong
            from ratio in GenDouble
            from amount in GenDecimal
            from active in Gen.Bool
            from timestamp in GenDateTime
            from reference in GenGuid
            from grade in GenGrade
            select new MixedTypesEntity
            {
                Id = id,
                Name = name,
                Quantity = quantity,
                Ticks = ticks,
                Ratio = ratio,
                Amount = amount,
                Active = active,
                Timestamp = timestamp,
                Reference = reference,
                Grade = grade,
            };

        AssertParity<MixedTypesView, MixedTypesCrud, MixedTypesEntity>(
            MixedTypesView.ViewName,
            models,
            entities,
            e => new MixedTypesEntity
            {
                Id = e.Id,
                Name = e.Name,
                Quantity = e.Quantity,
                Ticks = e.Ticks,
                Ratio = e.Ratio,
                Amount = e.Amount,
                Active = e.Active,
                Timestamp = e.Timestamp,
                Reference = e.Reference,
                Grade = e.Grade,
            },
            (g, r) =>
            {
                Expect(g.Id == r.Id, "MixedTypes.Id", g.Id, r.Id);
                Expect(string.Equals(g.Name, r.Name, StringComparison.Ordinal), "MixedTypes.Name", g.Name, r.Name);
                Expect(g.Quantity == r.Quantity, "MixedTypes.Quantity", g.Quantity, r.Quantity);
                Expect(g.Ticks == r.Ticks, "MixedTypes.Ticks", g.Ticks, r.Ticks);
                Expect(g.Ratio.Equals(r.Ratio), "MixedTypes.Ratio", g.Ratio, r.Ratio);
                Expect(g.Amount == r.Amount, "MixedTypes.Amount", g.Amount, r.Amount);
                Expect(g.Active == r.Active, "MixedTypes.Active", g.Active, r.Active);
                Expect(g.Timestamp == r.Timestamp, "MixedTypes.Timestamp", g.Timestamp, r.Timestamp);
                Expect(g.Reference == r.Reference, "MixedTypes.Reference", g.Reference, r.Reference);
                Expect(g.Grade == r.Grade, "MixedTypes.Grade", g.Grade, r.Grade);
            });
    }

    // ---- Empty / fully-omitted whitelist: unchanged entity, no error (R3.6, R5.5) -------------------

    [Test]
    public void Empty_Whitelist_Leaves_Entity_Unchanged_And_Raises_No_Error()
    {
        // Feature: source-generator-write-mapper, Property 1 (empty/no-op whitelist half): a facet with no
        // MapWritable mappings yields a conforming no-op — the reflection oracle and an empty-body generated
        // mapper both leave the entity byte-identical to its pre-write state and raise no error. Under the
        // active write-DSL diagnostics an empty whitelist is a VISTA0030 build error, so it is exercised
        // DIRECTLY against the oracle here (design "Reconciling Requirement 5 with Requirement 9").
        const string viewName = "wm-empty-whitelist-oracle";

        var view = new ViewMetadata(
            Name: viewName,
            Route: $"/test/{viewName}",
            QueryType: typeof(OneMappingEntity),
            CrudType: typeof(OneMappingCrud),
            CrudEntityType: typeof(OneMappingEntity),
            Fields: Array.Empty<FieldMetadata>(),
            Authorization: null,
            Limits: new HardLimits(HardLimits.DefaultMaxPageSize, HardLimits.DefaultMaxExportRows),
            IsReadOnly: false)
        {
            KeyFields = new[] { nameof(OneMappingEntity.Id) },
        };

        var facet = new CrudFacetDefinition(
            CrudType: typeof(OneMappingCrud),
            EntityType: typeof(OneMappingEntity),
            WritableFields: Array.Empty<WritableFieldMapping>(),
            ConcurrencyToken: null,
            AllowsBulk: false);

        var registry = new WriteFacetRegistry();
        registry.Register(viewName, facet);

        // The reflection oracle for an empty whitelist, and the shape the generator emits for a zero-safe
        // subset: an empty-body WriteMapper.
        var reflection = new ReflectionWriteMapper(registry).GetOrCreate(view);
        WriteMapper emptyGenerated = static (_, _) => { };

        var cases =
            from model in
                (from text in PickString select new OneMappingCrud { Text = text })
            from seed in
                (from id in GenInt from text in PickString select new OneMappingEntity { Id = id, Text = text })
            select (model, seed);

        cases.Sample(
            tuple =>
            {
                var (model, seed) = tuple;
                var pre = new OneMappingEntity { Id = seed.Id, Text = seed.Text };
                var reflectionEntity = new OneMappingEntity { Id = seed.Id, Text = seed.Text };
                var generatedEntity = new OneMappingEntity { Id = seed.Id, Text = seed.Text };

                // No error is raised for either mapper (R4.4, R5.5).
                reflection(model, reflectionEntity);
                emptyGenerated(model, generatedEntity);

                // Both leave the entity byte-identical to its pre-write state, and identical to each other.
                Expect(reflectionEntity.Id == pre.Id, "EmptyWhitelist(reflection).Id", reflectionEntity.Id, pre.Id);
                Expect(
                    string.Equals(reflectionEntity.Text, pre.Text, StringComparison.Ordinal),
                    "EmptyWhitelist(reflection).Text", reflectionEntity.Text, pre.Text);
                Expect(generatedEntity.Id == pre.Id, "EmptyWhitelist(generated).Id", generatedEntity.Id, pre.Id);
                Expect(
                    string.Equals(generatedEntity.Text, pre.Text, StringComparison.Ordinal),
                    "EmptyWhitelist(generated).Text", generatedEntity.Text, pre.Text);
            },
            iter: Iterations);
    }

    // ---- infrastructure -----------------------------------------------------------------------------

    /// <summary>
    /// Resolves the GENERATED mapper for <paramref name="viewName"/> from
    /// <see cref="GeneratedWriteMapperStore"/> (populated by the sample assembly's module initializers) and
    /// builds the reflection ORACLE from the same captured <see cref="CrudFacetDefinition"/>, then quantifies
    /// over random <c>(model, entity)</c> values, applying each mapper to an equal copy of the pair and
    /// asserting member-by-member equality of the mutated entities.
    /// </summary>
    private static void AssertParity<TView, TModel, TEntity>(
        string viewName,
        Gen<TModel> modelGen,
        Gen<TEntity> entityGen,
        Func<TEntity, TEntity> clone,
        Action<TEntity, TEntity> assertEqual)
        where TView : class, new()
    {
        var (generated, reflection) = BuildMappers<TView>(viewName);

        var cases =
            from model in modelGen
            from seed in entityGen
            select (model, seed);

        cases.Sample(
            tuple =>
            {
                var (model, seed) = tuple;

                // Apply each mapper to an equal copy of the pair. The model is only READ by both mappers, so
                // sharing it is equivalent to an equal copy; the entity is cloned per mapper so their
                // pre-write state is independent (byte[] arrays deep-copied).
                var generatedEntity = clone(seed);
                var reflectionEntity = clone(seed);

                generated(model!, generatedEntity!);
                reflection(model!, reflectionEntity!);

                assertEqual(generatedEntity, reflectionEntity);
            },
            iter: Iterations);
    }

    /// <summary>
    /// Registers <typeparamref name="TView"/> through <c>AddVista</c> to capture its
    /// <see cref="CrudFacetDefinition"/> and <see cref="ViewMetadata"/>, builds the reflection oracle from
    /// them, and resolves the generated mapper from the process-wide store.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification = "AddVista/Register<TView> and the reflection oracle are exercised on purpose; tests are not trimmed.")]
    private static (WriteMapper Generated, WriteMapper Reflection) BuildMappers<TView>(string viewName)
        where TView : class, new()
    {
        // Force the sample assembly's module to load so its generated [ModuleInitializer]s register the
        // write mappers into GeneratedWriteMapperStore before we resolve them.
        _ = new TView();

        var services = new ServiceCollection();
        services.AddVista(v => v.Register<TView>());
        using var provider = services.BuildServiceProvider();

        var metadata = provider.GetRequiredService<IViewRegistry>().Get(viewName)
            ?? throw new InvalidOperationException(
                $"View '{viewName}' was not registered; cannot run the write-parity property.");

        var facetRegistry = provider.GetRequiredService<IWriteFacetRegistry>();
        var reflection = new ReflectionWriteMapper(facetRegistry).GetOrCreate(metadata);

        if (!GeneratedWriteMapperStore.TryGet(viewName, out var generated))
        {
            throw new InvalidOperationException(
                $"No generated write mapper is registered for '{viewName}'. The WriteMapperGenerator must " +
                "emit a mapper + [ModuleInitializer] for this representative view.");
        }

        return (generated, reflection);
    }

    private static byte[]? CloneBytes(byte[]? source) => source is null ? null : (byte[])source.Clone();

    private static bool BytesEqual(byte[]? a, byte[]? b)
    {
        if (a is null || b is null)
        {
            return ReferenceEquals(a, b);
        }

        return a.AsSpan().SequenceEqual(b);
    }

    private static string Describe(byte[]? bytes) =>
        bytes is null ? "null" : "[" + string.Join(",", bytes.Select(x => x.ToString())) + "]";

    /// <summary>Throws a descriptive parity failure when a mutated member differs between the two mappers.</summary>
    private static void Expect(bool condition, string member, object? generated, object? reflection)
    {
        if (!condition)
        {
            throw new Exception(
                $"Write-parity mismatch on member '{member}': generated mapper produced '{generated}', " +
                $"reflection oracle produced '{reflection}'.");
        }
    }
}
