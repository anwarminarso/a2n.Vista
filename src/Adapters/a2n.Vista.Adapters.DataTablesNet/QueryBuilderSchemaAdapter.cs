using System;
using System.Collections.Generic;
using a2n.Vista.Adapters;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Adapters.DataTablesNet;

/// <summary>
/// Emits the jQuery-QueryBuilder metadata schema (<c>metadataQB</c>) from a view's
/// <see cref="ViewMetadata"/> (Decision Log D116, Spec 04 §8.2). The shape is DynData-compatible so an
/// existing jQuery-QueryBuilder client consumes it unchanged: <c>{ viewName, metaData[],
/// queryBuilderOptions: { filters[] } }</c>. Built as nested dictionaries with verbatim keys, so the
/// casing survives any serializer naming policy.
/// </summary>
public sealed class QueryBuilderSchemaAdapter : IViewMetadataAdapter
{
    /// <inheritdoc />
    public string Id => "querybuilder";

    /// <inheritdoc />
    public string? RouteSuffix => "querybuilder";

    /// <inheritdoc />
    public object BuildSchema(ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var metaData = new List<object?>();
        var filters = new List<object?>();

        foreach (var field in view.Fields)
        {
            if (!field.IsHidden)
            {
                metaData.Add(new Dictionary<string, object?>
                {
                    ["FieldName"] = field.Name,
                    ["FieldLabel"] = field.Label,
                    ["FieldType"] = TypeName(field.ClrType),
                    ["IsSearchable"] = field.IsSearchable,
                    ["IsOrderable"] = field.IsSortable,
                    ["IsPrimaryKey"] = field.IsPrimaryKey,
                });
            }

            // filters: only filterable fields; a hidden field only when it is a scopable lookup (D65).
            if (field.IsFilterable && (!field.IsHidden || field.IsScopable))
            {
                var (type, input) = TypeAndInput(field.ClrType);
                filters.Add(new Dictionary<string, object?>
                {
                    ["id"] = field.Name,
                    ["label"] = field.Label,
                    ["type"] = type,
                    ["input"] = input,
                    ["operators"] = Operators(field.AllowedOperators),
                });
            }
        }

        return new Dictionary<string, object?>
        {
            ["viewName"] = view.Name,
            ["metaData"] = metaData,
            ["queryBuilderOptions"] = new Dictionary<string, object?>
            {
                ["filters"] = filters,
            },
        };
    }

    /// <summary>Maps a field's allowed operators to jQuery-QueryBuilder operator ids (inverse of Spec 04 §8.1).</summary>
    private static List<string> Operators(FilterOperator allowed)
    {
        var ops = new List<string>();
        void Add(FilterOperator flag, string name)
        {
            if ((allowed & flag) == flag)
            {
                ops.Add(name);
            }
        }

        Add(FilterOperator.Equals, "equal");
        Add(FilterOperator.NotEquals, "not_equal");
        Add(FilterOperator.Contains, "contains");
        Add(FilterOperator.StartsWith, "begins_with");
        Add(FilterOperator.EndsWith, "ends_with");
        Add(FilterOperator.LessThan, "less");
        Add(FilterOperator.LessThanOrEqual, "less_or_equal");
        Add(FilterOperator.GreaterThan, "greater");
        Add(FilterOperator.GreaterThanOrEqual, "greater_or_equal");
        Add(FilterOperator.Between, "between");
        Add(FilterOperator.In, "in");
        Add(FilterOperator.IsNull, "is_empty");
        return ops;
    }

    private static string TypeName(Type clrType) => TypeAndInput(clrType).Type;

    private static (string Type, string Input) TypeAndInput(Type clrType)
    {
        var t = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (t == typeof(string))
        {
            return ("string", "text");
        }

        if (t == typeof(bool))
        {
            return ("boolean", "radio");
        }

        if (t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong))
        {
            return ("integer", "number");
        }

        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
        {
            return ("double", "number");
        }

        if (t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly))
        {
            return ("date", "text");
        }

        return ("string", "text");
    }
}
