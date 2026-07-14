namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// A single TypeScript object-member declaration: a verbatim, case-sensitive property name (Requirement
/// 3.1), its <see cref="TsType"/>, and whether it is optional (the <c>?</c> modifier, Requirement 3.4).
/// Optionality lives here — at the property level — rather than on <see cref="TsType"/>, because an
/// optional member and a nullable type are distinct concerns (a member may be optional, nullable, both,
/// or neither).
/// </summary>
/// <param name="Name">The property name, used exactly and case-sensitively as the schema names it.</param>
/// <param name="Type">The property's mapped TypeScript type.</param>
/// <param name="Optional">Whether the property is optional (emits the trailing <c>?</c> modifier).</param>
public sealed record TsProperty(string Name, TsType Type, bool Optional)
{
    /// <summary>Renders this property as deterministic TypeScript source, e.g. <c>name?: string;</c>.</summary>
    public string Render()
    {
        var modifier = Optional ? "?" : string.Empty;
        return $"{Name}{modifier}: {Type.Render()};";
    }
}
