using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// A deferred, server-trusted row-level predicate captured by
/// <see cref="IReadViewBuilder{TRow}.WithRowFilter{TSource}"/> on a Gaya A (central template) view.
/// It is applied pre-projection over the EF source entity (<c>TSource</c>) and AND-ed into the query
/// before paging. Authoritative shape: docs/spec/01-view.md §5.2 (Decision Log D28).
/// </summary>
/// <remarks>
/// <para>
/// The predicate is produced lazily from an <see cref="IServiceProvider"/> so it can depend on
/// request-scoped services (for example the current tenant or user). Core only captures the factory;
/// the EF execution layer (Task 9) calls <see cref="Create"/> at query time and translates the
/// resulting predicate to SQL.
/// </para>
/// <para>
/// These row filters are part of the view's authored shape and are distinct from the per-request
/// scope supplied through <c>IViewAuthorizer.ShapeQuery</c> (§5.6); both are server-trusted and are
/// not subject to client whitelist validation (Requirement R6.3).
/// </para>
/// </remarks>
public sealed class TemplateRowFilter
{
    private readonly Func<IServiceProvider, LambdaExpression> _factory;

    /// <summary>
    /// Initializes a new <see cref="TemplateRowFilter"/>.
    /// </summary>
    /// <param name="sourceType">The EF source entity type (<c>TSource</c>) the predicate applies to.</param>
    /// <param name="factory">
    /// A factory that, given the request <see cref="IServiceProvider"/>, returns the predicate as an
    /// <c>Expression&lt;Func&lt;TSource, bool&gt;&gt;</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sourceType"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    public TemplateRowFilter(Type sourceType, Func<IServiceProvider, LambdaExpression> factory)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(factory);

        SourceType = sourceType;
        _factory = factory;
    }

    /// <summary>The EF source entity type (<c>TSource</c>) this predicate is expressed over.</summary>
    public Type SourceType { get; }

    /// <summary>
    /// Builds the predicate for the current request.
    /// </summary>
    /// <param name="services">The request <see cref="IServiceProvider"/>.</param>
    /// <returns>
    /// The predicate as a <see cref="LambdaExpression"/> of shape <c>Func&lt;TSource, bool&gt;</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public LambdaExpression Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return _factory(services);
    }
}
