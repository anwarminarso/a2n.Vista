using System;
using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Adapters;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Adapters.AgGrid;

/// <summary>
/// Parses an AG Grid <c>filterModel</c> map (<c>colId → filter descriptor</c>) into a neutral
/// <see cref="FilterNode"/> for the structured-filter channel (D134). Every leaf is built for the
/// <c>Filter</c> origin; the engine enforces the whitelist. The descriptor is polymorphic across
/// <c>filterType</c> (<c>text</c>/<c>number</c>/<c>date</c>/<c>set</c>) and combined
/// (<c>operator</c> + <c>conditions</c>) shapes, so the branching lives here rather than in the
/// source-generated JSON context. AG Grid Advanced Filter (a nested join/column tree) is deferred for
/// v1 and rejected loudly with <see cref="AdapterBindException"/> — never silently dropped (D134).
/// </summary>
public static class AgGridFilterModelParser
{
    /// <summary>
    /// Parses an AG Grid <paramref name="filterModel"/> map into an AND-of-columns
    /// <see cref="FilterNode"/> for the <c>Filter</c> channel, or <see langword="null"/> when the map is
    /// empty. Each column descriptor is mapped per the D134 table; multiple columns are combined with a
    /// <see cref="FilterAnd"/> in map order.
    /// </summary>
    /// <param name="filterModel">The AG Grid <c>filterModel</c> map keyed by <c>colId</c>.</param>
    /// <param name="fields">
    /// The view fields, keyed by field name. Reserved for field-type-aware value handling and consumed for
    /// consistency with the other adapter parsers; the AG Grid mapping table (D134) is field-type-neutral.
    /// </param>
    /// <returns>The parsed filter tree, or <see langword="null"/> when nothing maps.</returns>
    /// <exception cref="AdapterBindException">
    /// A descriptor is malformed, carries an unknown <c>filterType</c>/<c>type</c>, is missing a required
    /// <c>inRange</c> bound, or is an Advanced-Filter shape (deferred for v1).
    /// </exception>
    public static FilterNode? Parse(
        IReadOnlyDictionary<string, JsonElement> filterModel,
        IReadOnlyDictionary<string, FieldMetadata> fields)
    {
        ArgumentNullException.ThrowIfNull(filterModel);
        ArgumentNullException.ThrowIfNull(fields);

        if (filterModel.Count == 0)
        {
            return null;
        }

        var children = new List<FilterNode>(filterModel.Count);
        foreach (var (colId, descriptor) in filterModel)
        {
            children.Add(ParseColumn(colId, descriptor));
        }

        return children.Count == 1 ? children[0] : new FilterAnd(children);
    }

    /// <summary>Parses a single column descriptor into a <see cref="FilterNode"/>.</summary>
    private static FilterNode ParseColumn(string colId, JsonElement descriptor)
    {
        if (descriptor.ValueKind != JsonValueKind.Object)
        {
            throw new AdapterBindException(
                $"The AG Grid filterModel entry for column '{colId}' is not a filter object.");
        }

        var filterType = GetString(descriptor, "filterType");

        // Advanced Filter (nested join/column tree) is deferred for v1 (D134): reject loudly.
        if (IsAdvancedFilter(filterType, descriptor))
        {
            throw new AdapterBindException(
                $"AG Grid Advanced Filter is not supported (column '{colId}'); it is deferred for v1 (D134).");
        }

        // Combined column filter: operator + conditions[].
        if (descriptor.TryGetProperty("conditions", out var conditions)
            && conditions.ValueKind == JsonValueKind.Array)
        {
            return ParseCombined(colId, descriptor, conditions);
        }

        // Set filter: In over values.
        if (string.Equals(filterType, "set", StringComparison.OrdinalIgnoreCase))
        {
            return new FilterLeaf(colId, FilterOperator.In, ReadSetValues(descriptor));
        }

        // Scalar filter: text (default when filterType is absent) / number / date.
        if (string.IsNullOrEmpty(filterType) || string.Equals(filterType, "text", StringComparison.OrdinalIgnoreCase))
        {
            return ParseTextFilter(colId, descriptor);
        }

        if (string.Equals(filterType, "number", StringComparison.OrdinalIgnoreCase))
        {
            return ParseNumberOrDateFilter(colId, descriptor, isDate: false);
        }

        if (string.Equals(filterType, "date", StringComparison.OrdinalIgnoreCase))
        {
            return ParseNumberOrDateFilter(colId, descriptor, isDate: true);
        }

        throw new AdapterBindException($"Unknown AG Grid filterType '{filterType}' on column '{colId}'.");
    }

    /// <summary>Maps a text-column filter descriptor (D134 text rows).</summary>
    private static FilterNode ParseTextFilter(string colId, JsonElement descriptor)
    {
        var type = GetString(descriptor, "type")
            ?? throw new AdapterBindException($"The AG Grid text filter on column '{colId}' is missing its 'type'.");

        switch (type.ToLowerInvariant())
        {
            case "contains": return new FilterLeaf(colId, FilterOperator.Contains, ReadValue(descriptor, "filter"));
            case "notcontains": return new FilterNot(new FilterLeaf(colId, FilterOperator.Contains, ReadValue(descriptor, "filter")));
            case "startswith": return new FilterLeaf(colId, FilterOperator.StartsWith, ReadValue(descriptor, "filter"));
            case "endswith": return new FilterLeaf(colId, FilterOperator.EndsWith, ReadValue(descriptor, "filter"));
            case "equals": return new FilterLeaf(colId, FilterOperator.Equals, ReadValue(descriptor, "filter"));
            case "notequal": return new FilterLeaf(colId, FilterOperator.NotEquals, ReadValue(descriptor, "filter"));
            case "blank": return new FilterLeaf(colId, FilterOperator.IsNull, null);
            case "notblank": return new FilterNot(new FilterLeaf(colId, FilterOperator.IsNull, null));
            default:
                throw new AdapterBindException($"Unknown AG Grid text filter type '{type}' on column '{colId}'.");
        }
    }

    /// <summary>Maps a number- or date-column filter descriptor (D134 number/date rows).</summary>
    private static FilterNode ParseNumberOrDateFilter(string colId, JsonElement descriptor, bool isDate)
    {
        var type = GetString(descriptor, "type")
            ?? throw new AdapterBindException(
                $"The AG Grid {(isDate ? "date" : "number")} filter on column '{colId}' is missing its 'type'.");

        // Value sources: number uses filter/filterTo; date uses dateFrom/dateTo.
        var fromProp = isDate ? "dateFrom" : "filter";
        var toProp = isDate ? "dateTo" : "filterTo";

        switch (type.ToLowerInvariant())
        {
            case "equals": return new FilterLeaf(colId, FilterOperator.Equals, ReadValue(descriptor, fromProp));
            case "notequal": return new FilterLeaf(colId, FilterOperator.NotEquals, ReadValue(descriptor, fromProp));
            case "greaterthan": return new FilterLeaf(colId, FilterOperator.GreaterThan, ReadValue(descriptor, fromProp));
            case "greaterthanorequal": return new FilterLeaf(colId, FilterOperator.GreaterThanOrEqual, ReadValue(descriptor, fromProp));
            case "lessthan": return new FilterLeaf(colId, FilterOperator.LessThan, ReadValue(descriptor, fromProp));
            case "lessthanorequal": return new FilterLeaf(colId, FilterOperator.LessThanOrEqual, ReadValue(descriptor, fromProp));
            case "inrange": return BuildInRange(colId, descriptor, fromProp, toProp);
            case "blank": return new FilterLeaf(colId, FilterOperator.IsNull, null);
            case "notblank": return new FilterNot(new FilterLeaf(colId, FilterOperator.IsNull, null));
            default:
                throw new AdapterBindException(
                    $"Unknown AG Grid {(isDate ? "date" : "number")} filter type '{type}' on column '{colId}'.");
        }
    }

    /// <summary>
    /// Builds an <c>inRange</c> leaf as <see cref="FilterOperator.Between"/> over both bounds. Both bounds
    /// are required; a missing bound is a bind failure (D134, R4.1).
    /// </summary>
    private static FilterNode BuildInRange(string colId, JsonElement descriptor, string fromProp, string toProp)
    {
        if (!TryReadBound(descriptor, fromProp, out var from) || !TryReadBound(descriptor, toProp, out var to))
        {
            throw new AdapterBindException(
                $"The AG Grid 'inRange' filter on column '{colId}' requires both bounds ('{fromProp}' and '{toProp}').");
        }

        return new FilterLeaf(colId, FilterOperator.Between, new List<object?> { from, to });
    }

    /// <summary>
    /// Maps a combined column filter (<c>operator: "AND"/"OR"</c> with <c>conditions</c>) to a
    /// <see cref="FilterAnd"/>/<see cref="FilterOr"/> over the mapped condition leaves, preserving order.
    /// </summary>
    private static FilterNode ParseCombined(string colId, JsonElement descriptor, JsonElement conditions)
    {
        var mapped = new List<FilterNode>(conditions.GetArrayLength());
        foreach (var condition in conditions.EnumerateArray())
        {
            mapped.Add(ParseColumn(colId, condition));
        }

        if (mapped.Count == 0)
        {
            throw new AdapterBindException(
                $"The AG Grid combined filter on column '{colId}' carries no conditions.");
        }

        if (mapped.Count == 1)
        {
            return mapped[0];
        }

        var op = GetString(descriptor, "operator");
        return string.Equals(op, "OR", StringComparison.OrdinalIgnoreCase)
            ? new FilterOr(mapped)
            : new FilterAnd(mapped);
    }

    /// <summary>
    /// Detects an AG Grid Advanced-Filter shape: a join node (<c>filterType == "join"</c> or
    /// <c>type == "join"</c>) or an explicit <c>filterType == "advanced"</c> marker.
    /// </summary>
    private static bool IsAdvancedFilter(string? filterType, JsonElement descriptor)
    {
        if (string.Equals(filterType, "advanced", StringComparison.OrdinalIgnoreCase)
            || string.Equals(filterType, "join", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var type = GetString(descriptor, "type");
        return string.Equals(type, "join", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads a <c>set</c> filter's <c>values</c> array into a neutral list (empty when absent).</summary>
    private static List<object?> ReadSetValues(JsonElement descriptor)
    {
        if (!descriptor.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return new List<object?>();
        }

        var list = new List<object?>(values.GetArrayLength());
        foreach (var item in values.EnumerateArray())
        {
            list.Add(Value(item));
        }

        return list;
    }

    /// <summary>Reads a scalar value property (absent → <see langword="null"/>).</summary>
    private static object? ReadValue(JsonElement descriptor, string prop) =>
        descriptor.TryGetProperty(prop, out var value) ? Value(value) : null;

    /// <summary>Reads a required bound: present and not JSON <c>null</c>.</summary>
    private static bool TryReadBound(JsonElement descriptor, string prop, out object? value)
    {
        if (descriptor.TryGetProperty(prop, out var element)
            && element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            value = Value(element);
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Reads a string property, or <see langword="null"/> when absent or non-string.</summary>
    private static string? GetString(JsonElement element, string prop) =>
        element.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Converts a filter value <see cref="JsonElement"/> to a neutral CLR value (mirrors
    /// <c>QueryBuilderParser.Value</c>): strings stay strings; numbers pass as their raw text (the engine
    /// coerces to the field type); booleans/null pass through; arrays become a
    /// <see cref="List{T}"/> of converted elements.
    /// </summary>
    private static object? Value(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Array => ToList(element),
        _ => element.GetRawText(),
    };

    private static List<object?> ToList(JsonElement array)
    {
        var list = new List<object?>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            list.Add(Value(item));
        }

        return list;
    }
}
