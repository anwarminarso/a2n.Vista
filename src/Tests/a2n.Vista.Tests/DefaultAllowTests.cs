using System.Diagnostics.CodeAnalysis;
using System.Linq;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Correctness Property 1 — default-allow limited to projection (design.md §"Property 1";
/// authoritative docs/spec/01-view.md §5.5 / §5.1, Decision Log D42). Exercises the authoring
/// surface end-to-end through the PUBLIC Gaya A path (<see cref="ViewTemplate{TDbContext}.BuildViews"/>):
/// every projected field is filterable and sortable by default, string fields are searchable by
/// default, numeric/date fields never participate in global search, and each flag can be opted out
/// per field:
/// <list type="bullet">
/// <item>R5.1 — every projected field <see cref="FieldMetadata.IsFilterable"/> defaults to <see langword="true"/>; <c>Filterable(false)</c> opts out.</item>
/// <item>R5.2 — every projected field <see cref="FieldMetadata.IsSortable"/> defaults to <see langword="true"/>; <c>Sortable(false)</c> opts out.</item>
/// <item>R5.3 — string fields <see cref="FieldMetadata.IsSearchable"/> default to <see langword="true"/>; <c>Searchable(false)</c> opts out.</item>
/// <item>R5.4 — numeric/date fields are excluded from global search regardless of the flag.</item>
/// </list>
/// Tests go through the public authoring surface only (no <c>InternalsVisibleTo</c>): a small
/// <see cref="ViewTemplate{TDbContext}"/> subclass registers views over a named projection row, and
/// the produced <see cref="ViewMetadata.Fields"/> are asserted. The projection delegate is captured,
/// never executed, so a trivial empty queryable suffices.
/// </summary>
public sealed class DefaultAllowTests
{
    /// <summary>
    /// R5.1: every projected field is filterable by default (no per-field configuration applied).
    /// </summary>
    [Test]
    public async Task All_Fields_Filterable_By_Default()
    {
        var fields = BuildView(DefaultAllowTemplate.DefaultsView).Fields;

        await Assert.That(fields.Count).IsEqualTo(4);
        foreach (var field in fields)
        {
            await Assert.That(field.IsFilterable).IsTrue();
        }
    }

    /// <summary>
    /// R5.2: every projected field is sortable by default (no per-field configuration applied).
    /// </summary>
    [Test]
    public async Task All_Fields_Sortable_By_Default()
    {
        var fields = BuildView(DefaultAllowTemplate.DefaultsView).Fields;

        foreach (var field in fields)
        {
            await Assert.That(field.IsSortable).IsTrue();
        }
    }

    /// <summary>
    /// R5.3: a <see cref="string"/> field participates in global search by default.
    /// </summary>
    [Test]
    public async Task String_Field_Searchable_By_Default()
    {
        var name = Field(DefaultAllowTemplate.DefaultsView, nameof(SearchRow.Name));

        await Assert.That(name.ClrType).IsEqualTo(typeof(string));
        await Assert.That(name.IsSearchable).IsTrue();
    }

    /// <summary>
    /// R5.4: numeric (<see cref="int"/>, <see cref="decimal"/>) and date (<see cref="DateTime"/>)
    /// fields never participate in global search, even though they are filterable/sortable by default.
    /// </summary>
    [Test]
    [Arguments(nameof(SearchRow.Id))]
    [Arguments(nameof(SearchRow.Price))]
    [Arguments(nameof(SearchRow.CreatedOn))]
    public async Task Numeric_And_Date_Fields_Not_Searchable(string fieldName)
    {
        var field = Field(DefaultAllowTemplate.DefaultsView, fieldName);

        await Assert.That(field.ClrType).IsNotEqualTo(typeof(string));
        await Assert.That(field.IsSearchable).IsFalse();
    }

    /// <summary>
    /// R5.3 (opt-out): <c>Searchable(false)</c> excludes an otherwise-searchable string field from
    /// global search, while leaving filter/sort defaults intact.
    /// </summary>
    [Test]
    public async Task Searchable_False_Opts_Out_String_Field()
    {
        var name = Field(DefaultAllowTemplate.OptOutView, nameof(SearchRow.Name));

        await Assert.That(name.IsSearchable).IsFalse();
        // Opting out of search must not silently disable the other default-allow flags.
        await Assert.That(name.IsFilterable).IsTrue();
        await Assert.That(name.IsSortable).IsTrue();
    }

    /// <summary>
    /// R5.1 (opt-out): <c>Filterable(false)</c> excludes a field from client filtering.
    /// </summary>
    [Test]
    public async Task Filterable_False_Opts_Out_Field()
    {
        var id = Field(DefaultAllowTemplate.OptOutView, nameof(SearchRow.Id));

        await Assert.That(id.IsFilterable).IsFalse();
        // Sort default is independent and remains allowed.
        await Assert.That(id.IsSortable).IsTrue();
    }

    /// <summary>
    /// R5.2 (opt-out): <c>Sortable(false)</c> excludes a field from client sorting.
    /// </summary>
    [Test]
    public async Task Sortable_False_Opts_Out_Field()
    {
        var price = Field(DefaultAllowTemplate.OptOutView, nameof(SearchRow.Price));

        await Assert.That(price.IsSortable).IsFalse();
        // Filter default is independent and remains allowed.
        await Assert.That(price.IsFilterable).IsTrue();
    }

    /// <summary>
    /// Sanity check on the default per-type operator whitelist (optional, design.md note): a string
    /// field defaults to the text-search operator group while a numeric field defaults to ordered
    /// comparisons and explicitly does NOT include the text <see cref="FilterOperator.Contains"/>.
    /// </summary>
    [Test]
    public async Task Default_Operators_Differ_Between_String_And_Numeric()
    {
        var name = Field(DefaultAllowTemplate.DefaultsView, nameof(SearchRow.Name));
        var price = Field(DefaultAllowTemplate.DefaultsView, nameof(SearchRow.Price));

        // String → text group (Contains/StartsWith/EndsWith) is available.
        await Assert.That(name.AllowedOperators.HasFlag(FilterOperator.Contains)).IsTrue();

        // Numeric → ordered comparison available, text Contains is not.
        await Assert.That(price.AllowedOperators.HasFlag(FilterOperator.GreaterThanOrEqual)).IsTrue();
        await Assert.That(price.AllowedOperators.HasFlag(FilterOperator.Contains)).IsFalse();
    }

    /// <summary>
    /// Builds the template via the public Gaya A path and returns the metadata for the named view.
    /// </summary>
    [SuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification = "Test exercises the runtime reflection path of Gaya A authoring by design; trimming is not used for tests.")]
    private static ViewMetadata BuildView(string viewName)
    {
        var definitions = new DefaultAllowTemplate().BuildViews();
        return definitions.Single(d => d.Metadata.Name == viewName).Metadata;
    }

    /// <summary>Returns the projected field metadata with the given name from the named view.</summary>
    private static FieldMetadata Field(string viewName, string fieldName) =>
        BuildView(viewName).Fields.Single(f => f.Name == fieldName);
}

/// <summary>
/// Minimal data-source stand-in for the Gaya A template. Core constrains
/// <c>ViewTemplate&lt;TDbContext&gt;</c> to <c>class</c> (it is EF-free), so a plain sealed class is a
/// valid context — no EF <c>DbContext</c> is required.
/// </summary>
internal sealed class DummyContext;

/// <summary>
/// Named projection row (not anonymous) so reflection over its public properties yields a stable,
/// asserted field set: one string field (<see cref="Name"/>, searchable by default) plus numeric and
/// date fields (excluded from global search).
/// </summary>
/// <param name="Id">Numeric primary-key-like field — filterable/sortable, not searchable.</param>
/// <param name="Name">String field — filterable/sortable/searchable by default.</param>
/// <param name="Price">Numeric field — filterable/sortable, not searchable.</param>
/// <param name="CreatedOn">Date field — filterable/sortable, not searchable.</param>
internal sealed record SearchRow(int Id, string Name, decimal Price, DateTime CreatedOn);

/// <summary>
/// Gaya A template registering two views over <see cref="SearchRow"/>: one with pure defaults and one
/// that opts individual fields out of filter/sort/search. The projection delegate returns an empty
/// queryable because Core only captures (never executes) it.
/// </summary>
internal sealed class DefaultAllowTemplate : ViewTemplate<DummyContext>
{
    /// <summary>Name of the view that applies no per-field configuration (pure defaults).</summary>
    public const string DefaultsView = "defaults";

    /// <summary>Name of the view that opts fields out of filter/sort/search.</summary>
    public const string OptOutView = "optOut";

    /// <inheritdoc />
    protected override void Configure(IViewTemplateBuilder<DummyContext> views)
    {
        // Defaults: no .Field(...) customization → every field gets default-allow metadata.
        views.AddView(DefaultsView, static (db, sp) => Enumerable.Empty<SearchRow>().AsQueryable());

        // Opt-outs: each flag toggled off on a distinct field so the others stay at their defaults.
        views.AddView(OptOutView, static (db, sp) => Enumerable.Empty<SearchRow>().AsQueryable())
            .Field(x => x.Name, f => f.Searchable(false))
            .Field(x => x.Id, f => f.Filterable(false))
            .Field(x => x.Price, f => f.Sortable(false));
    }
}
