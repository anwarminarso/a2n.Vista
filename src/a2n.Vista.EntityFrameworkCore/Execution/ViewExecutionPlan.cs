using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using a2n.Vista.Authoring;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// Factory helpers for constructing <see cref="IViewExecutionPlan"/>s from the two authoring styles (and
/// for manual/source-generated registration). Centralizing construction here keeps the resolution of
/// Decision Log <b>D11</b> (the <c>DbContext.Set&lt;TSource&gt;()</c> convention) and the §4.1 split
/// model in one place, so the DI wiring (Task 9.4) only has to hand the resulting plans to an
/// <see cref="IViewExecutionPlanRegistry"/>.
/// </summary>
public static class ViewExecutionPlan
{
    /// <summary>
    /// Builds the §4.1-aligned <see cref="SplitViewExecutionPlan{TSource, TRow}"/>: a separate source
    /// query and projection, with server-trusted scope and row filters applied pre-projection over
    /// <typeparamref name="TSource"/>. This is the correct, secure path and the target for Gaya B
    /// (class-per-view) and the source generator.
    /// </summary>
    /// <typeparam name="TSource">The EF source entity type.</typeparam>
    /// <typeparam name="TRow">The projected (read) row type.</typeparam>
    /// <param name="viewName">The unique view name.</param>
    /// <param name="projection">The projection <c>Expression&lt;Func&lt;TSource, TRow&gt;&gt;</c>.</param>
    /// <param name="sourceFactory">
    /// The explicit source factory (<c>FromQuery</c> escape hatch); <see langword="null"/> to use the
    /// <c>DbContext.Set&lt;TSource&gt;()</c> convention (D11).
    /// </param>
    /// <param name="authoredRowFilters">The authored pre-projection row filters; <see langword="null"/> for none.</param>
    /// <returns>A ready-to-register execution plan.</returns>
    public static IViewExecutionPlan Split<TSource, TRow>(
        string viewName,
        Expression<Func<TSource, TRow>> projection,
        Func<IServiceProvider, IQueryable<TSource>>? sourceFactory = null,
        IReadOnlyList<Func<IServiceProvider, Expression<Func<TSource, bool>>>>? authoredRowFilters = null)
        where TSource : class
        where TRow : class =>
        new SplitViewExecutionPlan<TSource, TRow>(viewName, projection, sourceFactory, authoredRowFilters);

    /// <summary>
    /// Builds a <see cref="ProjectedViewExecutionPlan"/> from a Gaya A (central-template) view
    /// definition. The definition combines source and projection in one delegate, so the resulting plan
    /// carries the §4.1 limitation documented on <see cref="ProjectedViewExecutionPlan"/> (it fails
    /// closed if the view declared any server-trusted row filter).
    /// </summary>
    /// <typeparam name="TDbContext">
    /// The template's data-source type. At execution time the <see cref="DbContext"/> handed to the plan
    /// must be assignable to this type (the DI composition root supplies the concrete context).
    /// </typeparam>
    /// <param name="definition">The authored central-template view definition.</param>
    /// <returns>A ready-to-register execution plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The view's key fields are surfaced through <see cref="ViewMetadata.KeyFields"/> (Decision Log
    /// D104); Gaya A views derive them from <c>.PrimaryKey()</c> marks at authoring time, or rely on
    /// single-source EF-model derivation at registration (Decision Log D105).
    /// </remarks>
    [RequiresUnreferencedCode("Gaya A execution erases the source type behind the projection and composes the query at runtime; use the source generator path for AOT.")]
    public static IViewExecutionPlan FromTemplateDefinition<TDbContext>(TemplateViewDefinition<TDbContext> definition)
        where TDbContext : class
    {
        ArgumentNullException.ThrowIfNull(definition);

        var metadata = definition.Metadata;

        IQueryable ProjectedFactory(DbContext dbContext, IServiceProvider services)
        {
            if (dbContext is not TDbContext typedContext)
            {
                throw new InvalidOperationException(
                    $"View '{metadata.Name}' was authored against data-source type '{typeof(TDbContext)}', " +
                    $"but the executor supplied a context of type '{dbContext.GetType()}'. Register the view " +
                    "against the same DbContext the executor resolves (composition root, Task 9.4).");
            }

            return definition.CreateQuery(typedContext, services);
        }

        return new ProjectedViewExecutionPlan(
            metadata.Name,
            metadata.QueryType,
            ProjectedFactory,
            authoredRowFilterCount: definition.RowFilters.Count);
    }
}
