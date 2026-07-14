using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace a2n.Vista.Adapters.AgGrid;

/// <summary>
/// Source-generated JsonSerializerContext for the AG Grid request POCOs and response, keeping JSON parsing
/// and response serialization AOT-clean (no reflection-based JsonSerializer.Deserialize). Property-name
/// matching is case-insensitive to accept the AG Grid camelCase wire shape.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AgGridRowsRequest))]
[JsonSerializable(typeof(AgGridSortModel))]
[JsonSerializable(typeof(List<AgGridSortModel>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(AgGridRowsResponse))]
public sealed partial class AgGridJsonContext : JsonSerializerContext
{
}
