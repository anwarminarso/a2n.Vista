using System;
using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Adapters;
using a2n.Vista.Contracts;

namespace a2n.Vista.Adapters.DataTablesNet;

/// <summary>
/// Parses the DynData <c>externalFilter</c> mini-language (a JSON object whose properties are AND-ed) into
/// a neutral <see cref="FilterNode"/> for the <b>scope</b> channel (Spec 04 §7.4). Every leaf is built for
/// the <c>Scope</c> origin; the engine enforces <c>IsScopable</c> (a non-scopable field → 400, never a
/// silent skip).
/// </summary>
public static class ExternalFilterParser
{
    /// <summary>
    /// Parses <paramref name="json"/> into a conjunction over its properties, or <see langword="null"/>
    /// when empty.
    /// </summary>
    /// <param name="json">The raw <c>externalFilter</c> JSON object string.</param>
    /// <returns>The parsed scope filter tree, or <see langword="null"/>.</returns>
    /// <exception cref="AdapterBindException">The JSON is malformed.</exception>
    public static FilterNode? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        Dictionary<string, JsonElement>? map;
        try
        {
            map = JsonSerializer.Deserialize(json, DataTablesJsonContext.Default.DictionaryStringJsonElement);
        }
        catch (JsonException ex)
        {
            throw new AdapterBindException($"The 'externalFilter' value is not valid JSON: {ex.Message}", ex);
        }

        if (map is null || map.Count == 0)
        {
            return null;
        }

        var children = new List<FilterNode>(map.Count);
        foreach (var (field, value) in map)
        {
            children.Add(ParseField(field, value));
        }

        return children.Count == 1 ? children[0] : new FilterAnd(children);
    }

    private static FilterNode ParseField(string field, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => ParseArray(field, value),
        JsonValueKind.String => ParseSpec(field, value.GetString() ?? string.Empty),
        JsonValueKind.Number => new FilterLeaf(field, FilterOperator.Equals, value.GetRawText()),
        JsonValueKind.True => new FilterLeaf(field, FilterOperator.Equals, true),
        JsonValueKind.False => new FilterLeaf(field, FilterOperator.Equals, false),
        JsonValueKind.Null or JsonValueKind.Undefined => new FilterLeaf(field, FilterOperator.IsNull, null),
        _ => new FilterLeaf(field, FilterOperator.Equals, value.GetRawText()),
    };

    /// <summary>
    /// Array rule (§7.4): if any element is an operator-prefixed string (<c>&gt;</c>/<c>&lt;</c>/<c>=</c>),
    /// the <c>In</c> mode is cancelled and each element becomes a single operator AND-ed together (a range);
    /// otherwise the array becomes an <c>In</c>.
    /// </summary>
    private static FilterNode ParseArray(string field, JsonElement array)
    {
        var elements = new List<string>(array.GetArrayLength());
        var anyOperator = false;
        foreach (var item in array.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.GetRawText();
            text = text.Trim();
            elements.Add(text);
            if (HasComparisonPrefix(text))
            {
                anyOperator = true;
            }
        }

        if (!anyOperator)
        {
            return new FilterLeaf(field, FilterOperator.In, new List<object?>(elements));
        }

        var children = new List<FilterNode>(elements.Count);
        foreach (var element in elements)
        {
            children.Add(ParseSpec(field, element));
        }

        return children.Count == 1 ? children[0] : new FilterAnd(children);
    }

    /// <summary>Parses a single scalar spec string (prefix/suffix mini-language) into a leaf.</summary>
    private static FilterNode ParseSpec(string field, string raw)
    {
        var spec = raw.Trim();

        if (spec.StartsWith(">=", StringComparison.Ordinal))
        {
            return new FilterLeaf(field, FilterOperator.GreaterThanOrEqual, spec[2..].Trim());
        }

        if (spec.StartsWith("<=", StringComparison.Ordinal))
        {
            return new FilterLeaf(field, FilterOperator.LessThanOrEqual, spec[2..].Trim());
        }

        if (spec.StartsWith(">", StringComparison.Ordinal))
        {
            return new FilterLeaf(field, FilterOperator.GreaterThan, spec[1..].Trim());
        }

        if (spec.StartsWith("<", StringComparison.Ordinal))
        {
            return new FilterLeaf(field, FilterOperator.LessThan, spec[1..].Trim());
        }

        if (spec.StartsWith("=", StringComparison.Ordinal))
        {
            return new FilterLeaf(field, FilterOperator.Equals, spec[1..].Trim());
        }

        // Wildcard forms: %val% (Contains), val% (StartsWith), %val (EndsWith).
        var startsPct = spec.StartsWith("%", StringComparison.Ordinal);
        var endsPct = spec.EndsWith("%", StringComparison.Ordinal);
        if (startsPct && endsPct && spec.Length >= 2)
        {
            return new FilterLeaf(field, FilterOperator.Contains, spec[1..^1]);
        }

        if (endsPct)
        {
            return new FilterLeaf(field, FilterOperator.StartsWith, spec[..^1]);
        }

        if (startsPct)
        {
            return new FilterLeaf(field, FilterOperator.EndsWith, spec[1..]);
        }

        return new FilterLeaf(field, FilterOperator.Equals, spec);
    }

    private static bool HasComparisonPrefix(string text) =>
        text.StartsWith(">", StringComparison.Ordinal)
        || text.StartsWith("<", StringComparison.Ordinal)
        || text.StartsWith("=", StringComparison.Ordinal);
}
