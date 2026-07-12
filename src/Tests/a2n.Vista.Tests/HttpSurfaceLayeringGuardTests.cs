// Licensed to the a2n.Vista project. Published artifact — English only.
//
// HTTP-surface packaging / layering + RUC-confinement guard (spec source-generator-http-surface,
// task 11.3; Requirements R1.4, R4.2, R5.6, R7.1, R7.5). This is the HTTP-surface analogue of the
// runtime a2n.Vista.Tests/LayeringGuardTests and WriteLayeringGuardTests, anchored on the types this
// phase added (the Core IViewInvoker port + ViewInvokerStore, D123; the AspNetCore serialization seam
// VistaJson / VistaJsonWriter / VistaStaticJsonContext, D124) and on the ViewRequestExecutor whose RUC
// boundary this phase relaxed (R4.2).
//
// The generator-side half of task 11.3 (the ViewInvokerGenerator is a netstandard2.0
// IIncrementalGenerator that references no a2n.Vista project — R1.4/R7.1) is asserted in the
// SourceGenerators.Tests project (ViewInvokerGeneratorPackagingTests, mirroring
// WriteMapperGeneratorPackagingTests), because a2n.Vista.Tests deliberately takes no reference to the
// generator or to Microsoft.CodeAnalysis and therefore cannot load the netstandard2.0 Roslyn component
// to inspect it. This file covers the runtime-assembly half that a2n.Vista.Tests CAN observe.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Contracts;
using a2n.Vista.Ports;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// HTTP-surface layering + RUC-confinement guard (design.md §"The central design axis — layering (D48)",
/// §"The RUC boundary"). Pins, via reflection over the produced assemblies and member custom attributes,
/// the four structural non-negotiables this phase must preserve:
/// <list type="bullet">
/// <item>R5.6/R7.5 — <c>a2n.Vista.Core</c> — now carrying the new <see cref="IViewInvoker"/> port and
/// <see cref="ViewInvokerStore"/> — still references no <c>System.Text.Json</c>, EF Core, or ASP.NET
/// Core assembly (the dispatch port + store add none).</item>
/// <item>R7.5 — the serialization seam and contexts (<see cref="VistaJson"/>, <see cref="VistaJsonWriter"/>,
/// <c>VistaStaticJsonContext</c>) reside in <c>a2n.Vista.AspNetCore</c>, which carries no EF reference.</item>
/// <item>R4.2 — the public <see cref="ViewRequestExecutor"/> read/write facets carry no unconditional
/// <see cref="RequiresUnreferencedCodeAttribute"/> (they use a justified
/// <c>[UnconditionalSuppressMessage]</c> instead), while the private <c>*ReflectionAsync</c> fallback
/// helpers DO carry it — so a caller that resolves a generated invoker is not forced onto an RUC
/// method.</item>
/// </list>
/// <para>
/// Nuance (identical to <see cref="LayeringGuardTests"/>): <see cref="Assembly.GetReferencedAssemblies()"/>
/// reports the DIRECT references the compiler emitted into an assembly's metadata; transitive and
/// compiler-trimmed-unused references are absent. That is exactly the compile-time dependency property we
/// assert. The positive checks (the seam types genuinely live in AspNetCore; the port/store genuinely
/// live in Core) prove the absences are real layering properties, not artifacts of empty reference sets.
/// </para>
/// </summary>
public sealed class HttpSurfaceLayeringGuardTests
{
    private const string CoreAssemblyName = "a2n.Vista.Core";
    private const string EfAssemblyName = "a2n.Vista.EntityFrameworkCore";
    private const string AspNetCoreAssemblyName = "a2n.Vista.AspNetCore";

    private const string EfCorePrefix = "Microsoft.EntityFrameworkCore";
    private const string AspNetCorePrefix = "Microsoft.AspNetCore";
    private const string SystemTextJsonPrefix = "System.Text.Json";

    // The internal shipped Static_Envelope_Context — resolved by full name (it is internal to
    // a2n.Vista.AspNetCore, so the test cannot name the type directly without InternalsVisibleTo).
    private const string StaticJsonContextFullName = "a2n.Vista.AspNetCore.Serialization.VistaStaticJsonContext";

    // Anchor types: typeof(...).Assembly loads the owning Vista assembly the test project references.
    // Core is anchored on the NEW dispatch port + store (D123), so the reference-set assertions are
    // specifically about the surface this phase added.
    private static readonly Assembly CoreAssembly = typeof(IViewInvoker).Assembly;
    private static readonly Assembly AspNetCoreAssembly = typeof(VistaJson).Assembly;

    // The public ViewRequestExecutor read/write facets that lost their unconditional RUC this phase
    // (R4.2). Each must resolve the generated invoker before falling back to reflection, so none may
    // carry [RequiresUnreferencedCode] on the public method itself.
    private static readonly string[] PublicNonRucFacetMethods =
    [
        nameof(ViewRequestExecutor.ListAsync),
        nameof(ViewRequestExecutor.DetailAsync),
        nameof(ViewRequestExecutor.CreateAsync),
        nameof(ViewRequestExecutor.UpdateAsync),
        nameof(ViewRequestExecutor.ListForAdapterAsync),
        nameof(ViewRequestExecutor.ExportAsync),
        nameof(ViewRequestExecutor.ExportRowsAsync),
    ];

    // The private reflection-fallback helpers that MUST carry [RequiresUnreferencedCode] (the RUC is
    // confined to these branches, R4.2).
    private static readonly string[] PrivateRucReflectionHelpers =
    [
        "ListReflectionAsync",
        "DetailReflectionAsync",
        "CreateReflectionAsync",
        "UpdateReflectionAsync",
    ];

    /// <summary>
    /// R5.6 / R7.5: <c>a2n.Vista.Core</c>, anchored on the new dispatch port + store, has no direct
    /// reference to <c>System.Text.Json</c>, EF Core (<c>Microsoft.EntityFrameworkCore*</c>), or ASP.NET
    /// Core (<c>Microsoft.AspNetCore*</c>). The <see cref="IViewInvoker"/> port and
    /// <see cref="ViewInvokerStore"/> use only Core + BCL types, so they introduce no new cross-reference.
    /// </summary>
    [Test]
    public async Task Core_With_Dispatch_Port_References_Neither_Stj_Ef_Nor_AspNetCore()
    {
        // The new dispatch types must resolve to the Core assembly (proves they live in Core, not an adapter).
        await Assert.That(CoreAssembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        await Assert.That(typeof(IViewInvoker).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        await Assert.That(typeof(ViewInvokerStore).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        await Assert.That(typeof(ViewInvocationListResult).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);

        var referencedNames = ReferencedAssemblyNames(CoreAssembly).ToList();

        var referencesStj = referencedNames
            .Any(name => name.StartsWith(SystemTextJsonPrefix, StringComparison.Ordinal));
        var referencesEfCore = referencedNames
            .Any(name => name.StartsWith(EfCorePrefix, StringComparison.Ordinal));
        var referencesAspNetCore = referencedNames
            .Any(name => name.StartsWith(AspNetCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesStj).IsFalse();
        await Assert.That(referencesEfCore).IsFalse();
        await Assert.That(referencesAspNetCore).IsFalse();
    }

    /// <summary>
    /// R7.5 (positive sanity + placement): the serialization seam and its contexts reside in
    /// <c>a2n.Vista.AspNetCore</c> — the shared writer <see cref="VistaJsonWriter"/>, the seam options
    /// <see cref="VistaJson"/>, and the shipped <c>VistaStaticJsonContext</c>. This proves the "Core has
    /// no STJ" assertion above is a real layering property: the STJ-touching code genuinely lives in the
    /// ASP.NET Core package, not in Core.
    /// </summary>
    [Test]
    public async Task Serialization_Seam_And_Contexts_Reside_In_AspNetCore()
    {
        await Assert.That(AspNetCoreAssembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);

        await Assert.That(typeof(VistaJson).Assembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);
        await Assert.That(typeof(VistaJsonWriter).Assembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);

        // The Static_Envelope_Context is internal to AspNetCore — resolve it by full name from the
        // assembly rather than referencing the type (no InternalsVisibleTo).
        var staticContext = AspNetCoreAssembly.GetType(StaticJsonContextFullName, throwOnError: false);
        await Assert.That(staticContext).IsNotNull();
        await Assert.That(staticContext!.Assembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);
    }

    /// <summary>
    /// R7.5: <c>a2n.Vista.AspNetCore</c> (which hosts the serialization seam) carries no direct reference
    /// to the EF assembly and no EF Core package reference — the current package layering is preserved
    /// (AspNetCore has no EF reference).
    /// </summary>
    [Test]
    public async Task AspNetCore_Seam_Package_Has_No_Ef_Reference()
    {
        var referencedNames = ReferencedAssemblyNames(AspNetCoreAssembly).ToList();

        var referencesEfAssembly = referencedNames
            .Any(name => string.Equals(name, EfAssemblyName, StringComparison.Ordinal));
        var referencesEfCorePackages = referencedNames
            .Any(name => name.StartsWith(EfCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesEfAssembly).IsFalse();
        await Assert.That(referencesEfCorePackages).IsFalse();
    }

    /// <summary>
    /// R4.2: the public <see cref="ViewRequestExecutor"/> read/write facets
    /// (<c>ListAsync</c>/<c>DetailAsync</c>/<c>CreateAsync</c>/<c>UpdateAsync</c>/
    /// <c>ListForAdapterAsync</c>/<c>ExportAsync</c>/<c>ExportRowsAsync</c>) carry NO unconditional
    /// <see cref="RequiresUnreferencedCodeAttribute"/> — the RUC is confined to the private
    /// reflection-fallback helpers, so a caller that resolves a generated invoker is not forced onto an
    /// RUC method.
    /// </summary>
    [Test]
    public async Task Public_Executor_Facets_Carry_No_Unconditional_Ruc()
    {
        foreach (var methodName in PublicNonRucFacetMethods)
        {
            var method = typeof(ViewRequestExecutor).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance);

            await Assert.That(method).IsNotNull();

            var ruc = method!.GetCustomAttribute<RequiresUnreferencedCodeAttribute>();
            await Assert.That(ruc).IsNull();
        }
    }

    /// <summary>
    /// R4.2: the private reflection-fallback helpers
    /// (<c>ListReflectionAsync</c>/<c>DetailReflectionAsync</c>/<c>CreateReflectionAsync</c>/
    /// <c>UpdateReflectionAsync</c>) DO carry <see cref="RequiresUnreferencedCodeAttribute"/>. This is the
    /// other half of the confinement: the RUC did not vanish, it moved onto the branch that actually
    /// reflects (<c>MakeGenericMethod</c>).
    /// </summary>
    [Test]
    public async Task Private_Reflection_Helpers_Carry_Ruc()
    {
        foreach (var methodName in PrivateRucReflectionHelpers)
        {
            var method = typeof(ViewRequestExecutor).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);

            await Assert.That(method).IsNotNull();

            var ruc = method!.GetCustomAttribute<RequiresUnreferencedCodeAttribute>();
            await Assert.That(ruc).IsNotNull();
        }
    }

    /// <summary>
    /// R4.2 (documented exception): <see cref="ViewRequestExecutor.DeleteAsync"/> legitimately keeps its
    /// <see cref="RequiresUnreferencedCodeAttribute"/>. Delete is a non-generic executor call
    /// (<c>IViewExecutor.DeleteAsync</c>) with metadata-driven runtime key resolution — it is not routed
    /// through a generated invoker (the <see cref="IViewInvoker"/> port has no Delete member), so its RUC
    /// is correct and intentional, not a relaxation target.
    /// </summary>
    [Test]
    public async Task DeleteAsync_Keeps_Its_Ruc()
    {
        var deleteAsync = typeof(ViewRequestExecutor).GetMethod(
            nameof(ViewRequestExecutor.DeleteAsync),
            BindingFlags.Public | BindingFlags.Instance);

        await Assert.That(deleteAsync).IsNotNull();

        var ruc = deleteAsync!.GetCustomAttribute<RequiresUnreferencedCodeAttribute>();
        await Assert.That(ruc).IsNotNull();
    }

    /// <summary>
    /// Projects the direct referenced-assembly simple names of <paramref name="assembly"/>
    /// (<see cref="Assembly.GetReferencedAssemblies()"/> → <see cref="AssemblyName.Name"/>), dropping any
    /// null names defensively. Mirrors the helper in <see cref="LayeringGuardTests"/>.
    /// </summary>
    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)!;
}
