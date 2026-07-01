// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Linq.Expressions;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// The <b>non-RUC compiled</b> execution capability a source-generated Style B plan implements
/// (source-generator Phase 2 / Decision Log D118). Unlike <see cref="IViewExecutionPlan"/>, none of its
/// members are <c>[RequiresUnreferencedCode]</c>: the generator builds the projection, per-field
/// member-access, and ordering as compile-time expression nodes (emitted as C# the consumer compiles),
/// so neither <c>Expression.Property(string)</c> nor <c>MethodInfo.MakeGenericMethod</c> is reached at
/// runtime. This is the AOT-clean seam (R5) that runs in parallel to the reflection
/// <see cref="IViewExecutionPlan"/> path.
/// </summary>
/// <remarks>
/// <para>
/// A view registered with a plan that is <em>not</em> an <see cref="ICompiledViewExecutionPlan"/> (for
/// example a hand-built <see cref="SplitViewExecutionPlan{TSource, TRow}"/>) keeps the existing RUC
/// path; the executor routes through the compiled helpers only when the resolved plan implements this
/// interface. Behavioral parity between the two paths is the central correctness guard (Property 1).
/// </para>
/// <para>
/// The interface deliberately does <b>not</b> inherit <see cref="IViewExecutionPlan"/>, so the
/// executor's call site against a compiled plan never touches the RUC <c>CreateScopedQueryable</c>
/// member and stays warning-free. <see cref="ViewName"/> and <see cref="RowType"/> still satisfy the
/// values the registry needs.
/// </para>
/// </remarks>
public interface ICompiledViewExecutionPlan
{
    /// <summary>The unique view name this plan executes; matches <see cref="ViewMetadata.Name"/>.</summary>
    string ViewName { get; }

    /// <summary>The projected (read) row type produced by this plan; equals <c>typeof(TQuery)</c>.</summary>
    Type RowType { get; }

    /// <summary>
    /// The single EF source entity the view projects from, or <c>typeof(void)</c> when the view is
    /// multi-source (a join). Used by the M11 startup hook to derive <see cref="ViewMetadata.KeyFields"/>
    /// from the model when no key is declared (Decision Log D105).
    /// </summary>
    Type SourceType { get; }

    /// <summary>
    /// Whether the view projects from exactly one source entity with no join. Single-source views are the
    /// only ones eligible for model-based primary-key derivation (D105).
    /// </summary>
    bool IsSingleSource { get; }

    /// <summary>
    /// Builds the scoped, projected queryable for one request — AOT-clean. Authored server-trusted row
    /// filters and the per-request scope predicates are AND-ed <em>pre-projection</em> over the source
    /// entity, then the generated projection produces the row type. The returned non-generic
    /// <see cref="IQueryable"/> has <see cref="RowType"/> as its element type.
    /// </summary>
    /// <param name="dbContext">The active EF context used to resolve the source set (D11).</param>
    /// <param name="services">The request <see cref="IServiceProvider"/> used to build deferred row filters.</param>
    /// <param name="scope">The server-trusted scope whose predicates are AND-ed pre-projection.</param>
    IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope);

    /// <summary>
    /// Resolves a generated member-access lambda (<c>Expression&lt;Func&lt;TRow, TField&gt;&gt;</c>,
    /// typed as a <see cref="LambdaExpression"/>) for a projected, filterable/sortable field. The
    /// executor feeds it to the filter compiler and the sort appliers so no runtime
    /// <c>Expression.Property(string)</c> is needed (R2).
    /// </summary>
    /// <param name="fieldName">The projected field name.</param>
    /// <param name="accessor">The generated member-access lambda when present.</param>
    /// <returns><see langword="true"/> when a member-access lambda exists for the field.</returns>
    bool TryGetMemberAccess(string fieldName, out LambdaExpression accessor);

    /// <summary>
    /// Applies the primary ordering using a strongly-typed, generated applier that calls the closed
    /// generic <c>Queryable.OrderBy</c>/<c>OrderByDescending</c> directly (no
    /// <c>MakeGenericMethod</c>) (R3.4).
    /// </summary>
    IOrderedQueryable ApplyPrimarySort(IQueryable source, string fieldName, bool descending);

    /// <summary>
    /// Applies a secondary ordering using a strongly-typed, generated applier that calls the closed
    /// generic <c>Queryable.ThenBy</c>/<c>ThenByDescending</c> directly (no <c>MakeGenericMethod</c>)
    /// (R3.5).
    /// </summary>
    IOrderedQueryable ApplyThenSort(IOrderedQueryable source, string fieldName, bool descending);

    /// <summary>
    /// The generated masked-field accessors (read original / write masked), in declaration order, for
    /// AOT-clean masking at materialization (R7). Empty when the view has no masked fields.
    /// </summary>
    IReadOnlyList<MaskAccessor> MaskAccessors { get; }
}
