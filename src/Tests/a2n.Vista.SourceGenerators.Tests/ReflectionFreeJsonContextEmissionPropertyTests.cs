// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 5 (M9, D125/D126, source-generator-json-typeinfo)
// ViewJsonContextGenerator's emitted per-view JsonTypeInfo context source (task 5.2).
//
// Feature: source-generator-json-typeinfo, Property 4: Generated context source is reflection-free,
// attribute-free, and uses JsonMetadataServices.
//
// Validates: Requirements 2.2, 2.4, 3.4, 7.3, 7.5
//
// The emitter (task 5.1) turns a covered typed Style B view (a non-abstract partial class deriving
// a2n.Vista.Authoring.View<TQuery> / View<TQuery, TCrud> with a named TQuery — and, when writable, a named
// TCrud — every DTO member an Emittable_Shape, and a public parameterless ctor) into a reflection-free
// <View>_VistaJsonContext.g.cs: a `file sealed class` implementing IJsonTypeInfoResolver whose GetTypeInfo
// dispatch returns a JsonMetadataServices-built JsonTypeInfo for each DTO in the Serializable_DTO_Set, plus
// one [ModuleInitializer] registering it into a2n.Vista.Metadata.GeneratedJsonContextStore. It names only
// Core + BCL/shared-framework System.Text.Json types — never [JsonSerializable], never reflection, never
// ASP.NET Core.
//
// This property proves that for RANDOMLY-SHAPED covered views — varying namespace / view / row / crud type
// names, read-only vs writable, plain writable properties vs init-only members, and 1..4 emittable scalar
// row members drawn from the Emittable_Shape palette — the emitted context source:
//
//   Is ATTRIBUTE-FREE (the generator-of-generator constraint — R2.2/R7.3):
//     * no [JsonSerializable] attribute route ("JsonSerializable" must be ABSENT).
//
//   Is REFLECTION-FREE / AOT-clean (R2.2/R2.4/R3.4/R7.3):
//     * no System.Reflection namespace / PropertyInfo reflection ("System.Reflection" ABSENT — the
//       emitted System.Text.Json `JsonPropertyInfo`/`CreatePropertyInfo` names are NOT reflection).
//     * no reflective instantiation ("Activator.CreateInstance" ABSENT — the creators are `new T(...)`).
//     * no expression-tree compilation ("Expression.Compile" / ".Compile(" ABSENT).
//     * no open-generic closing over a runtime Type ("MakeGenericMethod" ABSENT).
//
//   References only the view assembly's reachable types — Core + BCL/shared-framework STJ (R7.5):
//     * no ASP.NET Core reference ("Microsoft.AspNetCore" ABSENT).
//
//   Builds every JsonTypeInfo via JsonMetadataServices and has the expected AOT-clean shape (R2.2/R7.3):
//     * "JsonMetadataServices.CreateObjectInfo<" PRESENT (metadata built by hand, not by attribute).
//     * "IJsonTypeInfoResolver" PRESENT (the emitted resolver contract).
//     * "file sealed class" PRESENT (net8.0 `file` type).
//     * "[global::System.Runtime.CompilerServices.ModuleInitializer]" PRESENT (before-DI registration).
//
// Strategy: a CsCheck generator produces valid, distinct C# identifiers for the namespace and the view /
// row / crud type names (distinct prefixes guarantee no collision even when the random cores coincide), a
// read-only/writable flag, an init-only-members flag (to exercise both the parameterless-creator and the
// parameterized/init-creator emitter branches), and 1..4 row members whose types are drawn from the
// Emittable_Shape palette. Each case renders a small compilable covered view (implicit public parameterless
// ctor so a context IS emitted), drives the ViewJsonContextGenerator via CSharpGeneratorDriver
// (ViewJsonContextGeneratorTestHarness), extracts the single generated <View>_VistaJsonContext.g.cs source,
// and asserts the attribute-free / reflection-free / Core-only invariants above. Only the generated source
// TEXT is inspected; no generated [ModuleInitializer] is executed.
//
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck (imperative Sample, pairs
// cleanly with TUnit [Test]).

using System;
using System.Text;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class ReflectionFreeJsonContextEmissionPropertyTests
{
    // A palette of Emittable_Shape scalar member types (BCL scalars, string, and nullable value types), so
    // every generated row is fully emittable and the view is COVERED — the emitter always produces a
    // context. These span the range of CreatePropertyInfo<TMember> elements the emitter must render.
    private static readonly string[] EmittableTypes =
    {
        "int",
        "string",
        "long",
        "double",
        "decimal",
        "bool",
        "global::System.Guid",
        "global::System.DateTime",
        "int?",
        "double?",
    };

    /// <summary>One generated covered-view input: its flags, four varied identifiers, and row member types.</summary>
    private sealed record ViewInput(
        bool IsWritable,
        bool InitOnlyMembers,
        string Namespace,
        string ViewName,
        string RowName,
        string CrudName,
        int[] MemberTypeIndices);

    // A valid C# identifier core: an uppercase leading letter followed by 2–6 lowercase letters, so it is
    // never a C# keyword (keywords are all lowercase) and always parses.
    private static readonly Gen<string> GenIdentifierCore =
        from first in Gen.Char['a', 'z']
        from rest in Gen.Char['a', 'z'].Array[2, 6]
        select char.ToUpperInvariant(first) + new string(rest);

    // 1..4 emittable row members, each an index into EmittableTypes.
    private static readonly Gen<int[]> GenMemberTypeIndices =
        Gen.Int[0, EmittableTypes.Length - 1].Array[1, 4];

    // Distinct prefixes ("Ns"/"View"/"Row"/"Crud") guarantee the four names never collide even when their
    // random cores happen to coincide, so the rendered source always compiles with distinct type/namespace
    // names while still varying every name across iterations.
    private static readonly Gen<ViewInput> GenViewInput =
        from isWritable in Gen.Bool
        from initOnly in Gen.Bool
        from ns in GenIdentifierCore
        from view in GenIdentifierCore
        from row in GenIdentifierCore
        from crud in GenIdentifierCore
        from members in GenMemberTypeIndices
        select new ViewInput(
            isWritable, initOnly, "Ns" + ns, "View" + view, "Row" + row, "Crud" + crud, members);

    [Test]
    public void Generated_Json_Context_Source_Is_Reflection_Free_Attribute_Free_And_Uses_JsonMetadataServices()
    {
        // Feature: source-generator-json-typeinfo, Property 4: Generated context source is reflection-free,
        // attribute-free, and uses JsonMetadataServices.
        GenViewInput.Sample(
            input =>
            {
                var source = RenderViewSource(input);
                var result = ViewJsonContextGeneratorTestHarness.Run(source);

                // A covered view with a public parameterless ctor always emits exactly its context source
                // (task 5.1). Its hint name is "<Namespace>_<ViewName>_VistaJsonContext.g.cs".
                if (!result.HasGeneratedContextFor(input.ViewName))
                {
                    return false;
                }

                var generated = result.GeneratedContextSourceFor(input.ViewName);

                // --- ATTRIBUTE-FREE: the generator-of-generator constraint (R2.2/R7.3). ---

                // No [JsonSerializable] attribute route — metadata is built by hand via JsonMetadataServices.
                if (generated.Contains("JsonSerializable", StringComparison.Ordinal))
                {
                    return false;
                }

                // --- REFLECTION-FREE / AOT-clean (R2.2/R2.4/R3.4/R7.3). ---

                // No System.Reflection namespace / PropertyInfo reflection. Note: the emitted System.Text.Json
                // `JsonPropertyInfo` / `CreatePropertyInfo` names are metadata factory names, NOT reflection —
                // so the reflection check keys on the System.Reflection namespace, not the "PropertyInfo"
                // substring.
                if (generated.Contains("System.Reflection", StringComparison.Ordinal))
                {
                    return false;
                }

                // No reflective instantiation — the object creators are `new T(...)`.
                if (generated.Contains("Activator.CreateInstance", StringComparison.Ordinal))
                {
                    return false;
                }

                // No expression-tree compilation.
                if (generated.Contains("Expression.Compile", StringComparison.Ordinal)
                    || generated.Contains(".Compile(", StringComparison.Ordinal))
                {
                    return false;
                }

                // No open-generic closing over a runtime Type.
                if (generated.Contains("MakeGenericMethod", StringComparison.Ordinal))
                {
                    return false;
                }

                // --- Core + BCL/STJ only — no ASP.NET Core type in the view assembly (R7.5). ---
                if (generated.Contains("Microsoft.AspNetCore", StringComparison.Ordinal))
                {
                    return false;
                }

                // --- Builds every JsonTypeInfo via JsonMetadataServices, with the AOT-clean shape. ---

                // Metadata built by hand via JsonMetadataServices.CreateObjectInfo (not by attribute).
                if (!generated.Contains("JsonMetadataServices.CreateObjectInfo<", StringComparison.Ordinal))
                {
                    return false;
                }

                // The emitted per-view resolver contract.
                if (!generated.Contains("IJsonTypeInfoResolver", StringComparison.Ordinal))
                {
                    return false;
                }

                // Emitted as a net8.0 `file` sealed type.
                if (!generated.Contains("file sealed class", StringComparison.Ordinal))
                {
                    return false;
                }

                // Exactly one [ModuleInitializer] registering into the Core store before DI.
                if (!generated.Contains(
                        "[global::System.Runtime.CompilerServices.ModuleInitializer]", StringComparison.Ordinal))
                {
                    return false;
                }

                return true;
            },
            iter: 100,
            // On failure, print the exact view source that broke the property for a reproducible example.
            print: RenderViewSource);
    }

    /// <summary>
    /// Renders a compilable source file declaring the row type (with 1..4 emittable members, either plain
    /// writable properties or init-only members) and — for a writable view — the crud type, plus a covered
    /// partial view deriving the recognized Vista base: arity-1 View&lt;TRow&gt; for a read-only view or
    /// arity-2 View&lt;TRow, TCrud&gt; for a writable view. The view declares no constructor, so it has an
    /// implicit public parameterless ctor and the emitter produces a context plus its [ModuleInitializer].
    /// </summary>
    private static string RenderViewSource(ViewInput input)
    {
        var accessor = input.InitOnlyMembers ? "{ get; init; }" : "{ get; set; }";

        var sb = new StringBuilder();
        sb.Append("namespace ").Append(input.Namespace).Append("\n{\n");

        // Row type (TQuery) — 1..4 emittable members M0..M{n-1}.
        sb.Append("    public sealed class ").Append(input.RowName).Append("\n    {\n");
        for (var i = 0; i < input.MemberTypeIndices.Length; i++)
        {
            sb.Append("        public ").Append(EmittableTypes[input.MemberTypeIndices[i]])
              .Append(" M").Append(i).Append(' ').Append(accessor).Append('\n');
        }

        sb.Append("    }\n\n");

        if (input.IsWritable)
        {
            // Crud type (TCrud) — a couple of emittable members so the writable view is covered including TCrud.
            sb.Append("    public sealed class ").Append(input.CrudName).Append("\n    {\n");
            sb.Append("        public string Name ").Append(accessor).Append('\n');
            sb.Append("        public int? Score ").Append(accessor).Append('\n');
            sb.Append("    }\n\n");

            sb.Append("    public partial class ").Append(input.ViewName)
              .Append(" : a2n.Vista.Authoring.View<").Append(input.RowName).Append(", ")
              .Append(input.CrudName).Append(">\n    {\n    }\n}\n");
        }
        else
        {
            sb.Append("    public partial class ").Append(input.ViewName)
              .Append(" : a2n.Vista.Authoring.View<").Append(input.RowName).Append(">\n    {\n    }\n}\n");
        }

        return sb.ToString();
    }
}
