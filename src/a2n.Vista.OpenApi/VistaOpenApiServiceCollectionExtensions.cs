using System.Diagnostics.CodeAnalysis;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.OpenApi;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition-root wiring for the opt-in Vista OpenAPI emitter (Decision Log D128; spec openapi-emitter,
/// task 7.1). Lives in the <c>Microsoft.Extensions.DependencyInjection</c> namespace by .NET convention so
/// <c>AddVistaOpenApi(...)</c> surfaces on <see cref="IServiceCollection"/> without an extra <c>using</c>,
/// mirroring <c>AddVistaEndpoints(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The emitter is <b>off by default</b> (Requirement 10.3): a host that never calls
/// <c>AddVistaOpenApi()</c> / <c>MapVistaOpenApi()</c> registers nothing and behaves exactly as before.
/// This method registers the validated options, the metadata-driven
/// <see cref="a2n.Vista.OpenApi.VistaOpenApiDocumentBuilder"/>, and the build-once
/// <see cref="a2n.Vista.OpenApi.VistaOpenApiDocumentCache"/> — all singletons — and adds nothing to any
/// existing view endpoint (Requirement 10.2).
/// </para>
/// <para>
/// It reuses the <em>real</em> Vista wiring so the emitted document matches the live wire: the registered
/// <see cref="IViewRegistry"/> (the endpoint-parity oracle), the serialization seam's
/// <see cref="VistaJson.Options"/> (the schema/wire-parity oracle — the very options the mapped view
/// endpoints (de)serialize with), the singleton <see cref="VistaEndpointOptions"/> registered by
/// <c>AddVistaEndpoints(...)</c> (the anonymity + metadata-caching posture), and the optional
/// <see cref="IWriteFacetRegistry"/> (resolved with <see cref="System.IServiceProviderServiceExtensions.GetService{T}(System.IServiceProvider)"/>
/// so token-gated <c>428</c>/<c>409</c> responses are documented when it is available, omitted when not).
/// </para>
/// <para>
/// <b>AOT posture (Requirement 13.3).</b> Per-view DTO schema generation reflects over CLR row/write
/// types, so this method carries <see cref="RequiresUnreferencedCodeAttribute"/> to propagate the RUC
/// honestly up the call chain (consistent with <c>MapVistaViews()</c> and the D96 asymmetry). The actual
/// reflection is deferred to the first request via <see cref="a2n.Vista.OpenApi.VistaOpenApiDocumentCache"/>,
/// not run during registration.
/// </para>
/// </remarks>
public static class VistaOpenApiServiceCollectionExtensions
{
    private const string AotMessage =
        "The Vista OpenAPI emitter generates per-view DTO component schemas by reflecting over the view "
        + "row/write CLR types under the serialization seam options; use the envelopes-only document for AOT.";

    /// <summary>
    /// Registers the Vista OpenAPI emitter: validated options, the document builder, and the build-once
    /// document cache (all singletons). Call <c>MapVistaOpenApi()</c> afterwards to expose the served
    /// document endpoint.
    /// </summary>
    /// <param name="services">The service collection to add the emitter to.</param>
    /// <param name="configure">
    /// An optional callback to override the document title, version, OpenAPI version, security scheme, or
    /// endpoint path on the <see cref="a2n.Vista.OpenApi.VistaOpenApiOptions"/>. Runs synchronously during
    /// this call; the resulting options are validated immediately (fail-fast, Requirement 11.2).
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The configured options are invalid (for example an empty title or a relative endpoint path); thrown
    /// here, at registration time, rather than later at request time (design.md "Error Handling").
    /// </exception>
    [RequiresUnreferencedCode(AotMessage)]
    public static IServiceCollection AddVistaOpenApi(
        this IServiceCollection services,
        Action<VistaOpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Build, configure, and validate the options up front so misconfiguration fails fast at the
        // composition root with a clear ArgumentException (Requirement 11.2; design "Error Handling").
        var options = new VistaOpenApiOptions();
        configure?.Invoke(options);
        options.Validate();

        // The options singleton is the instance the builder and the serve-endpoint mapper both read.
        services.TryAddSingleton(options);

        // The metadata-driven builder, resolving its inputs from DI: the registry (endpoint-parity oracle),
        // the serialization seam's options (schema/wire-parity oracle — the live VistaJson.Options the view
        // endpoints (de)serialize with), the AspNetCore endpoint options (anonymity + metadata caching),
        // the emitter options, and the OPTIONAL write-facet registry (GetService — may be null, in which
        // case no view is treated as token-bearing and the 428/409 responses are omitted).
        services.TryAddSingleton(sp => new VistaOpenApiDocumentBuilder(
            sp.GetRequiredService<IViewRegistry>(),
            VistaJson.Options,
            sp.GetRequiredService<VistaEndpointOptions>(),
            sp.GetRequiredService<VistaOpenApiOptions>(),
            sp.GetService<IWriteFacetRegistry>()));

        // The build-once cache the serve endpoint returns (design "Runtime path": built once, cached).
        services.TryAddSingleton<VistaOpenApiDocumentCache>();

        return services;
    }
}
