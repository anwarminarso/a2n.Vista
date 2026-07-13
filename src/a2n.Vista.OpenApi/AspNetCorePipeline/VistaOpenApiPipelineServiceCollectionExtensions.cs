// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Turnkey service-collection helper for the optional built-in ASP.NET Core OpenAPI pipeline integration
// (spec openapi-emitter, task 7.2; Requirement 11.4; Decision Log D128). net9.0+ only.

#if NET9_0_OR_GREATER

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.OpenApi;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition-root helper that wires the Vista views into the built-in ASP.NET Core OpenAPI pipeline in a
/// single call (spec openapi-emitter, task 7.2; Requirement 11.4). Lives in the
/// <c>Microsoft.Extensions.DependencyInjection</c> namespace by convention so it surfaces on
/// <see cref="IServiceCollection"/> without an extra <c>using</c>, mirroring <c>AddVistaOpenApi(...)</c>.
/// </summary>
public static class VistaOpenApiPipelineServiceCollectionExtensions
{
    private const string AotMessage =
        "The Vista OpenAPI pipeline transformer builds the Vista document by reflecting over per-view DTO "
        + "row/write types under the serialization seam options; the built document is not reflection-free.";

    /// <summary>
    /// Registers the built-in OpenAPI pipeline (<c>AddOpenApi</c>) with the Vista document transformer
    /// attached, so the pipeline's <c>/openapi/{document}.json</c> output includes the Vista view
    /// operations and component schemas (Requirement 11.4). Call <c>AddVistaOpenApi(...)</c> as well so the
    /// transformer can resolve the Vista document builder from DI, and map the pipeline endpoint with
    /// <c>app.MapOpenApi()</c>.
    /// </summary>
    /// <param name="services">The service collection to add the pipeline integration to.</param>
    /// <param name="documentName">
    /// The built-in pipeline document name to attach the transformer to, or <see langword="null"/> to use
    /// the pipeline default (<c>v1</c>, served at <c>/openapi/v1.json</c>).
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(AotMessage)]
    public static IServiceCollection AddVistaOpenApiPipelineIntegration(
        this IServiceCollection services,
        string? documentName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (documentName is null)
        {
            services.AddOpenApi(options => options.AddVistaOpenApiTransformer());
        }
        else
        {
            services.AddOpenApi(documentName, options => options.AddVistaOpenApiTransformer());
        }

        return services;
    }
}

#endif
