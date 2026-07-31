using System;
using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Adapters;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Adapters.DataTablesNet;

/// <summary>
/// Parses jQuery-QueryBuilder JSON (<c>jsonQB</c>) into a neutral <see cref="FilterNode"/> for the
/// structured-filter channel (Spec 04 §8.1). Every leaf is built for the <c>Filter</c> origin; the engine
/// enforces the whitelist. Operator mapping follows §8.1 including D64 (<c>is_empty</c>/<c>is_not_empty</c>).
/// </summary>
public static class QueryBuilderParser
{
    /// <summary>
    /// Parses <paramref name="json"/> (a QueryBuilder group object) into a <see cref="FilterNode"/>, or
    /// <see langword="null"/> when the input is empty or has no rules.
    /// </summary>
    /// <param name="json">The raw <c>jsonQB</c> string.</param>
    /// <param name="fields">The view fields, used to branch <c>is_empty</c> on string vs non-string (D64).</param>
    /// <returns>The parsed filter tree, or <see langword="null"/>.</returns>
    /// <exception cref="AdapterBindException">The JSON is malformed or contains an unknown operator.</exception>
    public static FilterNode? Parse(string? json, IReadOnlyDictionary<string, FieldMetadata> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        QbNode? root;
        try
        {
            root = JsonSerializer.Deserialize(json, DataTablesJsonContext.Default.QbNode);
        }
        catch (JsonException ex)
        {
            throw new AdapterBindException($"The 'jsonQB' value is not valid JSON: {ex.Message}", ex);
        }

        return root is null ? null : ParseNode(root, fields);
    }

    private static FilterNode? ParseNode(QbNode node, IReadOnlyDictionary<string, FieldMetadata> fields)
    {
        // A group carries Rules; a rule carries a field + operator.
        if (node.Rules is not null)
        {
            var children = new List<FilterNode>(node.Rules.Count);
            foreach (var child in node.Rules)
            {
                var parsed = ParseNode(child, fields);
                if (parsed is not null)
                {
                    children.Add(parsed);
                }
            }

            // An empty group must NOT collapse to "no filter": the compiler treats an empty AND/OR group as
            // vacuously true, so a negated empty group ({"not":true,"rules":[]}) means "no rows". Returning
            // null here inverted that into "every row", handing back the entire unfiltered set. Only an
            // un-negated empty group is a genuine no-op.
            if (children.Count == 0 && !node.Not)
            {
                return null;
            }

            FilterNode group = string.Equals(node.Condition, "OR", StringComparison.OrdinalIgnoreCase)
                ? new FilterOr(children)
                : new FilterAnd(children);

            return node.Not ? new FilterNot(group) : group;
        }

        return ParseRule(node, fields);
    }

    private static FilterNode ParseRule(QbNode rule, IReadOnlyDictionary<string, FieldMetadata> fields)
    {
        var field = rule.Field ?? rule.Id;
        if (string.IsNullOrEmpty(field))
        {
            throw new AdapterBindException("A 'jsonQB' rule is missing its 'field'/'id'.");
        }

        var op = rule.Operator?.ToLowerInvariant()
            ?? throw new AdapterBindException($"A 'jsonQB' rule on field '{field}' is missing its 'operator'.");

        switch (op)
        {
            case "equal": return new FilterLeaf(field, FilterOperator.Equals, Value(rule.Value));
            case "not_equal": return new FilterLeaf(field, FilterOperator.NotEquals, Value(rule.Value));
            case "begins_with": return new FilterLeaf(field, FilterOperator.StartsWith, Value(rule.Value));
            case "ends_with": return new FilterLeaf(field, FilterOperator.EndsWith, Value(rule.Value));
            case "contains": return new FilterLeaf(field, FilterOperator.Contains, Value(rule.Value));
            case "less": return new FilterLeaf(field, FilterOperator.LessThan, Value(rule.Value));
            case "less_or_equal": return new FilterLeaf(field, FilterOperator.LessThanOrEqual, Value(rule.Value));
            case "greater": return new FilterLeaf(field, FilterOperator.GreaterThan, Value(rule.Value));
            case "greater_or_equal": return new FilterLeaf(field, FilterOperator.GreaterThanOrEqual, Value(rule.Value));
            case "between": return new FilterLeaf(field, FilterOperator.Between, Value(rule.Value));
            case "not_between": return new FilterNot(new FilterLeaf(field, FilterOperator.Between, Value(rule.Value)));
            case "in": return new FilterLeaf(field, FilterOperator.In, Value(rule.Value));
            case "not_in": return new FilterNot(new FilterLeaf(field, FilterOperator.In, Value(rule.Value)));
            case "is_empty": return BuildIsEmpty(field, fields);
            case "is_not_empty": return new FilterNot(BuildIsEmpty(field, fields));
            default:
                throw new AdapterBindException($"Unknown 'jsonQB' operator '{rule.Operator}' on field '{field}'.");
        }
    }

    /// <summary>
    /// Builds the <c>is_empty</c> predicate (D64): <c>IsNull</c> for a non-string field; for a string field
    /// <c>Or(IsNull, Equals "")</c> to also catch the empty string.
    /// </summary>
    private static FilterNode BuildIsEmpty(string field, IReadOnlyDictionary<string, FieldMetadata> fields)
    {
        var isString = fields.TryGetValue(field, out var meta) && meta.ClrType == typeof(string);
        if (!isString)
        {
            return new FilterLeaf(field, FilterOperator.IsNull, null);
        }

        return new FilterOr(new FilterNode[]
        {
            new FilterLeaf(field, FilterOperator.IsNull, null),
            new FilterLeaf(field, FilterOperator.Equals, string.Empty),
        });
    }

    /// <summary>
    /// Converts a QueryBuilder value <see cref="JsonElement"/> to a neutral CLR value: strings stay
    /// strings, numbers become their raw text (the engine coerces to the field type), booleans/null pass
    /// through, and arrays become a <see cref="List{T}"/> of converted elements (for <c>in</c>/<c>between</c>).
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
