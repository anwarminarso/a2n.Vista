// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Phase 6 AOT verification (spec openapi-emitter, Task 11.1, R13.2/R13.3/R13.4; Decision Log D127).
// Proves that the OpenAPI emitter's *structure + descriptor* surface is AOT-clean: an OpenAPI document
// built from nothing but
//
//   1) the metadata-driven operation STRUCTURE (paths, HTTP methods, operationIds, request/response
//      $refs, per-facet security, RFC 7807 error responses) derived purely from a hand-built
//      ViewMetadata plus the fixed FacetOperations table (R13.2), and
//   2) the hand-authored, reflection-free ENVELOPE + FilterNode descriptors (EnvelopeSchemas +
//      FilterNodeSchema, R13.4),
//
// serialized through the source-generated VistaOpenApiJson context (D127) — builds green under the
// project's IL2026/IL3050-as-error posture. Because the RUC DtoSchemaGenerator branch (R13.3) is the one
// path that reflects over CLR row/write types, and it is NEVER referenced here, a green build is itself
// the proof that the envelopes + FilterNode document is free of IL2026 (RequiresUnreferencedCode) and
// IL3050 (RequiresDynamicCode).
//
// Why this probe does NOT call VistaOpenApiDocumentBuilder.Build(). Build() always collects the per-view
// TRow/TCrud DTO schemas via the RUC DtoSchemaGenerator, so Build()/BuildJson() are themselves marked
// [RequiresUnreferencedCode]; calling either on this strict (warning-as-error) surface would raise IL2026
// and fail the build. The emitter's design (design.md "AOT posture") states exactly this asymmetry: the
// path/operation structure and the envelope/FilterNode descriptors are AOT-clean, while DTO schema
// generation is the permanent RUC branch (D96). This probe therefore assembles the envelopes + FilterNode
// document from the AOT-clean building blocks the builder itself uses for structure, demonstrating the
// AOT-clean subset the design promises (R13.4: "a document restricted to envelopes plus already-described
// DTOs is AOT-clean").
//
// Keeping the analyzed surface honest (mirrors the other probes):
//   * EnvelopeSchemas, FilterNodeSchema, FacetOperations, the OpenApi object model, and
//     VistaOpenApiJson.Serialize carry no [RequiresUnreferencedCode]/[RequiresDynamicCode]; exercising
//     them on this non-suppressed surface is the member-level RUC proof — a RUC member here would raise
//     IL2026 and fail the build.
//   * The row/list DTO schema used by the detail/list responses is HAND-AUTHORED here (an "already
//     described DTO", R13.4), never produced by reflecting a CLR type, so no DtoSchemaGenerator call site
//     appears on this surface.
//   * The runtime assertions below confirm the produced document contains ONLY descriptor-derived schemas
//     (envelopes, FilterNode variants, ProblemDetails, and the hand-authored row) — no reflection-derived
//     per-view DTO leaked in — which is the observable form of "the DtoSchemaGenerator is not reached"
//     (R13.3).

using System;
using System.Collections.Generic;
using a2n.Vista.OpenApi;
using a2n.Vista.OpenApi.Model;
using a2n.Vista.OpenApi.Schemas;
using a2n.Vista.OpenApi.Serialization;

namespace a2n.Vista.AotProbe;

/// <summary>
/// Exercises the OpenAPI emitter's AOT-clean structure + descriptor path (Task 11.1): assembles an
/// envelopes + <c>FilterNode</c>-only <see cref="OpenApiDocument"/> reflection-free and serializes it
/// through the source-gen context, proving the path is free of IL2026/IL3050 and that the RUC
/// <c>DtoSchemaGenerator</c> is never reached (R13.2/R13.3/R13.4).
/// </summary>
internal static class OpenApiDescriptorProbe
{
    /// <summary>The component names of the fixed Vista envelope schemas (mirrors the builder's private table).</summary>
    private static class EnvelopeNames
    {
        public const string ListRequest = "VistaListRequestBody";
        public const string SortBody = "VistaSortBody";
        public const string DetailRequest = "VistaDetailRequestBody";
        public const string WriteRequest = "VistaWriteRequestBody";
        public const string WriteResponse = "VistaWriteResponse";
        public const string MetadataResponse = "VistaMetadataResponse";
        public const string FieldMetadataResponse = "VistaFieldMetadataResponse";
        public const string ProblemDetails = "ProblemDetails";
    }

    /// <summary>The hand-authored ("already described") row component the list/detail responses reference.</summary>
    private const string RowName = "AotProbeRow";

    /// <summary>The per-view <c>ViewListResult&lt;AotProbeRow&gt;</c> wrapper component name.</summary>
    private const string ListResultName = "ViewListResult_" + RowName;

    /// <summary>The default HTTP bearer security scheme name (mirrors the builder's not-anonymous default).</summary>
    private const string SecuritySchemeName = "bearer";

    /// <summary>
    /// A minimal, reflection-free view descriptor. The structure path reads only these three metadata
    /// fields (name, route, writability) — exactly the inputs the metadata-driven builder consumes for the
    /// operation skeleton (R13.2). No CLR row/write type is ever handed to the RUC DTO generator.
    /// </summary>
    private readonly record struct ProbeView(string Name, string Route, bool IsReadOnly);

    /// <summary>
    /// Builds the envelopes + <c>FilterNode</c>-only document, serializes it AOT-clean, and asserts the
    /// output carries only descriptor-derived schemas (so the RUC <c>DtoSchemaGenerator</c> was not
    /// reached).
    /// </summary>
    public static void Run()
    {
        Console.WriteLine();

        var document = BuildEnvelopesAndFilterNodeDocument();

        // Serialize through the source-generated VistaOpenApiJsonContext (D127). This overload uses the
        // compile-time JsonTypeInfo<OpenApiDocument>, so it is AOT-clean and byte-stable — no reflection
        // serializer, no IL2026/IL3050.
        var json = VistaOpenApiJson.Serialize(document);

        // Observable form of "DtoSchemaGenerator is not reached" (R13.3): every emitted component is a
        // descriptor-derived schema (an envelope, a FilterNode variant, ProblemDetails, or the
        // hand-authored row) — nothing was produced by reflecting a CLR DTO type.
        AssertOnlyDescriptorSchemas(document);

        // Endpoint-parity sanity on the AOT-clean structure (R13.2): the read-only view contributes the
        // four read facets; the writable view contributes all seven.
        AssertOperationStructure(document);

        Console.WriteLine(
            "AOT probe: OpenAPI envelopes + FilterNode document built reflection-free and serialized " +
            "AOT-clean (no DtoSchemaGenerator).");
        Console.WriteLine(
            $"Emitted {document.Paths!.Count} path(s), {document.Components!.Schemas!.Count} descriptor " +
            $"schema(s); document JSON {json.Length} chars (R13.2/R13.3/R13.4).");
    }

    /// <summary>
    /// Assembles the document from the reflection-free descriptors and the fixed facet table only. Mirrors
    /// the non-RUC structural half of <c>VistaOpenApiDocumentBuilder</c> without ever touching the RUC
    /// <c>DtoSchemaGenerator</c>.
    /// </summary>
    private static OpenApiDocument BuildEnvelopesAndFilterNodeDocument()
    {
        var views = new[]
        {
            new ProbeView("aotprobe-widgets", "/api/views/aotprobe-widgets", IsReadOnly: true),
            new ProbeView("aotprobe-memos", "/api/views/aotprobe-memos", IsReadOnly: false),
        };

        var schemas = OpenApiCollections.CreateMap<OpenApiSchema>();

        // --- Reflection-free component schemas (R13.4) ---------------------------------------------
        // Envelopes (hand-authored from the real wire types).
        schemas[EnvelopeNames.ListRequest] = EnvelopeSchemas.VistaListRequestBody();
        schemas[EnvelopeNames.SortBody] = EnvelopeSchemas.VistaSortBody();
        schemas[EnvelopeNames.DetailRequest] = EnvelopeSchemas.VistaDetailRequestBody();
        schemas[EnvelopeNames.WriteRequest] = EnvelopeSchemas.VistaWriteRequestBody();
        schemas[EnvelopeNames.WriteResponse] = EnvelopeSchemas.VistaWriteResponse();
        schemas[EnvelopeNames.MetadataResponse] = EnvelopeSchemas.VistaMetadataResponse();
        schemas[EnvelopeNames.FieldMetadataResponse] = EnvelopeSchemas.VistaFieldMetadataResponse();
        schemas[EnvelopeNames.ProblemDetails] = EnvelopeSchemas.ProblemDetails();

        // The polymorphic FilterNode + its four variants (referenced by VistaListRequestBody's
        // filter/scope slots).
        foreach (var node in FilterNodeSchema.All())
        {
            schemas[node.Key] = node.Value;
        }

        // A hand-authored ("already described") row DTO plus its ViewListResult wrapper. Authoring the row
        // by hand — rather than reflecting a CLR type through DtoSchemaGenerator — keeps this path RUC-free
        // while still exercising the list/detail response wiring (R13.4).
        var rowRef = "#/components/schemas/" + RowName;
        schemas[RowName] = HandAuthoredRow();
        schemas[ListResultName] = EnvelopeSchemas.ViewListResult(rowRef);

        // --- Metadata-driven operation structure (R13.2) ------------------------------------------
        // Not anonymous: emit the default HTTP bearer scheme once and attach the same one-door requirement
        // to every operation (mirrors the builder's Requirement 7 posture).
        var securitySchemes = OpenApiCollections.CreateMap<OpenApiSecurityScheme>();
        securitySchemes[SecuritySchemeName] = new OpenApiSecurityScheme { Type = "http", Scheme = "bearer" };
        var securityRequirement = BuildSecurityRequirement(SecuritySchemeName);

        var paths = OpenApiCollections.CreateMap<OpenApiPathItem>();
        foreach (var view in views)
        {
            EmitView(view, paths, rowRef, securityRequirement);
        }

        return new OpenApiDocument
        {
            Openapi = "3.0.4",
            Info = new OpenApiInfo { Title = "a2n.Vista AOT probe (envelopes + FilterNode)", Version = "1.0.0" },
            Paths = paths,
            Components = new OpenApiComponents { Schemas = schemas, SecuritySchemes = securitySchemes },
            Security = securityRequirement,
        };
    }

    /// <summary>
    /// Emits the <c>View_Operation_Set</c> for one view from the fixed <see cref="FacetOperations"/> table:
    /// path = <c>{Route}/{facet}</c>, method per the table, request/response referencing descriptor schemas
    /// by <c>$ref</c>, plus the per-facet <c>400</c>/<c>403</c>/<c>404</c> error responses. Purely data over
    /// the view's route + writability — no reflection (R13.2).
    /// </summary>
    private static void EmitView(
        ProbeView view,
        SortedDictionary<string, OpenApiPathItem> paths,
        string rowRef,
        IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>> securityRequirement)
    {
        var route = view.Route.TrimEnd('/');

        foreach (var facet in FacetOperations.ForView(view.IsReadOnly))
        {
            var path = route + "/" + facet.PathSuffix;

            var responses = OpenApiCollections.CreateMap<OpenApiResponse>();
            responses["200"] = BuildSuccessResponse(facet, rowRef);
            foreach (var code in facet.AlwaysErrorCodes)
            {
                responses[code.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                    ProblemResponse();
            }

            // 403 on every facet when not anonymous (mirrors the builder).
            responses["403"] = ProblemResponse();

            var operation = new OpenApiOperation
            {
                OperationId = view.Name + "_" + facet.PathSuffix,
                Summary = facet.Facet + " " + view.Name,
                Tags = new[] { view.Name },
                RequestBody = BuildRequestBody(facet),
                Responses = responses,
                Security = securityRequirement,
            };

            var pathItem = paths.TryGetValue(path, out var existing) ? existing : new OpenApiPathItem();
            paths[path] = facet.HttpMethod == "GET"
                ? pathItem with { Get = operation }
                : pathItem with { Post = operation };
        }
    }

    private static OpenApiRequestBody? BuildRequestBody(FacetOperation facet) => facet.RequestBody switch
    {
        FacetRequestBody.None => null,
        FacetRequestBody.List => JsonRequestBody(Ref(EnvelopeNames.ListRequest)),
        FacetRequestBody.Detail => JsonRequestBody(Ref(EnvelopeNames.DetailRequest)),
        FacetRequestBody.Write => JsonRequestBody(Ref(EnvelopeNames.WriteRequest)),
        _ => null,
    };

    private static OpenApiResponse BuildSuccessResponse(FacetOperation facet, string rowRef) => facet.SuccessBody switch
    {
        FacetSuccessBody.ViewListResult => JsonResponse("The filtered, paged list result.", Ref(ListResultName)),
        FacetSuccessBody.Row => JsonResponse("The matching row.", new OpenApiSchema { Ref = rowRef }),
        FacetSuccessBody.Metadata => JsonResponse("The view's metadata descriptor.", Ref(EnvelopeNames.MetadataResponse)),
        FacetSuccessBody.WriteResponse => JsonResponse("The created row's primary key.", Ref(EnvelopeNames.WriteResponse)),
        _ => new OpenApiResponse { Description = "The operation succeeded." },
    };

    /// <summary>A hand-authored row schema standing in for an already-described per-view <c>TRow</c> (no reflection).</summary>
    private static OpenApiSchema HandAuthoredRow()
    {
        var properties = OpenApiCollections.CreateMap<OpenApiSchema>();
        properties["id"] = new OpenApiSchema { Type = "integer", Format = "int32" };
        properties["name"] = new OpenApiSchema { Type = "string" };
        properties["payload"] = new OpenApiSchema { Type = "string", Format = "byte", Nullable = true };
        return new OpenApiSchema { Type = "object", Properties = properties, Required = new[] { "id", "name" } };
    }

    private static OpenApiResponse ProblemResponse()
    {
        var content = OpenApiCollections.CreateMap<OpenApiMediaType>();
        content["application/problem+json"] = new OpenApiMediaType { Schema = Ref(EnvelopeNames.ProblemDetails) };
        return new OpenApiResponse { Description = "An RFC 7807 problem-details error.", Content = content };
    }

    private static OpenApiRequestBody JsonRequestBody(OpenApiSchema schema)
    {
        var content = OpenApiCollections.CreateMap<OpenApiMediaType>();
        content["application/json"] = new OpenApiMediaType { Schema = schema };
        return new OpenApiRequestBody { Required = true, Content = content };
    }

    private static OpenApiResponse JsonResponse(string description, OpenApiSchema schema)
    {
        var content = OpenApiCollections.CreateMap<OpenApiMediaType>();
        content["application/json"] = new OpenApiMediaType { Schema = schema };
        return new OpenApiResponse { Description = description, Content = content };
    }

    private static OpenApiSchema Ref(string componentName) =>
        new() { Ref = "#/components/schemas/" + componentName };

    private static IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>> BuildSecurityRequirement(
        string schemeName)
    {
        var requirement = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [schemeName] = Array.Empty<string>(),
        };
        return new[] { (IReadOnlyDictionary<string, IReadOnlyList<string>>)requirement };
    }

    /// <summary>
    /// Asserts the produced document contains ONLY descriptor-derived component schemas — the fixed
    /// envelopes, the <c>FilterNode</c> variants, <c>ProblemDetails</c>, the hand-authored row, and its
    /// list wrapper. The presence of any other schema would mean a reflected per-view DTO leaked in, i.e.
    /// the RUC <c>DtoSchemaGenerator</c> was reached — which this AOT-clean path must never do (R13.3).
    /// </summary>
    private static void AssertOnlyDescriptorSchemas(OpenApiDocument document)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            EnvelopeNames.ListRequest,
            EnvelopeNames.SortBody,
            EnvelopeNames.DetailRequest,
            EnvelopeNames.WriteRequest,
            EnvelopeNames.WriteResponse,
            EnvelopeNames.MetadataResponse,
            EnvelopeNames.FieldMetadataResponse,
            EnvelopeNames.ProblemDetails,
            FilterNodeSchema.FilterNodeName,
            FilterNodeSchema.FilterLeafName,
            FilterNodeSchema.FilterAndName,
            FilterNodeSchema.FilterOrName,
            FilterNodeSchema.FilterNotName,
            RowName,
            ListResultName,
        };

        var schemas = document.Components?.Schemas
            ?? throw new InvalidOperationException("The envelopes + FilterNode document emitted no component schemas.");

        foreach (var name in schemas.Keys)
        {
            if (!expected.Contains(name))
            {
                throw new InvalidOperationException(
                    $"The AOT-clean OpenAPI document emitted an unexpected component schema '{name}'. Only " +
                    "descriptor-derived schemas (envelopes, FilterNode variants, ProblemDetails, the " +
                    "hand-authored row) are permitted on this path; any other schema means the RUC " +
                    "DtoSchemaGenerator was reached, violating the AOT-clean guarantee (R13.3).");
            }
        }

        // The FilterNode schema must be present and referenced — the document is a genuine
        // "envelopes + FilterNode" document, not merely envelopes.
        if (!schemas.ContainsKey(FilterNodeSchema.FilterNodeName))
        {
            throw new InvalidOperationException("The document is missing the FilterNode schema (R13.4).");
        }
    }

    /// <summary>
    /// Asserts the AOT-clean structure path produced the exact core operation set: four read facets for the
    /// read-only view and all seven for the writable view, each on its <c>{Route}/{facet}</c> path (R13.2).
    /// </summary>
    private static void AssertOperationStructure(OpenApiDocument document)
    {
        var paths = document.Paths
            ?? throw new InvalidOperationException("The envelopes + FilterNode document emitted no paths.");

        string[] readOnlyFacets = { "list", "detail", "metadata", "export" };
        string[] writableFacets = { "list", "detail", "metadata", "export", "create", "update", "delete" };

        AssertViewFacets(paths, "/api/views/aotprobe-widgets", readOnlyFacets);
        AssertViewFacets(paths, "/api/views/aotprobe-memos", writableFacets);
    }

    private static void AssertViewFacets(
        IReadOnlyDictionary<string, OpenApiPathItem> paths,
        string route,
        IReadOnlyList<string> facets)
    {
        foreach (var facet in facets)
        {
            var path = route + "/" + facet;
            if (!paths.TryGetValue(path, out var pathItem))
            {
                throw new InvalidOperationException($"Expected operation path '{path}' was not emitted (R13.2).");
            }

            var hasOperation = facet == "metadata" ? pathItem.Get is not null : pathItem.Post is not null;
            if (!hasOperation)
            {
                throw new InvalidOperationException(
                    $"Operation path '{path}' is missing its {(facet == "metadata" ? "GET" : "POST")} operation (R13.2).");
            }
        }
    }
}
