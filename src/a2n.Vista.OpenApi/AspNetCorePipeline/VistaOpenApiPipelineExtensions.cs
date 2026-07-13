// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Registration helpers for the optional built-in ASP.NET Core OpenAPI pipeline integration (spec
// openapi-emitter, task 7.2; Requirement 11.4; Decision Log D128). net9.0+ only — see the file header of
// VistaOpenApiDocumentTransformer.cs for the TFM rationale.

#if NET9_0_OR_GREATER

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.AspNetCore.OpenApi;

/// <summary>
/// Extends <see cref="OpenApiOptions"/> so a host that manages its own <c>AddOpenApi(...)</c> call can pull
/// the Vista views into that document with a single line.
/// </summary>
public static class VistaOpenApiOptionsExtensions
{
    private const string AotMessage =
        "The Vista OpenAPI pipeline transformer builds the Vista document by reflecting over per-view DTO "
        + "row/write types under the serialization seam options; the built document is not reflection-free.";

    /// <summary>
    /// Adds the <see cref="a2n.Vista.OpenApi.AspNetCorePipeline.VistaOpenApiDocumentTransformer"/> to this
    /// <paramref name="options"/> so the built-in pipeline document is augmented with the Vista
    /// paths/components (Requirement 11.4). Requires <c>AddVistaOpenApi(...)</c> to have registered the
    /// Vista document builder in DI (the transformer resolves it from the application services).
    /// </summary>
    /// <param name="options">The built-in pipeline options being configured inside <c>AddOpenApi(...)</c>.</param>
    /// <returns>The same <paramref name="options"/>, for chaining.</returns>
    [RequiresUnreferencedCode(AotMessage)]
    public static OpenApiOptions AddVistaOpenApiTransformer(this OpenApiOptions options)
    {
        System.ArgumentNullException.ThrowIfNull(options);
        return options.AddDocumentTransformer<a2n.Vista.OpenApi.AspNetCorePipeline.VistaOpenApiDocumentTransformer>();
    }
}

#endif
