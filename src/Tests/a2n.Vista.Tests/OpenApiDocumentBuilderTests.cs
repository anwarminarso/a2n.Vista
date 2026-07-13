using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Example coverage for <see cref="VistaOpenApiDocumentBuilder"/> (spec openapi-emitter, task 5.2). Builds
/// a document over a small in-memory registry with one read-only and one writable view and asserts endpoint
/// parity (the exact per-view operation set), the fixed method/path shape, unique <c>{name}_{facet}</c>
/// operationIds, the absence of path parameters, request/response <c>$ref</c> wiring, and referential
/// integrity of every <c>$ref</c>. Security and error responses are layered by task 5.3, so this suite does
/// not assert them.
/// </summary>
public sealed class OpenApiDocumentBuilderTests
{
    // ---- Representative DTOs ----------------------------------------------------------------------

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

    private const string ReadRoute = "/api/views/readWidgets";
    private const string WriteRoute = "/api/views/writeWidgets";

    // Mirrors the serialization seam: web defaults (camelCase) + JsonStringEnumConverter.
    private static JsonSerializerOptions SeamOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static ViewMetadata ReadView() => new(
        Name: "readWidgets",
        Route: ReadRoute,
        QueryType: typeof(ReadRow),
        CrudType: null,
        CrudEntityType: null,
        Fields: Array.Empty<FieldMetadata>(),
        Authorization: null,
        Limits: HardLimits.Default,
        IsReadOnly: true);

    private static ViewMetadata WriteView() => new(
        Name: "writeWidgets",
        Route: WriteRoute,
        QueryType: typeof(WritableRow),
        CrudType: typeof(WriteModel),
        CrudEntityType: typeof(WriteModel),
        Fields: Array.Empty<FieldMetadata>(),
        Authorization: null,
        Limits: HardLimits.Default,
        IsReadOnly: false);

    private static IViewRegistry BuildRegistry()
    {
        var registry = new ViewRegistry();
        registry.Add(ReadView());
        registry.Add(WriteView());
        return registry;
    }

    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    private static OpenApiDocument BuildDocument()
    {
        var builder = new VistaOpenApiDocumentBuilder(
            BuildRegistry(),
            SeamOptions(),
            new VistaEndpointOptions(),
            new VistaOpenApiOptions());
        return builder.Build();
    }

    // Enumerates (path, method, operation) for every emitted operation.
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

    // ---- Endpoint parity --------------------------------------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task ReadOnly_View_Yields_Exactly_The_Four_Read_Facets()
    {
        var document = BuildDocument();

        var paths = Operations(document)
            .Where(op => op.Operation.OperationId!.StartsWith("readWidgets_", StringComparison.Ordinal))
            .Select(op => op.Path)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(paths).IsEquivalentTo(new[]
        {
            ReadRoute + "/detail",
            ReadRoute + "/export",
            ReadRoute + "/list",
            ReadRoute + "/metadata",
        });
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task ReadOnly_View_Has_No_Write_Operations()
    {
        var document = BuildDocument();

        foreach (var suffix in new[] { "create", "update", "delete" })
        {
            await Assert.That(document.Paths!.ContainsKey(ReadRoute + "/" + suffix)).IsFalse();
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Writable_View_Yields_All_Seven_Facets()
    {
        var document = BuildDocument();

        var suffixes = new[] { "list", "detail", "metadata", "export", "create", "update", "delete" };
        foreach (var suffix in suffixes)
        {
            await Assert.That(document.Paths!.ContainsKey(WriteRoute + "/" + suffix)).IsTrue();
        }
    }

    // ---- Method + path soundness ------------------------------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Metadata_Is_Get_And_Every_Other_Facet_Is_Post()
    {
        var document = BuildDocument();

        foreach (var (path, method, _) in Operations(document))
        {
            var expected = path.EndsWith("/metadata", StringComparison.Ordinal) ? "GET" : "POST";
            await Assert.That(method).IsEqualTo(expected);
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Every_Path_Is_Route_Plus_Facet_Suffix()
    {
        var document = BuildDocument();

        foreach (var path in document.Paths!.Keys)
        {
            var isRead = path.StartsWith(ReadRoute + "/", StringComparison.Ordinal);
            var isWrite = path.StartsWith(WriteRoute + "/", StringComparison.Ordinal);
            await Assert.That(isRead || isWrite).IsTrue();
        }
    }

    // ---- operationId uniqueness + no path parameters ----------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task OperationIds_Are_Name_Underscore_Facet_And_Unique()
    {
        var document = BuildDocument();

        var ids = Operations(document).Select(op => op.Operation.OperationId!).ToArray();

        // Uniqueness (Requirement 1.5).
        await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Length);

        // Shape {name}_{facet} matches the path.
        foreach (var (path, _, operation) in Operations(document))
        {
            var facet = path[(path.LastIndexOf('/') + 1)..];
            var expectedPrefix = path.StartsWith(ReadRoute, StringComparison.Ordinal) ? "readWidgets_" : "writeWidgets_";
            await Assert.That(operation.OperationId).IsEqualTo(expectedPrefix + facet);
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task No_Operation_Declares_A_Path_Parameter()
    {
        var document = BuildDocument();

        foreach (var (_, _, operation) in Operations(document))
        {
            var pathParams = operation.Parameters?.Where(p => p.In == "path") ?? Enumerable.Empty<OpenApiParameter>();
            await Assert.That(pathParams.Any()).IsFalse();
        }
    }

    // ---- Request body $ref wiring -----------------------------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task List_And_Export_Request_Bodies_Ref_The_List_Envelope()
    {
        var document = BuildDocument();

        foreach (var suffix in new[] { "list", "export" })
        {
            var operation = document.Paths![ReadRoute + "/" + suffix].Post!;
            await Assert.That(RequestSchemaRef(operation)).IsEqualTo("#/components/schemas/VistaListRequestBody");
        }
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Detail_Request_Body_Refs_The_Detail_Envelope()
    {
        var document = BuildDocument();
        var operation = document.Paths![ReadRoute + "/detail"].Post!;
        await Assert.That(RequestSchemaRef(operation)).IsEqualTo("#/components/schemas/VistaDetailRequestBody");
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Metadata_Operation_Has_No_Request_Body()
    {
        var document = BuildDocument();
        var operation = document.Paths![ReadRoute + "/metadata"].Get!;
        await Assert.That(operation.RequestBody).IsNull();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Write_Request_Bodies_Ref_A_Write_Envelope_Whose_Model_Refs_TCrud()
    {
        var document = BuildDocument();

        foreach (var suffix in new[] { "create", "update", "delete" })
        {
            var operation = document.Paths![WriteRoute + "/" + suffix].Post!;
            var writeRef = RequestSchemaRef(operation);
            await Assert.That(writeRef).IsNotNull();
            await Assert.That(writeRef!.StartsWith("#/components/schemas/VistaWriteRequestBody", StringComparison.Ordinal)).IsTrue();

            // The specialized write body's `model` slot references the view's TCrud (WriteModel).
            var bodyName = writeRef["#/components/schemas/".Length..];
            var body = document.Components!.Schemas![bodyName];
            await Assert.That(body.Properties!["model"].Ref).IsEqualTo("#/components/schemas/WriteModel");
        }

        await Assert.That(document.Components!.Schemas!.ContainsKey("WriteModel")).IsTrue();
    }

    // ---- Success response $ref wiring -------------------------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task List_Success_Refs_A_ViewListResult_That_Refs_TRow()
    {
        var document = BuildDocument();

        var listOp = document.Paths![ReadRoute + "/list"].Post!;
        var successRef = SuccessSchema(listOp)!.Ref;
        await Assert.That(successRef).IsEqualTo("#/components/schemas/ViewListResult_ReadRow");

        // The wrapper's page.items array references the TRow component.
        var wrapper = document.Components!.Schemas!["ViewListResult_ReadRow"];
        var itemsRef = wrapper.Properties!["page"].Properties!["items"].Items!.Ref;
        await Assert.That(itemsRef).IsEqualTo("#/components/schemas/ReadRow");

        await Assert.That(document.Components!.Schemas!.ContainsKey("ReadRow")).IsTrue();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Detail_Success_Refs_TRow_And_Metadata_Success_Refs_The_Metadata_Envelope()
    {
        var document = BuildDocument();

        var detailOp = document.Paths![ReadRoute + "/detail"].Post!;
        await Assert.That(SuccessSchema(detailOp)!.Ref).IsEqualTo("#/components/schemas/ReadRow");

        var metadataOp = document.Paths![ReadRoute + "/metadata"].Get!;
        await Assert.That(SuccessSchema(metadataOp)!.Ref).IsEqualTo("#/components/schemas/VistaMetadataResponse");
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Create_Success_Refs_The_Write_Response_Envelope()
    {
        var document = BuildDocument();
        var createOp = document.Paths![WriteRoute + "/create"].Post!;
        await Assert.That(SuccessSchema(createOp)!.Ref).IsEqualTo("#/components/schemas/VistaWriteResponse");
    }

    // ---- Referential integrity smoke check --------------------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Every_Ref_Resolves_To_A_Component_Schema()
    {
        var document = BuildDocument();
        var components = document.Components!.Schemas!;

        var refs = new List<string>();
        foreach (var (_, _, operation) in Operations(document))
        {
            CollectRefs(operation.RequestBody?.Content, refs);
            if (operation.Responses is not null)
            {
                foreach (var response in operation.Responses.Values)
                {
                    CollectRefs(response.Content, refs);
                }
            }
        }

        foreach (var schema in components.Values)
        {
            CollectRefs(schema, refs);
        }

        await Assert.That(refs.Count).IsGreaterThan(0);
        foreach (var reference in refs.Distinct())
        {
            var name = reference["#/components/schemas/".Length..];
            await Assert.That(components.ContainsKey(name)).IsTrue();
        }
    }

    private static void CollectRefs(IReadOnlyDictionary<string, OpenApiMediaType>? content, List<string> sink)
    {
        if (content is null)
        {
            return;
        }

        foreach (var media in content.Values)
        {
            CollectRefs(media.Schema, sink);
        }
    }

    private static void CollectRefs(OpenApiSchema? schema, List<string> sink)
    {
        if (schema is null)
        {
            return;
        }

        if (schema.Ref is not null)
        {
            sink.Add(schema.Ref);
        }

        CollectRefs(schema.Items, sink);

        if (schema.Properties is not null)
        {
            foreach (var property in schema.Properties.Values)
            {
                CollectRefs(property, sink);
            }
        }

        if (schema.OneOf is not null)
        {
            foreach (var alternative in schema.OneOf)
            {
                CollectRefs(alternative, sink);
            }
        }
    }
}
