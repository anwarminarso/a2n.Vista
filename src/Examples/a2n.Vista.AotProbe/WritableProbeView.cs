// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Self-contained typed Style B WRITABLE fixture for the Phase 3 AOT verification probe
// (spec source-generator-write-mapper, Task 11.1, R10.1–R10.6; Decision Log D121/D122).
//
// This is a REAL, minimal, VALID, partial, single-source typed Style B WRITABLE view plus its EF source
// entity, projected read row, and typed write contract. Because the probe assembly references the EF
// layer AND the source generator (as an analyzer), the Phase 3 WriteMapperGenerator will — once its
// emitter (tasks 6.x) lands — produce INTO this assembly a `file static`
// <View>_VistaWriteMapper.g.cs holding a reflection-free WriteMapper (cast + direct member assignments)
// plus a [ModuleInitializer] that registers it into
// a2n.Vista.EntityFrameworkCore.Execution.GeneratedWriteMapperStore keyed by the view's runtime Name.
// GeneratedWriteMapperProbe.cs then resolves that generated mapper through WriteMapperResolver and
// applies it to an entity under the strict trim/AOT analyzer.
//
// The write facet deliberately covers the AOT-critical generated surface a write exercises:
//   * scalar member assignments of different shapes: a string (Text), a value type (Priority), and a
//     nullable reference-shaped scalar (Payload, byte[]) — the exact Scalar_Member set the oracle and
//     the generated mapper agree on;
//   * a declared primary key (Id) that the mapper must NEVER assign (defense in depth, R5.1);
//   * a member-initialization read projection over a single EF source entity so the Phase 2 emitter can
//     also produce a compiled read plan for the same view (live coexistence).

using a2n.Vista.Authoring;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.AotProbe;

/// <summary>
/// EF source entity the writable probe view projects from and writes to. Has a conventional single
/// primary key (<see cref="Id"/>) plus three whitelisted scalar members.
/// </summary>
public sealed class ProbeMemo
{
    /// <summary>Primary key (EF infers it by convention). Never assigned by the write mapper (R5.1).</summary>
    public int Id { get; set; }

    /// <summary>A memo text — a whitelisted <see cref="string"/> scalar target.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>A priority — a whitelisted value-type (<see cref="int"/>) scalar target.</summary>
    public int Priority { get; set; }

    /// <summary>An optional binary payload — a whitelisted nullable <c>byte[]</c> scalar target.</summary>
    public byte[]? Payload { get; set; }
}

/// <summary>
/// Projected (read) row sent to clients. Init-only auto-properties projected via member-initialization
/// so EF Core maps each assigned member back to its source column.
/// </summary>
public sealed class ProbeMemoRow
{
    /// <summary>Primary key.</summary>
    public int Id { get; init; }

    /// <summary>Memo text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Priority.</summary>
    public int Priority { get; init; }
}

/// <summary>
/// Typed write contract (<c>TCrud</c>) clients post against. Closes mass-assignment by design (D38): only
/// the members whitelisted via <c>MapWritable</c> ever reach the entity. Deliberately carries no key
/// member, so the request key — never the body — sets row identity.
/// </summary>
public sealed class ProbeMemoCrud
{
    /// <summary>New memo text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>New priority.</summary>
    public int Priority { get; init; }

    /// <summary>New optional binary payload.</summary>
    public byte[]? Payload { get; init; }
}

/// <summary>
/// Typed Style B WRITABLE view over <see cref="ProbeMemo"/>. It is <c>partial</c>, single-source, has a
/// public parameterless constructor, declares a primary key, uses a member-initialization read
/// projection, and declares a typed CRUD facet (<c>CrudOn</c> + three scalar <c>MapWritable</c>
/// mappings) — all the conditions the Phase 3 WriteMapperGenerator needs to generate a reflection-free
/// write mapper (<c>ProbeMemoView_VistaWriteMapper</c>) and register it at module load.
/// </summary>
public partial class ProbeMemoView : View<ProbeMemoRow, ProbeMemoCrud>
{
    /// <summary>The globally-unique view name; also the key the generated write mapper is stored under.</summary>
    public const string ViewName = "aotprobe-memos";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<ProbeMemoRow, ProbeMemoCrud> builder)
    {
        builder
            .Named(ViewName)
            .From<ProbeMemo>(s => new ProbeMemoRow
            {
                Id = s.Id,
                Text = s.Text,
                Priority = s.Priority,
            })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<ProbeMemo>()
            .MapWritable(c => c.Text, e => e.Text)
            .MapWritable(c => c.Priority, e => e.Priority)
            .MapWritable(c => c.Payload, e => e.Payload);
    }
}

/// <summary>
/// EF context exposing <see cref="ProbeMemo"/> over SQLite so the write path can load, mutate, and
/// persist a keyed target.
/// </summary>
public sealed class ProbeMemoDbContext : DbContext
{
    /// <summary>Initializes the context with the supplied options (SQLite in-memory in the probe).</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification = "The base DbContext constructor is EF Core framework infrastructure (not AOT-compatible), not the generated write-mapper path under verification (R10.1/R10.2).")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Members annotated with 'RequiresDynamicCodeAttribute' may break when AOT compiling",
        Justification = "The base DbContext constructor is EF Core framework infrastructure (not AOT-compatible), not the generated write-mapper path under verification (R10.1/R10.2).")]
    public ProbeMemoDbContext(DbContextOptions<ProbeMemoDbContext> options)
        : base(options)
    {
    }

    /// <summary>The source memos the probe view projects from and writes to.</summary>
    public DbSet<ProbeMemo> Memos => Set<ProbeMemo>();
}
