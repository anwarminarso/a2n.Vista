using System.Collections.Generic;
using a2n.Vista.OpenApi.Model;

namespace a2n.Vista.OpenApi.Schemas;

/// <summary>
/// Hand-authored, reflection-free <see cref="OpenApiSchema"/> descriptors for the fixed Vista wire
/// envelopes (spec openapi-emitter, task 2.1; Requirements 3.1, 3.2, 3.4, 3.5, 6.1, 13.4).
/// </summary>
/// <remarks>
/// <para>
/// These descriptors are authored <b>by hand</b> from the real envelope types in
/// <c>a2n.Vista.AspNetCore</c> (<see cref="a2n.Vista.AspNetCore.Execution.VistaListRequestBody"/>,
/// <c>VistaDetailRequestBody</c>, <c>VistaWriteRequestBody</c>, <c>VistaWriteResponse</c>,
/// <c>VistaMetadataResponse</c>, <c>VistaFieldMetadataResponse</c>) and Core
/// (<see cref="a2n.Vista.Ports.ViewListResult{TRow}"/>, <see cref="a2n.Vista.Results.PagedResult{T}"/>),
/// so no reflection is used and the envelope portion of a document is AOT-clean (Requirement 13.4).
/// </para>
/// <para>
/// Property names are authored under the serialization seam's <c>Naming_Policy</c> (the web default,
/// camelCase — see <c>VistaJson.Options</c>, built from <see cref="System.Text.Json.JsonSerializerDefaults.Web"/>),
/// so they match the JSON the seam actually emits. Task 8.5 parity-checks these names against real
/// serialization, so accuracy matters.
/// </para>
/// <para>
/// The <c>filter</c> and <c>scope</c> slots of <see cref="VistaListRequestBody"/> reference
/// <c>#/components/schemas/FilterNode</c>; task 2.2 supplies the <c>FilterNode</c> schema itself. The
/// generic wrappers <see cref="ViewListResult"/> and <see cref="PagedResult"/> take the row schema's
/// reference as a slot so the metadata-driven builder (task 5.x) can bind each view's <c>TRow</c>.
/// </para>
/// </remarks>
public static class EnvelopeSchemas
{
    /// <summary>The component name of the <c>FilterNode</c> schema (authored by task 2.2).</summary>
    public const string FilterNodeRef = "#/components/schemas/FilterNode";

    private static OpenApiSchema Ref(string componentName) =>
        new() { Ref = "#/components/schemas/" + componentName };

    private static OpenApiSchema String() => new() { Type = "string" };

    private static OpenApiSchema NullableString() => new() { Type = "string", Nullable = true };

    private static OpenApiSchema Boolean() => new() { Type = "boolean" };

    private static OpenApiSchema Int32() => new() { Type = "integer", Format = "int32" };

    private static OpenApiSchema Int64() => new() { Type = "integer", Format = "int64" };

    private static IReadOnlyDictionary<string, OpenApiSchema> Props(
        params (string Name, OpenApiSchema Schema)[] members)
    {
        var map = OpenApiCollections.CreateMap<OpenApiSchema>();
        foreach (var (name, schema) in members)
        {
            map[name] = schema;
        }

        return map;
    }

    /// <summary>
    /// A single sort instruction (<see cref="a2n.Vista.AspNetCore.Execution.VistaSortBody"/>):
    /// <c>{ "field": "Name", "desc": true }</c>. Referenced from the <c>sort</c> array of
    /// <see cref="VistaListRequestBody"/>.
    /// </summary>
    public static OpenApiSchema VistaSortBody() => new()
    {
        Type = "object",
        Properties = Props(
            ("field", String()),
            ("desc", Boolean())),
    };

    /// <summary>
    /// The List/Export request body (<see cref="a2n.Vista.AspNetCore.Execution.VistaListRequestBody"/>):
    /// the neutral query (filter/search/scope/sort/paging) plus the Export-only <c>format</c>
    /// (Requirement 3.1). <c>filter</c> and <c>scope</c> reference the polymorphic <c>FilterNode</c>
    /// schema (task 2.2).
    /// </summary>
    public static OpenApiSchema VistaListRequestBody() => new()
    {
        Type = "object",
        // filter/scope are optional (absent from `required`); the FilterNode $ref carries no sibling
        // `nullable` because OpenAPI 3.0 ignores keywords alongside a `$ref`.
        Properties = Props(
            ("filter", new OpenApiSchema { Ref = FilterNodeRef }),
            ("search", NullableString()),
            ("scope", new OpenApiSchema { Ref = FilterNodeRef }),
            ("sort", new OpenApiSchema
            {
                Type = "array",
                Nullable = true,
                Items = Ref("VistaSortBody"),
            }),
            ("page", Int32()),
            ("pageSize", Int32()),
            ("format", NullableString())),
    };

    /// <summary>
    /// The Detail request body (<see cref="a2n.Vista.AspNetCore.Execution.VistaDetailRequestBody"/>):
    /// a single <c>key</c> that is a scalar (single key) or a field-name→value object (composite key),
    /// carried as an arbitrary JSON element (Requirement 3.2). The permissive schema (no <c>type</c>)
    /// admits both shapes.
    /// </summary>
    public static OpenApiSchema VistaDetailRequestBody() => new()
    {
        Type = "object",
        Properties = Props(
            ("key", new OpenApiSchema { Description = "A scalar key value or a field-name to value object for a composite key." })),
        Required = new[] { "key" },
    };

    /// <summary>
    /// The single write request body for Create/Update/Delete
    /// (<see cref="a2n.Vista.AspNetCore.Execution.VistaWriteRequestBody"/>): the typed <c>model</c>
    /// (bound to the view's <c>TCrud</c>) and the <c>key</c> for Update/Delete (Requirement 3.2).
    /// Both ride as arbitrary JSON elements and are described permissively here; the metadata-driven
    /// builder may specialize <c>model</c> to the view's <c>TCrud</c> schema.
    /// </summary>
    public static OpenApiSchema VistaWriteRequestBody() => new()
    {
        Type = "object",
        Properties = Props(
            ("model", new OpenApiSchema { Nullable = true, Description = "The typed write payload (the view's TCrud model)." }),
            ("key", new OpenApiSchema { Nullable = true, Description = "A scalar key value or a field-name to value object for a composite key." })),
    };

    /// <summary>
    /// The Create success response (<see cref="a2n.Vista.AspNetCore.Execution.VistaWriteResponse"/>):
    /// the created row's primary key only — a scalar for a single key or a field-name→value object for
    /// a composite key (Requirement 3.5). The permissive <c>key</c> schema admits both.
    /// </summary>
    public static OpenApiSchema VistaWriteResponse() => new()
    {
        Type = "object",
        Properties = Props(
            ("key", new OpenApiSchema { Description = "The created row's primary key: a scalar or a field-name to value object." })),
        Required = new[] { "key" },
    };

    /// <summary>
    /// A single field's metadata projection
    /// (<see cref="a2n.Vista.AspNetCore.Execution.VistaFieldMetadataResponse"/>) used inside the
    /// Metadata response field list (Requirement 3.4).
    /// </summary>
    public static OpenApiSchema VistaFieldMetadataResponse() => new()
    {
        Type = "object",
        Properties = Props(
            ("name", String()),
            ("label", String()),
            ("clrType", String()),
            ("isFilterable", Boolean()),
            ("isSortable", Boolean()),
            ("isSearchable", Boolean()),
            ("isScopable", Boolean()),
            ("isHidden", Boolean()),
            ("isPrimaryKey", Boolean()),
            ("allowedOperators", String()),
            // Optional (D149): the author's display-format hint, absent when none was set. Not in Required
            // because it is nullable and omitted from the payload when null.
            ("format", new OpenApiSchema
            {
                Type = "string",
                Nullable = true,
                Description = "Display-format hint for the client to apply when rendering; the server never interprets it.",
            })),
        Required = new[]
        {
            "name", "label", "clrType", "isFilterable", "isSortable", "isSearchable",
            "isScopable", "isHidden", "isPrimaryKey", "allowedOperators",
        },
    };

    /// <summary>
    /// The Metadata response (<see cref="a2n.Vista.AspNetCore.Execution.VistaMetadataResponse"/>):
    /// name, route, isReadOnly, keyFields, the paging/export limits, and the visible field list
    /// (Requirement 3.4). The <c>fields</c> array items reference the <c>VistaFieldMetadataResponse</c>
    /// schema.
    /// </summary>
    public static OpenApiSchema VistaMetadataResponse() => new()
    {
        Type = "object",
        Properties = Props(
            ("name", String()),
            ("route", String()),
            ("isReadOnly", Boolean()),
            ("keyFields", new OpenApiSchema { Type = "array", Items = String() }),
            ("maxPageSize", Int32()),
            ("maxExportRows", Int32()),
            ("fields", new OpenApiSchema { Type = "array", Items = Ref("VistaFieldMetadataResponse") })),
        Required = new[]
        {
            "name", "route", "isReadOnly", "keyFields", "maxPageSize", "maxExportRows", "fields",
        },
    };

    /// <summary>
    /// The generic paged result (<see cref="a2n.Vista.Results.PagedResult{T}"/>): the materialized
    /// <c>items</c> array plus the paging totals. The element type is supplied by the caller as
    /// <paramref name="rowRef"/> (a <c>$ref</c> to the row's component schema), so this wrapper is
    /// bound per view.
    /// </summary>
    /// <param name="rowRef">The <c>$ref</c> string for the row (<c>TRow</c>) schema.</param>
    public static OpenApiSchema PagedResult(string rowRef) => new()
    {
        Type = "object",
        Properties = Props(
            ("items", new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Ref = rowRef } }),
            ("totalRows", Int64()),
            ("pageIndex", Int32()),
            ("pageSize", Int32()),
            ("totalPages", Int64())),
        Required = new[] { "items", "totalRows", "pageIndex", "pageSize", "totalPages" },
    };

    /// <summary>
    /// The List success response (<see cref="a2n.Vista.Ports.ViewListResult{TRow}"/>): the filtered,
    /// paged result (<c>page</c>) plus the unfiltered total (<c>totalRowsUnfiltered</c>)
    /// (Requirement 3.3). The <c>page</c> shape is the row-bound <see cref="PagedResult(string)"/>.
    /// </summary>
    /// <param name="rowRef">The <c>$ref</c> string for the row (<c>TRow</c>) schema.</param>
    public static OpenApiSchema ViewListResult(string rowRef) => new()
    {
        Type = "object",
        Properties = Props(
            ("page", PagedResult(rowRef)),
            ("totalRowsUnfiltered", Int64())),
        Required = new[] { "page", "totalRowsUnfiltered" },
    };

    /// <summary>
    /// The RFC 7807 problem-details schema for <c>application/problem+json</c> error responses: the
    /// standard members (<c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c>, <c>instance</c>) plus
    /// the Vista <c>code</c> extension member, and open <c>additionalProperties</c> for other extensions
    /// (Requirement 6.1). The wire members mirror <c>VistaProblemResults</c>.
    /// </summary>
    public static OpenApiSchema ProblemDetails() => new()
    {
        Type = "object",
        Properties = Props(
            ("type", NullableString()),
            ("title", NullableString()),
            ("status", new OpenApiSchema { Type = "integer", Format = "int32", Nullable = true }),
            ("detail", NullableString()),
            ("instance", NullableString()),
            ("code", NullableString())),
        AdditionalProperties = true,
    };
}
