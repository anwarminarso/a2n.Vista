using System;
using System.Diagnostics.CodeAnalysis;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.Ports;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Requirement R4 (Decision Log D99) — wire versioning is deferred this release. There is a version
/// seam, but no version-prefixed routes are emitted and unversioned requests serve the latest version:
/// <list type="bullet">
/// <item>R4.3 — a <c>CurrentWireVersion</c> seam exists so versioning is additive later.</item>
/// <item>R4.1/R4.2 — no <c>/api/v{n}/</c> route segment is produced; a view resolves under the
/// unversioned default root.</item>
/// </list>
/// </summary>
public sealed class WireVersionTests
{
    private const string Il2026 = "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming";
    private const string Why = "Test exercises the runtime reflection authoring path by design; trimming is not used for tests.";

    /// <summary>R4.3: the wire-version seam exists (the value is reserved for future versioning).</summary>
    [Test]
    public async Task Wire_Version_Seam_Exists()
    {
        await Assert.That(VistaEndpointOptions.CurrentWireVersion).IsEqualTo("v1");
    }

    /// <summary>R4.1/R4.2: the composed route carries no version segment; it uses the unversioned root.</summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task No_Version_Prefix_In_Route()
    {
        var services = new ServiceCollection();
        services.AddVista(v => v.Register<AlphaView>());
        using var provider = services.BuildServiceProvider();
        var route = provider.GetRequiredService<IViewRegistry>().Get("alpha")!.Route;

        await Assert.That(route).IsEqualTo("/api/views/alpha");
        await Assert.That(route).DoesNotContain("/v1/");
    }
}
