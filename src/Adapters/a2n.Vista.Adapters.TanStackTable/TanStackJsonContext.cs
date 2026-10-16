using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace a2n.Vista.Adapters.TanStackTable;

/// <summary>
/// Source-generated <c>JsonSerializerContext</c> for the TanStack request POCOs and response, keeping
/// JSON parsing and response serialization AOT-clean (no reflection-based
/// <c>JsonSerializer.Deserialize</c>). Property-name matching is case-insensitive so the camelCase
/// table-state wire shape binds.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TanStackTableRequest))]
[JsonSerializable(typeof(TanStackPaginationState))]
[JsonSerializable(typeof(TanStackColumnSort))]
[JsonSerializable(typeof(List<TanStackColumnSort>))]
[JsonSerializable(typeof(TanStackColumnFilter))]
[JsonSerializable(typeof(List<TanStackColumnFilter>))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
[JsonSerializable(typeof(TanStackTableResponse))]
public sealed partial class TanStackJsonContext : JsonSerializerContext
{
}
