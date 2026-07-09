// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 3 (M9, D121/D122) WriteMapperGenerator's diagnostic descriptor
// contract (task 4.4).
//
// Feature: source-generator-write-mapper, Property 7: For any view input, every diagnostic the generator
// emits has an identifier matching the VISTA prefix immediately followed by exactly four decimal digits,
// and the category a2n.Vista.SourceGenerators.
//
// Validates: Requirements 9.4
//
// Strategy: the diagnostic contract must hold for EVERY view the generator sees, so a CsCheck generator
// spans the full recognition/diagnostic matrix — candidate vs non-candidate, analyzable vs unanalyzable,
// and each mass-assignment error shape — then drives the WriteMapperGenerator via CSharpGeneratorDriver
// (WriteMapperGeneratorTestHarness) and asserts EVERY emitted diagnostic conforms to the id regex
// `^VISTA[0-9]{4}$` and the `a2n.Vista.SourceGenerators` category (R9.4). The generated shapes cover:
//
//   * WellFormed   — analyzable candidate with safe scalar mappings (emits a mapper, NO diagnostics);
//   * Abstract / NonPartial / ReadOnly / NoFacet — non-candidate shapes dropped silently (NO diagnostic);
//   * ObjectCrud   — candidate with no named TCrud, skipped silently (NO diagnostic, R1.4);
//   * Unanalyzable — a non-simple MapWritable selector → VISTA0033 warning (R8);
//   * ZeroMapping  — CRUD facet with no MapWritable → VISTA0030 error (R9.1);
//   * NonScalar    — a navigation target → VISTA0031 error (R9.2);
//   * KeyTarget    — a mapping onto a declared key member → VISTA0032 error (R9.3);
//   * TokenTarget  — a mapping onto the concurrency token → VISTA0032 error (R9.3).
//
// Each shape is rendered with a random class-name suffix (and the WellFormed shape with a random safe
// mapping count) so the generator sees varied inputs, not a fixed corpus. The property is universal
// conformance: whatever the shape emits — one warning, several errors, or nothing — every VISTA
// diagnostic that comes out must satisfy the id/category contract.
//
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck (imperative Sample,
// pairs cleanly with TUnit [Test]). Only the run diagnostics are inspected; no generated
// [ModuleInitializer] is executed.

using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class DiagnosticConformancePropertyTests
{
    /// <summary>The recognition/diagnostic matrix the property spans.</summary>
    private enum ViewShape
    {
        WellFormed,
        Abstract,
        NonPartial,
        ReadOnly,
        NoFacet,
        ObjectCrud,
        Unanalyzable,
        ZeroMapping,
        NonScalar,
        KeyTarget,
        TokenTarget,
    }

    // Shared entity / query-row / write-contract types every view body reuses. `Related` is a navigation
    // (non-scalar); `RowVersion`/`Token` are byte[] scalars used for the concurrency-token case; `Id` is
    // the declared key member used for the key-target case.
    private const string SharedTypes = @"
namespace App
{
    public sealed class Related
    {
        public int Id { get; set; }
    }

    public sealed class Source
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public byte[] RowVersion { get; set; } = System.Array.Empty<byte>();
        public Related Related { get; set; } = new Related();
    }

    public sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public sealed class WriteCrud
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public byte[] Token { get; set; } = System.Array.Empty<byte>();
        public Related Related { get; set; } = new Related();
    }
}
";

    // The scalar write-safe members available for the WellFormed shape's random-length mapping list. All
    // are non-key, non-token scalar members, so a WellFormed view never trips a mass-assignment error.
    private static readonly (string Crud, string Entity)[] SafePairs =
    {
        ("Name", "Name"),
        ("Quantity", "Quantity"),
    };

    /// <summary>One generated view input: its shape plus a class-name suffix (and safe-mapping count for
    /// the WellFormed shape) to vary the input the generator sees.</summary>
    private sealed record ViewInput(ViewShape Shape, int Suffix, int MappingCount);

    private static readonly Gen<ViewInput> GenViewInput =
        from shape in Gen.Int[0, Enum.GetValues(typeof(ViewShape)).Length - 1]
        from suffix in Gen.Int[0, 999]
        from mappingCount in Gen.Int[1, SafePairs.Length]
        select new ViewInput((ViewShape)shape, suffix, mappingCount);

    // The diagnostic id contract (R9.4 / Spec 03 D81): the VISTA prefix immediately followed by exactly
    // four decimal digits.
    private static readonly Regex DiagnosticIdPattern = new("^VISTA[0-9]{4}$", RegexOptions.Compiled);

    private const string ExpectedCategory = "a2n.Vista.SourceGenerators";

    [Test]
    public void Every_Emitted_Diagnostic_Conforms_To_The_Id_And_Category_Contract()
    {
        // Feature: source-generator-write-mapper, Property 7: For any view input, every diagnostic the
        // generator emits has an identifier matching the VISTA prefix immediately followed by exactly four
        // decimal digits, and the category a2n.Vista.SourceGenerators.
        GenViewInput.Sample(
            input =>
            {
                var source = RenderViewSource(input);
                var result = WriteMapperGeneratorTestHarness.Run(source);

                // R9.4: EVERY diagnostic the generator emits must carry a VISTA#### id and the
                // a2n.Vista.SourceGenerators category — regardless of how many (or how few) it emits.
                foreach (var diagnostic in result.Diagnostics)
                {
                    if (!DiagnosticIdPattern.IsMatch(diagnostic.Id))
                    {
                        return false;
                    }

                    if (!string.Equals(diagnostic.Descriptor.Category, ExpectedCategory, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            },
            iter: 100,
            // On failure, print the exact view source that broke the property for a reproducible example.
            print: RenderViewSource);
    }

    /// <summary>
    /// Renders a compilable source file (shared types + one view) for the given input's shape. Each shape
    /// exercises a distinct branch of the generator's recognition/diagnostic matrix.
    /// </summary>
    private static string RenderViewSource(ViewInput input)
    {
        var name = input.Shape + "View" + input.Suffix;

        var body = input.Shape switch
        {
            ViewShape.WellFormed => RenderWellFormed(name, input.MappingCount),
            ViewShape.Abstract => RenderAbstract(name),
            ViewShape.NonPartial => RenderNonPartial(name),
            ViewShape.ReadOnly => RenderReadOnly(name),
            ViewShape.NoFacet => RenderNoFacet(name),
            ViewShape.ObjectCrud => RenderObjectCrud(name),
            ViewShape.Unanalyzable => RenderUnanalyzable(name),
            ViewShape.ZeroMapping => RenderZeroMapping(name),
            ViewShape.NonScalar => RenderNonScalar(name),
            ViewShape.KeyTarget => RenderKeyTarget(name),
            ViewShape.TokenTarget => RenderTokenTarget(name),
            _ => RenderWellFormed(name, input.MappingCount),
        };

        return SharedTypes + body;
    }

    // ---- analyzable candidate: emits a mapper, no diagnostics -------------------------------------

    private static string RenderWellFormed(string name, int mappingCount)
    {
        var sb = new StringBuilder();
        sb.Append("namespace App\n{\n");
        sb.Append($"    public partial class {name} : a2n.Vista.Authoring.View<Row, WriteCrud>\n    {{\n");
        sb.Append("        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)\n");
        sb.Append("            => builder\n");
        sb.Append("                .CrudOn<Source>()\n");
        for (var i = 0; i < mappingCount; i++)
        {
            var (crud, entity) = SafePairs[i];
            sb.Append($"                .MapWritable(c => c.{crud}, e => e.{entity})\n");
        }

        sb.Append("                ;\n");
        sb.Append("    }\n}\n");
        return sb.ToString();
    }

    // ---- non-candidate shapes: dropped silently, no diagnostic ------------------------------------

    private static string RenderAbstract(string name) => $@"
namespace App
{{
    public abstract partial class {name} : a2n.Vista.Authoring.View<Row, WriteCrud>
    {{
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name, e => e.Name);
    }}
}}
";

    private static string RenderNonPartial(string name) => $@"
namespace App
{{
    public class {name} : a2n.Vista.Authoring.View<Row, WriteCrud>
    {{
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name, e => e.Name);
    }}
}}
";

    private static string RenderReadOnly(string name) => $@"
namespace App
{{
    public partial class {name} : a2n.Vista.Authoring.View<Row>
    {{
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row> builder)
            => builder
                .From<Source>(src => new Row {{ Id = src.Id, Name = src.Name, Quantity = src.Quantity }})
                .Field(x => x.Id, f => f.PrimaryKey());
    }}
}}
";

    private static string RenderNoFacet(string name) => $@"
namespace App
{{
    public partial class {name} : a2n.Vista.Authoring.View<Row, WriteCrud>
    {{
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .From<Source>(src => new Row {{ Id = src.Id, Name = src.Name, Quantity = src.Quantity }})
                .Field(x => x.Id, f => f.PrimaryKey());
    }}
}}
";

    // ---- candidate with no named TCrud: skipped silently (R1.4) -----------------------------------

    private static string RenderObjectCrud(string name) => $@"
namespace App
{{
    public partial class {name} : a2n.Vista.Authoring.View<Row, object>
    {{
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, object> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.ToString(), e => e.Name);
    }}
}}
";

    // ---- VISTA0033 (warning): a non-simple MapWritable selector (R8) ------------------------------

    private static string RenderUnanalyzable(string name) => $@"
namespace App
{{
    public partial class {name} : a2n.Vista.Authoring.View<Row, WriteCrud>
    {{
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name.ToUpper(), e => e.Name);
    }}
}}
";

    // ---- VISTA0030 (error): a CRUD facet with zero MapWritable mappings (R9.1) ---------------------

    private static string RenderZeroMapping(string name) => $@"
namespace App
{{
    public partial class {name} : a2n.Vista.Authoring.View<Row, WriteCrud>
    {{
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>();
    }}
}}
";

    // ---- VISTA0031 (error): a navigation (non-scalar) target (R9.2) --------------------------------

    private static string RenderNonScalar(string name) => $@"
namespace App
{{
    public partial class {name} : a2n.Vista.Authoring.View<Row, WriteCrud>
    {{
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Related, e => e.Related);
    }}
}}
";

    // ---- VISTA0032 (error): a mapping onto a declared key member (R9.3) ----------------------------

    private static string RenderKeyTarget(string name) => $@"
namespace App
{{
    public partial class {name} : a2n.Vista.Authoring.View<Row, WriteCrud>
    {{
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
        {{
            // .Key(...) is declared on IViewBuilder<TQuery>; keep it a separate statement so the arity-2
            // builder's .CrudOn(...) chain stays well-typed. The analyzer scans all invocations in the
            // class body, so the declared key ('Id') is still picked up.
            builder.Key(x => x.Id);
            builder
                .CrudOn<Source>()
                .MapWritable(c => c.Quantity, e => e.Id);
        }}
    }}
}}
";

    // ---- VISTA0032 (error): a mapping onto the concurrency token (R9.3) ----------------------------

    private static string RenderTokenTarget(string name) => $@"
namespace App
{{
    public partial class {name} : a2n.Vista.Authoring.View<Row, WriteCrud>
    {{
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .WithConcurrencyToken(e => e.RowVersion)
                .MapWritable(c => c.Token, e => e.RowVersion);
    }}
}}
";
}
