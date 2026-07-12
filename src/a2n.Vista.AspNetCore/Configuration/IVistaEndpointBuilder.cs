using System.Text.Json.Serialization;
using a2n.Vista.AspNetCore.Authorization;

namespace a2n.Vista.AspNetCore.Configuration;

/// <summary>
/// The fluent configuration surface for Vista's AspNetCore HTTP layer, returned by
/// <c>AddVistaEndpoints</c>. It owns the HTTP-side cross-cutting settings: the single one-door
/// authorizer (Decision Log D43) and the explicit anonymous-access opt-in (Decision Log D94). The
/// route root is no longer configured here — a view's route is composed at registration (the EF
/// layer's <c>RouteGroup</c>/default root) and recorded in <c>ViewMetadata.Route</c> (D101/D103).
/// </summary>
/// <remarks>
/// <para>
/// This builder is intentionally separate from the Entity Framework layer's <c>IVistaBuilder</c>
/// (which registers views and execution plans). The AspNetCore package must not reference the EF
/// package (Requirement R11.3); the two layers meet only through <c>a2n.Vista.Core</c> ports
/// (<c>IViewRegistry</c>, <c>IViewExecutor</c>, <c>IViewScope</c>) resolved from DI at request time.
/// </para>
/// <para>
/// A typical composition root calls both: <c>services.AddVista(...)</c> (EF — registers views, the
/// executor, and the registry) and <c>services.AddVistaEndpoints(...)</c> (this builder — route root +
/// authorizer).
/// </para>
/// </remarks>
public interface IVistaEndpointBuilder
{
    /// <summary>
    /// Registers <typeparamref name="T"/> as the single one-door authorizer (§5.6/D43). When configured,
    /// <see cref="IViewAuthorizer.IsAllowedAsync"/> gates every request and a <see langword="false"/>
    /// result maps to HTTP 403 (R7.1). When this is never called, access defaults to allow (R7.2) and the
    /// startup fail-open warning is emitted (R7.3, Task 10.4).
    /// </summary>
    /// <typeparam name="T">The authorizer implementation type.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The authorizer is registered with a <b>scoped</b> lifetime so it can take request-scoped
    /// dependencies (current user/tenant accessors, a scoped <c>DbContext</c>) even though the
    /// authorizer itself is stateless across requests. It is resolved per request by the glue
    /// (<c>ViewRequestExecutor</c>).
    /// </remarks>
    IVistaEndpointBuilder UseAuthorizer<T>() where T : class, IViewAuthorizer;

    /// <summary>
    /// Explicitly opts into anonymous (no-authorizer) access — serving all views publicly. This is the
    /// deliberate, reviewed way to run open in any environment (D94). Without it, a missing authorizer
    /// is allowed in Development (with a warning) but **fails host startup** in non-Development
    /// environments, so a forgotten authorizer cannot silently expose views in production.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Use this only when public access is intended (e.g. an internal back-office or a public read-only
    /// catalog). It does not register an authorizer; it records that open access is a conscious choice.
    /// </remarks>
    IVistaEndpointBuilder AllowAnonymousAccess();

    /// <summary>
    /// Enables HTTP caching for the <c>GET {route}/metadata</c> facet: responses carry an <c>ETag</c>
    /// (a hash of the serialized metadata) and <c>Cache-Control: private, max-age=...</c>, and a matching
    /// <c>If-None-Match</c> returns <c>304 Not Modified</c>. Off by default so metadata edits are visible
    /// immediately during development; enable it in production to cut metadata round-trips.
    /// </summary>
    /// <param name="maxAgeSeconds">The <c>max-age</c> in seconds (default 60).</param>
    /// <returns>This builder, for chaining.</returns>
    IVistaEndpointBuilder EnableMetadataCaching(int maxAgeSeconds = 60);

    /// <summary>
    /// Chains a developer-authored <c>App_Json_Context</c> (a source-generated
    /// <see cref="JsonSerializerContext"/> listing a view's DTOs via <c>[JsonSerializable]</c>) into the
    /// Vista serialization seam (Decision Log D124). The context is inserted <b>ahead of</b> the
    /// reflection fallback, so once it is registered the runtime types it covers (a view's row type,
    /// <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>, and — for a writable view — its
    /// CRUD type) (de)serialize AOT-clean. The generator emits <c>VISTA0041</c> naming exactly which
    /// types to include.
    /// </summary>
    /// <param name="context">The developer-authored source-generated context to chain in.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Call this at the composition root, before the first request serializes a response — the seam's
    /// resolver chain freezes on first use. It is safe to register several contexts and to register the
    /// same context twice (the duplicate is ignored). Where the framework <c>Results</c> path is still
    /// used, the context is mirrored into the ASP.NET Core <c>JsonOptions</c> resolver chain so both
    /// paths resolve identically.
    /// </remarks>
    IVistaEndpointBuilder AddVistaJsonContext(JsonSerializerContext context);

    /// <summary>
    /// Removes the reflection fallback resolver from the Vista serialization seam (Decision Log D124,
    /// R5.5), leaving the source-generated contexts as the only resolvers. Use this in a fully
    /// AOT/trim-clean application whose views are all covered typed Style B with registered
    /// <c>App_Json_Context</c>s; after opting out, a runtime type that no chained context covers can no
    /// longer be (de)serialized.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The reflection fallback (<c>DefaultJsonTypeInfoResolver</c>) is the seam's only trim/AOT-unsafe
    /// (RUC) serialization branch; disabling it is the switch that makes the serialization path provably
    /// reflection-free. Call it at the composition root before the first serialization.
    /// </remarks>
    IVistaEndpointBuilder DisableVistaReflectionSerializationFallback();
}
