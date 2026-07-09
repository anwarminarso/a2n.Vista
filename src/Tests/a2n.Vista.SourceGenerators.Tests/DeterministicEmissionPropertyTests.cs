// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 3 (M9, D121/D122) WriteMapperGenerator's write-mapper source
// emitter (task 6.4).
//
// Feature: source-generator-write-mapper, Property 3: For any writable view input, two runs of the
// generator over the same input produce byte-identical write-mapper source, with exactly one assignment
// per safe mapping and the assignments emitted in the same order as their textual MapWritable
// declaration.
//
// Validates: Requirements 3.1, 2.2
//
// Strategy: a CsCheck generator produces random typed Style B WRITABLE view inputs — a CRUD facet with
// 1..6 MapWritable mappings whose targets are all distinct, non-key, non-token Scalar_Members (so the
// emitter's "safe subset" equals the full whitelist and the view actually emits a mapper rather than
// erroring on VISTA0030/0031/0032). Each mapping's shared TProp is drawn from a pool of scalar types
// (value types incl. a nullable, string, byte[]), and the mappings are AUTHORED in a random permutation
// of the field indices so declaration order is genuinely shuffled and distinct from any natural sort.
//
// The generator is driven TWICE through CSharpGeneratorDriver (WriteMapperGeneratorTestHarness.Run) over
// the SAME source. The property asserts:
//   * both runs emit the view's <View>_VistaWriteMapper.g.cs source;
//   * the two emitted sources are byte-identical (determinism — R3.1);
//   * the emitted body carries exactly one `e.<EntityMember> = m.<CrudMember>;` assignment per safe
//     mapping (no duplicates, no drops — R3.1);
//   * the assignment sequence is exactly the AUTHORED MapWritable declaration order (R2.2).
//
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck (imperative Sample,
// pairs cleanly with TUnit [Test]). Only the generated source TEXT is inspected; no generated
// [ModuleInitializer] is executed (per the test-design guidance).

using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class DeterministicEmissionPropertyTests
{
    // Scalar CLR types the emitter classifies as Scalar_Member (value type with Nullable<T> unwrapped,
    // string, or byte[]). Each field's TCrud source member and TEntity target member share one of these
    // so the two-argument MapWritable<TProp> overload always resolves. All are non-navigation, so every
    // mapping is "safe" and survives the emitter's safe-subset filter.
    private static readonly (string Keyword, bool NeedsInit)[] ScalarTypes =
    {
        ("int", false),
        ("long", false),
        ("bool", false),
        ("decimal", false),
        ("double", false),
        ("int?", false),
        ("global::System.Guid", false),
        ("global::System.DateTime", false),
        ("string", true),
        ("byte[]", true),
    };

    /// <summary>One generated field: the scalar type shared by its source/target members.</summary>
    private sealed record FieldSpec(int TypeIndex);

    /// <summary>
    /// A full generated writable-view shape: the per-field scalar types plus the permutation that fixes
    /// the textual MapWritable declaration order (an index into <see cref="ViewSpec.Fields"/> per
    /// authored mapping).
    /// </summary>
    private sealed record ViewSpec(FieldSpec[] Fields, int[] DeclarationOrder);

    private static readonly Gen<FieldSpec> GenField =
        Gen.Int[0, ScalarTypes.Length - 1].Select(static typeIndex => new FieldSpec(typeIndex));

    private static readonly Gen<ViewSpec> GenViewSpec =
        from count in Gen.Int[1, 6]
        from fields in GenField.Array[count]
        // Shuffle the field indices to author the mappings in a non-trivial declaration order, so the
        // "assignments in declaration order" assertion is not satisfied by accident of natural sorting.
        from order in Gen.Shuffle(Enumerable.Range(0, count).ToArray())
        select new ViewSpec(fields, order);

    // Matches each emitted direct assignment `e.<EntityMember> = m.<CrudMember>;`, so the emitted pair
    // sequence can be recovered from the generated source in emission (= declaration) order.
    private static readonly Regex AssignmentPattern =
        new(@"e\.(?<entity>\w+)\s*=\s*m\.(?<crud>\w+);", RegexOptions.Compiled);

    [Test]
    public void Emission_Is_Byte_Identical_And_One_Ordered_Assignment_Per_Safe_Mapping()
    {
        // Feature: source-generator-write-mapper, Property 3: For any writable view input, two runs of
        // the generator over the same input produce byte-identical write-mapper source, with exactly one
        // assignment per safe mapping and the assignments emitted in the same order as their textual
        // MapWritable declaration.
        GenViewSpec.Sample(
            spec =>
            {
                var source = RenderViewSource(spec);

                // Drive the generator twice over identical input.
                var firstRun = WriteMapperGeneratorTestHarness.Run(source);
                var secondRun = WriteMapperGeneratorTestHarness.Run(source);

                // Both runs must emit the write mapper (the view is analyzable and all-safe).
                if (!firstRun.HasGeneratedSourceContaining("GenView_VistaWriteMapper")
                    || !secondRun.HasGeneratedSourceContaining("GenView_VistaWriteMapper"))
                {
                    return false;
                }

                var firstSource = firstRun.GeneratedSourceContaining("GenView_VistaWriteMapper");
                var secondSource = secondRun.GeneratedSourceContaining("GenView_VistaWriteMapper");

                // R3.1: byte-identical emitted source across repeated generations for the same input.
                if (!string.Equals(firstSource, secondSource, StringComparison.Ordinal))
                {
                    return false;
                }

                // Recover the emitted assignments in emission order.
                var emitted = AssignmentPattern.Matches(firstSource)
                    .Select(match => (Entity: match.Groups["entity"].Value, Crud: match.Groups["crud"].Value))
                    .ToArray();

                // Every mapping is a distinct, non-key, non-token scalar, so the safe subset equals the
                // full whitelist: exactly one assignment per authored mapping (R3.1 — no duplicates/drops)
                // in the authored declaration order (R2.2).
                var expected = spec.DeclarationOrder
                    .Select(fieldIndex => (Entity: $"Em{fieldIndex}", Crud: $"Cm{fieldIndex}"))
                    .ToArray();

                return emitted.SequenceEqual(expected);
            },
            iter: 100,
            // On failure, print the exact view source that broke the property for a reproducible example.
            print: RenderViewSource);
    }

    /// <summary>
    /// Renders a compilable typed Style B writable view whose CRUD facet declares one MapWritable mapping
    /// per field, authored in <see cref="ViewSpec.DeclarationOrder"/>. The entity (<c>Source</c>) and
    /// CRUD contract (<c>WriteCrud</c>) each expose members <c>Em{i}</c> / <c>Cm{i}</c> of the field's
    /// shared scalar type — all non-key, non-token scalars, so every mapping is a "safe" assignment the
    /// emitter keeps. No key or concurrency token is declared, so the safe subset is the full whitelist.
    /// </summary>
    private static string RenderViewSource(ViewSpec spec)
    {
        var sb = new StringBuilder();
        sb.Append("namespace App\n{\n");

        // Entity (TEntity for CrudOn<Source>) — carries the Em{i} targets.
        sb.Append("    public sealed class Source\n    {\n");
        AppendMembers(sb, spec.Fields, "Em");
        sb.Append("    }\n\n");

        // TQuery row — minimal; the write mapper does not read it.
        sb.Append("    public sealed class Row { }\n\n");

        // TCrud write contract — carries the Cm{i} sources.
        sb.Append("    public sealed class WriteCrud\n    {\n");
        AppendMembers(sb, spec.Fields, "Cm");
        sb.Append("    }\n\n");

        // The writable view: CrudOn<Source>() then one MapWritable per field, in the authored order.
        sb.Append("    public partial class GenView : a2n.Vista.Authoring.View<Row, WriteCrud>\n    {\n");
        sb.Append("        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)\n");
        sb.Append("            => builder\n");
        sb.Append("                .CrudOn<Source>()\n");

        foreach (var fieldIndex in spec.DeclarationOrder)
        {
            sb.Append($"                .MapWritable(c => c.Cm{fieldIndex}, e => e.Em{fieldIndex})\n");
        }

        sb.Append("                ;\n");
        sb.Append("    }\n}\n");

        return sb.ToString();
    }

    private static void AppendMembers(StringBuilder sb, FieldSpec[] fields, string prefix)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            var (keyword, needsInit) = ScalarTypes[fields[i].TypeIndex];
            var initializer = needsInit
                ? (keyword == "string" ? " = string.Empty;" : " = global::System.Array.Empty<byte>();")
                : string.Empty;
            sb.Append($"        public {keyword} {prefix}{i} {{ get; set; }}{initializer}\n");
        }
    }
}
