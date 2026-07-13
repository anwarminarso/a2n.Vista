// Licensed to the a2n.Vista project. Published artifact — English only.
//
// OpenAPI-emitter packaging / layering + multi-target + English guard (spec openapi-emitter, task 11.2;
// Requirements 13.1, 13.5; Decision Log D127/D128, D48). This is the OpenAPI analogue of the runtime
// a2n.Vista.Tests/LayeringGuardTests, HttpSurfaceLayeringGuardTests, JsonContextLayeringGuardTests, and
// WriteLayeringGuardTests, anchored on the types this feature added (the a2n.Vista.OpenApi emitter surface:
// VistaOpenApiDocumentBuilder / VistaOpenApiOptions / the OpenApiDocument object model).
//
// The design's non-negotiable is that the OpenAPI dependency is confined to the opt-in a2n.Vista.OpenApi
// package (R13.1, D48): the emitter package references a2n.Vista.AspNetCore (serving a document is an
// HTTP-host concern and it reads IViewRegistry + the seam options, both surfaced there), while
// a2n.Vista.Core, a2n.Vista.EntityFrameworkCore, and a2n.Vista.AspNetCore gain NO dependency on
// a2n.Vista.OpenApi — an app opts in explicitly. The package multi-targets net8.0/net9.0/net10.0 and its
// emitted document strings, code, and comments are English (R13.5).

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Encodings.Web;
using System.Text.Json;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.OpenApi;
using a2n.Vista.OpenApi.Model;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// OpenAPI-emitter layering + packaging guard (design.md §"Testing Strategy" → "Build / packaging / AOT
/// invariants"; Requirements 13.1, 13.5). Pins, via reflection over the produced assemblies, the
/// structural non-negotiables this feature must preserve:
/// <list type="bullet">
/// <item>R13.1 — the opt-in <c>a2n.Vista.OpenApi</c> package references <c>a2n.Vista.AspNetCore</c>.</item>
/// <item>R13.1 — <c>a2n.Vista.Core</c>, <c>a2n.Vista.EntityFrameworkCore</c>, and <c>a2n.Vista.AspNetCore</c>
/// gain NO dependency on <c>a2n.Vista.OpenApi</c> (asserted for each lower package).</item>
/// <item>R13.5 — the <c>a2n.Vista.OpenApi</c> package builds for and loads on the running TFM
/// (net8.0/net9.0/net10.0); because the whole suite runs on all three frameworks, a green run on each
/// proves the package multi-targets all three.</item>
/// <item>R13.5 — the strings Vista emits into the document (option defaults + a representative built
/// document) are English/ASCII, so no non-Latin script or mojibake leaks into a published artifact.</item>
/// </list>
/// <para>
/// Nuance (identical to <see cref="LayeringGuardTests"/>): <see cref="Assembly.GetReferencedAssemblies()"/>
/// reports the DIRECT references the compiler emitted into an assembly's metadata; transitive and
/// compiler-trimmed-unused references are absent. That is exactly the compile-time dependency property we
/// assert. The positive check (the emitter genuinely references AspNetCore) proves the absences are real
/// layering properties, not artifacts of empty reference sets.
/// </para>
/// <para>
/// The "English" checks are mechanical proxies: they assert the Vista-controlled emitted strings are ASCII
/// (catching non-Latin scripts / encoding corruption). Full natural-language English verification of code,
/// comments, and docs remains a review-time concern, consistent with the design's note that R13.5 is a
/// build/packaging invariant that is not property-amenable.
/// </para>
/// </summary>
public sealed class OpenApiLayeringGuardTests
{
    private const string CoreAssemblyName = "a2n.Vista.Core";
    private const string EfAssemblyName = "a2n.Vista.EntityFrameworkCore";
    private const string AspNetCoreAssemblyName = "a2n.Vista.AspNetCore";
    private const string OpenApiAssemblyName = "a2n.Vista.OpenApi";

    // Anchor types: typeof(...).Assembly loads the owning Vista assembly the test project references.
    private static readonly Assembly CoreAssembly = typeof(ViewQueryRequest).Assembly;
    private static readonly Assembly EfAssembly = typeof(EfViewExecutor).Assembly;
    private static readonly Assembly AspNetCoreAssembly = typeof(IViewAuthorizer).Assembly;

    // The emitter package is anchored on its public builder surface, so the reference-set assertions are
    // specifically about the a2n.Vista.OpenApi package this feature added.
    private static readonly Assembly OpenApiAssembly = typeof(VistaOpenApiDocumentBuilder).Assembly;

    // ---- R13.1 layering: the emitter references AspNetCore; the lower packages reference no OpenApi ----

    /// <summary>
    /// R13.1 (positive): the opt-in <c>a2n.Vista.OpenApi</c> package directly references
    /// <c>a2n.Vista.AspNetCore</c> — the emitter reads <c>IViewRegistry</c>, the serialization seam's
    /// <see cref="VistaEndpointOptions"/>, and the envelope types, all surfaced by the ASP.NET Core host
    /// package, and serving a document is itself an HTTP-host concern.
    /// </summary>
    [Test]
    public async Task OpenApi_References_AspNetCore()
    {
        // The emitter anchor must resolve to the OpenApi assembly (proves it lives there, not elsewhere).
        await Assert.That(OpenApiAssembly.GetName().Name).IsEqualTo(OpenApiAssemblyName);
        await Assert.That(typeof(VistaOpenApiOptions).Assembly.GetName().Name).IsEqualTo(OpenApiAssemblyName);

        var referencesAspNetCore = ReferencedAssemblyNames(OpenApiAssembly)
            .Any(name => string.Equals(name, AspNetCoreAssemblyName, StringComparison.Ordinal));

        await Assert.That(referencesAspNetCore).IsTrue();
    }

    /// <summary>
    /// R13.1: <c>a2n.Vista.Core</c> has no direct reference to the opt-in <c>a2n.Vista.OpenApi</c> package.
    /// The OpenAPI surface stays out of Core so a Core-only consumer never transitively pulls it in.
    /// </summary>
    [Test]
    public async Task Core_Does_Not_Reference_OpenApi()
    {
        await Assert.That(CoreAssembly.GetName().Name).IsEqualTo(CoreAssemblyName);

        var referencesOpenApi = ReferencedAssemblyNames(CoreAssembly)
            .Any(name => string.Equals(name, OpenApiAssemblyName, StringComparison.Ordinal));

        await Assert.That(referencesOpenApi).IsFalse();
    }

    /// <summary>
    /// R13.1: <c>a2n.Vista.EntityFrameworkCore</c> has no direct reference to <c>a2n.Vista.OpenApi</c>.
    /// </summary>
    [Test]
    public async Task EntityFrameworkCore_Does_Not_Reference_OpenApi()
    {
        await Assert.That(EfAssembly.GetName().Name).IsEqualTo(EfAssemblyName);

        var referencesOpenApi = ReferencedAssemblyNames(EfAssembly)
            .Any(name => string.Equals(name, OpenApiAssemblyName, StringComparison.Ordinal));

        await Assert.That(referencesOpenApi).IsFalse();
    }

    /// <summary>
    /// R13.1: <c>a2n.Vista.AspNetCore</c> — which the emitter references, not the other way around — has no
    /// direct reference back to <c>a2n.Vista.OpenApi</c>. This keeps the OpenAPI dependency one-directional
    /// (emitter → host) and off every Vista HTTP host that does not opt in.
    /// </summary>
    [Test]
    public async Task AspNetCore_Does_Not_Reference_OpenApi()
    {
        await Assert.That(AspNetCoreAssembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);

        var referencesOpenApi = ReferencedAssemblyNames(AspNetCoreAssembly)
            .Any(name => string.Equals(name, OpenApiAssemblyName, StringComparison.Ordinal));

        await Assert.That(referencesOpenApi).IsFalse();
    }

    // ---- R13.5 multi-target: the package builds for and loads on the running TFM ----------------------

    /// <summary>
    /// R13.5: the loaded <c>a2n.Vista.OpenApi</c> assembly was built for the currently running target
    /// framework. The test project multi-targets <c>net8.0;net9.0;net10.0</c> and references the emitter
    /// package, so each per-TFM run loads the emitter build for that TFM; a green run on all three
    /// frameworks therefore proves the package multi-targets <c>net8.0/net9.0/net10.0</c>. Verified by
    /// matching the assembly's <see cref="TargetFrameworkAttribute"/> to the running runtime's major
    /// version.
    /// </summary>
    [Test]
    public async Task OpenApi_Package_Is_Built_For_The_Running_TargetFramework()
    {
        await Assert.That(OpenApiAssembly.GetName().Name).IsEqualTo(OpenApiAssemblyName);

        var targetFramework = OpenApiAssembly.GetCustomAttribute<TargetFrameworkAttribute>();
        await Assert.That(targetFramework).IsNotNull();

        // FrameworkName looks like ".NETCoreApp,Version=v8.0"; assert it names the running major version so
        // the emitter build is genuinely the one for this TFM (net8.0 / net9.0 / net10.0).
        var runningMoniker = $"v{Environment.Version.Major}.0";
        await Assert.That(targetFramework!.FrameworkName).Contains(".NETCoreApp");
        await Assert.That(targetFramework.FrameworkName).Contains(runningMoniker);
    }

    // ---- R13.5 English: the Vista-controlled emitted strings are ASCII --------------------------------

    /// <summary>
    /// R13.5: the <see cref="VistaOpenApiOptions"/> default document strings a host inherits when it calls
    /// <c>AddVistaOpenApi()</c> with no arguments (title, OpenAPI version, endpoint path) are English/ASCII,
    /// so the out-of-the-box document Vista serves carries no non-Latin script or encoding corruption.
    /// </summary>
    [Test]
    public async Task OpenApi_Option_Defaults_Are_English_Ascii()
    {
        var defaults = new VistaOpenApiOptions();

        await Assert.That(FirstNonAsciiCodePoint(defaults.DocumentTitle)).IsNull();
        await Assert.That(FirstNonAsciiCodePoint(defaults.OpenApiVersion)).IsNull();
        await Assert.That(FirstNonAsciiCodePoint(defaults.EndpointPath)).IsNull();
    }

    /// <summary>
    /// R13.5: every string Vista emits into a representative built document — the <c>info</c> block, the
    /// operation summaries/descriptions, the response descriptions, the parameter/header names, the default
    /// security scheme, and the schema/property keys — is English/ASCII. The document is serialized with a
    /// relaxed encoder (<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>) so any non-ASCII
    /// character stays raw (rather than being escaped to an ASCII <c>\uXXXX</c> sequence) and is therefore
    /// caught by the scan.
    /// </summary>
    [Test]
    [RequiresUnreferencedCode("Builds the representative document via the RUC document builder.")]
    public async Task Emitted_Document_Strings_Are_English_Ascii()
    {
        var builder = new VistaOpenApiDocumentBuilder(
            EmitterFixtures.Registry(),
            EmitterFixtures.SeamOptions(),
            new VistaEndpointOptions(),
            new VistaOpenApiOptions(),
            EmitterFixtures.WriteFacets());

        OpenApiDocument document = builder.Build();

        // Serialize with a relaxed encoder so non-ASCII characters remain raw and are detectable (the
        // default encoder would escape them to ASCII \uXXXX and hide them from the scan).
        var json = JsonSerializer.Serialize(
            document,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        await Assert.That(FirstNonAsciiCodePoint(json)).IsNull();
    }

    /// <summary>
    /// Returns the numeric code point of the first non-ASCII character in <paramref name="text"/> (a
    /// character above <c>U+007F</c>), or <see langword="null"/> when the text is pure ASCII. Returning the
    /// offending code point rather than a bare bool makes a failure diagnosable.
    /// </summary>
    private static int? FirstNonAsciiCodePoint(string text)
    {
        foreach (var ch in text)
        {
            if (ch > '\u007F')
            {
                return ch;
            }
        }

        return null;
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
