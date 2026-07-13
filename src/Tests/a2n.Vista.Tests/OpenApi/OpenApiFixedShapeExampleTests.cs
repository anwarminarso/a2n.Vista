// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.Metadata;
using a2n.Vista.OpenApi;
using a2n.Vista.OpenApi.Model;
using a2n.Vista.Ports;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Consolidated fixed-shape and edge-case examples for the OpenAPI emitter (spec openapi-emitter, task 9.7;
/// Requirements 2.4, 3.1–3.5, 4.6, 5.1, 5.4, 6.1, 6.4, 7.2, 8.4). Rather than duplicate the isolated
/// descriptor/builder assertions already covered by
/// <see cref="OpenApiEnvelopeSchemasTests"/> / <see cref="OpenApiFilterNodeSchemaTests"/> (task 2.1/2.2),
/// <see cref="OpenApiDocumentBuilderTests"/> (5.2), <see cref="OpenApiSecurityAndErrorsTests"/> (5.3),
/// <see cref="OpenApiDocumentAssemblyTests"/> (5.4), and <see cref="DtoSchemaGeneratorTests"/> (3.1), these
/// tests assert the same requirement clauses <b>end-to-end over ONE representative document</b> built from
/// the shared <see cref="EmitterFixtures"/> registry (a read-only single-key view, a read-only composite-key
/// view, and a token-bearing writable view) with metadata caching enabled and a configured (non-default)
/// security scheme. That single build exercises the clauses as a cohesive whole against real representative
/// views/DTOs, catching wiring gaps the isolated suites cannot. A few genuinely-missing edge cases (the
/// FilterNode hierarchy as it appears <i>inside the assembled document</i>, a tokenless writable view via a
/// small local registry, and a bespoke/unsupported DTO member) are added where the fixture cannot express
/// them.
/// </summary>
public sealed class OpenApiFixedShapeExampleTests
{
    // The configured, non-default security scheme (Requirement 7.2): a JWT-flavored HTTP bearer named
    // "jwtBearer" — deliberately NOT the default "bearer" key, so the override is observable.
    private static readonly VistaSecurityScheme ConfiguredScheme = new("jwtBearer", "http", "bearer", "JWT");

    /// <summary>
    /// Builds the single representative document shared by these examples: the three <see cref="EmitterFixtures"/>
    /// views, the write-facet registry that gives the writable Subscription view a concurrency token, metadata
    /// caching enabled, and the configured (non-default) security scheme. Defaults are used for title/version
    /// so <c>info.version</c> derives from the emitting assembly (Requirement 8.4).
    /// </summary>
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    private static OpenApiDocument RepresentativeDocument()
    {
        var endpointOptions = new VistaEndpointOptions
        {
            AllowAnonymous = false,
            EnableMetadataCaching = true,
        };

        var builder = new VistaOpenApiDocumentBuilder(
            EmitterFixtures.Registry(),
            EmitterFixtures.Seam,
            endpointOptions,
            new VistaOpenApiOptions { Security = ConfiguredScheme },
            EmitterFixtures.WriteFacets());

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

    private static string? RequestSchemaRef(OpenApiOperation operation) =>
        operation.RequestBody?.Content?["application/json"].Schema?.Ref;

    private static OpenApiSchema? SuccessSchema(OpenApiOperation operation) =>
        operation.Responses?["200"].Content?["application/json"].Schema;

    // =====================================================================================================
    // R2.4 — cached metadata: If-None-Match request header + ETag response header + 304, on a REAL view.
    // (OpenApiSecurityAndErrorsTests covers this generically on a synthetic view; this confirms it fires
    // for the representative read-only single-key CatalogItem view within the fixture-built document.)
    // =====================================================================================================

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Cached_Metadata_On_Representative_View_Has_IfNoneMatch_ETag_And_304()
    {
        var document = RepresentativeDocument();
        var metadata = document.Paths![EmitterFixtures.CatalogItemRoute + "/metadata"].Get!;

        var ifNoneMatch = metadata.Parameters!.Single(p => p.Name == "If-None-Match");
        await Assert.That(ifNoneMatch.In).IsEqualTo("header");

        await Assert.That(metadata.Responses!["200"].Headers!.ContainsKey("ETag")).IsTrue();
        await Assert.That(metadata.Responses!.ContainsKey("304")).IsTrue();
    }

    // =====================================================================================================
    // R3.1–R3.5 — per-facet request/response body $ref wiring over the representative views.
    // =====================================================================================================

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task List_And_Export_Requests_Ref_The_List_Envelope() // R3.1
    {
        var document = RepresentativeDocument();

        foreach (var suffix in new[] { "list", "export" })
        {
            var operation = document.Paths![EmitterFixtures.CatalogItemRoute + "/" + suffix].Post!;
            await Assert.That(RequestSchemaRef(operation)).IsEqualTo("#/components/schemas/VistaListRequestBody");
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Detail_Request_Refs_The_Detail_Envelope() // R3.2
    {
        var document = RepresentativeDocument();
        var operation = document.Paths![EmitterFixtures.CatalogItemRoute + "/detail"].Post!;
        await Assert.That(RequestSchemaRef(operation)).IsEqualTo("#/components/schemas/VistaDetailRequestBody");
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Write_Requests_Ref_A_Write_Envelope_Whose_Model_Refs_SubscriptionCrud() // R3.2
    {
        var document = RepresentativeDocument();

        foreach (var suffix in new[] { "create", "update", "delete" })
        {
            var operation = document.Paths![EmitterFixtures.SubscriptionRoute + "/" + suffix].Post!;
            var writeRef = RequestSchemaRef(operation);
            await Assert.That(writeRef).IsNotNull();
            await Assert.That(writeRef!.StartsWith("#/components/schemas/VistaWriteRequestBody", StringComparison.Ordinal))
                .IsTrue();

            // The specialized write body's `model` slot references the view's TCrud (SubscriptionCrud).
            var bodyName = writeRef["#/components/schemas/".Length..];
            var body = document.Components!.Schemas![bodyName];
            await Assert.That(body.Properties!["model"].Ref).IsEqualTo("#/components/schemas/SubscriptionCrud");
        }

        await Assert.That(document.Components!.Schemas!.ContainsKey("SubscriptionCrud")).IsTrue();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task List_Success_Refs_A_ViewListResult_Wrapping_The_Row() // R3.3
    {
        var document = RepresentativeDocument();

        var listOp = document.Paths![EmitterFixtures.CatalogItemRoute + "/list"].Post!;
        await Assert.That(SuccessSchema(listOp)!.Ref).IsEqualTo("#/components/schemas/ViewListResult_CatalogItemRow");

        // The wrapper's page.items array references the TRow component.
        var wrapper = document.Components!.Schemas!["ViewListResult_CatalogItemRow"];
        var itemsRef = wrapper.Properties!["page"].Properties!["items"].Items!.Ref;
        await Assert.That(itemsRef).IsEqualTo("#/components/schemas/CatalogItemRow");
        await Assert.That(document.Components!.Schemas!.ContainsKey("CatalogItemRow")).IsTrue();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Detail_Success_Refs_The_Row_And_Documents_404_Miss() // R3.3
    {
        var document = RepresentativeDocument();
        var detailOp = document.Paths![EmitterFixtures.CatalogItemRoute + "/detail"].Post!;

        await Assert.That(SuccessSchema(detailOp)!.Ref).IsEqualTo("#/components/schemas/CatalogItemRow");
        await Assert.That(detailOp.Responses!.ContainsKey("404")).IsTrue();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Metadata_Success_Refs_The_Metadata_Envelope() // R3.4
    {
        var document = RepresentativeDocument();
        var metadataOp = document.Paths![EmitterFixtures.CatalogItemRoute + "/metadata"].Get!;
        await Assert.That(SuccessSchema(metadataOp)!.Ref).IsEqualTo("#/components/schemas/VistaMetadataResponse");
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Create_Success_Refs_The_Write_Response_Envelope() // R3.5
    {
        var document = RepresentativeDocument();
        var createOp = document.Paths![EmitterFixtures.SubscriptionRoute + "/create"].Post!;
        await Assert.That(SuccessSchema(createOp)!.Ref).IsEqualTo("#/components/schemas/VistaWriteResponse");
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Token_Bearing_Update_Delete_Carry_An_ETag_Success_Header() // R3.5
    {
        var document = RepresentativeDocument();

        foreach (var suffix in new[] { "update", "delete" })
        {
            var operation = document.Paths![EmitterFixtures.SubscriptionRoute + "/" + suffix].Post!;
            await Assert.That(operation.Responses!["200"].Headers!.ContainsKey("ETag")).IsTrue();
        }
    }

    // =====================================================================================================
    // R5.1 / R5.4 — the polymorphic FilterNode hierarchy is present *inside the assembled document*
    // (it is pulled in because the representative list body references FilterNode). The isolated descriptor
    // is covered by OpenApiFilterNodeSchemaTests; this asserts it survives into components.schemas end-to-end.
    // =====================================================================================================

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Assembled_Document_Contains_FilterNode_As_OneOf_Four_Variants() // R5.1
    {
        var document = RepresentativeDocument();
        var schemas = document.Components!.Schemas!;

        await Assert.That(schemas.ContainsKey("FilterNode")).IsTrue();
        var oneOf = schemas["FilterNode"].OneOf;
        await Assert.That(oneOf).IsNotNull();
        var refs = oneOf!.Select(s => s.Ref).ToArray();
        await Assert.That(refs).Contains("#/components/schemas/FilterLeaf");
        await Assert.That(refs).Contains("#/components/schemas/FilterAnd");
        await Assert.That(refs).Contains("#/components/schemas/FilterOr");
        await Assert.That(refs).Contains("#/components/schemas/FilterNot");
        await Assert.That(oneOf!.Count).IsEqualTo(4);
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Assembled_FilterNode_Variants_Recursively_Ref_FilterNode() // R5.4
    {
        var document = RepresentativeDocument();
        var schemas = document.Components!.Schemas!;
        const string filterNodeRef = "#/components/schemas/FilterNode";

        await Assert.That(schemas["FilterAnd"].Properties!["and"].Items!.Ref).IsEqualTo(filterNodeRef);
        await Assert.That(schemas["FilterOr"].Properties!["or"].Items!.Ref).IsEqualTo(filterNodeRef);
        await Assert.That(schemas["FilterNot"].Properties!["not"].Ref).IsEqualTo(filterNodeRef);

        // The list request body's filter/scope slots are the recursive entry points that pulled FilterNode in.
        var listBody = schemas["VistaListRequestBody"];
        await Assert.That(listBody.Properties!["filter"].Ref).IsEqualTo(filterNodeRef);
        await Assert.That(listBody.Properties!["scope"].Ref).IsEqualTo(filterNodeRef);
    }

    // =====================================================================================================
    // R6.1 / R6.4 — ProblemDetails wiring + token-gated 428/409.
    // =====================================================================================================

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task ProblemDetails_Is_Present_And_Every_Error_Response_References_It() // R6.1
    {
        var document = RepresentativeDocument();
        await Assert.That(document.Components!.Schemas!.ContainsKey("ProblemDetails")).IsTrue();

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
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Token_Bearing_Subscription_Update_Delete_Document_428_And_409() // R6.4
    {
        var document = RepresentativeDocument();

        foreach (var suffix in new[] { "update", "delete" })
        {
            var operation = document.Paths![EmitterFixtures.SubscriptionRoute + "/" + suffix].Post!;
            await Assert.That(operation.Responses!.ContainsKey("428")).IsTrue();
            await Assert.That(operation.Responses!.ContainsKey("409")).IsTrue();
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Tokenless_Writable_View_Documents_No_428_409_Or_Write_ETag() // R6.4 (absence)
    {
        // A small local registry with a writable view that has NO write-facet token: the 428/409 responses
        // and the write ETag success header must be absent. This complements the token-bearing fixture case
        // above (the fixture cannot express a tokenless writable view because WriteFacets() always tokenizes
        // the Subscription view).
        var registry = new ViewRegistry();
        registry.Add(new ViewMetadata(
            Name: "tokenlessWidgets",
            Route: "/api/views/tokenlessWidgets",
            QueryType: typeof(TokenlessRow),
            CrudType: typeof(TokenlessCrud),
            CrudEntityType: typeof(TokenlessCrud),
            Fields: Array.Empty<FieldMetadata>(),
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: false));

        var builder = new VistaOpenApiDocumentBuilder(
            registry,
            EmitterFixtures.Seam,
            new VistaEndpointOptions { AllowAnonymous = false },
            new VistaOpenApiOptions { Security = ConfiguredScheme },
            writeFacets: null); // no write-facet registry => no view is token-bearing

        var document = builder.Build();

        foreach (var suffix in new[] { "update", "delete" })
        {
            var operation = document.Paths!["/api/views/tokenlessWidgets/" + suffix].Post!;
            await Assert.That(operation.Responses!.ContainsKey("428")).IsFalse();
            await Assert.That(operation.Responses!.ContainsKey("409")).IsFalse();
            await Assert.That(operation.Responses!["200"].Headers).IsNull();
        }
    }

    // =====================================================================================================
    // R7.2 — a configured security scheme replaces the default bearer, end-to-end over the fixture document.
    // =====================================================================================================

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Configured_Security_Scheme_Replaces_The_Default_Bearer_Everywhere() // R7.2
    {
        var document = RepresentativeDocument();

        var schemes = document.Components!.SecuritySchemes!;
        await Assert.That(schemes.ContainsKey("jwtBearer")).IsTrue();
        await Assert.That(schemes.ContainsKey("bearer")).IsFalse();
        await Assert.That(schemes["jwtBearer"].BearerFormat).IsEqualTo("JWT");

        foreach (var (_, _, operation) in Operations(document))
        {
            var requirement = operation.Security;
            await Assert.That(requirement).IsNotNull();
            await Assert.That(requirement!.Count).IsEqualTo(1);
            await Assert.That(requirement[0].Keys.Single()).IsEqualTo("jwtBearer");
        }
    }

    // =====================================================================================================
    // R8.4 — info.version defaulting: null option -> emitting assembly informational version (non-empty,
    // no '+' build metadata); explicit option -> used verbatim.
    // =====================================================================================================

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Info_Version_Defaults_To_Emitting_Assembly_Version_And_Is_Overridable() // R8.4
    {
        // Default: the representative document (no DocumentVersion) derives a clean assembly version.
        var defaulted = RepresentativeDocument();
        await Assert.That(string.IsNullOrWhiteSpace(defaulted.Info.Version)).IsFalse();
        await Assert.That(defaulted.Info.Version.Contains('+', StringComparison.Ordinal)).IsFalse();

        // Explicit: the supplied version is used verbatim.
        var builder = new VistaOpenApiDocumentBuilder(
            EmitterFixtures.Registry(),
            EmitterFixtures.Seam,
            new VistaEndpointOptions { AllowAnonymous = false },
            new VistaOpenApiOptions { DocumentVersion = "7.3.1", Security = ConfiguredScheme },
            EmitterFixtures.WriteFacets());

        await Assert.That(builder.Build().Info.Version).IsEqualTo("7.3.1");
    }

    // =====================================================================================================
    // R4.6 — a bespoke/unsupported DTO member never fails the build; the property is emitted as a permissive
    // empty schema. DtoSchemaGeneratorTests asserts the generator's notice; the builder does not surface
    // DtoSchemaGenerator.Notices, so at the *document* level we assert the permissive `{}` and no throw.
    // =====================================================================================================

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over real DTO types.")]
    public async Task Bespoke_DTO_Member_Yields_A_Valid_Document_With_A_Permissive_Schema_Never_Throwing() // R4.6
    {
        var registry = new ViewRegistry();
        registry.Add(new ViewMetadata(
            Name: "bespokeWidgets",
            Route: "/api/views/bespokeWidgets",
            QueryType: typeof(BespokeRow),
            CrudType: null,
            CrudEntityType: null,
            Fields: Array.Empty<FieldMetadata>(),
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: true));

        var builder = new VistaOpenApiDocumentBuilder(
            registry,
            EmitterFixtures.Seam,
            new VistaEndpointOptions { AllowAnonymous = false },
            new VistaOpenApiOptions { Security = ConfiguredScheme });

        // Build() must not throw over the unsupported member (R4.6: never fail the build).
        var document = builder.Build();

        var component = document.Components!.Schemas!["BespokeRow"];

        // The ordinary member is still described precisely.
        await Assert.That(component.Properties!["ordinary"].Type).IsEqualTo("integer");

        // The bespoke IntPtr member is present (never omitted) and is the permissive empty schema:
        // no type, no $ref, no properties, no enum.
        var handle = component.Properties!["handle"];
        await Assert.That(component.Properties!.ContainsKey("handle")).IsTrue();
        await Assert.That(handle.Type).IsNull();
        await Assert.That(handle.Ref).IsNull();
        await Assert.That(handle.Properties).IsNull();
        await Assert.That(handle.Enum).IsNull();
    }

    // ---- Local edge-case DTO types ----------------------------------------------------------------

    // A writable view's DTOs whose write facet declares NO concurrency token (R6.4 absence case).
    private sealed class TokenlessRow
    {
        public int Id { get; init; }

        public string Title { get; init; } = string.Empty;
    }

    private sealed record TokenlessCrud(int Id, string Title);

    // A read row carrying a bespoke, unmappable member (IntPtr) alongside an ordinary scalar (R4.6).
    private sealed class BespokeRow
    {
        public int Ordinary { get; init; }

        // IntPtr has no conventional OpenAPI mapping -> permissive `{}` schema + a builder-internal notice.
        public IntPtr Handle { get; init; }
    }
}
