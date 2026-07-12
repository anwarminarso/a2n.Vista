// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 4 (M9, D123/D124, source-generator-http-surface) ViewInvokerGenerator's
// emitted dispatch-invoker source (task 6.2).
//
// Feature: source-generator-http-surface, Property 4: Generated invoker source is reflection-free and
// Core-only.
//
// Validates: Requirements 2.3, 2.4, 3.4, 7.3
//
// The emitter turns a covered typed Style B view (a non-abstract partial class deriving
// a2n.Vista.Authoring.View<TQuery> / View<TQuery, TCrud> with a named TQuery — and, when writable, a named
// TCrud — and a public parameterless ctor) into a reflection-free <View>_VistaViewInvoker.g.cs: a
// `file sealed class` implementing IViewInvoker that closes IViewExecutor.ListAsync<TRow> / DetailAsync<TRow>
// (and, when writable, CreateAsync<TCrud> / UpdateAsync<TCrud>) at compile time, awaits the returned task
// directly with .ConfigureAwait(false), and registers itself into a2n.Vista.Ports.ViewInvokerStore through
// a single [ModuleInitializer]. It references only Core + BCL types — never ASP.NET Core.
//
// This property proves that for RANDOMLY-SHAPED covered views — varying namespace / view / row / crud type
// names and read-only vs writable — the emitted invoker source contains NONE of the reflection / AOT-unsafe
// constructs and references ONLY Core + BCL:
//
//   Forbidden (reflection / AOT-unsafe / cross-layer):
//     * MakeGenericMethod             (R2.4/R3.4 — no open-generic closing over a runtime Type)
//     * Activator.CreateInstance      (R2.4/R3.4/R7.3 — no reflective instantiation)
//     * PropertyInfo                  (R2.2/R7.3 — no reflective member access)
//     * Expression.Compile / .Compile( (R7.3 — no expression-tree compilation)
//     * ".Result"                     (R2.1 — no Task<TResult>.Result reflection; the invoker awaits)
//     * "GetType()"                   (R2.2 — no reflection over ViewListResult<TRow>)
//     * Microsoft.AspNetCore          (R2.3/R7.5 — Core-only; no ASP.NET Core reference)
//
//   Required (the AOT-clean shape the invoker must have):
//     * closed generic ListAsync<...> (R2.1 — TRow fixed at compile time, no MakeGenericMethod)
//     * .ConfigureAwait(false)        (R2.1 — awaits the returned Task<...> directly)
//     * [ModuleInitializer]           (R4.5 — registers the invoker before DI)
//     * file sealed class             (R7.3 — net8.0 `file` type)
//     * ViewInvokerStore.Register     (R4.5 — first-wins store registration)
//
// Strategy: a CsCheck generator produces valid, distinct C# identifiers for the namespace and the view /
// row / crud type names (distinct prefixes guarantee no collision even when the random cores coincide) plus
// a read-only/writable flag. Each case renders a small compilable covered view (with an implicit public
// parameterless ctor so an invoker IS emitted), drives the ViewInvokerGenerator via CSharpGeneratorDriver
// (ViewInvokerGeneratorTestHarness), extracts the single generated source whose hint name ends with
// _VistaViewInvoker.g.cs, and asserts the reflection-free / Core-only invariants above. Only the generated
// source TEXT is inspected; no generated [ModuleInitializer] is executed.
//
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck (imperative Sample, pairs
// cleanly with TUnit [Test]).

using System;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class ReflectionFreeInvokerEmissionPropertyTests
{
    /// <summary>One generated covered-view input: its writable flag and the four varied identifiers.</summary>
    private sealed record ViewInput(
        bool IsWritable,
        string Namespace,
        string ViewName,
        string RowName,
        string CrudName);

    // A valid C# identifier core: an uppercase leading letter followed by 2–6 lowercase letters, so it is
    // never a C# keyword (keywords are all lowercase) and always parses.
    private static readonly Gen<string> GenIdentifierCore =
        from first in Gen.Char['a', 'z']
        from rest in Gen.Char['a', 'z'].Array[2, 6]
        select char.ToUpperInvariant(first) + new string(rest);

    // Distinct prefixes ("Ns"/"View"/"Row"/"Crud") guarantee the four names never collide even when their
    // random cores happen to coincide, so the rendered source always compiles with distinct type/namespace
    // names while still varying every name across iterations.
    private static readonly Gen<ViewInput> GenViewInput =
        from isWritable in Gen.Bool
        from ns in GenIdentifierCore
        from view in GenIdentifierCore
        from row in GenIdentifierCore
        from crud in GenIdentifierCore
        select new ViewInput(isWritable, "Ns" + ns, "View" + view, "Row" + row, "Crud" + crud);

    [Test]
    public void Generated_Invoker_Source_Is_Reflection_Free_And_Core_Only()
    {
        // Feature: source-generator-http-surface, Property 4: Generated invoker source is reflection-free
        // and Core-only.
        GenViewInput.Sample(
            input =>
            {
                var source = RenderViewSource(input);
                var result = ViewInvokerGeneratorTestHarness.Run(source);

                // A covered view with a public parameterless ctor always emits exactly its invoker source
                // (task 6.1). Its hint name is "<Namespace>_<ViewName>_VistaViewInvoker.g.cs".
                var hintFragment = input.ViewName + "_VistaViewInvoker";
                if (!result.HasGeneratedSourceContaining(hintFragment))
                {
                    return false;
                }

                var generated = result.GeneratedSourceContaining(hintFragment);

                // --- Forbidden reflection / AOT-unsafe / cross-layer constructs (must be ABSENT). ---

                // R2.4/R3.4: no open-generic closing over a runtime Type.
                if (generated.Contains("MakeGenericMethod", StringComparison.Ordinal))
                {
                    return false;
                }

                // R2.4/R3.4/R7.3: no reflective instantiation.
                if (generated.Contains("Activator.CreateInstance", StringComparison.Ordinal))
                {
                    return false;
                }

                // R2.2/R7.3: no reflective member access.
                if (generated.Contains("PropertyInfo", StringComparison.Ordinal))
                {
                    return false;
                }

                // R7.3: no expression-tree compilation.
                if (generated.Contains("Expression.Compile", StringComparison.Ordinal)
                    || generated.Contains(".Compile(", StringComparison.Ordinal))
                {
                    return false;
                }

                // R2.1: no Task<TResult>.Result reflection — the invoker awaits the returned task instead.
                // (Safe substring: the type name "ViewInvocationListResult" carries no leading dot.)
                if (generated.Contains(".Result", StringComparison.Ordinal))
                {
                    return false;
                }

                // R2.2: no reflection over ViewListResult<TRow> — totals/rows come from direct member access.
                if (generated.Contains("GetType()", StringComparison.Ordinal))
                {
                    return false;
                }

                // R2.3/R7.5: Core-only — no ASP.NET Core reference in the view assembly.
                if (generated.Contains("Microsoft.AspNetCore", StringComparison.Ordinal))
                {
                    return false;
                }

                // --- Required AOT-clean shape (must be PRESENT). ---

                // R2.1: closed generic ListAsync over the named row type (TRow fixed at compile time).
                var rowFqn = $"global::{input.Namespace}.{input.RowName}";
                if (!generated.Contains($".ListAsync<{rowFqn}>", StringComparison.Ordinal))
                {
                    return false;
                }

                // R2.1: awaits the returned Task<...> directly.
                if (!generated.Contains(".ConfigureAwait(false)", StringComparison.Ordinal))
                {
                    return false;
                }

                // R4.5: exactly one [ModuleInitializer] registering into the Core store.
                if (!generated.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", StringComparison.Ordinal))
                {
                    return false;
                }

                // R7.3: emitted as a net8.0 `file` sealed type.
                if (!generated.Contains("file sealed class", StringComparison.Ordinal))
                {
                    return false;
                }

                // R4.5: first-wins registration into the Core-resident ViewInvokerStore.
                if (!generated.Contains("global::a2n.Vista.Ports.ViewInvokerStore.Register", StringComparison.Ordinal))
                {
                    return false;
                }

                // A writable view additionally closes CreateAsync<TCrud> at compile time (R3.1); a
                // read-only view emits no write dispatch (its write members throw) so no CreateAsync<...>
                // closed generic appears.
                var crudFqn = $"global::{input.Namespace}.{input.CrudName}";
                var closesCreate = generated.Contains($".CreateAsync<{crudFqn}>", StringComparison.Ordinal);
                if (input.IsWritable != closesCreate)
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
    /// Renders a compilable source file declaring the row type (and, for a writable view, the crud type)
    /// and a covered partial view deriving the recognized Vista base — arity-1 View&lt;TRow&gt; for a
    /// read-only view or arity-2 View&lt;TRow, TCrud&gt; for a writable view. The view declares no
    /// constructor, so it has an implicit public parameterless ctor and the emitter produces an invoker
    /// plus its [ModuleInitializer] (R1.5).
    /// </summary>
    private static string RenderViewSource(ViewInput input)
    {
        if (input.IsWritable)
        {
            return $@"
namespace {input.Namespace}
{{
    public sealed class {input.RowName}
    {{
        public int Id {{ get; set; }}
    }}

    public sealed class {input.CrudName}
    {{
        public string Name {{ get; set; }} = string.Empty;
    }}

    public partial class {input.ViewName} : a2n.Vista.Authoring.View<{input.RowName}, {input.CrudName}>
    {{
    }}
}}
";
        }

        return $@"
namespace {input.Namespace}
{{
    public sealed class {input.RowName}
    {{
        public int Id {{ get; set; }}
    }}

    public partial class {input.ViewName} : a2n.Vista.Authoring.View<{input.RowName}>
    {{
    }}
}}
";
    }
}
