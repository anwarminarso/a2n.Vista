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
/// <item><b>Guards</b> — the interim startup fail-fast net (the M9 analyzer diagnostics VISTA0030/0031/0032
/// until the source generator reports them at build time) throws <see cref="InvalidOperationException"/>
/// during metadata build, naming the offending view and the offending mapping/member, for: a zero-mapping
/// facet (R4.4), a navigation/non-scalar target (R4.6), a key-field target (R5.4), and a concurrency-token
/// target (R5.4).</item>
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

    // ---- Guards (R4.4 / R4.6 / R5.4): fail-fast naming the offending view/mapping ------------------

    /// <summary>
    /// VISTA0030 (interim): a CRUD facet that declares zero <c>MapWritable</c> mappings fails fast, since
    /// write is default-deny (R4.4). The message names the offending view and points at <c>MapWritable</c>.
    /// </summary>
    [Test]
    public async Task Guard_ZeroMapping_Facet_Fails_Fast_Naming_View()
    {
        var ex = await CaptureRegistration<ZeroMappingView>();

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).Contains("write-zero-mapping");
        await Assert.That(ex.Message).Contains("MapWritable");
    }

    /// <summary>
    /// VISTA0031 (interim): a <c>MapWritable</c> target that is a navigation / non-scalar member is
    /// rejected (R4.6). The message names the offending view and the offending mapping's members.
    /// </summary>
    [Test]
    public async Task Guard_Navigation_Target_Fails_Fast_Naming_Mapping()
    {
        var ex = await CaptureRegistration<NavigationTargetView>();

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).Contains("write-navigation-target");
        await Assert.That(ex.Message).Contains(nameof(NavigationCrud.Related));
        await Assert.That(ex.Message).Contains("non-scalar");
    }

    /// <summary>
    /// VISTA0032 (interim): a <c>MapWritable</c> target that is a key field is rejected — row identity
    /// comes from the request key, never the body (R5.4). The message names the view and the key member.
    /// </summary>
    [Test]
    public async Task Guard_KeyField_Target_Fails_Fast_Naming_Key_Member()
    {
        var ex = await CaptureRegistration<KeyFieldTargetView>();

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).Contains("write-key-target");
        await Assert.That(ex.Message).Contains(nameof(CaptureSourceEntity.Id));
        await Assert.That(ex.Message).Contains("key field");
    }

    /// <summary>
    /// VISTA0032 (interim): a <c>MapWritable</c> target that is the concurrency token is rejected — the
    /// token is compared for optimistic concurrency, never client-assigned (R5.4). The message names the
    /// view and the token member.
    /// </summary>
    [Test]
    public async Task Guard_ConcurrencyToken_Target_Fails_Fast_Naming_Token_Member()
    {
        var ex = await CaptureRegistration<TokenTargetView>();

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).Contains("write-token-target");
        await Assert.That(ex.Message).Contains(nameof(CaptureSourceEntity.Version));
        await Assert.That(ex.Message).Contains("concurrency");
    }

    // ---- Helpers ------------------------------------------------------------------------------------

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

/// <summary>A writable view whose CRUD facet declares no <c>MapWritable</c> mapping (VISTA0030 guard).</summary>
internal sealed class ZeroMappingView : View<WriteCaptureRow, CaptureCrud>
{
    protected override void Configure(IViewBuilder<WriteCaptureRow, CaptureCrud> builder)
    {
        builder
            .Named("write-zero-mapping")
            .From<CaptureSourceEntity>(s => new WriteCaptureRow { Id = s.Id, Name = s.Name, Price = s.Price })
            .Field(x => x.Id, f => f.PrimaryKey());

        // CrudOn declared, but no MapWritable → default-deny with an empty whitelist must fail fast.
        builder.CrudOn<CaptureSourceEntity>();
    }
}

/// <summary>A writable view mapping to a navigation / non-scalar target (VISTA0031 guard).</summary>
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

/// <summary>A writable view mapping to a key field (VISTA0032 guard).</summary>
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

/// <summary>A writable view mapping to the concurrency-token member (VISTA0032 guard).</summary>
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
