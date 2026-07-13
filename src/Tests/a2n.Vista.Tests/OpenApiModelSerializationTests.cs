using a2n.Vista.OpenApi.Model;
using a2n.Vista.OpenApi.Serialization;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Unit coverage for the hand-authored OpenAPI object model and its source-generated, byte-stable
/// serialization (spec openapi-emitter, task 1.2; Requirements 8.1, 9.1, 9.2, 13.4). These are example
/// checks; the determinism/validity property tests arrive with tasks 9.2/9.3.
/// </summary>
public sealed class OpenApiModelSerializationTests
{
    private static OpenApiDocument SampleDocument()
    {
        // Paths inserted OUT of ordinal order on purpose; the ordinal map must reorder them on the wire.
        var paths = OpenApiCollections.CreateMap<OpenApiPathItem>();
        paths["/api/views/widgets/metadata"] = new OpenApiPathItem
        {
            Get = new OpenApiOperation
            {
                OperationId = "widgets_metadata",
                Responses = OpenApiCollections.ToOrdinalMap(new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new() { Description = "OK" },
                }),
            },
        };
        paths["/api/views/widgets/list"] = new OpenApiPathItem
        {
            Post = new OpenApiOperation
            {
                OperationId = "widgets_list",
                RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content = OpenApiCollections.ToOrdinalMap(new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new()
                        {
                            Schema = new OpenApiSchema { Ref = "#/components/schemas/VistaListRequestBody" },
                        },
                    }),
                },
                Responses = OpenApiCollections.ToOrdinalMap(new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new() { Description = "OK" },
                }),
                Security = new IReadOnlyDictionary<string, IReadOnlyList<string>>[]
                {
                    new Dictionary<string, IReadOnlyList<string>> { ["bearer"] = Array.Empty<string>() },
                },
            },
        };

        var schemas = OpenApiCollections.CreateMap<OpenApiSchema>();
        schemas["VistaListRequestBody"] = new OpenApiSchema
        {
            Type = "object",
            Properties = OpenApiCollections.ToOrdinalMap(new Dictionary<string, OpenApiSchema>
            {
                ["pageSize"] = new() { Type = "integer", Format = "int32", Nullable = true },
                ["filter"] = new() { Ref = "#/components/schemas/FilterNode" },
            }),
        };

        var securitySchemes = OpenApiCollections.CreateMap<OpenApiSecurityScheme>();
        securitySchemes["bearer"] = new OpenApiSecurityScheme
        {
            Type = "http",
            Scheme = "bearer",
            BearerFormat = "JWT",
        };

        return new OpenApiDocument
        {
            Openapi = "3.0.4",
            Info = new OpenApiInfo { Title = "a2n.Vista API", Version = "1.0.0" },
            Paths = paths,
            Components = new OpenApiComponents { Schemas = schemas, SecuritySchemes = securitySchemes },
        };
    }

    [Test]
    public async Task Serialize_Uses_CamelCase_And_Emits_DollarRef()
    {
        var json = VistaOpenApiJson.Serialize(SampleDocument());

        await Assert.That(json).Contains("\"openapi\":\"3.0.4\"");
        await Assert.That(json).Contains("\"requestBody\"");
        await Assert.That(json).Contains("\"securitySchemes\"");
        await Assert.That(json).Contains("\"bearerFormat\":\"JWT\"");
        // The reference member must serialize as "$ref", never "ref".
        await Assert.That(json).Contains("\"$ref\":\"#/components/schemas/VistaListRequestBody\"");
        await Assert.That(json).DoesNotContain("\"ref\":");
    }

    [Test]
    public async Task Serialize_Omits_Null_Members()
    {
        var json = VistaOpenApiJson.Serialize(SampleDocument());

        // OpenApiInfo.Description is null -> must be absent.
        await Assert.That(json).DoesNotContain("\"description\":null");
        // OpenApiSchema.additionalProperties is null on every schema -> must be absent.
        await Assert.That(json).DoesNotContain("\"additionalProperties\"");
        // The list operation has no parameters -> absent.
        await Assert.That(json).DoesNotContain("\"parameters\"");
    }

    [Test]
    public async Task Serialize_Is_Byte_Stable_Across_Repeated_Calls()
    {
        var first = VistaOpenApiJson.Serialize(SampleDocument());
        var second = VistaOpenApiJson.Serialize(SampleDocument());

        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task Serialize_Orders_Map_Keys_Ordinally_Independent_Of_Insertion_Order()
    {
        var json = VistaOpenApiJson.Serialize(SampleDocument());

        // Paths: "/api/views/widgets/list" (inserted second) must appear before ".../metadata".
        var listIndex = json.IndexOf("/api/views/widgets/list", StringComparison.Ordinal);
        var metadataIndex = json.IndexOf("/api/views/widgets/metadata", StringComparison.Ordinal);
        await Assert.That(listIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(metadataIndex).IsGreaterThan(listIndex);

        // Schema properties: "filter" (inserted second) must appear before "pageSize".
        var filterIndex = json.IndexOf("\"filter\"", StringComparison.Ordinal);
        var pageSizeIndex = json.IndexOf("\"pageSize\"", StringComparison.Ordinal);
        await Assert.That(filterIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(pageSizeIndex).IsGreaterThan(filterIndex);
    }

    [Test]
    public async Task Serialize_Empty_Schema_Produces_Permissive_Object()
    {
        // An all-null OpenApiSchema is the permissive "{}" schema (Requirement 4.6 building block).
        var doc = new OpenApiDocument
        {
            Openapi = "3.0.4",
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Components = new OpenApiComponents
            {
                Schemas = OpenApiCollections.ToOrdinalMap(new Dictionary<string, OpenApiSchema>
                {
                    ["Bespoke"] = new OpenApiSchema(),
                }),
            },
        };

        var json = VistaOpenApiJson.Serialize(doc);

        await Assert.That(json).Contains("\"Bespoke\":{}");
    }

    [Test]
    public async Task Serialize_Indented_Preserves_CamelCase_And_DollarRef()
    {
        var json = VistaOpenApiJson.Serialize(SampleDocument(), writeIndented: true);

        await Assert.That(json).Contains("\"$ref\"");
        await Assert.That(json).Contains("\"bearerFormat\": \"JWT\"");
        await Assert.That(json).DoesNotContain("\"ref\":");
    }
}
