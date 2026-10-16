using System;
using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Adapters;
using a2n.Vista.Contracts;

namespace a2n.Vista.Adapters.TanStackTable;

/// <summary>
/// Parses TanStack <c>columnFilters</c> (<c>{ id, value }</c> in array order) into a neutral
/// <see cref="FilterNode"/> for the structured-filter channel (D156). Every leaf is built for the
/// <c>Filter</c> origin; the engine enforces the whitelist. <c>value</c> is polymorphic — a scalar, a
/// faceted array, a <c>{ min, max }</c> range, or <c>{ op, value }</c> — so the branching lives here
/// rather than in the source-generated JSON context. A shape the table cannot have produced is rejected
/// with <see cref="AdapterBindException"/>, never silently dropped.
/// </summary>
public static class TanStackColumnFilterParser
{
    /// <summary>
    /// Parses <paramref name="filters"/> into an AND-of-columns <see cref="FilterNode"/>, or
    /// <see langword="null"/> when every entry is an empty filter (blank string, empty faceted array,
    /// unbounded range). A single column is returned unwrapped.
    /// </summary>
    /// <param name="filters">The TanStack <c>columnFilters</c> array, in priority order.</param>
    /// <returns>The parsed filter tree, or <see langword="null"/> when nothing maps.</returns>
    /// <exception cref="AdapterBindException">A value has a shape this adapter does not map.</exception>
    public static FilterNode? Parse(IReadOnlyList<TanStackColumnFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (filters.Count == 0)
        {
            return null;
        }

        var children = new List<FilterNode>(filters.Count);
        foreach (var filter in filters)
        {
            var node = ParseOne(filter.Id ?? string.Empty, filter.Value);
            if (node is not null)
            {
                children.Add(node);
            }
        }

        return children.Count switch
        {
            0 => null,
            1 => children[0],
            _ => new FilterAnd(children),
        };
    }

    /// <summary>Maps one column filter, or returns <see langword="null"/> when it is an empty filter.</summary>
    private static FilterNode? ParseOne(string columnId, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined:
                throw new AdapterBindException(
                    $"The TanStack column filter on '{columnId}' is missing its 'value'.");
            case JsonValueKind.Null:
                return new FilterLeaf(columnId, FilterOperator.IsNull, null);
            case JsonValueKind.String:
                var text = value.GetString();
                return string.IsNullOrWhiteSpace(text)
                    ? null
                    : new FilterLeaf(columnId, FilterOperator.Contains, text);
            case JsonValueKind.Number:
                return new FilterLeaf(columnId, FilterOperator.Equals, value.GetRawText());
            case JsonValueKind.True:
                return new FilterLeaf(columnId, FilterOperator.Equals, true);
            case JsonValueKind.False:
                return new FilterLeaf(columnId, FilterOperator.Equals, false);
            case JsonValueKind.Array:
                return ParseArray(columnId, value);
            case JsonValueKind.Object:
                return ParseObject(columnId, value);
            default:
                throw new AdapterBindException(
                    $"The TanStack column filter on '{columnId}' has an unsupported JSON value.");
        }
    }

    /// <summary>
    /// A JSON array is a faceted selection (<c>arrIncludesSome</c>) → <see cref="FilterOperator.In"/>.
    /// An empty array is "nothing selected" and contributes no leaf. A two-element numeric tuple is
    /// <b>not</b> treated as a range: that shape is ambiguous with a two-value set, so ranges use
    /// <c>{ min, max }</c> or <c>{ op: "between" }</c>.
    /// </summary>
    private static FilterNode? ParseArray(string columnId, JsonElement array)
    {
        if (array.GetArrayLength() == 0)
        {
            return null;
        }

        var values = new List<object?>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                throw new AdapterBindException(
                    $"The TanStack column filter on '{columnId}' has a nested value in its array; " +
                    "a faceted filter must be an array of scalars.");
            }

            values.Add(Scalar(item));
        }

        return new FilterLeaf(columnId, FilterOperator.In, values);
    }

    /// <summary>Maps <c>{ op, value }</c> or <c>{ min, max }</c>.</summary>
    private static FilterNode? ParseObject(string columnId, JsonElement obj)
    {
        var hasOp = TryGet(obj, "op", out var opElement);
        var hasMin = TryGet(obj, "min", out var minElement);
        var hasMax = TryGet(obj, "max", out var maxElement);

        if (hasOp && (hasMin || hasMax))
        {
            throw new AdapterBindException(
                $"The TanStack column filter on '{columnId}' carries both 'op' and a min/max range.");
        }

        if (hasOp)
        {
            RejectUnknownMembers(columnId, obj, "op", "value");
            if (opElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(opElement.GetString()))
            {
                throw new AdapterBindException(
                    $"The TanStack column filter on '{columnId}' has an invalid 'op'.");
            }

            TryGet(obj, "value", out var opValue);
            return MapOperator(columnId, opElement.GetString()!, opValue);
        }

        if (hasMin || hasMax)
        {
            RejectUnknownMembers(columnId, obj, "min", "max");
            return MapRange(columnId, hasMin ? minElement : default, hasMax ? maxElement : default, hasMin, hasMax);
        }

        throw new AdapterBindException(
            $"The TanStack column filter on '{columnId}' is an object without 'op' or 'min'/'max'.");
    }

    /// <summary>Maps an explicit operator. Names cover the short forms and TanStack's built-in filterFns.</summary>
    private static FilterNode MapOperator(string columnId, string op, JsonElement value)
    {
        switch (op.ToLowerInvariant())
        {
            case "contains":
            case "includesstring":
            case "includesstringsensitive":
                return new FilterLeaf(columnId, FilterOperator.Contains, RequireScalar(columnId, op, value));
            case "notcontains":
            case "notincludesstring":
                return new FilterNot(new FilterLeaf(columnId, FilterOperator.Contains, RequireScalar(columnId, op, value)));
            case "startswith":
                return new FilterLeaf(columnId, FilterOperator.StartsWith, RequireScalar(columnId, op, value));
            case "endswith":
                return new FilterLeaf(columnId, FilterOperator.EndsWith, RequireScalar(columnId, op, value));
            case "equals":
            case "eq":
            case "equalsstring":
            case "equalsstringsensitive":
            case "weakequals":
                return new FilterLeaf(columnId, FilterOperator.Equals, RequireScalar(columnId, op, value));
            case "notequals":
            case "notequal":
            case "neq":
                return new FilterLeaf(columnId, FilterOperator.NotEquals, RequireScalar(columnId, op, value));
            case "gt":
            case "greaterthan":
                return new FilterLeaf(columnId, FilterOperator.GreaterThan, RequireScalar(columnId, op, value));
            case "gte":
            case "greaterthanorequal":
            case "greaterthanorequalto":
                return new FilterLeaf(columnId, FilterOperator.GreaterThanOrEqual, RequireScalar(columnId, op, value));
            case "lt":
            case "lessthan":
                return new FilterLeaf(columnId, FilterOperator.LessThan, RequireScalar(columnId, op, value));
            case "lte":
            case "lessthanorequal":
            case "lessthanorequalto":
                return new FilterLeaf(columnId, FilterOperator.LessThanOrEqual, RequireScalar(columnId, op, value));
            case "between":
            case "innumberrange":
            case "indaterange":
            case "betweeninclusive":
                return MapBetween(columnId, value);
            case "in":
            case "arrincludes":
            case "arrincludessome":
            case "arrincludesall":
                return MapIn(columnId, value);
            case "isnull":
            case "blank":
            case "empty":
            case "isempty":
                return new FilterLeaf(columnId, FilterOperator.IsNull, null);
            case "notnull":
            case "notblank":
            case "notempty":
            case "isnotempty":
                return new FilterNot(new FilterLeaf(columnId, FilterOperator.IsNull, null));
            default:
                throw new AdapterBindException(
                    $"Unknown TanStack column filter op '{op}' on column '{columnId}'.");
        }
    }

    /// <summary>Both bounds are required. A missing bound is a bind failure, matching AG Grid <c>inRange</c>.</summary>
    private static FilterNode MapBetween(string columnId, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var hasMin = TryGet(value, "min", out var min);
            var hasMax = TryGet(value, "max", out var max);
            RejectUnknownMembers(columnId, value, "min", "max");
            if (!hasMin || !hasMax || IsAbsentBound(min) || IsAbsentBound(max))
            {
                throw new AdapterBindException(
                    $"The TanStack 'between' filter on column '{columnId}' requires both bounds.");
            }

            return new FilterLeaf(columnId, FilterOperator.Between, new List<object?> { Scalar(min), Scalar(max) });
        }

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
        {
            throw new AdapterBindException(
                $"The TanStack 'between' filter on column '{columnId}' requires a two-element [min, max] value.");
        }

        var lo = value[0];
        var hi = value[1];
        if (IsAbsentBound(lo) || IsAbsentBound(hi)
            || lo.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            || hi.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            throw new AdapterBindException(
                $"The TanStack 'between' filter on column '{columnId}' requires both bounds.");
        }

        return new FilterLeaf(columnId, FilterOperator.Between, new List<object?> { Scalar(lo), Scalar(hi) });
    }

    /// <summary>Explicit <c>in</c> requires an array. An empty array is a bind failure (the caller asked for a set).</summary>
    private static FilterNode MapIn(string columnId, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new AdapterBindException(
                $"The TanStack 'in' filter on column '{columnId}' requires an array value.");
        }

        if (value.GetArrayLength() == 0)
        {
            throw new AdapterBindException(
                $"The TanStack 'in' filter on column '{columnId}' requires at least one value.");
        }

        var node = ParseArray(columnId, value);
        return node ?? throw new AdapterBindException(
            $"The TanStack 'in' filter on column '{columnId}' requires at least one value.");
    }

    /// <summary>
    /// Maps <c>{ min, max }</c>. Either side may be absent, null, or <c>""</c> (TanStack
    /// <c>inNumberRange</c> treats an empty input as unbounded). Both absent → no leaf. Both present →
    /// <see cref="FilterOperator.Between"/>.
    /// </summary>
    private static FilterNode? MapRange(
        string columnId,
        JsonElement min,
        JsonElement max,
        bool hasMin,
        bool hasMax)
    {
        var minPresent = hasMin && !IsAbsentBound(min);
        var maxPresent = hasMax && !IsAbsentBound(max);

        if (minPresent && min.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            throw new AdapterBindException(
                $"The TanStack range filter on column '{columnId}' has a non-scalar 'min'.");
        }

        if (maxPresent && max.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            throw new AdapterBindException(
                $"The TanStack range filter on column '{columnId}' has a non-scalar 'max'.");
        }

        var minBound = minPresent ? Scalar(min) : null;
        var maxBound = maxPresent ? Scalar(max) : null;

        if (minPresent && maxPresent)
        {
            return new FilterLeaf(columnId, FilterOperator.Between, new List<object?> { minBound, maxBound });
        }

        if (minPresent)
        {
            return new FilterLeaf(columnId, FilterOperator.GreaterThanOrEqual, minBound);
        }

        if (maxPresent)
        {
            return new FilterLeaf(columnId, FilterOperator.LessThanOrEqual, maxBound);
        }

        return null;
    }

    /// <summary>A scalar is required. Blank strings and missing/null values are a bind failure for an explicit op.</summary>
    private static object? RequireScalar(string columnId, string op, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            || value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
        {
            throw new AdapterBindException(
                $"The TanStack '{op}' filter on column '{columnId}' requires a scalar value.");
        }

        return Scalar(value);
    }

    /// <summary>Null, undefined, or a blank string is an absent range bound.</summary>
    private static bool IsAbsentBound(JsonElement value) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
        || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

    /// <summary>
    /// Converts a scalar <see cref="JsonElement"/> the way the AG Grid parser does: strings stay strings,
    /// numbers pass as their raw text (the engine coerces to the field type), booleans pass through.
    /// </summary>
    private static object? Scalar(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => throw new AdapterBindException("A TanStack filter value was not a scalar."),
    };

    /// <summary>Reads a property by ordinal-ignore-case name.</summary>
    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>Rejects object members outside the allowed set so an unrecognized shape cannot be dropped.</summary>
    private static void RejectUnknownMembers(string columnId, JsonElement obj, params string[] allowed)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            var known = false;
            foreach (var name in allowed)
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                throw new AdapterBindException(
                    $"The TanStack column filter on '{columnId}' has an unexpected property '{prop.Name}'.");
            }
        }
    }
}
