// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Representative typed Style B WRITABLE view fixtures for the Phase 3 generated WRITE MAPPER
// (spec source-generator-write-mapper, task 7.1; Decision Log D121/D122).
//
// These are REAL, minimal, VALID, partial, single-source typed Style B WRITABLE views. Because this
// assembly references Core AND the EF layer AND the source generator (as an analyzer), the
// WriteMapperGenerator emits INTO this assembly, per view, a `file static`
// <View>_VistaWriteMapper.g.cs holding a reflection-free WriteMapper (cast + direct member assignments)
// plus a [ModuleInitializer] that registers it into
// a2n.Vista.EntityFrameworkCore.Execution.GeneratedWriteMapperStore keyed by the view's runtime Name.
//
// COMPILE-ONCE, QUANTIFY-OVER-VALUES (design "Cost control for the master parity property"). The master
// oracle-parity property test (task 7.2, Property 1) compiles this fixture set ONCE, resolves each
// view's GENERATED mapper from the store, and compares it — over random (model, entity) VALUES — against
// a ReflectionWriteMapper built from the same captured CrudFacetDefinition, asserting member-by-member
// equality of the mutated entities. It never re-compiles per iteration.
//
// The set is deliberately chosen to cover the generated-mapper surface Property 1 must exercise:
//   * OneMappingView            — exactly one scalar mapping (the minimal generated body).
//   * ManyMappingsView          — several ordered scalar mappings.
//   * AliasingView              — TWO source members mapped to ONE entity member, so the generated
//                                 mapper emits two ordered assignments to the same target and the
//                                 assignment ORDER is observable (last write wins) — the R4.6 vector.
//   * NullableAndBinaryView     — nullable value-type scalars (int?, DateTime?) and byte[] scalars,
//                                 including nulls (the AOT-critical Scalar_Member shapes).
//   * MixedTypesView            — a broad mix of scalar member types (string, int, long, double,
//                                 decimal, bool, DateTime, Guid, and an enum).
//
// EMPTY / NO-OP WHITELIST (R3.6 / R5.5) — INTENTIONALLY NOT A COMPILED FIXTURE HERE. A CRUD facet with
// zero declared MapWritable mappings is a VISTA0030 BUILD ERROR (R9.1), and a facet whose every mapping
// is unsafe is a VISTA0031/0032 build error, so under the active write-DSL diagnostics an empty safe
// subset is unreachable from author code — such a view would not compile and could carry no generated
// mapper to resolve. As the design ("Reconciling Requirement 5 with Requirement 9") states, the
// zero-assignment mapper shape is therefore exercised DIRECTLY AGAINST THE ORACLE by the property test:
// task 7.2 builds an empty CrudFacetDefinition by hand and asserts the reflection oracle (and an
// empty-body WriteMapper) leaves the entity byte-identical and raises no error. Keeping an empty-facet
// view out of this compiled assembly is what lets the assembly build green.
//
// Every view is single-source, partial, has an implicit public parameterless constructor, declares its
// primary key (Id) via a per-field PrimaryKey() mark, and maps ONLY safe scalar members (never the key)
// — the exact conditions the WriteMapperGenerator needs to emit a mapper for it (no VISTA0030/31/32).

using System;
using a2n.Vista.Authoring;

namespace a2n.Vista.GeneratorWriteMapperSample;

// =====================================================================================================
// Case 1 — ONE mapping. The minimal generated body: a single `e.Text = m.Text;` assignment.
// =====================================================================================================

/// <summary>Single EF source entity for <see cref="OneMappingView"/>; keyed by <see cref="Id"/>.</summary>
public sealed class OneMappingEntity
{
    /// <summary>Primary key — never assigned by the write mapper (defense in depth, R5.1).</summary>
    public int Id { get; set; }

    /// <summary>The only whitelisted scalar target.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>Projected (read) row for <see cref="OneMappingView"/>.</summary>
public sealed class OneMappingRow
{
    /// <summary>Primary key.</summary>
    public int Id { get; init; }

    /// <summary>Memo text.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>Typed write contract for <see cref="OneMappingView"/>: a single writable member.</summary>
public sealed class OneMappingCrud
{
    /// <summary>New memo text.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>Typed Style B writable view declaring exactly one scalar <c>MapWritable</c> mapping.</summary>
public partial class OneMappingView : View<OneMappingRow, OneMappingCrud>
{
    /// <summary>Globally-unique view name; the key the generated write mapper is stored under.</summary>
    public const string ViewName = "wm-one-mapping";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<OneMappingRow, OneMappingCrud> builder)
    {
        builder
            .Named(ViewName)
            .From<OneMappingEntity>(s => new OneMappingRow { Id = s.Id, Text = s.Text })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<OneMappingEntity>()
            .MapWritable(c => c.Text, e => e.Text);
    }
}

// =====================================================================================================
// Case 2 — MANY mappings. Several ordered scalar assignments emitted in declaration order.
// =====================================================================================================

/// <summary>Single EF source entity for <see cref="ManyMappingsView"/>; keyed by <see cref="Id"/>.</summary>
public sealed class ManyMappingsEntity
{
    /// <summary>Primary key — never assigned by the write mapper.</summary>
    public int Id { get; set; }

    /// <summary>Whitelisted string scalar.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Whitelisted string scalar.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Whitelisted value-type scalar.</summary>
    public int Priority { get; set; }

    /// <summary>Whitelisted value-type scalar.</summary>
    public int Weight { get; set; }

    /// <summary>Whitelisted value-type scalar.</summary>
    public bool Pinned { get; set; }
}

/// <summary>Projected (read) row for <see cref="ManyMappingsView"/>.</summary>
public sealed class ManyMappingsRow
{
    /// <summary>Primary key.</summary>
    public int Id { get; init; }

    /// <summary>Title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Body.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Priority.</summary>
    public int Priority { get; init; }
}

/// <summary>Typed write contract for <see cref="ManyMappingsView"/>: five writable members.</summary>
public sealed class ManyMappingsCrud
{
    /// <summary>New title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>New body.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>New priority.</summary>
    public int Priority { get; init; }

    /// <summary>New weight.</summary>
    public int Weight { get; init; }

    /// <summary>New pinned flag.</summary>
    public bool Pinned { get; init; }
}

/// <summary>Typed Style B writable view declaring several ordered scalar <c>MapWritable</c> mappings.</summary>
public partial class ManyMappingsView : View<ManyMappingsRow, ManyMappingsCrud>
{
    /// <summary>Globally-unique view name; the key the generated write mapper is stored under.</summary>
    public const string ViewName = "wm-many-mappings";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<ManyMappingsRow, ManyMappingsCrud> builder)
    {
        builder
            .Named(ViewName)
            .From<ManyMappingsEntity>(s => new ManyMappingsRow
            {
                Id = s.Id,
                Title = s.Title,
                Body = s.Body,
                Priority = s.Priority,
            })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<ManyMappingsEntity>()
            .MapWritable(c => c.Title, e => e.Title)
            .MapWritable(c => c.Body, e => e.Body)
            .MapWritable(c => c.Priority, e => e.Priority)
            .MapWritable(c => c.Weight, e => e.Weight)
            .MapWritable(c => c.Pinned, e => e.Pinned);
    }
}

// =====================================================================================================
// Case 3 — ALIASING (R4.6). Two source members are mapped to ONE entity member, so the generated mapper
// emits TWO ordered assignments to the same target and the assignment ORDER is observable: the reflection
// oracle applies them in the same relative order, so the last-declared source (Secondary) wins. Parity
// here proves the generator preserves declaration order and multiplicity rather than de-duplicating.
// =====================================================================================================

/// <summary>Single EF source entity for <see cref="AliasingView"/>; keyed by <see cref="Id"/>.</summary>
public sealed class AliasingEntity
{
    /// <summary>Primary key — never assigned by the write mapper.</summary>
    public int Id { get; set; }

    /// <summary>The single entity member two CRUD members alias onto (order decides the final value).</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>Projected (read) row for <see cref="AliasingView"/>.</summary>
public sealed class AliasingRow
{
    /// <summary>Primary key.</summary>
    public int Id { get; init; }

    /// <summary>Note.</summary>
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// Typed write contract for <see cref="AliasingView"/>: two members that both target the same entity
/// member. The generated mapper emits <c>e.Note = m.Primary;</c> then <c>e.Note = m.Secondary;</c>.
/// </summary>
public sealed class AliasingCrud
{
    /// <summary>First writer of <c>Note</c> (overwritten by <see cref="Secondary"/>).</summary>
    public string Primary { get; init; } = string.Empty;

    /// <summary>Last writer of <c>Note</c> (the value that survives).</summary>
    public string Secondary { get; init; } = string.Empty;
}

/// <summary>
/// Typed Style B writable view that maps two distinct source members to one entity member, exercising the
/// order-preservation guarantee (R4.6): both assignments are emitted, in declaration order.
/// </summary>
public partial class AliasingView : View<AliasingRow, AliasingCrud>
{
    /// <summary>Globally-unique view name; the key the generated write mapper is stored under.</summary>
    public const string ViewName = "wm-aliasing";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<AliasingRow, AliasingCrud> builder)
    {
        builder
            .Named(ViewName)
            .From<AliasingEntity>(s => new AliasingRow { Id = s.Id, Note = s.Note })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<AliasingEntity>()
            .MapWritable(c => c.Primary, e => e.Note)
            .MapWritable(c => c.Secondary, e => e.Note);
    }
}

// =====================================================================================================
// Case 4 — NULLABLE and byte[] scalars. Nullable value-type scalars (int?, DateTime?) and byte[] scalars
// (nullable and non-nullable) — the AOT-critical Scalar_Member shapes. The value generators in task 7.2
// include nulls and empty/populated byte[] so the generated assignment of null and of array references is
// proven byte-identical to the oracle.
// =====================================================================================================

/// <summary>Single EF source entity for <see cref="NullableAndBinaryView"/>; keyed by <see cref="Id"/>.</summary>
public sealed class NullableAndBinaryEntity
{
    /// <summary>Primary key — never assigned by the write mapper.</summary>
    public int Id { get; set; }

    /// <summary>Whitelisted nullable value-type scalar.</summary>
    public int? Count { get; set; }

    /// <summary>Whitelisted nullable value-type scalar.</summary>
    public DateTime? When { get; set; }

    /// <summary>Whitelisted nullable <c>byte[]</c> scalar.</summary>
    public byte[]? Blob { get; set; }

    /// <summary>Whitelisted non-nullable <c>byte[]</c> scalar.</summary>
    public byte[] Signature { get; set; } = Array.Empty<byte>();

    /// <summary>Whitelisted nullable reference (string) scalar.</summary>
    public string? Note { get; set; }
}

/// <summary>Projected (read) row for <see cref="NullableAndBinaryView"/>.</summary>
public sealed class NullableAndBinaryRow
{
    /// <summary>Primary key.</summary>
    public int Id { get; init; }

    /// <summary>Count.</summary>
    public int? Count { get; init; }

    /// <summary>Timestamp.</summary>
    public DateTime? When { get; init; }
}

/// <summary>Typed write contract for <see cref="NullableAndBinaryView"/>: nullable and binary scalars.</summary>
public sealed class NullableAndBinaryCrud
{
    /// <summary>New count (may be null).</summary>
    public int? Count { get; init; }

    /// <summary>New timestamp (may be null).</summary>
    public DateTime? When { get; init; }

    /// <summary>New optional binary payload (may be null).</summary>
    public byte[]? Blob { get; init; }

    /// <summary>New signature bytes.</summary>
    public byte[] Signature { get; init; } = Array.Empty<byte>();

    /// <summary>New note (may be null).</summary>
    public string? Note { get; init; }
}

/// <summary>
/// Typed Style B writable view whose whitelist is entirely nullable value-type and <c>byte[]</c> scalars,
/// exercising null assignment and array-reference assignment parity with the oracle.
/// </summary>
public partial class NullableAndBinaryView : View<NullableAndBinaryRow, NullableAndBinaryCrud>
{
    /// <summary>Globally-unique view name; the key the generated write mapper is stored under.</summary>
    public const string ViewName = "wm-nullable-and-binary";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<NullableAndBinaryRow, NullableAndBinaryCrud> builder)
    {
        builder
            .Named(ViewName)
            .From<NullableAndBinaryEntity>(s => new NullableAndBinaryRow
            {
                Id = s.Id,
                Count = s.Count,
                When = s.When,
            })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<NullableAndBinaryEntity>()
            .MapWritable(c => c.Count, e => e.Count)
            .MapWritable(c => c.When, e => e.When)
            .MapWritable(c => c.Blob, e => e.Blob)
            .MapWritable(c => c.Signature, e => e.Signature)
            .MapWritable(c => c.Note, e => e.Note);
    }
}

// =====================================================================================================
// Case 5 — MIXED member types. A broad mix of Scalar_Member types (string, int, long, double, decimal,
// bool, DateTime, Guid, and an enum) so parity is proven across the value-type/reference-scalar spectrum.
// =====================================================================================================

/// <summary>An enum used as one of the mixed scalar member types.</summary>
public enum WmGrade
{
    /// <summary>Low.</summary>
    Low = 0,

    /// <summary>Medium.</summary>
    Medium = 1,

    /// <summary>High.</summary>
    High = 2,
}

/// <summary>Single EF source entity for <see cref="MixedTypesView"/>; keyed by <see cref="Id"/>.</summary>
public sealed class MixedTypesEntity
{
    /// <summary>Primary key — never assigned by the write mapper.</summary>
    public int Id { get; set; }

    /// <summary>Whitelisted string scalar.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whitelisted 32-bit integer scalar.</summary>
    public int Quantity { get; set; }

    /// <summary>Whitelisted 64-bit integer scalar.</summary>
    public long Ticks { get; set; }

    /// <summary>Whitelisted double scalar.</summary>
    public double Ratio { get; set; }

    /// <summary>Whitelisted decimal scalar.</summary>
    public decimal Amount { get; set; }

    /// <summary>Whitelisted boolean scalar.</summary>
    public bool Active { get; set; }

    /// <summary>Whitelisted <see cref="DateTime"/> scalar.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Whitelisted <see cref="Guid"/> scalar.</summary>
    public Guid Reference { get; set; }

    /// <summary>Whitelisted enum scalar.</summary>
    public WmGrade Grade { get; set; }
}

/// <summary>Projected (read) row for <see cref="MixedTypesView"/>.</summary>
public sealed class MixedTypesRow
{
    /// <summary>Primary key.</summary>
    public int Id { get; init; }

    /// <summary>Name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>Grade.</summary>
    public WmGrade Grade { get; init; }
}

/// <summary>Typed write contract for <see cref="MixedTypesView"/>: a broad mix of scalar member types.</summary>
public sealed class MixedTypesCrud
{
    /// <summary>New name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>New quantity.</summary>
    public int Quantity { get; init; }

    /// <summary>New ticks.</summary>
    public long Ticks { get; init; }

    /// <summary>New ratio.</summary>
    public double Ratio { get; init; }

    /// <summary>New amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>New active flag.</summary>
    public bool Active { get; init; }

    /// <summary>New timestamp.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>New reference.</summary>
    public Guid Reference { get; init; }

    /// <summary>New grade.</summary>
    public WmGrade Grade { get; init; }
}

/// <summary>
/// Typed Style B writable view whose whitelist spans a broad mix of scalar member types, exercising
/// generated/oracle parity across the value-type and reference-scalar spectrum.
/// </summary>
public partial class MixedTypesView : View<MixedTypesRow, MixedTypesCrud>
{
    /// <summary>Globally-unique view name; the key the generated write mapper is stored under.</summary>
    public const string ViewName = "wm-mixed-types";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<MixedTypesRow, MixedTypesCrud> builder)
    {
        builder
            .Named(ViewName)
            .From<MixedTypesEntity>(s => new MixedTypesRow
            {
                Id = s.Id,
                Name = s.Name,
                Amount = s.Amount,
                Grade = s.Grade,
            })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<MixedTypesEntity>()
            .MapWritable(c => c.Name, e => e.Name)
            .MapWritable(c => c.Quantity, e => e.Quantity)
            .MapWritable(c => c.Ticks, e => e.Ticks)
            .MapWritable(c => c.Ratio, e => e.Ratio)
            .MapWritable(c => c.Amount, e => e.Amount)
            .MapWritable(c => c.Active, e => e.Active)
            .MapWritable(c => c.Timestamp, e => e.Timestamp)
            .MapWritable(c => c.Reference, e => e.Reference)
            .MapWritable(c => c.Grade, e => e.Grade);
    }
}
