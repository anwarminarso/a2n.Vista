using System.Diagnostics.CodeAnalysis;

namespace a2n.Vista.OpenApi;

/// <summary>
/// A process-lifetime, build-once cache of the serialized OpenAPI document JSON (Decision Log D128; spec
/// openapi-emitter, task 7.1). The <c>Serve_Endpoint</c> mapped by <c>MapVistaOpenApi()</c> resolves this
/// singleton and returns <see cref="GetJson"/>, so the document is materialized at most once — on the
/// first request — and every subsequent request returns the same cached string (design.md "Runtime path":
/// the document is <em>built (once, cached)</em>).
/// </summary>
/// <remarks>
/// <para>
/// Building the document dips into the RUC per-view DTO schema generation
/// (<see cref="VistaOpenApiDocumentBuilder.BuildJson"/>), so <see cref="GetJson"/> carries
/// <see cref="RequiresUnreferencedCodeAttribute"/>. Deferring the build to first request (rather than at
/// registration) keeps the reflection off the startup path and lets a host that never hits the endpoint
/// pay nothing for it.
/// </para>
/// <para>
/// The build is guarded so concurrent first requests serialize the document exactly once;
/// <see cref="_json"/> is <see langword="volatile"/> so the fast, lock-free read after the first build
/// observes the published value.
/// </para>
/// </remarks>
internal sealed class VistaOpenApiDocumentCache
{
    private readonly VistaOpenApiDocumentBuilder _builder;
    private readonly object _gate = new();
    private volatile string? _json;

    /// <summary>Creates the cache over the document <paramref name="builder"/> (resolved from DI).</summary>
    /// <param name="builder">The metadata-driven document builder.</param>
    public VistaOpenApiDocumentCache(VistaOpenApiDocumentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <summary>
    /// Returns the serialized OpenAPI document JSON, building and caching it on the first call and
    /// returning the cached string thereafter (build-once, R11.1).
    /// </summary>
    /// <returns>The deterministic OpenAPI document as <c>application/json</c> text.</returns>
    [RequiresUnreferencedCode("Building the OpenAPI document reflects over per-view DTO row/write types.")]
    public string GetJson()
    {
        var cached = _json;
        if (cached is not null)
        {
            return cached;
        }

        lock (_gate)
        {
            return _json ??= _builder.BuildJson();
        }
    }
}
