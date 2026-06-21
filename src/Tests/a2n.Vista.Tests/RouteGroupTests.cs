using System;
using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore;
using a2n.Vista.Ports;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Requirement R3 (Decision Log D101/D103) — route groups + single source. Registration composes a
/// view's full route into <c>ViewMetadata.Route</c>:
/// <list type="bullet">
/// <item>R3.2/R3.3 — an ungrouped view uses the default root <c>/api/views</c>; a grouped view uses the
/// group prefix; nested groups combine.</item>
/// <item>R3.4/R3.5 — view names are globally unique and a view maps to exactly one endpoint:
/// registering the same view in two groups fails fast with a duplicate-name error.</item>
/// </list>
/// </summary>
public sealed class RouteGroupTests
{
    private const string Il2026 = "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming";
    private const string Why = "Test exercises the runtime reflection authoring path by design; trimming is not used for tests.";

    /// <summary>R3.3: a view registered outside any group is served under the default root.</summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Ungrouped_View_Uses_Default_Root()
    {
        var route = RouteOf("alpha", v => v.Register<AlphaView>());
        await Assert.That(route).IsEqualTo("/api/views/alpha");
    }

    /// <summary>R3.2: a grouped view's route uses the group prefix (slashes normalized).</summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Grouped_View_Uses_Group_Prefix()
    {
        var route = RouteOf("alpha", v => v.RouteGroup("/internal", g => g.Register<AlphaView>()));
        await Assert.That(route).IsEqualTo("/internal/alpha");
    }

    /// <summary>R3.2: a prefix without a leading slash is normalized to one.</summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Group_Prefix_Without_Leading_Slash_Is_Normalized()
    {
        var route = RouteOf("alpha", v => v.RouteGroup("external", g => g.Register<AlphaView>()));
        await Assert.That(route).IsEqualTo("/external/alpha");
    }

    /// <summary>R3.2: nested groups append the inner prefix to the outer one.</summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Nested_Groups_Combine_Prefixes()
    {
        var route = RouteOf(
            "alpha",
            v => v.RouteGroup("/api/external", g => g.RouteGroup("orders", inner => inner.Register<AlphaView>())));
        await Assert.That(route).IsEqualTo("/api/external/orders/alpha");
    }

    /// <summary>R3.5: registering the same view in two groups fails (one view = one endpoint).</summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Same_View_In_Two_Groups_Fails()
    {
        var services = new ServiceCollection();

        InvalidOperationException? caught = null;
        try
        {
            services.AddVista(v =>
            {
                v.RouteGroup("/a", g => g.Register<AlphaView>());
                v.RouteGroup("/b", g => g.Register<AlphaView>());
            });
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
    }

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static string RouteOf(string viewName, Action<IVistaBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddVista(configure);
        using var provider = services.BuildServiceProvider();
        var view = provider.GetRequiredService<IViewRegistry>().Get(viewName)
            ?? throw new InvalidOperationException($"View '{viewName}' was not registered.");
        return view.Route;
    }
}

/// <summary>EF source entity for the route-group test views (POCO; not materialized in metadata-only tests).</summary>
internal sealed class GroupSource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Read projection for the route-group test views.</summary>
internal sealed class GroupRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>A minimal Gaya B read-only view named "alpha", used to assert route composition.</summary>
internal sealed class AlphaView : View<GroupRow>
{
    protected override void Configure(IViewBuilder<GroupRow> b) =>
        b.Named("alpha")
         .From<GroupSource>(s => new GroupRow { Id = s.Id, Name = s.Name })
         .Field(x => x.Id, f => f.PrimaryKey());
}
