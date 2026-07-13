namespace a2n.Vista.OpenApi.Model;

/// <summary>
/// Helpers for constructing the ordinal-ordered collections the OpenAPI object model expects, so that the
/// serialized output is byte-for-byte deterministic and independent of insertion / enumeration order
/// (Requirement 9). A plain <see cref="SortedDictionary{TKey,TValue}"/> would use the culture-sensitive
/// default string comparer; these helpers pin <see cref="StringComparer.Ordinal"/> so ordering is stable
/// across cultures and processes.
/// </summary>
public static class OpenApiCollections
{
    /// <summary>
    /// Creates an empty ordinal-ordered string-keyed map. Keys always enumerate in
    /// <see cref="StringComparer.Ordinal"/> order regardless of insertion order.
    /// </summary>
    public static SortedDictionary<string, TValue> CreateMap<TValue>() =>
        new(StringComparer.Ordinal);

    /// <summary>
    /// Creates an ordinal-ordered string-keyed map seeded from <paramref name="source"/>. The result
    /// enumerates in ordinal key order regardless of the source's enumeration order.
    /// </summary>
    public static SortedDictionary<string, TValue> ToOrdinalMap<TValue>(
        IEnumerable<KeyValuePair<string, TValue>> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var map = new SortedDictionary<string, TValue>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            map[pair.Key] = pair.Value;
        }

        return map;
    }
}
