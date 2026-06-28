using a2n.Vista.Metadata;

namespace a2n.Vista.Adapters;

/// <summary>
/// Emits a <b>grid-specific metadata schema</b> from a view's neutral <see cref="ViewMetadata"/>
/// (Decision Log D116, Spec 04 §5.2/§8.2) — for example the jQuery-QueryBuilder <c>filters[]</c> schema.
/// This is distinct from the neutral <c>GET {route}/metadata</c> and is inherently per grid component
/// (D113). Host-facing and type-erased so the AspNetCore layer can dispatch a schema adapter without
/// referencing the grid package (Decision Log D48).
/// </summary>
public interface IViewMetadataAdapter
{
    /// <summary>A unique identity for the schema adapter (for example <c>"querybuilder"</c>).</summary>
    string Id { get; }

    /// <summary>
    /// The route suffix the host mounts under each view's route (for example <c>"querybuilder"</c> →
    /// <c>GET {route}/querybuilder</c>); <see langword="null"/> means not exposed on its own route.
    /// </summary>
    string? RouteSuffix { get; }

    /// <summary>
    /// Builds the grid-specific schema object for <paramref name="view"/>. Returning <see cref="object"/>
    /// (typically a dictionary) lets the adapter control the exact key casing the grid client expects; the
    /// host serializes it verbatim.
    /// </summary>
    /// <param name="view">The view metadata to project into a grid schema.</param>
    /// <returns>The grid-specific schema (serialized verbatim by the host).</returns>
    object BuildSchema(ViewMetadata view);
}
