using System;
using System.Collections;
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
        JsonValueKind.Number => value.TryGetInt64(out var l) ? (object)l : value.GetDouble(),
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
                WriteValue(writer, leaf.Value);
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

    /// <summary>
    /// Writes a neutral leaf value (string/integer/floating-point/decimal/bool/null/list) directly to the
    /// <see cref="Utf8JsonWriter"/> without reflection, so the converter is compatible with a source-gen
    /// <see cref="JsonSerializerContext"/>. The switch mirrors the neutral CLR value space produced by
    /// <c>ReadValue</c> (string, <see cref="long"/>, <see cref="double"/>, <see cref="bool"/>, null, and
    /// lists of those), and preserves byte-for-byte parity with the previous reflection-based
    /// <c>JsonSerializer.Serialize(writer, value, options)</c> call for those values.
    /// </summary>
    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;

            case string s:
                writer.WriteStringValue(s);
                break;

            case bool b:
                writer.WriteBooleanValue(b);
                break;

            // Signed/unsigned integral values that fit a 64-bit signed range.
            case long l:
                writer.WriteNumberValue(l);
                break;

            case int i:
                writer.WriteNumberValue(i);
                break;

            case short sh:
                writer.WriteNumberValue(sh);
                break;

            case sbyte sb:
                writer.WriteNumberValue(sb);
                break;

            case byte by:
                writer.WriteNumberValue(by);
                break;

            case ushort us:
                writer.WriteNumberValue(us);
                break;

            case uint ui:
                writer.WriteNumberValue(ui);
                break;

            case ulong ul:
                writer.WriteNumberValue(ul);
                break;

            // Floating-point and exact decimal values.
            case double d:
                writer.WriteNumberValue(d);
                break;

            case float f:
                writer.WriteNumberValue(f);
                break;

            case decimal m:
                writer.WriteNumberValue(m);
                break;

            // Lists/arrays of neutral values (e.g. the operands of an 'in' or 'between' leaf).
            case IEnumerable enumerable:
                writer.WriteStartArray();
                foreach (var item in enumerable)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;

            default:
                throw new JsonException(
                    $"Unsupported filter value type '{value.GetType().Name}'. Filter leaf values must be a " +
                    "string, integer, floating-point number, decimal, boolean, null, or a list of those.");
        }
    }
}
