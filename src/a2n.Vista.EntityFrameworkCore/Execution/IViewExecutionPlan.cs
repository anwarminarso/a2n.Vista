using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// The EF-layer <b>execution plan</b> for a single view: everything the executor needs, beyond the
/// transport-facing <see cref="ViewMetadata"/>, to turn a view into a scoped, projected
/// <see cref="IQueryable"/>. It is the EF-side answer to Decision Log <b>D11</b> (how a view obtains its
/// base <c>IQueryable&lt;TSource&gt;</c> and produces the <c>IQueryable&lt;TRow&gt;</c> the executor
/// consumes).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate abstraction.</b> <see cref="IViewRegistry"/> stores only <see cref="ViewMetadata"/>
/// — the shape clients and adapters see. It deliberately holds no source type, no projection
/// expression, and no row-filter factories, because those are execution concerns that must not leak
/// into the EF-free Core transport surface (Requirement R11.1/R11.2, Decision Log D48). The execution
/// plan carries exactly those concerns and lives in the EF layer, keyed by the same
/// <see cref="ViewMetadata.Name"/> the registry uses, so the executor can look one up per request.
/// </para>
/// <para>
/// <b>Alignment with §4.1.</b> The authoritative model separates a view into a <em>source query</em>
/// (<c>Func&lt;TServices, IQueryable&lt;TSource&gt;&gt;</c>) and a <em>projection</em>
/// (<c>Expression&lt;Func&lt;TSource, TRow&gt;&gt;</c>). The fully-correct plan implementation
/// (<see cref="SplitViewExecutionPlan{TSource, TRow}"/>) keeps them separate so server-trusted scope
/// and authored row filters are AND-ed <em>pre-projection</em> over <c>TSource</c> and pushed down to
/// SQL. See <see cref="ViewExecutionPlan"/> for the combined-delegate case (Gaya A) and the documented
/// limitation it carries.
/// </para>
/// <para>
/// <b>Type erasure.</b> The projected row type is often an anonymous type the EF layer cannot name, so
/// <see cref="CreateScopedQueryable"/> returns a non-generic <see cref="IQueryable"/> whose
/// <see cref="IQueryable.ElementType"/> equals <see cref="RowType"/>. <see cref="EfViewExecutor"/> casts
/// it back to <c>IQueryable&lt;TRow&gt;</c> at the call site (where <c>TRow</c> is supplied by the
/// caller, e.g. the endpoint).
/// </para>
/// </remarks>
public interface IViewExecutionPlan
{
    /// <summary>The unique view name this plan executes; matches <see cref="ViewMetadata.Name"/>.</summary>
    string ViewName { get; }

    /// <summary>
    /// The projected (read) row type produced by this plan; equals <see cref="ViewMetadata.QueryType"/>.
    /// The non-generic <see cref="IQueryable"/> returned by <see cref="CreateScopedQueryable"/> has this
    /// as its <see cref="IQueryable.ElementType"/>.
    /// </summary>
    Type RowType { get; }

    /// <summary>
    /// The name of the primary-key field on the projected row, when authoring captured it; otherwise
    /// <see langword="null"/>. When present, <see cref="EfViewExecutor"/> uses it for Detail-by-key
    /// resolution in preference to its name convention (closing the PK metadata gap flagged by Task 9.1).
    /// </summary>
    /// <remarks>
    /// This is <see langword="null"/> for views whose authoring style does not yet surface the primary
    /// key to the EF layer (notably Gaya A central-template views — see <see cref="ViewExecutionPlan"/>),
    /// in which case the executor falls back to its documented name convention.
    /// </remarks>
    string? KeyFieldName { get; }

    /// <summary>
    /// Builds the scoped, projected queryable for one request: obtains the base queryable (the
    /// <c>DbContext.Set&lt;TSource&gt;()</c> convention, D11, unless an explicit source factory was
    /// supplied), AND-s in the authored row filters and the per-request server-trusted predicates from
    /// <paramref name="scope"/> — neither subject to client whitelist validation (Requirement R6.3) —
    /// then applies the projection.
    /// </summary>
    /// <param name="dbContext">The active EF context used to resolve the source set (D11).</param>
    /// <param name="services">The request <see cref="IServiceProvider"/> used to build deferred row filters.</param>
    /// <param name="scope">The server-trusted scope whose predicates are AND-ed pre-projection.</param>
    /// <returns>
    /// A not-yet-enumerated queryable whose <see cref="IQueryable.ElementType"/> equals
    /// <see cref="RowType"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode("View execution composes source/scope/projection from captured expressions at runtime; use the source generator path for AOT.")]
    IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope);
}
