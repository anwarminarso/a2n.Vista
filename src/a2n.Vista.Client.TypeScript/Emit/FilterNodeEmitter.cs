using System.Text;
using a2n.Vista.Client.TypeScript.Modeling;

namespace a2n.Vista.Client.TypeScript.Emit;

/// <summary>
/// Emits <c>filter-node.ts</c> (task 9.3; design "Design note — <c>FilterNode</c> is discriminated by member
/// presence (no <c>discriminator</c>)"): the <c>FilterOperator</c> literal union, the four variant interfaces
/// (<c>FilterLeaf</c>/<c>FilterAnd</c>/<c>FilterOr</c>/<c>FilterNot</c>) with their recursive
/// <c>FilterNode[]</c>/<c>FilterNode</c> edges, and the presence-discriminated <c>FilterNode</c> union itself.
/// </summary>
/// <remarks>
/// <para>
/// The file is <b>self-contained</b>: <c>FilterOperator</c>, the four variants, and <c>FilterNode</c> are all
/// declared here, and the recursive references resolve within the file, so it needs no imports. The
/// downstream modules (<c>types.ts</c>, task 9.2; the per-view clients, task 10.6) consume the family via
/// <c>import type { FilterNode, FilterOperator } from "./filter-node";</c>, so the exported names are kept
/// exactly <c>FilterNode</c>, <c>FilterOperator</c>, <c>FilterLeaf</c>, <c>FilterAnd</c>, <c>FilterOr</c>,
/// and <c>FilterNot</c>.
/// </para>
/// <para>
/// <b>Ordering choice (Requirement 9.2 vs the design).</b> The general determinism rule orders top-level
/// declarations by ordinal name (<see cref="DeterministicOrder"/>). Here both the <c>FilterOperator</c>
/// literal union and the <c>FilterNode</c> union are semantically <b>document-order</b>-defined (the task
/// states "in document order"), and the design lists the four interfaces in that same document order too.
/// Since the four variants are a fixed, closed set, the output is deterministic either way; this emitter
/// therefore uses the model's document order (<see cref="FilterNodeModel.MemberTypeNames"/> and
/// <see cref="FilterNodeModel.OperatorLiterals"/>) for both the interface declarations and the union so the
/// emitted file matches the design byte-for-byte. The members <em>within</em> each interface are already
/// pre-sorted by ordinal name in the model, preserving the per-member determinism guarantee.
/// </para>
/// <para>
/// The content is produced purely from the in-memory <see cref="FilterNodeModel"/> (no I/O, no clock, no
/// environment), with a fixed two-space indent, <c>\n</c> line terminators, a blank line between
/// declarations, and a single trailing newline, so the output is byte-identical on every run and OS
/// (Requirement 9.1). Given a valid model this emitter cannot fail, so it returns the
/// <see cref="GeneratedFile"/> directly.
/// </para>
/// </remarks>
public static class FilterNodeEmitter
{
    /// <summary>The output-directory-relative path of the emitted file (forward-slash separators).</summary>
    public const string RelativePath = "filter-node.ts";

    private const string Indent = "  ";

    /// <summary>
    /// Builds the <see cref="GeneratedFile"/> for <c>filter-node.ts</c> from the supplied
    /// <see cref="FilterNodeModel"/>.
    /// </summary>
    /// <param name="model">The presence-discriminated <c>FilterNode</c> family model.</param>
    /// <returns>The emitted <c>filter-node.ts</c> file.</returns>
    public static GeneratedFile Emit(FilterNodeModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // A by-name lookup so the interface declarations follow MemberTypeNames' document order regardless
        // of how the model happens to order its Members list.
        var variantsByName = model.Members.ToDictionary(variant => variant.Name, StringComparer.Ordinal);

        var builder = new StringBuilder();

        AppendHeader(builder);

        // 1. The FilterOperator literal union, in document order (Requirement 2.3 / 3.2).
        builder.Append("export type ")
            .Append(model.OperatorUnionName)
            .Append(" = ")
            .Append(model.OperatorUnion.Render())
            .Append(";\n");

        // 2. Each variant interface, in document order (Requirement 2.2). Members are already pre-sorted by
        //    ordinal name in the model.
        foreach (var memberName in model.MemberTypeNames)
        {
            builder.Append('\n');
            AppendInterface(builder, variantsByName[memberName]);
        }

        // 3. The presence-discriminated FilterNode union, members in document order (Requirement 2.2 / 2.3).
        builder.Append('\n')
            .Append("export type ")
            .Append(model.UnionName)
            .Append(" = ")
            .Append(string.Join(" | ", model.MemberTypeNames))
            .Append(";\n");

        return new GeneratedFile(RelativePath, builder.ToString());
    }

    private static void AppendHeader(StringBuilder builder)
    {
        builder.Append("// filter-node.ts\n");
        builder.Append("// The presence-discriminated FilterNode filter tree (no discriminator property).\n");
        builder.Append("//\n");
        builder.Append("// A FilterNode value narrows to exactly one variant by which member is present:\n");
        builder.Append("// FilterLeaf (field + op), FilterAnd (and), FilterOr (or), FilterNot (not). The tree\n");
        builder.Append("// is recursive and self-contained, so this module needs no imports.\n");
        builder.Append('\n');
    }

    private static void AppendInterface(StringBuilder builder, FilterVariant variant)
    {
        builder.Append("export interface ").Append(variant.Name);

        if (variant.Properties.Count == 0)
        {
            builder.Append(" {}\n");
            return;
        }

        builder.Append(" {\n");
        foreach (var property in variant.Properties)
        {
            builder.Append(Indent).Append(property.Render()).Append('\n');
        }

        builder.Append("}\n");
    }
}
