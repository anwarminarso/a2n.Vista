namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// A TypeScript object <em>type declaration</em> — an <c>export interface</c> with a verbatim name, an
/// optional ordered list of generic type parameters, and an ordered list of <see cref="TsProperty"/>
/// members. Where <see cref="TsType"/> models a type <em>expression</em> (a reference/primitive/union used
/// in a position), <see cref="TsTypeDecl"/> models the top-level <em>declaration</em> that is emitted once
/// and referenced by name from every use (Requirement 2.5).
/// </summary>
/// <remarks>
/// <para>
/// This is the shape the design's <c>ClientModel.Types</c> collection holds: the fixed Vista envelopes, the
/// <c>FilterNode</c> family variants, <c>ProblemDetails</c>, and each per-view <c>TRow</c>/<c>TCrud</c> DTO
/// (design "The <c>ClientModel</c> IR"). The generic row-parameterized envelopes
/// (<c>ViewListResult&lt;TRow&gt;</c>/<c>PagedResult&lt;TRow&gt;</c>) are also expressible here via
/// <see cref="TypeParameters"/>, though they are held separately in the IR.
/// </para>
/// <para>
/// <see cref="Render"/> is a pure function of the value, producing deterministic TypeScript source with a
/// fixed two-space indent and <c>\n</c> line terminators (Requirement 9). Members are emitted in the order
/// they are stored; callers that require the deterministic by-name member order (Requirement 9.2) pre-sort
/// the members before constructing the declaration (as <see cref="DtoModelBuilder"/> does).
/// </para>
/// </remarks>
/// <param name="Name">The declared interface name, used verbatim (e.g. <c>CustomerRow</c>).</param>
/// <param name="Members">The object members, in the order they are to be emitted.</param>
/// <param name="TypeParameters">
/// The generic type parameters in declaration order (e.g. <c>["TRow"]</c> for <c>PagedResult&lt;TRow&gt;</c>);
/// empty for a plain, non-generic declaration such as a per-view DTO.
/// </param>
public sealed record TsTypeDecl(
    string Name,
    IReadOnlyList<TsProperty> Members,
    IReadOnlyList<string> TypeParameters)
{
    /// <summary>The fixed indent applied to each member line — two spaces, matching the emitter convention.</summary>
    private const string MemberIndent = "  ";

    /// <summary>The fixed line terminator for emitted source (Requirement 9.1).</summary>
    private const string NewLine = "\n";

    /// <summary>Creates a non-generic interface declaration.</summary>
    /// <param name="name">The declared interface name, used verbatim.</param>
    /// <param name="members">The object members, in the order they are to be emitted.</param>
    public TsTypeDecl(string name, IReadOnlyList<TsProperty> members)
        : this(name, members, Array.Empty<string>())
    {
    }

    /// <summary>
    /// Renders this declaration as deterministic TypeScript source, e.g.
    /// <code>
    /// export interface CustomerRow {
    ///   companyName: string;
    ///   customerId: string;
    /// }
    /// </code>
    /// An interface with no members renders as <c>export interface Name {}</c>.
    /// </summary>
    public string Render()
    {
        var typeParameters = TypeParameters.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", TypeParameters)}>";

        if (Members.Count == 0)
        {
            return $"export interface {Name}{typeParameters} {{}}";
        }

        var builder = new System.Text.StringBuilder();
        builder.Append("export interface ").Append(Name).Append(typeParameters).Append(" {").Append(NewLine);

        foreach (var member in Members)
        {
            builder.Append(MemberIndent).Append(member.Render()).Append(NewLine);
        }

        builder.Append('}');
        return builder.ToString();
    }
}
