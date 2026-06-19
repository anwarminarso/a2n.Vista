using System.Linq.Expressions;

namespace a2n.Vista.Ports;

/// <summary>
/// Container for server-trusted row filters that are AND-ed into a view query before execution.
/// Authoritative shape: docs/spec/01-view.md §5.6 (Decision Log D46/D48).
/// </summary>
/// <remarks>
/// <para>
/// The scope is populated by the <c>a2n.Vista.AspNetCore</c> layer from
/// <c>IViewAuthorizer.ShapeQuery</c> and consumed by the executor in <c>a2n.Vista.EntityFrameworkCore</c>.
/// It lives in Core (HTTP/EF-neutral) so both layers can meet behind it without referencing each other.
/// </para>
/// <para>
/// Row filters added here are <b>server-trusted</b>: they are AND-ed into the query and are
/// <b>not</b> subject to the client whitelist validation that applies to filter/search/scope leaves
/// (Requirement R6.3). They model concerns such as tenant isolation and ownership and cannot be
/// bypassed by the client.
/// </para>
/// <para>
/// Predicates are expressed over the EF source entity type (<c>TSource</c>), which keeps them
/// push-down friendly (pre-projection, translated to SQL). Core only depends on
/// <see cref="System.Linq.Expressions"/>, never on EF Core itself.
/// </para>
/// <para>
/// Read side (<see cref="GetRowFilters{TSource}"/>): the authoritative spec defines only the write
/// side (<see cref="AddRowFilter{TSource}"/>). A typed, generic retrieval method is added here so the
/// executor can read back accumulated predicates without reflection over the stored values, keeping
/// the contract AOT-reasonable. Assumption documented for Task 4.1.
/// </para>
/// </remarks>
public interface IViewScope
{
    /// <summary>
    /// Adds a server-trusted row predicate over the source entity type <typeparamref name="TSource"/>.
    /// The predicate is AND-ed into the query and pushed down to SQL.
    /// </summary>
    /// <typeparam name="TSource">The EF source entity type the view projects from.</typeparam>
    /// <param name="filter">A strongly-typed predicate over <typeparamref name="TSource"/>.</param>
    void AddRowFilter<TSource>(Expression<Func<TSource, bool>> filter) where TSource : class;

    /// <summary>
    /// Returns the predicates accumulated for <typeparamref name="TSource"/>, in the order they were
    /// added. The executor combines them with logical AND. Returns an empty list when none were added.
    /// </summary>
    /// <typeparam name="TSource">The EF source entity type to retrieve predicates for.</typeparam>
    /// <returns>An ordered, read-only list of predicates for <typeparamref name="TSource"/>.</returns>
    IReadOnlyList<Expression<Func<TSource, bool>>> GetRowFilters<TSource>() where TSource : class;
}
