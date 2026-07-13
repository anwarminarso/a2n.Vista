using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.Authoring;
using a2n.Vista.Metadata;
using a2n.Vista.OpenApi;
using a2n.Vista.OpenApi.Model;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Example coverage for the security requirements and RFC 7807 error responses layered by
/// <see cref="VistaOpenApiDocumentBuilder"/> (spec openapi-emitter, task 5.3). Asserts the anonymity-driven
/// security posture (Requirement 7), the per-facet error matrix sourced from the facet table
/// (<c>400</c>/<c>403</c>/<c>404</c>/<c>428</c>/<c>409</c>, Requirement 6), that every error uses
/// <c>application/problem+json</c> referencing the single <c>ProblemDetails</c> schema, and the
/// cached-metadata <c>If-None-Match</c>/<c>ETag</c>/<c>304</c> contract (Requirement 2.4) plus the
/// token-bearing write <c>ETag</c> success header (Requirement 3.5).
/// </summary>
public sealed class OpenApiSecurityAndErrorsTests
{
    // ---- Representative DTOs / views --------------------------------------------------------------

    private enum Status
    {
        Active,
        Retired,
    }

    private sealed class ReadRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public Status Status { get; init; }
    }

    private sealed class WritableRow
    {
        public int Id { get; init; }

        public string Title { get; init; } = string.Empty;
    }

    private sealed record WriteModel(int Id, string Title);

    private sealed class TokenEntity
    {
        public int Id { get; init; }

        public string Title { get; init; } = string.Empty;

        public int Version { get; init; }
    }

    private const string ReadRoute = "/api/views/readWidgets";
    private const string WriteRoute = "/api/views/writeWidgets";
    private const string TokenRoute = "/api/views/tokenWidgets";
    private const string ReadName = "readWidgets";
    private const string WriteName = "writeWidgets";
    private const string TokenName = "tokenWidgets";

    // Mirrors the serialization seam: web defaults (camelCase) + JsonStringEnumConverter.
    private static JsonSerializerOptions SeamOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static ViewMetadata ReadView() => new(
        Name: ReadName,
        Route: ReadRoute,
        QueryType: typeof(ReadRow),
        CrudType: null,
        CrudEntityType: null,
        Fields: Array.Empty<FieldMetadata>(),
        Authorization: null,
        Limits: HardLimits.Default,
        IsReadOnly: true);

    private static ViewMetadata WriteView() => new(
        Name: WriteName,
        Route: WriteRoute,
        QueryType: typeof(WritableRow),
        CrudType: typeof(WriteModel),
        CrudEntityType: typeof(WriteModel),
        Fields: Array.Empty<FieldMetadata>(),
        Authorization: null,
        Limits: HardLimits.Default,
        IsReadOnly: false);

    private static ViewMetadata TokenView() => new(
        Name: TokenName,
        Route: TokenRoute,
        QueryType: typeof(WritableRow),
        CrudType: typeof(WriteModel),
        CrudEntityType: typeof(TokenEntity),
        Fields: Array.Empty<FieldMetadata>(),
        Authorization: null,
        Limits: HardLimits.Default,
        IsReadOnly: false);

    private static IViewRegistry BuildRegistry(bool includeToken = false)
    {
        var registry = new ViewRegistry();
        registry.Add(ReadView());
        registry.Add(WriteView());
        if (includeToken)
        {
            registry.Add(TokenView());
        }

        return registry;
    }

    /// <summary>A write-facet registry in which only <see cref="TokenName"/> declares a concurrency token.</summary>
    private static WriteFacetRegistry TokenFacets()
    {
        var registry = new WriteFacetRegistry();

        // The token view declares an optimistic-concurrency token; the plain writable view does not.
        Expression<Func<TokenEntity, int>> token = e => e.Version;
        registry.Register(TokenName, new CrudFacetDefinition(
            CrudType: typeof(WriteModel),
            EntityType: typeof(TokenEntity),
            WritableFields: Array.Empty<WritableFieldMapping>(),
            ConcurrencyToken: token,
            AllowsBulk: false));

        registry.Register(WriteName, new CrudFacetDefinition(
            CrudType: typeof(WriteModel),
            EntityType: typeof(WriteModel),
            WritableFields: Array.Empty<WritableFieldMapping>(),
            ConcurrencyToken: null,
            AllowsBulk: false));

        return registry;
    }

    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    private static OpenApiDocument Build(
        bool anonymous = false,
        VistaSecurityScheme? security = null,
        bool metadataCaching = false,
        bool includeToken = false,
        IWriteFacetRegistry? writeFacets = null)
    {
        var endpointOptions = new VistaEndpointOptions
        {
            AllowAnonymous = anonymous,
            EnableMetadataCaching = metadataCaching,
        };

        var builder = new VistaOpenApiDocumentBuilder(
            BuildRegistry(includeToken),
            SeamOptions(),
            endpointOptions,
            new VistaOpenApiOptions { Security = security },
            writeFacets);

        return builder.Build();
    }

    private static IEnumerable<(string Path, string Method, OpenApiOperation Operation)> Operations(
        OpenApiDocument document)
    {
        foreach (var (path, item) in document.Paths!)
        {
            if (item.Get is not null)
            {
                yield return (path, "GET", item.Get);
            }

            if (item.Post is not null)
            {
                yield return (path, "POST", item.Post);
            }
        }
    }

    private static string? OnlySchemeName(OpenApiOperation operation)
    {
        var requirement = operation.Security;
        if (requirement is null || requirement.Count != 1)
        {
            return null;
        }

        var alternative = requirement[0];
        return alternative.Count == 1 ? alternative.Keys.Single() : null;
    }

    // ---- Requirement 7: security posture ----------------------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task NotAnonymous_Emits_Bearer_Scheme_And_Every_Operation_References_It()
    {
        var document = Build(anonymous: false);

        // The default HTTP bearer scheme is emitted once under components.securitySchemes (R7.1).
        var schemes = document.Components!.SecuritySchemes!;
        await Assert.That(schemes.ContainsKey("bearer")).IsTrue();
        await Assert.That(schemes["bearer"].Type).IsEqualTo("http");
        await Assert.That(schemes["bearer"].Scheme).IsEqualTo("bearer");

        // Every operation carries the same one-door requirement naming that scheme (R7.4).
        foreach (var (_, _, operation) in Operations(document))
        {
            await Assert.That(OnlySchemeName(operation)).IsEqualTo("bearer");
        }

        // The document-level requirement mirrors the per-operation one.
        await Assert.That(document.Security).IsNotNull();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Anonymous_Emits_No_Scheme_And_No_Operation_Security()
    {
        var document = Build(anonymous: true);

        // No security scheme is emitted at all (R7.3).
        await Assert.That(document.Components!.SecuritySchemes).IsNull();
        await Assert.That(document.Security).IsNull();

        // No operation carries a security requirement (R7.3).
        foreach (var (_, _, operation) in Operations(document))
        {
            await Assert.That(operation.Security).IsNull();
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Configured_Security_Scheme_Overrides_The_Default_Bearer()
    {
        var custom = new VistaSecurityScheme("jwt", "http", "bearer", "JWT");
        var document = Build(anonymous: false, security: custom);

        var schemes = document.Components!.SecuritySchemes!;
        await Assert.That(schemes.ContainsKey("jwt")).IsTrue();
        await Assert.That(schemes.ContainsKey("bearer")).IsFalse();
        await Assert.That(schemes["jwt"].BearerFormat).IsEqualTo("JWT");

        foreach (var (_, _, operation) in Operations(document))
        {
            await Assert.That(OnlySchemeName(operation)).IsEqualTo("jwt");
        }
    }

    // ---- Requirement 6: error responses -----------------------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Body_Operations_Document_400_And_Metadata_Does_Not()
    {
        var document = Build(anonymous: false);

        // 400 on every body-bearing operation (list/detail/export/create/update/delete) (R6.2).
        foreach (var (_, _, operation) in Operations(document))
        {
            var hasBody = operation.RequestBody is not null;
            var has400 = operation.Responses!.ContainsKey("400");
            await Assert.That(has400).IsEqualTo(hasBody);
        }

        // The metadata GET has no body and thus no 400.
        var metadata = document.Paths![ReadRoute + "/metadata"].Get!;
        await Assert.That(metadata.Responses!.ContainsKey("400")).IsFalse();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Every_Operation_Documents_403_When_Not_Anonymous()
    {
        var document = Build(anonymous: false);

        foreach (var (_, _, operation) in Operations(document))
        {
            await Assert.That(operation.Responses!.ContainsKey("403")).IsTrue();
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task No_Operation_Documents_403_When_Anonymous()
    {
        var document = Build(anonymous: true);

        foreach (var (_, _, operation) in Operations(document))
        {
            await Assert.That(operation.Responses!.ContainsKey("403")).IsFalse();
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Detail_Create_Update_Delete_Document_404()
    {
        var document = Build(anonymous: false);

        await Assert.That(document.Paths![ReadRoute + "/detail"].Post!.Responses!.ContainsKey("404")).IsTrue();
        await Assert.That(document.Paths![WriteRoute + "/create"].Post!.Responses!.ContainsKey("404")).IsTrue();
        await Assert.That(document.Paths![WriteRoute + "/update"].Post!.Responses!.ContainsKey("404")).IsTrue();
        await Assert.That(document.Paths![WriteRoute + "/delete"].Post!.Responses!.ContainsKey("404")).IsTrue();

        // list/export/metadata carry no 404.
        await Assert.That(document.Paths![ReadRoute + "/list"].Post!.Responses!.ContainsKey("404")).IsFalse();
        await Assert.That(document.Paths![ReadRoute + "/export"].Post!.Responses!.ContainsKey("404")).IsFalse();
        await Assert.That(document.Paths![ReadRoute + "/metadata"].Get!.Responses!.ContainsKey("404")).IsFalse();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Every_Error_Response_Uses_ProblemJson_Referencing_The_Single_ProblemDetails()
    {
        var document = Build(anonymous: false);
        var errorCodes = new[] { "400", "403", "404", "409", "428" };

        foreach (var (_, _, operation) in Operations(document))
        {
            foreach (var code in errorCodes)
            {
                if (!operation.Responses!.TryGetValue(code, out var response))
                {
                    continue;
                }

                var content = response.Content!;
                await Assert.That(content.ContainsKey("application/problem+json")).IsTrue();
                await Assert.That(content["application/problem+json"].Schema!.Ref)
                    .IsEqualTo("#/components/schemas/ProblemDetails");
            }
        }

        // The ProblemDetails schema is registered exactly once as a shared component (R6.1).
        await Assert.That(document.Components!.Schemas!.ContainsKey("ProblemDetails")).IsTrue();
    }

    // ---- Requirement 6.4 / 3.5: token-gated 428/409 + write ETag ----------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task TokenBearing_Update_Delete_Document_428_And_409_With_ETag()
    {
        var document = Build(anonymous: false, includeToken: true, writeFacets: TokenFacets());

        foreach (var suffix in new[] { "update", "delete" })
        {
            var operation = document.Paths![TokenRoute + "/" + suffix].Post!;
            await Assert.That(operation.Responses!.ContainsKey("428")).IsTrue();
            await Assert.That(operation.Responses!.ContainsKey("409")).IsTrue();

            // The success (200) response carries an ETag header for the token-bearing view (R3.5).
            await Assert.That(operation.Responses!["200"].Headers!.ContainsKey("ETag")).IsTrue();
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Tokenless_Writable_View_Documents_No_428_409_Or_ETag()
    {
        // A writable view whose facet declares no token (and no registry at all) must not emit 428/409.
        var document = Build(anonymous: false, writeFacets: TokenFacets());

        foreach (var suffix in new[] { "update", "delete" })
        {
            var operation = document.Paths![WriteRoute + "/" + suffix].Post!;
            await Assert.That(operation.Responses!.ContainsKey("428")).IsFalse();
            await Assert.That(operation.Responses!.ContainsKey("409")).IsFalse();
            await Assert.That(operation.Responses!["200"].Headers).IsNull();
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Without_WriteFacetRegistry_No_View_Is_Token_Bearing()
    {
        // No write-facet registry supplied: 428/409 cannot be detected from ViewMetadata alone, so they
        // are omitted for every writable operation (documented limitation).
        var document = Build(anonymous: false, includeToken: true, writeFacets: null);

        foreach (var route in new[] { WriteRoute, TokenRoute })
        {
            foreach (var suffix in new[] { "update", "delete" })
            {
                var operation = document.Paths![route + "/" + suffix].Post!;
                await Assert.That(operation.Responses!.ContainsKey("428")).IsFalse();
                await Assert.That(operation.Responses!.ContainsKey("409")).IsFalse();
            }
        }
    }

    // ---- Requirement 2.4: cached metadata If-None-Match / ETag / 304 ------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Metadata_Caching_Enabled_Documents_IfNoneMatch_ETag_And_304()
    {
        var document = Build(anonymous: false, metadataCaching: true);
        var metadata = document.Paths![ReadRoute + "/metadata"].Get!;

        // If-None-Match request header parameter.
        var ifNoneMatch = metadata.Parameters!.Single(p => p.Name == "If-None-Match");
        await Assert.That(ifNoneMatch.In).IsEqualTo("header");

        // ETag response header on the 200 and a 304 Not Modified response.
        await Assert.That(metadata.Responses!["200"].Headers!.ContainsKey("ETag")).IsTrue();
        await Assert.That(metadata.Responses!.ContainsKey("304")).IsTrue();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Metadata_Caching_Disabled_Documents_No_Conditional_Artifacts()
    {
        var document = Build(anonymous: false, metadataCaching: false);
        var metadata = document.Paths![ReadRoute + "/metadata"].Get!;

        await Assert.That(metadata.Parameters).IsNull();
        await Assert.That(metadata.Responses!.ContainsKey("304")).IsFalse();
        await Assert.That(metadata.Responses!["200"].Headers).IsNull();
    }
}
