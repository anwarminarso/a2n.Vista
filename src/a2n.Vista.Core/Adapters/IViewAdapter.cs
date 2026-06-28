using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Adapters;

/// <summary>
/// The host-facing, type-erased adapter contract (Decision Log D66). It translates a grid-specific
/// request into the neutral <see cref="ViewQueryRequest"/> and the neutral <see cref="AdapterListResult"/>
/// back into a grid-specific response, in three pure steps. The AspNetCore host depends only on this
/// non-generic surface, so it can dispatch any adapter without referencing the grid package
/// (Decision Log D48). Adapter authors implement <see cref="ViewAdapter{TRequest, TResponse}"/> for
/// strongly-typed members.
/// </summary>
public interface IViewAdapter
{
    /// <summary>A unique adapter identity (for example <c>"datatables"</c>).</summary>
    string Id { get; }

    /// <summary>
    /// An optional route suffix the host mounts under each view's route (for example <c>"datatable"</c> →
    /// <c>POST {route}/datatable</c>); <see langword="null"/> means the adapter is not exposed on its own
    /// route.
    /// </summary>
    string? RouteSuffix { get; }

    /// <summary>Parses the neutral request bag into the adapter's request POCO.</summary>
    /// <param name="raw">The neutral request bag built by the host.</param>
    /// <returns>The boxed request POCO.</returns>
    /// <exception cref="AdapterBindException">The request cannot be parsed (syntactic failure).</exception>
    object BindRequest(AdapterRequest raw);

    /// <summary>Maps the adapter request POCO to the engine's neutral query request.</summary>
    /// <param name="request">The boxed request POCO from <see cref="BindRequest"/>.</param>
    /// <param name="view">The target view metadata (supplies searchable/field info).</param>
    /// <returns>The neutral query request, with per-channel sub-trees populated.</returns>
    ViewQueryRequest ToQuery(object request, ViewMetadata view);

    /// <summary>Maps the engine's neutral list result back to the grid-specific response.</summary>
    /// <param name="result">The neutral, type-erased list result.</param>
    /// <param name="request">The boxed request POCO (for echo fields such as DataTables <c>draw</c>).</param>
    /// <param name="view">The target view metadata.</param>
    /// <returns>The boxed grid-specific response.</returns>
    object ToResponse(AdapterListResult result, object request, ViewMetadata view);
}

/// <summary>
/// The strongly-typed authoring base for an <see cref="IViewAdapter"/> over one grid ecosystem. Authors
/// override the typed <see cref="BindRequest"/>/<see cref="ToQuery"/>/<see cref="ToResponse"/> members;
/// the explicit <see cref="IViewAdapter"/> implementation delegates to them, casting the boxed request.
/// Each step is a pure function so it can be unit-tested without HTTP or EF (Spec 04 §5.1).
/// </summary>
/// <typeparam name="TRequest">The grid-specific request POCO (for example <c>DataTablesQuery</c>).</typeparam>
/// <typeparam name="TResponse">The grid-specific response POCO (for example <c>DataTablesResponse&lt;object&gt;</c>).</typeparam>
public abstract class ViewAdapter<TRequest, TResponse> : IViewAdapter
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public virtual string? RouteSuffix => null;

    /// <summary>Parses the neutral request bag into <typeparamref name="TRequest"/>.</summary>
    /// <param name="raw">The neutral request bag built by the host.</param>
    /// <returns>The typed request POCO.</returns>
    /// <exception cref="AdapterBindException">The request cannot be parsed (syntactic failure).</exception>
    public abstract TRequest BindRequest(AdapterRequest raw);

    /// <summary>Maps <typeparamref name="TRequest"/> to the engine's neutral query request.</summary>
    /// <param name="request">The typed request POCO.</param>
    /// <param name="view">The target view metadata.</param>
    /// <returns>The neutral query request, with per-channel sub-trees populated.</returns>
    public abstract ViewQueryRequest ToQuery(TRequest request, ViewMetadata view);

    /// <summary>Maps the neutral list result back to <typeparamref name="TResponse"/>.</summary>
    /// <param name="result">The neutral, type-erased list result.</param>
    /// <param name="request">The typed request POCO (for echo fields).</param>
    /// <param name="view">The target view metadata.</param>
    /// <returns>The grid-specific response.</returns>
    public abstract TResponse ToResponse(AdapterListResult result, TRequest request, ViewMetadata view);

    /// <inheritdoc />
    object IViewAdapter.BindRequest(AdapterRequest raw) => BindRequest(raw)!;

    /// <inheritdoc />
    ViewQueryRequest IViewAdapter.ToQuery(object request, ViewMetadata view) => ToQuery((TRequest)request, view);

    /// <inheritdoc />
    object IViewAdapter.ToResponse(AdapterListResult result, object request, ViewMetadata view) =>
        ToResponse(result, (TRequest)request, view)!;
}
