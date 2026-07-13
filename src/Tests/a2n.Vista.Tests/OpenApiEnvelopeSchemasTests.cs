using a2n.Vista.OpenApi.Model;
using a2n.Vista.OpenApi.Schemas;
using a2n.Vista.OpenApi.Serialization;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Example coverage for the hand-authored Vista envelope schema descriptors (spec openapi-emitter,
/// task 2.1; Requirements 3.1, 3.2, 3.4, 3.5, 6.1, 13.4). These assert the presence and shape of each
/// descriptor: the camelCase property names the seam emits, correct BCL scalar type/format, the generic
/// <c>$ref</c> slot for <c>ViewListResult</c>/<c>PagedResult</c>, the <c>FilterNode</c> <c>$ref</c> on the
/// list body's filter/scope slots, and the <c>ProblemDetails</c> <c>code</c> extension member. The
/// schema/wire-parity property test against real serialization lands with task 8.5.
/// </summary>
public sealed class OpenApiEnvelopeSchemasTests
{
    private static OpenApiSchema PropertyOf(OpenApiSchema schema, string name)
    {
        if (schema.Properties is null || !schema.Properties.TryGetValue(name, out var property))
        {
            throw new KeyNotFoundException($"Schema has no property '{name}'.");
        }

        return property;
    }

    [Test]
    public async Task VistaListRequestBody_Has_CamelCase_Members_And_FilterNode_Refs()
    {
        var schema = EnvelopeSchemas.VistaListRequestBody();

        await Assert.That(schema.Type).IsEqualTo("object");
        await Assert.That(schema.Properties!.Keys).Contains("filter");
        await Assert.That(schema.Properties!.Keys).Contains("search");
        await Assert.That(schema.Properties!.Keys).Contains("scope");
        await Assert.That(schema.Properties!.Keys).Contains("sort");
        await Assert.That(schema.Properties!.Keys).Contains("page");
        await Assert.That(schema.Properties!.Keys).Contains("pageSize");
        await Assert.That(schema.Properties!.Keys).Contains("format");

        // filter/scope reference the polymorphic FilterNode schema (wired by task 2.2).
        await Assert.That(PropertyOf(schema, "filter").Ref).IsEqualTo(EnvelopeSchemas.FilterNodeRef);
        await Assert.That(PropertyOf(schema, "scope").Ref).IsEqualTo(EnvelopeSchemas.FilterNodeRef);

        // page/pageSize are 32-bit integers.
        var page = PropertyOf(schema, "page");
        await Assert.That(page.Type).IsEqualTo("integer");
        await Assert.That(page.Format).IsEqualTo("int32");

        // sort is an array whose items reference the VistaSortBody schema.
        var sort = PropertyOf(schema, "sort");
        await Assert.That(sort.Type).IsEqualTo("array");
        await Assert.That(sort.Items!.Ref).IsEqualTo("#/components/schemas/VistaSortBody");
    }

    [Test]
    public async Task VistaSortBody_Has_Field_And_Desc()
    {
        var schema = EnvelopeSchemas.VistaSortBody();

        await Assert.That(schema.Type).IsEqualTo("object");
        await Assert.That(PropertyOf(schema, "field").Type).IsEqualTo("string");
        await Assert.That(PropertyOf(schema, "desc").Type).IsEqualTo("boolean");
    }

    [Test]
    public async Task VistaDetailRequestBody_Has_Permissive_Required_Key()
    {
        var schema = EnvelopeSchemas.VistaDetailRequestBody();

        await Assert.That(schema.Type).IsEqualTo("object");
        await Assert.That(schema.Properties!.Keys).Contains("key");
        await Assert.That(schema.Required).IsNotNull();
        await Assert.That(schema.Required!).Contains("key");

        // The key is permissive (scalar OR composite object) -> no `type` constraint.
        await Assert.That(PropertyOf(schema, "key").Type).IsNull();
    }

    [Test]
    public async Task VistaWriteRequestBody_Has_Model_And_Key()
    {
        var schema = EnvelopeSchemas.VistaWriteRequestBody();

        await Assert.That(schema.Type).IsEqualTo("object");
        await Assert.That(schema.Properties!.Keys).Contains("model");
        await Assert.That(schema.Properties!.Keys).Contains("key");
    }

    [Test]
    public async Task VistaWriteResponse_Has_Required_Key()
    {
        var schema = EnvelopeSchemas.VistaWriteResponse();

        await Assert.That(schema.Type).IsEqualTo("object");
        await Assert.That(schema.Properties!.Keys).Contains("key");
        await Assert.That(schema.Required!).Contains("key");
    }

    [Test]
    public async Task VistaFieldMetadataResponse_Has_All_CamelCase_Members()
    {
        var schema = EnvelopeSchemas.VistaFieldMetadataResponse();

        foreach (var member in new[]
                 {
                     "name", "label", "clrType", "isFilterable", "isSortable", "isSearchable",
                     "isScopable", "isHidden", "isPrimaryKey", "allowedOperators",
                 })
        {
            await Assert.That(schema.Properties!.Keys).Contains(member);
        }

        await Assert.That(PropertyOf(schema, "name").Type).IsEqualTo("string");
        await Assert.That(PropertyOf(schema, "isPrimaryKey").Type).IsEqualTo("boolean");
    }

    [Test]
    public async Task VistaMetadataResponse_Has_Members_And_Fields_Array_Ref()
    {
        var schema = EnvelopeSchemas.VistaMetadataResponse();

        await Assert.That(schema.Properties!.Keys).Contains("name");
        await Assert.That(schema.Properties!.Keys).Contains("route");
        await Assert.That(schema.Properties!.Keys).Contains("isReadOnly");
        await Assert.That(schema.Properties!.Keys).Contains("keyFields");
        await Assert.That(schema.Properties!.Keys).Contains("maxPageSize");
        await Assert.That(schema.Properties!.Keys).Contains("maxExportRows");
        await Assert.That(schema.Properties!.Keys).Contains("fields");

        var keyFields = PropertyOf(schema, "keyFields");
        await Assert.That(keyFields.Type).IsEqualTo("array");
        await Assert.That(keyFields.Items!.Type).IsEqualTo("string");

        var fields = PropertyOf(schema, "fields");
        await Assert.That(fields.Type).IsEqualTo("array");
        await Assert.That(fields.Items!.Ref).IsEqualTo("#/components/schemas/VistaFieldMetadataResponse");
    }

    [Test]
    public async Task PagedResult_Binds_Row_Ref_Into_Items()
    {
        const string rowRef = "#/components/schemas/WidgetRow";
        var schema = EnvelopeSchemas.PagedResult(rowRef);

        await Assert.That(schema.Type).IsEqualTo("object");

        var items = PropertyOf(schema, "items");
        await Assert.That(items.Type).IsEqualTo("array");
        await Assert.That(items.Items!.Ref).IsEqualTo(rowRef);

        var totalRows = PropertyOf(schema, "totalRows");
        await Assert.That(totalRows.Type).IsEqualTo("integer");
        await Assert.That(totalRows.Format).IsEqualTo("int64");

        await Assert.That(PropertyOf(schema, "pageIndex").Format).IsEqualTo("int32");
        await Assert.That(PropertyOf(schema, "totalPages").Format).IsEqualTo("int64");
    }

    [Test]
    public async Task ViewListResult_Wraps_PagedResult_With_Generic_Row_Ref_Slot()
    {
        const string rowRef = "#/components/schemas/WidgetRow";
        var schema = EnvelopeSchemas.ViewListResult(rowRef);

        await Assert.That(schema.Type).IsEqualTo("object");
        await Assert.That(schema.Properties!.Keys).Contains("page");
        await Assert.That(schema.Properties!.Keys).Contains("totalRowsUnfiltered");

        // The generic $ref slot flows through page.items.
        var page = PropertyOf(schema, "page");
        var items = PropertyOf(page, "items");
        await Assert.That(items.Items!.Ref).IsEqualTo(rowRef);

        var total = PropertyOf(schema, "totalRowsUnfiltered");
        await Assert.That(total.Type).IsEqualTo("integer");
        await Assert.That(total.Format).IsEqualTo("int64");
    }

    [Test]
    public async Task ProblemDetails_Has_Rfc7807_Members_Plus_Code_Extension()
    {
        var schema = EnvelopeSchemas.ProblemDetails();

        await Assert.That(schema.Type).IsEqualTo("object");
        foreach (var member in new[] { "type", "title", "status", "detail", "instance", "code" })
        {
            await Assert.That(schema.Properties!.Keys).Contains(member);
        }

        // The Vista extension discriminator.
        await Assert.That(PropertyOf(schema, "code").Type).IsEqualTo("string");
        // status is an integer.
        await Assert.That(PropertyOf(schema, "status").Type).IsEqualTo("integer");
        // Open for other RFC 7807 extension members.
        await Assert.That(schema.AdditionalProperties).IsTrue();
    }

    [Test]
    public async Task Descriptors_Serialize_With_CamelCase_And_DollarRef()
    {
        // A round-trip through the source-gen serializer proves the descriptors emit clean OpenAPI JSON.
        var schemas = OpenApiCollections.CreateMap<OpenApiSchema>();
        schemas["VistaListRequestBody"] = EnvelopeSchemas.VistaListRequestBody();
        schemas["ViewListResult"] = EnvelopeSchemas.ViewListResult("#/components/schemas/WidgetRow");
        schemas["ProblemDetails"] = EnvelopeSchemas.ProblemDetails();

        var doc = new OpenApiDocument
        {
            Openapi = "3.0.4",
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Components = new OpenApiComponents { Schemas = schemas },
        };

        var json = VistaOpenApiJson.Serialize(doc);

        await Assert.That(json).Contains("\"$ref\":\"#/components/schemas/FilterNode\"");
        await Assert.That(json).Contains("\"$ref\":\"#/components/schemas/WidgetRow\"");
        await Assert.That(json).Contains("\"pageSize\"");
        await Assert.That(json).DoesNotContain("\"ref\":");
    }
}
