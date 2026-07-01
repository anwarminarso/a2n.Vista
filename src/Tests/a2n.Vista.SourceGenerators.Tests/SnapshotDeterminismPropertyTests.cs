// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 2 (M10, D118) compiled execution-plan emitter.
//
// Feature: style-b-executable, Property 8: For any fixed set of input view sources, repeated runs of
// the generator SHALL produce byte-identical generated execution-plan and member-access output.
//
// Validates: Requirements 10.1
//
// Strategy: a CsCheck generator produces typed Style B view-source variations (varying field
// names/types/counts, masked vs not, single vs composite key, plus filterable/sortable opt-ins). For
// each generated source the generator is driven TWICE through CSharpGeneratorDriver on identical input
// (GeneratorTestHarness.RunWithExecutionPlanSupport, which makes ICompiledViewExecutionPlan present so
// the <View>_VistaExecutionPlan.g.cs plan — projection + member-access map — is emitted). The property
// asserts the two runs yield a byte-identical full-output snapshot. Minimum 100 generated cases (CsCheck
// default iter = 100). PBT library: CsCheck (imperative Sample, pairs cleanly with TUnit [Test]).

using System;
using System.Linq;
using System.Text;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class SnapshotDeterminismPropertyTests
{
    // Pool of valid, distinct, non-keyword PascalCase identifiers used for projected fields. A spec
    // takes the first N of these, so both the field count AND the resulting field-name set vary.
    private static readonly string[] FieldNamePool =
    {
        "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel",
    };

    // Candidate CLR field types. IsString marks the only maskable shape used here (string masker => "***").
    private static readonly (string Keyword, bool IsString)[] FieldTypes =
    {
        ("int", false),
        ("long", false),
        ("bool", false),
        ("decimal", false),
        ("global::System.Guid", false),
        ("global::System.DateTime", false),
        ("string", true),
    };

    /// <summary>One generated field: its CLR type plus filter/sort opt-ins.</summary>
    private sealed record FieldSpec(int TypeIndex, bool Filterable, bool Sortable);

    /// <summary>A full generated typed Style B view shape.</summary>
    private sealed record ViewSpec(FieldSpec[] Fields, bool CompositeKey, bool Mask);

    private static readonly Gen<FieldSpec> GenField =
        Gen.Select(
            Gen.Int[0, FieldTypes.Length - 1],
            Gen.Bool,
            Gen.Bool,
            (typeIndex, filterable, sortable) => new FieldSpec(typeIndex, filterable, sortable));

    private static readonly Gen<ViewSpec> GenViewSpec =
        from count in Gen.Int[2, FieldNamePool.Length]
        from fields in GenField.Array[count]
        from composite in Gen.Bool
        from mask in Gen.Bool
        select new ViewSpec(fields, composite, mask);

    [Test]
    public void Generator_Output_Is_Byte_Identical_Across_Repeated_Runs()
    {
        // Feature: style-b-executable, Property 8: For any fixed set of input view sources, repeated runs
        // of the generator SHALL produce byte-identical generated execution-plan and member-access output.
        GenViewSpec.Sample(
            spec =>
            {
                var source = RenderViewSource(spec);

                var firstRun = GeneratorTestHarness.RunWithExecutionPlanSupport(source)
                    .AllGeneratedSourcesSnapshot();
                var secondRun = GeneratorTestHarness.RunWithExecutionPlanSupport(source)
                    .AllGeneratedSourcesSnapshot();

                // Byte-identical output on identical input is the determinism guarantee (R10.1).
                return string.Equals(firstRun, secondRun, StringComparison.Ordinal);
            },
            iter: 100,
            // On failure, print the exact view source that broke determinism for a reproducible example.
            print: RenderViewSource);
    }

    /// <summary>
    /// Renders a compilable typed Style B view (source entity + projected row + view with a
    /// member-initialization projection the Phase 2 emitter can analyze) from a <see cref="ViewSpec"/>.
    /// </summary>
    private static string RenderViewSource(ViewSpec spec)
    {
        var names = FieldNamePool.Take(spec.Fields.Length).ToArray();
        var keyCount = spec.CompositeKey ? 2 : 1;

        // First key field index that is also a string (only string fields are masked); mask is applied to
        // the first NON-key string field so the masked field is distinct from the key (D95 intent).
        var maskIndex = -1;
        if (spec.Mask)
        {
            for (var i = keyCount; i < spec.Fields.Length; i++)
            {
                if (FieldTypes[spec.Fields[i].TypeIndex].IsString)
                {
                    maskIndex = i;
                    break;
                }
            }
        }

        var sb = new StringBuilder();
        sb.Append("namespace App\n{\n");

        // Source entity.
        sb.Append("    public sealed class GenSource\n    {\n");
        AppendProperties(sb, names, spec.Fields);
        sb.Append("    }\n\n");

        // Projected row.
        sb.Append("    public sealed class GenRow\n    {\n");
        AppendProperties(sb, names, spec.Fields);
        sb.Append("    }\n\n");

        // View with the analyzable member-init projection.
        sb.Append("    public partial class GenView : a2n.Vista.Authoring.View<GenRow>\n    {\n");
        sb.Append("        public void Configure(a2n.Vista.Authoring.IViewBuilder<GenRow> builder)\n");
        sb.Append("            => builder\n");

        var projectionBindings = string.Join(", ", names.Select(n => $"{n} = src.{n}"));
        sb.Append($"                .From<GenSource>(src => new GenRow {{ {projectionBindings} }})\n");

        // Key: composite uses .Key(...) over the first two fields; single uses .Field(...).PrimaryKey().
        if (spec.CompositeKey)
        {
            var keyArgs = string.Join(", ", names.Take(2).Select(n => $"x => x.{n}"));
            sb.Append($"                .Key({keyArgs})\n");
        }
        else
        {
            sb.Append($"                .Field(x => x.{names[0]}, f => f.PrimaryKey())\n");
        }

        // Filter/sort opt-ins for non-key, non-masked fields (drives member-access map emission).
        for (var i = keyCount; i < spec.Fields.Length; i++)
        {
            if (i == maskIndex)
            {
                continue;
            }

            var field = spec.Fields[i];
            if (!field.Filterable && !field.Sortable)
            {
                continue;
            }

            var config = new StringBuilder("f => f");
            if (field.Filterable)
            {
                config.Append(".Filterable()");
            }

            if (field.Sortable)
            {
                config.Append(".Sortable()");
            }

            sb.Append($"                .Field(x => x.{names[i]}, {config})\n");
        }

        // Optional masked string field (string masker returns a constant).
        if (maskIndex >= 0)
        {
            sb.Append($"                .MaskField(x => x.{names[maskIndex]}, services => true, value => \"***\")\n");
        }

        sb.Append("                ;\n");
        sb.Append("    }\n}\n");

        return sb.ToString();
    }

    private static void AppendProperties(StringBuilder sb, string[] names, FieldSpec[] fields)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var (keyword, isString) = FieldTypes[fields[i].TypeIndex];
            var initializer = isString ? " = string.Empty;" : string.Empty;
            sb.Append($"        public {keyword} {names[i]} {{ get; set; }}{initializer}\n");
        }
    }
}
