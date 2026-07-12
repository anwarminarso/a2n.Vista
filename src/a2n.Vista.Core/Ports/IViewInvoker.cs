using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Ports;

/// <summary>
/// A type-erased, reflection-free dispatch port implemented by generated code, one instance per
/// covered typed Style B view (Decision Log D123). It closes the generic <see cref="IViewExecutor"/>
/// read/write facets over the view's row type (<c>TRow</c>) and, when writable, its write model
/// (<c>TCrud</c>) at compile time, awaits the returned task directly, and returns the exact
/// type-erased shapes the HTTP layer already consumes.
/// </summary>
/// <remarks>
/// <para>
/// This port replaces the <c>MakeGenericMethod</c> + <c>Task&lt;TResult&gt;.Result</c> +
/// <see cref="ViewListResult{TRow}"/> reflection in the ASP.NET Core <c>ViewRequestExecutor</c> for
/// covered views, so the read and write HTTP paths carry no <c>IL2026</c>/<c>IL3050</c> warnings
/// (Requirements R2, R3). Uncovered views (Style A, anonymous/<see cref="object"/> row types, or
/// views without generated artifacts) stay on the reflection fallback.
/// </para>
/// <para>
/// <b>Layering (D48).</b> The port and its generated implementations use only Core ports
/// (<see cref="IViewExecutor"/>, <see cref="ViewMetadata"/>, <see cref="ViewQueryRequest"/>,
/// <see cref="IViewScope"/>, <see cref="ViewListResult{TRow}"/>) and BCL types. It adds no
/// System.Text.Json, EF Core, or ASP.NET Core dependency to <c>a2n.Vista.Core</c>, and generated
/// invokers introduce no ASP.NET Core dependency into the view's own assembly.
/// </para>
/// <para>
/// <b>Delete is deliberately absent.</b> <see cref="IViewExecutor.DeleteAsync"/> is non-generic, so it
/// needs no compile-time type closing and stays a direct executor call from the HTTP layer
/// (Requirement R3).
/// </para>
/// </remarks>
public interface IViewInvoker
{
    /// <summary>
    /// <see langword="true"/> for a writable view invoker (derives <c>View&lt;TQuery, TCrud&gt;</c>);
    /// <see langword="false"/> for a read-only view invoker (derives <c>View&lt;TQuery&gt;</c>). A
    /// read-only invoker's <see cref="CreateAsync"/>/<see cref="UpdateAsync"/> throw
    /// <see cref="System.InvalidOperationException"/> as defense in depth (Requirement R3.3).
    /// </summary>
    bool IsWritable { get; }

    /// <summary>
    /// Executes the List facet by closing <see cref="IViewExecutor.ListAsync{TRow}"/> at compile time,
    /// awaiting it directly, and returning the boxed <see cref="ViewListResult{TRow}"/> together with the
    /// materialized rows and both totals extracted by direct member access (no reflection over
    /// <see cref="ViewListResult{TRow}"/>; Requirements R2.1, R2.2).
    /// </summary>
    /// <param name="executor">The Core execution port to dispatch to.</param>
    /// <param name="view">The metadata of the view to execute.</param>
    /// <param name="request">The neutral query request (paging/filter/search/sort).</param>
    /// <param name="scope">The server-trusted row-filter scope to AND into the query.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The type-erased List result for JSON serialization and the adapter/export paths.</returns>
    Task<ViewInvocationListResult> ListAsync(
        IViewExecutor executor,
        ViewMetadata view,
        ViewQueryRequest request,
        IViewScope scope,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes the Detail facet by closing <see cref="IViewExecutor.DetailAsync{TRow}"/> at compile
    /// time and returning the boxed projected row, or <see langword="null"/> when no row matches within
    /// the authorized scope (mapped to HTTP 404 by the HTTP layer; Requirement R2.1).
    /// </summary>
    /// <param name="executor">The Core execution port to dispatch to.</param>
    /// <param name="view">The metadata of the view to execute.</param>
    /// <param name="key">The primary-key value identifying the row.</param>
    /// <param name="scope">The server-trusted row-filter scope to AND into the query.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The boxed projected row, or <see langword="null"/> when no row matches.</returns>
    Task<object?> DetailAsync(
        IViewExecutor executor,
        ViewMetadata view,
        object key,
        IViewScope scope,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes the Create facet by closing <see cref="IViewExecutor.CreateAsync{TCrud}"/> at compile
    /// time and returning the boxed primary-key value of the newly created row (Requirements R3.1, R3.2).
    /// A read-only invoker throws <see cref="System.InvalidOperationException"/>.
    /// </summary>
    /// <param name="executor">The Core execution port to dispatch to.</param>
    /// <param name="view">The metadata of the view to execute.</param>
    /// <param name="model">The typed write model, boxed as <see cref="object"/>.</param>
    /// <param name="scope">The server-trusted row-filter scope to honor.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The boxed primary-key value of the created row.</returns>
    Task<object> CreateAsync(
        IViewExecutor executor,
        ViewMetadata view,
        object model,
        IViewScope scope,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes the Update facet by closing <see cref="IViewExecutor.UpdateAsync{TCrud}"/> at compile
    /// time and returning the boolean outcome. Row identity comes solely from <paramref name="key"/>
    /// (never the model body) and the optimistic-concurrency token is passed through unchanged
    /// (Requirements R3.1, R3.2). A read-only invoker throws
    /// <see cref="System.InvalidOperationException"/>.
    /// </summary>
    /// <param name="executor">The Core execution port to dispatch to.</param>
    /// <param name="view">The metadata of the view to execute.</param>
    /// <param name="key">The primary-key value identifying the row to update.</param>
    /// <param name="model">The typed write model, boxed as <see cref="object"/>.</param>
    /// <param name="scope">The server-trusted row-filter scope to honor.</param>
    /// <param name="concurrencyToken">
    /// Optional optimistic-concurrency token (HTTP <c>If-Match</c>); <see langword="null"/> when the
    /// view declares no concurrency token.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when a row was updated; <see langword="false"/> when no row matched the
    /// key within the authorized scope.
    /// </returns>
    Task<bool> UpdateAsync(
        IViewExecutor executor,
        ViewMetadata view,
        object key,
        object model,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken);
}
