using System.Collections.Generic;
using System.Text.Json;

namespace a2n.Vista.AspNetCore.Serialization;

/// <summary>
/// Converts a JSON <c>key</c> member from a Detail/Update/Delete request body into the Core-neutral key
/// shape the executor normalizes against <c>ViewMetadata.KeyFields</c> (Decision Log D109): a boxed
/// scalar for a single key, or an <see cref="IReadOnlyDictionary{TKey, TValue}"/> (field name → value)
/// for a composite key. No System.Text.Json type crosses into Core.
/// </summary>
public static class VistaKeyReader
{
    /// <summary>Reads the key element into a scalar or a name→value map.</summary>
    /// <param name="key">The JSON key element from the request body.</param>
    /// <returns>A boxed scalar, or an <see cref="IReadOnlyDictionary{TKey, TValue}"/> for a composite key.</returns>
    /// <exception cref="JsonException">The key is null, an array, or otherwise unsupported.</exception>
    public static object Read(JsonElement key)
    {
        if (key.ValueKind == JsonValueKind.Object)
        {
            var map = new Dictionary<string, object?>(System.StringComparer.Ordinal);
            foreach (var member in key.EnumerateObject())
            {
                map[member.Name] = ReadScalar(member.Value);
            }

            return map;
        }

        return ReadScalar(key)
            ?? throw new JsonException("A 'key' value must not be null.");
    }

    private static object? ReadScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        _ => throw new JsonException($"A key value must be a scalar (string/number/boolean), but was '{value.ValueKind}'."),
    };
}
