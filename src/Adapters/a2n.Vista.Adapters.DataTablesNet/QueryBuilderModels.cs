using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace a2n.Vista.Adapters.DataTablesNet;

/// <summary>
/// A jQuery-QueryBuilder node: either a <b>group</b> (<see cref="Condition"/> + <see cref="Rules"/>) or a
/// <b>rule</b> (<see cref="Field"/>/<see cref="Id"/> + <see cref="Operator"/> + <see cref="Value"/>).
/// A node is a group when <see cref="Rules"/> is non-null, otherwise a rule. Deserialized with the
/// source-generated <see cref="DataTablesJsonContext"/> (AOT-clean).
/// </summary>
public sealed class QbNode
{
    /// <summary>Group combinator (<c>AND</c>/<c>OR</c>); null for a rule.</summary>
    public string? Condition { get; set; }

    /// <summary>Whether to negate the group; ignored for a rule.</summary>
    public bool Not { get; set; }

    /// <summary>The child nodes when this is a group; null for a rule.</summary>
    public List<QbNode>? Rules { get; set; }

    /// <summary>The QueryBuilder field id; used when <see cref="Field"/> is absent.</summary>
    public string? Id { get; set; }

    /// <summary>The field name a rule applies to.</summary>
    public string? Field { get; set; }

    /// <summary>The QueryBuilder operator (<c>equal</c>, <c>contains</c>, <c>between</c>, …).</summary>
    public string? Operator { get; set; }

    /// <summary>The rule value (scalar or array).</summary>
    public JsonElement Value { get; set; }
}

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the adapter's parsed shapes (<c>jsonQB</c>
/// QueryBuilder nodes and the <c>externalFilter</c> object), keeping JSON parsing AOT-clean (Spec 04 §9).
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(QbNode))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public sealed partial class DataTablesJsonContext : JsonSerializerContext
{
}
