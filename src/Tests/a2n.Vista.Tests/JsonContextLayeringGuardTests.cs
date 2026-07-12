// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Per-view JsonTypeInfo packaging / layering + cast-guard assertions (spec source-generator-json-typeinfo,
// task 9.3; Decision Log D125/D126; Requirements 4.3, 4.4, 7.1, 7.5). This is the JsonTypeInfo analogue of
// the runtime a2n.Vista.Tests/LayeringGuardTests, HttpSurfaceLayeringGuardTests, and WriteLayeringGuardTests,
// anchored on the types this phase added (the Core-resident, serializer-neutral GeneratedJsonContextStore,
// D125) and the AspNetCore serialization seam that drains it (VistaJson, D126), plus the generator-consumer
// fixture assembly (a2n.Vista.GeneratorJsonContextSample) the generated per-view contexts are emitted into.
//
// The generator-side half of task 9.3 (the ViewJsonContextGenerator is a netstandard2.0
// IIncrementalGenerator that references no a2n.Vista project — R7.1) is asserted in the
// SourceGenerators.Tests project (ViewJsonContextGeneratorPackagingTests), because a2n.Vista.Tests
// deliberately takes no reference to the generator or to Microsoft.CodeAnalysis and therefore cannot load
// the netstandard2.0 Roslyn component to inspect it. This file covers the runtime-assembly + cast-guard
// half that a2n.Vista.Tests CAN observe.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.GeneratorJsonContextSample;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Per-view <c>JsonTypeInfo</c> layering + cast-guard guard (design.md §"The central design axis —
/// layering (D48)"). Pins, via reflection over the produced assemblies and by exercising the registered
/// generated contexts, the structural non-negotiables this phase must preserve:
/// <list type="bullet">
/// <item>R4.3 / R7.5 — <c>a2n.Vista.Core</c>, now carrying the new <see cref="GeneratedJsonContextStore"/>,
/// still references no <c>System.Text.Json</c>, EF Core, or ASP.NET Core assembly (the store holds opaque
/// handles and adds none).</item>
/// <item>R7.5 — the <see cref="GeneratedJsonContextStore.All"/> snapshot is typed
/// <c>IReadOnlyCollection&lt;object&gt;</c> (a serializer-neutral opaque handle), never a
/// <c>System.Text.Json</c> type, so Core stays STJ-free.</item>
/// <item>R4.4 / R7.5 — the generated per-view context is emitted into the CONSUMER assembly
/// (<c>a2n.Vista.GeneratorJsonContextSample</c>), which carries no ASP.NET Core dependency.</item>
/// <item>R7.5 — the STJ-typed drain that names <c>IJsonTypeInfoResolver</c> to chain the store lives in
/// <c>a2n.Vista.AspNetCore</c> (the seam, <see cref="VistaJson"/>), not in Core.</item>
/// <item>R7.5 (cast-guard) — every handle drained from <see cref="GeneratedJsonContextStore.All"/> casts
/// to <see cref="IJsonTypeInfoResolver"/>, upholding the store's registration contract (the single
/// unchecked cast the AspNetCore drain performs is always valid).</item>
/// </list>
/// <para>
/// Nuance (identical to <see cref="LayeringGuardTests"/>): <see cref="Assembly.GetReferencedAssemblies()"/>
/// reports the DIRECT references the compiler emitted into an assembly's metadata; transitive and
/// compiler-trimmed-unused references are absent. That is exactly the compile-time dependency property we
/// assert. The positive checks (the store genuinely lives in Core; the seam genuinely lives in AspNetCore;
/// the fixture assembly has registered contexts) prove the absences are real layering properties, not
/// artifacts of empty reference sets.
/// </para>
/// <para>
/// <b>Fixture reuse.</b> The generated per-view contexts come from the referenced
/// <c>a2n.Vista.GeneratorJsonContextSample</c> assembly (the ProjectReference added by task 8.2), whose
/// <c>[ModuleInitializer]</c>s register each context into <see cref="GeneratedJsonContextStore"/> at module
/// load. No new fixtures are declared here, so there is no first-wins store collision with the sibling
/// seam/parity tasks.
/// </para>
/// </summary>
public sealed class JsonContextLayeringGuardTests
{
    private const string CoreAssemblyName = "a2n.Vista.Core";
    private const string AspNetCoreAssemblyName = "a2n.Vista.AspNetCore";
    private const string ConsumerAssemblyName = "a2n.Vista.GeneratorJsonContextSample";

    private const string EfCorePrefix = "Microsoft.EntityFrameworkCore";
    private const string AspNetCorePrefix = "Microsoft.AspNetCore";
    private const string SystemTextJsonPrefix = "System.Text.Json";

    // Anchor types: typeof(...).Assembly loads the owning Vista assembly the test project references.
    // Core is anchored on the NEW serializer-neutral store (D125), so the reference-set assertions are
    // specifically about the surface this phase added.
    private static readonly Assembly CoreAssembly = typeof(GeneratedJsonContextStore).Assembly;
    private static readonly Assembly AspNetCoreAssembly = typeof(VistaJson).Assembly;
    private static readonly Assembly ConsumerAssembly = typeof(CatalogItemView).Assembly;

    /// <summary>
    /// R4.3 / R7.5: <c>a2n.Vista.Core</c>, anchored on the new <see cref="GeneratedJsonContextStore"/>, has
    /// no direct reference to <c>System.Text.Json</c>, EF Core (<c>Microsoft.EntityFrameworkCore*</c>), or
    /// ASP.NET Core (<c>Microsoft.AspNetCore*</c>). The store holds each generated context as an opaque
    /// <see cref="object"/> handle, so it introduces no new cross-reference and preserves the
    /// pluggable-serializer boundary (<c>a2n.Vista.Newtonsoft</c>).
    /// </summary>
    [Test]
    public async Task Core_With_GeneratedJsonContextStore_References_Neither_Stj_Ef_Nor_AspNetCore()
    {
        // The store must resolve to the Core assembly (proves it lives in Core, not an adapter).
        await Assert.That(CoreAssembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        await Assert.That(typeof(GeneratedJsonContextStore).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);

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
    /// R7.5: the <see cref="GeneratedJsonContextStore.All"/> snapshot is typed
    /// <c>IReadOnlyCollection&lt;object&gt;</c> — a serializer-neutral opaque handle — never a
    /// <c>System.Text.Json</c> type. This is what keeps Core STJ-free: the Core store surface names no
    /// serializer type, and the only place a handle is cast to a serializer type is the AspNetCore drain.
    /// </summary>
    [Test]
    public async Task Store_All_Exposes_Opaque_Object_Handles_Not_A_Stj_Type()
    {
        var allProperty = typeof(GeneratedJsonContextStore).GetProperty(
            nameof(GeneratedJsonContextStore.All),
            BindingFlags.Public | BindingFlags.Static);

        await Assert.That(allProperty).IsNotNull();
        await Assert.That(allProperty!.PropertyType).IsEqualTo(typeof(IReadOnlyCollection<object>));

        // The element type of the opaque handle collection is object, not any System.Text.Json type.
        var elementType = allProperty.PropertyType.GetGenericArguments().Single();
        await Assert.That(elementType).IsEqualTo(typeof(object));
        await Assert.That(elementType.Namespace?.StartsWith(SystemTextJsonPrefix, StringComparison.Ordinal) ?? false)
            .IsFalse();
    }

    /// <summary>
    /// R4.4 / R7.5: the generated per-view context is emitted into the CONSUMER assembly
    /// (<c>a2n.Vista.GeneratorJsonContextSample</c>), which carries no ASP.NET Core dependency — neither
    /// the <c>a2n.Vista.AspNetCore</c> assembly nor any <c>Microsoft.AspNetCore*</c> package. The
    /// generated code uses only <c>System.Text.Json</c> from the shared framework, never introducing an
    /// ASP.NET Core reference into a domain assembly.
    /// </summary>
    [Test]
    public async Task Consumer_Assembly_With_Generated_Context_Has_No_AspNetCore_Dependency()
    {
        // The view (and thus its generated context) genuinely lives in the consumer assembly.
        await Assert.That(ConsumerAssembly.GetName().Name).IsEqualTo(ConsumerAssemblyName);
        await Assert.That(typeof(SubscriptionView).Assembly.GetName().Name).IsEqualTo(ConsumerAssemblyName);

        var referencedNames = ReferencedAssemblyNames(ConsumerAssembly).ToList();

        var referencesVistaAspNetCore = referencedNames
            .Any(name => string.Equals(name, AspNetCoreAssemblyName, StringComparison.Ordinal));
        var referencesAspNetCorePackages = referencedNames
            .Any(name => name.StartsWith(AspNetCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesVistaAspNetCore).IsFalse();
        await Assert.That(referencesAspNetCorePackages).IsFalse();
    }

    /// <summary>
    /// R7.5 (positive sanity + placement): the STJ-typed drain — the only code that must NAME
    /// <c>System.Text.Json.Serialization.Metadata</c> resolver types to chain the store — lives in
    /// <c>a2n.Vista.AspNetCore</c> (the serialization seam <see cref="VistaJson"/>). This proves the "Core
    /// has no STJ" assertion above is a real layering property: the STJ-touching drain genuinely lives in
    /// the ASP.NET Core package, not in Core.
    /// </summary>
    [Test]
    public async Task Stj_Typed_Drain_Resides_In_AspNetCore()
    {
        await Assert.That(AspNetCoreAssembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);
        await Assert.That(typeof(VistaJson).Assembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);

        // The drain references System.Text.Json — proving the STJ-typed code lives here, not in Core.
        var referencesStj = ReferencedAssemblyNames(AspNetCoreAssembly)
            .Any(name => name.StartsWith(SystemTextJsonPrefix, StringComparison.Ordinal));
        await Assert.That(referencesStj).IsTrue();
    }

    /// <summary>
    /// R7.5 (cast-guard): every handle drained from <see cref="GeneratedJsonContextStore.All"/> casts to
    /// <see cref="IJsonTypeInfoResolver"/> — the single unchecked cast the AspNetCore drain performs is
    /// always valid, upholding the store's registration contract (only an <c>IJsonTypeInfoResolver</c> is
    /// ever registered). Exercised over the fixture-registered generated contexts.
    /// </summary>
    [Test]
    public async Task Every_Drained_Handle_Casts_To_IJsonTypeInfoResolver()
    {
        // Force the consumer fixture assembly's module initializers to run (they register the generated
        // per-view contexts into the store). Referencing a type via typeof alone does not guarantee the
        // module .cctor has run, so run it explicitly — this keeps the guard deterministic whether this
        // class runs in isolation or as part of the full suite.
        RuntimeHelpers.RunModuleConstructor(ConsumerAssembly.ManifestModule.ModuleHandle);

        var handles = GeneratedJsonContextStore.All;

        // Positive sanity: the fixture assembly's [ModuleInitializer]s registered at least the three
        // representative covered views, so the guard runs over a populated store (not an empty set).
        await Assert.That(handles.Count).IsGreaterThanOrEqualTo(3);

        foreach (var handle in handles)
        {
            await Assert.That(handle).IsNotNull();

            // The contract: every stored opaque handle is an IJsonTypeInfoResolver at runtime.
            await Assert.That(handle is IJsonTypeInfoResolver).IsTrue();

            // The exact cast the AspNetCore drain performs must not throw.
            await Assert.That(() => (IJsonTypeInfoResolver)handle).ThrowsNothing();
        }
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
