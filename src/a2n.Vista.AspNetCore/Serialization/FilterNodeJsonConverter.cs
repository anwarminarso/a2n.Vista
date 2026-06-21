using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using a2n.Vista.Contracts;

namespace a2n.Vista.AspNetCore.Serialization;

/// <summary>
/// System.Text.Json converter for the polymorphic <see cref="FilterNode"/> tree (Decision Log D110).
/// The wire shape is discriminated by member presence:
/// <list type="bullet">
///   <item><description><c>{ "and": [ ... ] }</c> → <see cref="FilterAnd"/></description></item>
///   <item><description><c>{ "or": [ ... ] }</c> → <see cref="FilterOr"/></description></item>
///   <item><description><c>{ "not": { ... } }</c> → <see cref="FilterNot"/></description></item>
///   <item><description><c>{ "field": "...", "op": "...", "value": ... }</c> → <see cref="FilterLeaf"/></description></item>
/// </list>
/// Leaf values are read as neutral CLR values (string/long/double/bool/list/null); the engine coerces
/// them to the field's CLR type. The converter is serializer-neutral at the Core boundary: no
/// System.Text.Json type leaks into Core.
/// </summary>
public sealed class FilterNodeJsonConverter : JsonConverter<FilterNode>
{
    /// <inheritdoc />
    public override FilterNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return ReadNode(document.RootElement);
    }

    private static FilterNode ReadNode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"A filter node must be a JSON object, but was '{element.ValueKind}'.");
        }

        if (element.TryGetProperty("and", out var andArray))
        {
            return new FilterAnd(ReadChildren(andArray));
        }

        if (element.TryGetProperty("or", out var orArray))
        {
            return new FilterOr(ReadChildren(orArray));
        }

        if (element.TryGetProperty("not", out var notChild))
        {
            return new FilterNot(ReadNode(notChild));
        }

        if (element.TryGetProperty("field", out var fieldElement))
        {
            var field = fieldElement.GetString()
                ?? throw new JsonException("A filter leaf 'field' must be a string.");
            var op = ReadOperator(element);
            var value = element.TryGetProperty("value", out var valueElement) ? ReadValue(valueElement) : null;
            return new FilterLeaf(field, op, value);
        }

        throw new JsonException("A filter node must be one of: 'and', 'or', 'not', or a leaf with 'field'/'op'.");
    }

    private static IReadOnlyList<FilterNode> ReadChildren(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("'and'/'or' must be a JSON array of filter nodes.");
        }

        var children = new List<FilterNode>(array.GetArrayLength());
        foreach (var child in array.EnumerateArray())
        {
            children.Add(ReadNode(child));
        }

        return children;
    }

    private static FilterOperator ReadOperator(JsonElement element)
    {
        if (!element.TryGetProperty("op", out var opElement) || opElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("A filter leaf must carry a string 'op' operator.");
        }

        var text = opElement.GetString()!;
        if (!Enum.TryParse<FilterOperator>(text, ignoreCase: true, out var op) || op == FilterOperator.None)
        {
            throw new JsonException($"Unknown filter operator '{text}'.");
        }

        return op;
    }

    private static object? ReadValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        JsonValueKind.Array => ReadValueArray(value),
        _ => throw new JsonException($"Unsupported filter value kind '{value.ValueKind}'."),
    };

    private static List<object?> ReadValueArray(JsonElement array)
    {
        var items = new List<object?>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            items.Add(ReadValue(item));
        }

        return items;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, FilterNode value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        switch (value)
        {
            case FilterLeaf leaf:
                writer.WriteStartObject();
                writer.WriteString("field", leaf.Field);
                writer.WriteString("op", leaf.Op.ToString());
                writer.WritePropertyName("value");
                JsonSerializer.Serialize(writer, leaf.Value, options);
                writer.WriteEndObject();
                break;

            case FilterAnd and:
                WriteChildren(writer, "and", and.Children, options);
                break;

            case FilterOr or:
                WriteChildren(writer, "or", or.Children, options);
                break;

            case FilterNot not:
                writer.WriteStartObject();
                writer.WritePropertyName("not");
                Write(writer, not.Child, options);
                writer.WriteEndObject();
                break;

            default:
                throw new JsonException($"Unsupported filter node type '{value.GetType().Name}'.");
        }
    }

    private void WriteChildren(Utf8JsonWriter writer, string name, IReadOnlyList<FilterNode> children, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var child in children)
        {
            Write(writer, child, options);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
