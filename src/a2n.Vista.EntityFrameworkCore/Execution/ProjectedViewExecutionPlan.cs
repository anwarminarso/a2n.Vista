using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Authoring;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// Execution plan for a view whose source query and projection are <b>combined</b> into a single
/// captured delegate that already returns the projected <c>IQueryable&lt;TRow&gt;</c>. This is the shape
/// produced by the Gaya A (central-template) authoring path
/// (<see cref="TemplateViewDefinition{TDbContext}"/>), whose <c>CreateQuery</c> erases the source type
/// <c>TSource</c> behind the projection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Documented limitation (FLAGGED Core follow-up).</b> Because the captured delegate already
/// projected to the (often anonymous) row type, the source type <c>TSource</c> is no longer visible to
/// the EF layer, so server-trusted predicates expressed over <c>TSource</c> — both the authored
/// <see cref="TemplateRowFilter"/>s and the per-request scope from <c>IViewAuthorizer.ShapeQuery</c> —
/// <b>cannot be AND-ed pre-projection</b> through this plan. This contradicts §4.1, which separates the
/// source query (<c>Func&lt;TServices, IQueryable&lt;TSource&gt;&gt;</c>) from the projection
/// (<c>Expression&lt;Func&lt;TSource, TRow&gt;&gt;</c>).
/// </para>
/// <para>
/// <b>Fail-closed, not fail-open.</b> Silently dropping a row-level security predicate would be a data
/// leak (Requirement R6.3). Therefore, when the view declares any authored row filter, this plan
/// throws rather than returning unscoped rows. A Gaya A view with no <c>WithRowFilter&lt;TSource&gt;</c>
/// (for example the Northwind <c>vProductCategory</c> sample) executes normally.
/// </para>
/// <para>
/// <b>Recommended Core change (not made here).</b> Align Gaya A capture with §4.1 by having
/// <c>IViewTemplateBuilder.AddView</c>/<c>TemplateViewDefinition</c> retain the base
/// <c>Func&lt;TDbContext, IServiceProvider, IQueryable&lt;TSource&gt;&gt;</c> and the
/// <c>Expression&lt;Func&lt;TSource, TRow&gt;&gt;</c> projection separately (as Gaya B already does in
/// its builder state). The EF layer could then build a <see cref="SplitViewExecutionPlan{TSource, TRow}"/>
/// for Gaya A too, and this combined plan — together with its limitation — would be retired.
/// </para>
/// </remarks>
public sealed class ProjectedViewExecutionPlan : IViewExecutionPlan
{
    private readonly Func<DbContext, IServiceProvider, IQueryable> _projectedFactory;
    private readonly int _authoredRowFilterCount;

    /// <summary>
    /// Initializes a new <see cref="ProjectedViewExecutionPlan"/>.
    /// </summary>
    /// <param name="viewName">The unique view name (matches <c>ViewMetadata.Name</c>).</param>
    /// <param name="rowType">The projected row type produced by <paramref name="projectedFactory"/>.</param>
    /// <param name="projectedFactory">
    /// The combined source+projection factory; its result's <see cref="IQueryable.ElementType"/> must
    /// equal <paramref name="rowType"/>.
    /// </param>
    /// <param name="authoredRowFilterCount">
    /// The number of authored <see cref="TemplateRowFilter"/>s the view declared. When greater than
    /// zero, <see cref="CreateScopedQueryable"/> fails closed because those <c>TSource</c> predicates
    /// cannot be applied pre-projection through the combined delegate.
    /// </param>
    /// <param name="keyFieldName">
    /// The primary-key field name, when known; otherwise <see langword="null"/>. Gaya A does not
    /// currently surface the primary key, so this is typically <see langword="null"/> and the executor
    /// falls back to its name convention.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="viewName"/> is <see langword="null"/> or whitespace.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="rowType"/> or <paramref name="projectedFactory"/> is <see langword="null"/>.
    /// </exception>
    public ProjectedViewExecutionPlan(
        string viewName,
        Type rowType,
        Func<DbContext, IServiceProvider, IQueryable> projectedFactory,
        int authoredRowFilterCount = 0,
        string? keyFieldName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);
        ArgumentNullException.ThrowIfNull(rowType);
        ArgumentNullException.ThrowIfNull(projectedFactory);

        ViewName = viewName;
        RowType = rowType;
        _projectedFactory = projectedFactory;
        _authoredRowFilterCount = authoredRowFilterCount < 0 ? 0 : authoredRowFilterCount;
        KeyFieldName = keyFieldName;
    }

    /// <inheritdoc />
    public string ViewName { get; }

    /// <inheritdoc />
    public Type RowType { get; }

    /// <inheritdoc />
    public string? KeyFieldName { get; }

    /// <inheritdoc />
    [RequiresUnreferencedCode("View execution composes source/scope/projection from captured expressions at runtime; use the source generator path for AOT.")]
    public IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scope);

        if (_authoredRowFilterCount > 0)
        {
            // Fail closed: the view declared server-trusted TSource row filters that cannot be honored
            // through the combined Gaya A delegate. Returning rows here would bypass row-level security.
            throw new NotSupportedException(
                $"View '{ViewName}' declares {_authoredRowFilterCount} server-trusted row filter(s) over its " +
                "source entity, but its central-template (Gaya A) query captured the source and projection as a " +
                "single delegate, so those predicates cannot be applied pre-projection (§4.1). Executing it would " +
                "silently drop row-level security. Resolve by aligning the Gaya A capture with §4.1 (retain the " +
                "source query and projection separately so a SplitViewExecutionPlan can be built), or author the " +
                "view in the class-per-view (Gaya B) style.");
        }

        var queryable = _projectedFactory(dbContext, services)
            ?? throw new InvalidOperationException(
                $"The captured query for view '{ViewName}' produced a null queryable.");

        return queryable;
    }
}
