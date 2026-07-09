// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.Write;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Authoring-capture and build-time guard tests for the Style B ("Gaya B", class-per-view) write facet
/// (write-path task 2.4; Requirements R4.4, R4.6, R5.4).
/// <list type="bullet">
/// <item><b>Capture</b> — registering a writable <c>View&lt;TQuery, TCrud&gt;</c> publishes a full
/// <see cref="CrudFacetDefinition"/> into the process <see cref="IWriteFacetRegistry"/>: the ordered
/// <see cref="WritableFieldMapping"/> whitelist (with both the <c>From</c>/<c>To</c> selectors and the
/// resolved <c>CrudMember</c>/<c>EntityMember</c> names), the <c>ConcurrencyToken</c> selector, and the
/// <c>AllowsBulk</c> flag — matching the Style A shape so both styles feed the same registry (D119).</item>
/// <item><b>Guards</b> — the interim mass-assignment startup fail-fast net that mirrored the M9 analyzer
/// diagnostics VISTA0030/0031/0032 has been <b>retired</b> (D122, Requirement 9.6): those unsafe-mapping
/// cases are now caught exactly once, at build time, by the source-generator write-DSL analyzer, so
/// <c>ValidateWriteFacet</c> no longer throws for a zero-mapping facet (R4.4), a navigation/non-scalar
/// target (R4.6), or a key-field/concurrency-token target (R5.4) — these facets now register and capture
/// cleanly. The only <b>retained</b> startup guards are the two <em>write-executability</em>
/// preconditions that cannot be a build-time diagnostic: a write-capable view must declare a write facet
/// (<c>CrudOn</c>) and must have a resolvable primary key so a write can locate the target row (R4.4).
/// Both still throw <see cref="InvalidOperationException"/> during metadata build, naming the offending
/// view.</item>
/// </list>
/// </summary>
/// <remarks>
/// <see cref="IWriteFacetRegistry"/> is a per-composition-root singleton (not a process static), so each
/// test builds its own service provider and reads the facet back from DI. The guard cases assert directly
/// against the throwing <c>AddVista</c> call, because the write-facet shape is validated when metadata is
/// built during registration (<c>ValidateWriteFacet</c>). <c>Register&lt;TView&gt;()</c> is RUC-annotated
/// (runtime reflection authoring path); trimming is not used for tests, so IL2026 is suppressed at the
/// class level, matching the sibling registration-outcome tests.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests exercise the runtime reflection authoring path (Register<TView>) by design; trimming is not used for tests.")]
public sealed class WriteAuthoringCaptureTests
{
    // ---- Capture (R4.4 positive / D119): MapWritable expressions reach the registry -----------------

    /// <summary>
    /// Registering a writable Style B view captures its full write facet into the
    /// <see cref="IWriteFacetRegistry"/>: correct <c>CrudType</c>/<c>EntityType</c>, both ordered
    /// <c>MapWritable</c> mappings (names and non-null selectors), the concurrency-token selector, and the
    /// <c>AllowBulk</c> flag.
    /// </summary>
    [Test]
    public async Task Register_Captures_Full_CrudFacetDefinition_For_StyleB()
    {
        var services = new ServiceCollection();
        services.AddVista(v => v.Register<CaptureWritableView>());
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IWriteFacetRegistry>();

        var found = registry.TryGet("write-capture", out var facet);

        await Assert.That(found).IsTrue();
        await Assert.That(facet).IsNotNull();

        // Contract + entity types flow through unchanged.
        await Assert.That(facet!.CrudType).IsEqualTo(typeof(CaptureCrud));
        await Assert.That(facet.EntityType).IsEqualTo(typeof(CaptureSourceEntity));

        // The MapWritable whitelist is captured in declaration order, names resolved, selectors kept.
        await Assert.That(facet.WritableFields.Count).IsEqualTo(2);

        var name = facet.WritableFields[0];
        await Assert.That(name.CrudMember).IsEqualTo(nameof(CaptureCrud.Name));
        await Assert.That(name.EntityMember).IsEqualTo(nameof(CaptureSourceEntity.Name));
        await Assert.That(name.From).IsNotNull();
        await Assert.That(name.To).IsNotNull();

        var price = facet.WritableFields[1];
        await Assert.That(price.CrudMember).IsEqualTo(nameof(CaptureCrud.Price));
        await Assert.That(price.EntityMember).IsEqualTo(nameof(CaptureSourceEntity.Price));
        await Assert.That(price.From).IsNotNull();
        await Assert.That(price.To).IsNotNull();

        // The concurrency-token selector is captured and points at the token member.
        await Assert.That(facet.ConcurrencyToken).IsNotNull();
        await Assert.That(MemberNameOf(facet.ConcurrencyToken!)).IsEqualTo(nameof(CaptureSourceEntity.Version));

        // The AllowBulk opt-in is captured.
        await Assert.That(facet.AllowsBulk).IsTrue();
    }

    /// <summary>
    /// A writable view that does not opt into bulk captures <c>AllowsBulk == false</c> (the secure
    /// default), and a view that declares no concurrency token captures a <see langword="null"/> selector.
    /// </summary>
    [Test]
    public async Task Register_Captures_Defaults_When_No_Bulk_And_No_Token()
    {
        var services = new ServiceCollection();
        services.AddVista(v => v.Register<MinimalWritableView>());
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IWriteFacetRegistry>();

        await Assert.That(registry.TryGet("write-minimal", out var facet)).IsTrue();
        await Assert.That(facet!.WritableFields.Count).IsEqualTo(1);
        await Assert.That(facet.WritableFields[0].EntityMember).IsEqualTo(nameof(CaptureSourceEntity.Name));
        await Assert.That(facet.ConcurrencyToken).IsNull();
        await Assert.That(facet.AllowsBulk).IsFalse();
    }

    // ---- Guard retirement (D122, R9.6): the interim mass-assignment fail-fast net is gone ----------

    /// <summary>
    /// VISTA0030 retired (R9.6): a CRUD facet that declares zero <c>MapWritable</c> mappings no longer
    /// fails fast at startup — the source generator reports it at build time. Registration now succeeds
    /// and the facet is captured with an empty whitelist (the reflection oracle yields a conforming no-op
    /// mapper for it).
    /// </summary>
    [Test]
    public async Task ZeroMapping_Facet_No_Longer_Fails_Fast_And_Captures_Empty_Whitelist()
    {
        var facet = await RegisterAndGetFacet<ZeroMappingView>("write-zero-mapping");

        await Assert.That(facet).IsNotNull();
        await Assert.That(facet!.WritableFields.Count).IsEqualTo(0);
    }

    /// <summary>
    /// VISTA0031 retired (R9.6): a <c>MapWritable</c> target that is a navigation / non-scalar member no
    /// longer fails fast at startup — the source generator reports it at build time. Registration now
    /// succeeds and the mapping is captured verbatim (the reflection oracle's defense in depth skips the
    /// non-scalar target at write time).
    /// </summary>
    [Test]
    public async Task Navigation_Target_No_Longer_Fails_Fast_And_Captures_Mapping()
    {
        var facet = await RegisterAndGetFacet<NavigationTargetView>("write-navigation-target");

        await Assert.That(facet).IsNotNull();
        await Assert.That(facet!.WritableFields.Count).IsEqualTo(1);
        await Assert.That(facet.WritableFields[0].EntityMember).IsEqualTo(nameof(CaptureSourceEntity.Related));
    }

    /// <summary>
    /// VISTA0032 retired (R9.6): a <c>MapWritable</c> target that is a key field no longer fails fast at
    /// startup — the source generator reports it at build time. Registration now succeeds and the mapping
    /// is captured verbatim (the reflection oracle's defense in depth skips the key member at write time).
    /// </summary>
    [Test]
    public async Task KeyField_Target_No_Longer_Fails_Fast_And_Captures_Mapping()
    {
        var facet = await RegisterAndGetFacet<KeyFieldTargetView>("write-key-target");

        await Assert.That(facet).IsNotNull();
        await Assert.That(facet!.WritableFields.Count).IsEqualTo(1);
        await Assert.That(facet.WritableFields[0].EntityMember).IsEqualTo(nameof(CaptureSourceEntity.Id));
    }

    /// <summary>
    /// VISTA0032 retired (R9.6): a <c>MapWritable</c> target that is the concurrency token no longer fails
    /// fast at startup — the source generator reports it at build time. Registration now succeeds and the
    /// mapping is captured verbatim (the reflection oracle's defense in depth skips the token member at
    /// write time).
    /// </summary>
    [Test]
    public async Task ConcurrencyToken_Target_No_Longer_Fails_Fast_And_Captures_Mapping()
    {
        var facet = await RegisterAndGetFacet<TokenTargetView>("write-token-target");

        await Assert.That(facet).IsNotNull();
        await Assert.That(facet!.WritableFields.Count).IsEqualTo(1);
        await Assert.That(facet.WritableFields[0].EntityMember).IsEqualTo(nameof(CaptureSourceEntity.Version));
        await Assert.That(MemberNameOf(facet.ConcurrencyToken!)).IsEqualTo(nameof(CaptureSourceEntity.Version));
    }

    // ---- Retained write-executability guards (R4.4): these are NOT mass-assignment guards --------------

    /// <summary>
    /// Retained (R4.4): a view that derives <c>View&lt;TQuery, TCrud&gt;</c> is write-capable and must
    /// declare a write facet; omitting <c>CrudOn</c> still fails fast, naming the offending view. This is a
    /// write-executability precondition, not a mass-assignment guard, so it is unaffected by D122/R9.6.
    /// </summary>
    [Test]
    public async Task Guard_Missing_CrudFacet_Still_Fails_Fast_Naming_View()
    {
        var ex = await CaptureRegistration<MissingCrudFacetView>();

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).Contains("write-missing-facet");
        await Assert.That(ex.Message).Contains("CrudOn");
    }

    /// <summary>
    /// Retained (R4.4): a write-capable view requires a resolvable primary key so a write can locate the
    /// target row; omitting <c>.PrimaryKey()</c> still fails fast, naming the offending view. This is a
    /// write-executability precondition, not a mass-assignment guard, so it is unaffected by D122/R9.6.
    /// </summary>
    [Test]
    public async Task Guard_Missing_PrimaryKey_Still_Fails_Fast_Naming_View()
    {
        var ex = await CaptureRegistration<NoPrimaryKeyWritableView>();

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).Contains("write-no-pk");
        await Assert.That(ex.Message).Contains("primary key");
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    /// <summary>
    /// Runs registration for <typeparamref name="TView"/>, expecting it to succeed, and returns the
    /// captured <see cref="CrudFacetDefinition"/> read back from the composition root's
    /// <see cref="IWriteFacetRegistry"/> under <paramref name="viewName"/>.
    /// </summary>
    private static Task<CrudFacetDefinition?> RegisterAndGetFacet<TView>(string viewName)
        where TView : class, new()
    {
        var services = new ServiceCollection();
        services.AddVista(v => v.Register<TView>());
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IWriteFacetRegistry>();
        registry.TryGet(viewName, out var facet);
        return Task.FromResult(facet);
    }

    /// <summary>
    /// Runs registration for <typeparamref name="TView"/> and returns the <see cref="InvalidOperationException"/>
    /// the build-time guard throws, or <see langword="null"/> if registration unexpectedly succeeds.
    /// </summary>
    private static Task<InvalidOperationException?> CaptureRegistration<TView>()
        where TView : class, new()
    {
        try
        {
            var services = new ServiceCollection();
            services.AddVista(v => v.Register<TView>());
            return Task.FromResult<InvalidOperationException?>(null);
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult<InvalidOperationException?>(ex);
        }
    }

    /// <summary>Unwraps a (possibly <c>Convert</c>-wrapped) member selector to its member name.</summary>
    private static string? MemberNameOf(LambdaExpression selector)
    {
        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        return body is MemberExpression member ? member.Member.Name : null;
    }
}

// ---- Test entities / contracts / views ------------------------------------------------------------

/// <summary>EF source entity the writable capture views project from (single-source, Id-keyed).</summary>
internal sealed class CaptureSourceEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    /// <summary>Optimistic-concurrency token member (scalar).</summary>
    public int Version { get; set; }

    /// <summary>A navigation (non-scalar) member used by the navigation-target guard case.</summary>
    public RelatedThing? Related { get; set; }
}

/// <summary>A related entity used only to give the source a navigation (non-scalar) member.</summary>
internal sealed class RelatedThing
{
    public int Id { get; set; }
}

/// <summary>Projected (read) row type sent to clients.</summary>
internal sealed class WriteCaptureRow
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal Price { get; init; }
}

/// <summary>Typed write contract for the capture view (closes mass-assignment).</summary>
internal sealed class CaptureCrud
{
    public string Name { get; init; } = string.Empty;

    public decimal Price { get; init; }
}

/// <summary>Write contract that carries a navigation member, for the navigation-target guard.</summary>
internal sealed class NavigationCrud
{
    public RelatedThing? Related { get; init; }
}

/// <summary>Write contract that carries the key member, for the key-target guard.</summary>
internal sealed class KeyCrud
{
    public int Id { get; init; }
}

/// <summary>Write contract that carries the token member, for the token-target guard.</summary>
internal sealed class TokenCrud
{
    public int Version { get; init; }
}

/// <summary>
/// A fully-formed writable Style B view: two ordered <c>MapWritable</c> mappings, a concurrency token,
/// and bulk enabled — the positive capture case.
/// </summary>
internal sealed class CaptureWritableView : View<WriteCaptureRow, CaptureCrud>
{
    protected override void Configure(IViewBuilder<WriteCaptureRow, CaptureCrud> builder)
    {
        builder
            .Named("write-capture")
            .From<CaptureSourceEntity>(s => new WriteCaptureRow { Id = s.Id, Name = s.Name, Price = s.Price })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<CaptureSourceEntity>()
            .MapWritable(c => c.Name, e => e.Name)
            .MapWritable(c => c.Price, e => e.Price)
            .WithConcurrencyToken(e => e.Version)
            .AllowBulk();
    }
}

/// <summary>A minimal writable view: one mapping, no token, no bulk — the secure-default capture case.</summary>
internal sealed class MinimalWritableView : View<WriteCaptureRow, CaptureCrud>
{
    protected override void Configure(IViewBuilder<WriteCaptureRow, CaptureCrud> builder)
    {
        builder
            .Named("write-minimal")
            .From<CaptureSourceEntity>(s => new WriteCaptureRow { Id = s.Id, Name = s.Name, Price = s.Price })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<CaptureSourceEntity>()
            .MapWritable(c => c.Name, e => e.Name);
    }
}

/// <summary>
/// A writable view whose CRUD facet declares no <c>MapWritable</c> mapping (formerly the VISTA0030 startup
/// guard; now a build-time diagnostic — registration succeeds with an empty whitelist).
/// </summary>
internal sealed class ZeroMappingView : View<WriteCaptureRow, CaptureCrud>
{
    protected override void Configure(IViewBuilder<WriteCaptureRow, CaptureCrud> builder)
    {
        builder
            .Named("write-zero-mapping")
            .From<CaptureSourceEntity>(s => new WriteCaptureRow { Id = s.Id, Name = s.Name, Price = s.Price })
            .Field(x => x.Id, f => f.PrimaryKey());

        // CrudOn declared, but no MapWritable → default-deny with an empty whitelist. The zero-mapping
        // safety check is now a build-time diagnostic (VISTA0030, D122), so registration succeeds.
        builder.CrudOn<CaptureSourceEntity>();
    }
}

/// <summary>
/// A writable view mapping to a navigation / non-scalar target (formerly the VISTA0031 startup guard; now a
/// build-time diagnostic — registration succeeds and the mapping is captured).
/// </summary>
internal sealed class NavigationTargetView : View<WriteCaptureRow, NavigationCrud>
{
    protected override void Configure(IViewBuilder<WriteCaptureRow, NavigationCrud> builder)
    {
        builder
            .Named("write-navigation-target")
            .From<CaptureSourceEntity>(s => new WriteCaptureRow { Id = s.Id, Name = s.Name, Price = s.Price })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<CaptureSourceEntity>()
            .MapWritable(c => c.Related, e => e.Related);
    }
}

/// <summary>
/// A writable view mapping to a key field (formerly the VISTA0032 startup guard; now a build-time
/// diagnostic — registration succeeds and the mapping is captured).
/// </summary>
internal sealed class KeyFieldTargetView : View<WriteCaptureRow, KeyCrud>
{
    protected override void Configure(IViewBuilder<WriteCaptureRow, KeyCrud> builder)
    {
        builder
            .Named("write-key-target")
            .From<CaptureSourceEntity>(s => new WriteCaptureRow { Id = s.Id, Name = s.Name, Price = s.Price })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<CaptureSourceEntity>()
            .MapWritable(c => c.Id, e => e.Id);
    }
}

/// <summary>
/// A writable view mapping to the concurrency-token member (formerly the VISTA0032 startup guard; now a
/// build-time diagnostic — registration succeeds and the mapping is captured).
/// </summary>
internal sealed class TokenTargetView : View<WriteCaptureRow, TokenCrud>
{
    protected override void Configure(IViewBuilder<WriteCaptureRow, TokenCrud> builder)
    {
        builder
            .Named("write-token-target")
            .From<CaptureSourceEntity>(s => new WriteCaptureRow { Id = s.Id, Name = s.Name, Price = s.Price })
            .Field(x => x.Id, f => f.PrimaryKey());

        builder
            .CrudOn<CaptureSourceEntity>()
            .MapWritable(c => c.Version, e => e.Version)
            .WithConcurrencyToken(e => e.Version);
    }
}

/// <summary>
/// A write-capable view (derives <c>View&lt;TQuery, TCrud&gt;</c>) that never declares a write facet
/// (<c>CrudOn</c>). Exercises the retained write-executability guard (R4.4), which is not a
/// mass-assignment guard and therefore survives the D122/R9.6 retirement.
/// </summary>
internal sealed class MissingCrudFacetView : View<WriteCaptureRow, CaptureCrud>
{
    protected override void Configure(IViewBuilder<WriteCaptureRow, CaptureCrud> builder)
    {
        builder
            .Named("write-missing-facet")
            .From<CaptureSourceEntity>(s => new WriteCaptureRow { Id = s.Id, Name = s.Name, Price = s.Price })
            .Field(x => x.Id, f => f.PrimaryKey());

        // No CrudOn: a write-capable view must declare its write facet → retained fail-fast (R4.4).
    }
}

/// <summary>
/// A writable view that declares a full write facet but marks no projected field as the primary key.
/// Exercises the retained primary-key executability guard (R4.4), which is not a mass-assignment guard and
/// therefore survives the D122/R9.6 retirement.
/// </summary>
internal sealed class NoPrimaryKeyWritableView : View<WriteCaptureRow, CaptureCrud>
{
    protected override void Configure(IViewBuilder<WriteCaptureRow, CaptureCrud> builder)
    {
        builder
            .Named("write-no-pk")
            .From<CaptureSourceEntity>(s => new WriteCaptureRow { Id = s.Id, Name = s.Name, Price = s.Price });

        // A write facet is declared, but no field is marked .PrimaryKey() → retained fail-fast (R4.4).
        builder
            .CrudOn<CaptureSourceEntity>()
            .MapWritable(c => c.Name, e => e.Name);
    }
}
