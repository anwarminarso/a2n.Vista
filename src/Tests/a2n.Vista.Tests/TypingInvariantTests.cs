using System.Diagnostics.CodeAnalysis;
using System.Linq;
using a2n.Vista.Authoring;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Correctness Property 2 — typing invariant (design.md §"Property 2"; authoritative
/// docs/spec/01-view.md §4.5, Decision Log D38/D1). Exercised end-to-end through the PUBLIC Gaya A
/// authoring surface (<see cref="ViewTemplate{TDbContext}.BuildViews"/>), so the invariant is proven
/// on the same path developers use — no <c>InternalsVisibleTo</c>:
/// <list type="bullet">
/// <item>R3.1 / R3.3 — an anonymous projection registered without <c>WithCrud</c> yields a read-only
/// view: <see cref="ViewMetadata.IsReadOnly"/> is <see langword="true"/> and no CRUD types are
/// populated (no write endpoint is generated).</item>
/// <item>R3.2 — a Write facet requires a typed <c>TCrud</c> plus at least one <c>MapWritable</c>; a
/// <c>WithCrud</c> facet with zero mappings is rejected at build time
/// (<see cref="System.InvalidOperationException"/>), closing mass-assignment by design.</item>
/// <item>R3.2 (positive) — <c>WithCrud&lt;TCrud, TEntity&gt;().MapWritable(...)</c> builds a writable
/// view whose metadata carries <c>CrudType</c>/<c>CrudEntityType</c> and is not read-only.</item>
/// </list>
/// Gaya B (class-per-view) is intentionally NOT covered here: its metadata is produced via the
/// <c>internal</c> <c>IViewMetadataSource.BuildMetadata</c> path, which is not reachable from tests
/// without <c>InternalsVisibleTo</c>. Gaya A fully covers R3.1/R3.2/R3.3 through public API, which is
/// the spec intent (the anonymous-read-only invariant is a Gaya A concern by definition).
/// </summary>
public sealed class TypingInvariantTests
{
    /// <summary>
    /// R3.1 / R3.3: a view authored from an anonymous projection with no <c>WithCrud</c> is read-only.
    /// The anonymous row type (<c>new { r.Id, r.Name }</c>) flows through Gaya A reflection-based field
    /// derivation, producing one field per anonymous member while keeping the view read-only.
    /// </summary>
    [Test]
    public async Task Anonymous_Projection_Without_WithCrud_Is_ReadOnly()
    {
        var view = BuildView(TypingInvariantTemplate.AnonymousReadOnlyView);

        // R3.3: read-only, and therefore no write endpoint (CRUD types are absent).
        await Assert.That(view.IsReadOnly).IsTrue();
        await Assert.That(view.CrudType).IsNull();
        await Assert.That(view.CrudEntityType).IsNull();

        // The anonymous TRow ({ Id, Name }) was enumerated successfully by BuildViews → two fields.
        await Assert.That(view.Fields.Count).IsEqualTo(2);
        await Assert.That(view.Fields.Any(f => f.Name == "Id")).IsTrue();
        await Assert.That(view.Fields.Any(f => f.Name == "Name")).IsTrue();
    }

    /// <summary>
    /// R3.2: a Write facet declared via <c>WithCrud</c> but with ZERO <c>MapWritable</c> mappings must
    /// fail at build time. An empty CRUD facet would re-open mass assignment, so Core rejects it with
    /// an <see cref="System.InvalidOperationException"/> when the template materializes.
    /// </summary>
    [Test]
    public async Task WithCrud_Without_MapWritable_Throws_At_Build()
    {
        var ex = CaptureBuild(new EmptyCrudTemplate());

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).Contains("MapWritable");
    }

    /// <summary>
    /// R3.2 (positive): a view with <c>WithCrud&lt;TestCrud, TestEntity&gt;()</c> and at least one
    /// <c>MapWritable</c> builds successfully, is NOT read-only, and carries the typed CRUD metadata.
    /// </summary>
    [Test]
    public async Task WithCrud_And_MapWritable_Builds_Writable_View()
    {
        var view = BuildView(TypingInvariantTemplate.WritableView);

        await Assert.That(view.IsReadOnly).IsFalse();
        await Assert.That(view.CrudType).IsEqualTo(typeof(TestCrud));
        await Assert.That(view.CrudEntityType).IsEqualTo(typeof(TestEntity));
    }

    /// <summary>
    /// Builds the template through the public Gaya A path and returns the metadata for the named view.
    /// </summary>
    [SuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification = "Test exercises the runtime reflection path of Gaya A authoring by design; trimming is not used for tests.")]
    private static ViewMetadata BuildView(string viewName)
    {
        var definitions = new TypingInvariantTemplate().BuildViews();
        return definitions.Single(d => d.Metadata.Name == viewName).Metadata;
    }

    /// <summary>
    /// Materializes a template, asserting that an <see cref="System.InvalidOperationException"/> IS
    /// thrown, and returns it for inspection. Throws when no exception (or a different type) occurs.
    /// </summary>
    [SuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification = "Test exercises the runtime reflection path of Gaya A authoring by design; trimming is not used for tests.")]
    private static System.InvalidOperationException? CaptureBuild(ViewTemplate<DummyContext> template)
    {
        try
        {
            _ = template.BuildViews();
        }
        catch (System.InvalidOperationException ex)
        {
            return ex;
        }

        throw new System.InvalidOperationException(
            "Expected an InvalidOperationException from BuildViews, but it completed without throwing.");
    }
}

/// <summary>
/// Named projection row backing the typing-invariant template. Anonymous projections are produced by
/// <c>Select(r =&gt; new { ... })</c> over this row; the row itself is never enumerated (Core only
/// captures the projection delegate).
/// </summary>
/// <param name="Id">Numeric key-like field.</param>
/// <param name="Name">String field.</param>
/// <param name="UnitPrice">Numeric field that the writable view maps as writable.</param>
internal sealed record TypingRow(int Id, string Name, decimal UnitPrice);

/// <summary>The typed write contract clients post against (Gaya A <c>WithCrud</c> requires a class).</summary>
internal sealed class TestCrud
{
    /// <summary>The single writable field, bound to <see cref="TestEntity.UnitPrice"/>.</summary>
    public decimal UnitPrice { get; set; }
}

/// <summary>The entity that writes target.</summary>
internal sealed class TestEntity
{
    /// <summary>The writable column the CRUD facet maps onto.</summary>
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Gaya A template demonstrating the typing invariant: one anonymous read-only view (no
/// <c>WithCrud</c>) and one writable view (<c>WithCrud</c> + a single <c>MapWritable</c>).
/// </summary>
internal sealed class TypingInvariantTemplate : ViewTemplate<DummyContext>
{
    /// <summary>Name of the anonymous, read-only view (R3.1/R3.3).</summary>
    public const string AnonymousReadOnlyView = "anonymousReadOnly";

    /// <summary>Name of the typed writable view (R3.2 positive).</summary>
    public const string WritableView = "writable";

    /// <inheritdoc />
    protected override void Configure(IViewTemplateBuilder<DummyContext> views)
    {
        // R3.1/R3.3: anonymous projection (TRow inferred as an anonymous class), no WithCrud → read-only.
        views.AddView(
            AnonymousReadOnlyView,
            static (db, sp) => Enumerable.Empty<TypingRow>().AsQueryable().Select(r => new { r.Id, r.Name }));

        // R3.2 (positive): a typed Write facet with at least one MapWritable → writable view.
        views.AddView(
                WritableView,
                static (db, sp) => Enumerable.Empty<TypingRow>().AsQueryable())
            .WithCrud<TestCrud, TestEntity>()
            .MapWritable(c => c.UnitPrice, e => e.UnitPrice);
    }
}

/// <summary>
/// Gaya A template whose only view declares a Write facet via <c>WithCrud</c> but maps no writable
/// fields. Materializing it must fail (R3.2): an empty CRUD facet re-opens mass assignment.
/// </summary>
internal sealed class EmptyCrudTemplate : ViewTemplate<DummyContext>
{
    /// <summary>Name of the view with an (illegal) empty CRUD facet.</summary>
    public const string ViewName = "emptyCrud";

    /// <inheritdoc />
    protected override void Configure(IViewTemplateBuilder<DummyContext> views)
    {
        // WithCrud declared, but no MapWritable → BuildViews() must throw InvalidOperationException.
        _ = views
            .AddView(ViewName, static (db, sp) => Enumerable.Empty<TypingRow>().AsQueryable())
            .WithCrud<TestCrud, TestEntity>();
    }
}
