using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Source-generator Phase 2 / Decision Log D118 — masking fail-closed (R7.6) and the D95 author opt-in
/// (R8.4), exercised as focused unit tests (the universal invariants live in the Property 5/6 tests).
/// <list type="bullet">
/// <item>
/// R7.6 — when a masked field's <c>shouldMask</c> predicate or <c>masker</c> transform throws, the
/// <see cref="MaskApplier"/> fails <b>closed</b>: a <see cref="MaskingException"/> surfaces and the
/// field's original value is never written back / emitted.
/// </item>
/// <item>
/// R8.4 — absent an explicit opt-in a masked field is non-filterable and excluded from search (the
/// whitelist rejects a filter/search on it before any query executes); an explicit
/// <c>Filterable(true)</c> / <c>Searchable(true)</c> opt-in is honored as the author's reviewed choice.
/// </item>
/// </list>
/// </summary>
public sealed class MaskingFailClosedAndOptInTests
{
    private const string Il2026 = "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming";
    private const string Why = "Test exercises the runtime reflection authoring/filter path by design; trimming is not used for tests.";

    // --- R7.6: masking fails closed -----------------------------------------------------------------

    /// <summary>
    /// R7.6: a throwing <c>shouldMask</c> predicate fails closed. The predicate is evaluated once when
    /// the applier is built, so <see cref="MaskApplier.Create"/> surfaces a <see cref="MaskingException"/>
    /// rather than silently falling back to emitting the original value.
    /// </summary>
    [Test]
    public async Task Throwing_Predicate_Fails_Closed_On_Create()
    {
        const string view = "mask-fail-closed-predicate";
        MaskSpecRegistry.Register(view, new[]
        {
            new MaskSpec(
                nameof(SecretRow.Secret),
                _ => throw new InvalidOperationException("predicate boom"),
                _ => "***"),
        });

        using var services = new ServiceCollection().BuildServiceProvider();
        var accessors = new[] { SecretAccessor() };

        await Assert.That(() => MaskApplier.Create(view, accessors, services))
            .Throws<MaskingException>();
    }

    /// <summary>
    /// R7.6: a throwing <c>masker</c> transform fails closed. The predicate returns <see langword="true"/>,
    /// so the applier is built, but applying it to a row surfaces a <see cref="MaskingException"/> and the
    /// row's original value is left untouched — the original is never written back or emitted.
    /// </summary>
    [Test]
    public async Task Throwing_Masker_Fails_Closed_And_Never_Emits_Original()
    {
        const string view = "mask-fail-closed-masker";
        MaskSpecRegistry.Register(view, new[]
        {
            new MaskSpec(
                nameof(SecretRow.Secret),
                _ => true,
                _ => throw new InvalidOperationException("masker boom")),
        });

        using var services = new ServiceCollection().BuildServiceProvider();
        var applier = MaskApplier.Create(view, new[] { SecretAccessor() }, services);
        var row = new SecretRow { Secret = "top-secret" };

        var thrown = Capture(() => applier.Apply(row));

        await Assert.That(thrown).IsTypeOf<MaskingException>();
        // Fail-closed: the masked value was never written, so nothing partial leaks. Crucially, the
        // applier surfaced an error instead of returning the row with its original value.
        await Assert.That(row.Secret).IsEqualTo("top-secret");
    }

    /// <summary>
    /// R7.6: a throwing accessor getter (reading the pre-mask value) also fails closed with a
    /// <see cref="MaskingException"/>; the original value is never emitted.
    /// </summary>
    [Test]
    public async Task Throwing_Accessor_Get_Fails_Closed()
    {
        const string view = "mask-fail-closed-getter";
        MaskSpecRegistry.Register(view, new[]
        {
            new MaskSpec(nameof(SecretRow.Secret), _ => true, _ => "***"),
        });

        using var services = new ServiceCollection().BuildServiceProvider();
        var throwingAccessor = new MaskAccessor(
            nameof(SecretRow.Secret),
            _ => throw new InvalidOperationException("get boom"),
            (r, _) => r);
        var applier = MaskApplier.Create(view, new[] { throwingAccessor }, services);

        await Assert.That(() => applier.Apply(new SecretRow { Secret = "top-secret" }))
            .Throws<MaskingException>();
    }

    // --- R8.4: D95 author opt-in --------------------------------------------------------------------

    /// <summary>
    /// R8.4 (default): a masked field with no opt-in is non-filterable and non-searchable, so a client
    /// filter or search leaf targeting it is rejected by the field whitelist with
    /// <see cref="FilterErrorCode.FieldNotAllowed"/> before any query is built — no result, no probing
    /// channel.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Masked_Field_Without_OptIn_Is_Rejected_For_Filter_And_Search()
    {
        var view = OptInMetadata();
        var email = Field(view, nameof(OptInRow.Email));

        // D95 default (R8.4): neither filterable nor searchable.
        await Assert.That(email.IsMaskable).IsTrue();
        await Assert.That(email.IsFilterable).IsFalse();
        await Assert.That(email.IsSearchable).IsFalse();

        // Filtering is rejected through the whitelist path (R8.1/R8.4).
        var filterRejection = CaptureFilter(
            new FilterLeaf(nameof(OptInRow.Email), FilterOperator.Equals, "x"), FilterOrigin.Filter, view);
        await Assert.That(filterRejection.Code).IsEqualTo(FilterErrorCode.FieldNotAllowed);
        await Assert.That(filterRejection.Field).IsEqualTo(nameof(OptInRow.Email));

        // Search exclusion holds: a Contains over the masked-without-opt-in string is rejected too.
        var searchRejection = CaptureFilter(
            new FilterLeaf(nameof(OptInRow.Email), FilterOperator.Contains, "x"), FilterOrigin.Search, view);
        await Assert.That(searchRejection.Code).IsEqualTo(FilterErrorCode.FieldNotAllowed);
        await Assert.That(searchRejection.Field).IsEqualTo(nameof(OptInRow.Email));
    }

    /// <summary>
    /// R8.4 (filter opt-in honored): a masked field explicitly marked <c>Filterable(true)</c> is
    /// filterable, and a client filter leaf targeting it compiles (is accepted) rather than being
    /// rejected. The field still falls back to the masked search-exclusion default.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Masked_Field_With_Explicit_Filterable_OptIn_Is_Filterable()
    {
        var view = OptInMetadata();
        var phone = Field(view, nameof(OptInRow.Phone));

        await Assert.That(phone.IsMaskable).IsTrue();
        await Assert.That(phone.IsFilterable).IsTrue();   // explicit opt-in wins (R8.4)
        await Assert.That(phone.IsSearchable).IsFalse();  // not opted into search → masked default

        // The filter is honored: compilation succeeds (no FilterValidationException).
        var predicate = Compile<OptInRow>(
            new FilterLeaf(nameof(OptInRow.Phone), FilterOperator.Equals, "555"), FilterOrigin.Filter, view);
        await Assert.That(predicate).IsNotNull();
    }

    /// <summary>
    /// R8.4 (search opt-in honored): a masked string field explicitly marked <c>Searchable(true)</c>
    /// participates in global search, and a search leaf targeting it compiles. The field still falls back
    /// to the masked non-filterable default.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Masked_Field_With_Explicit_Searchable_OptIn_Is_Searchable()
    {
        var view = OptInMetadata();
        var note = Field(view, nameof(OptInRow.Note));

        await Assert.That(note.IsMaskable).IsTrue();
        await Assert.That(note.IsSearchable).IsTrue();   // explicit opt-in wins (R8.4)
        await Assert.That(note.IsFilterable).IsFalse();  // not opted into filter → masked default

        var predicate = Compile<OptInRow>(
            new FilterLeaf(nameof(OptInRow.Note), FilterOperator.Contains, "abc"), FilterOrigin.Search, view);
        await Assert.That(predicate).IsNotNull();
    }

    // --- Helpers ------------------------------------------------------------------------------------

    private static MaskAccessor SecretAccessor() =>
        new(
            nameof(SecretRow.Secret),
            r => ((SecretRow)r).Secret,
            (r, v) =>
            {
                ((SecretRow)r).Secret = (string)v!;
                return r;
            });

    private static Exception Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Expected an exception, but none was thrown.");
    }

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static ViewMetadata OptInMetadata()
    {
        var services = new ServiceCollection();
        services.AddVista(v => v.Register<OptInView>());
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IViewRegistry>();
        return registry.Get("mask-optin")
            ?? throw new InvalidOperationException("View 'mask-optin' was not registered.");
    }

    private static FieldMetadata Field(ViewMetadata view, string name) =>
        view.Fields.Single(f => f.Name == name);

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static FilterValidationException CaptureFilter(FilterNode node, FilterOrigin origin, ViewMetadata view)
    {
        try
        {
            _ = new FilterCompiler().Compile<OptInRow>(node, origin, view);
        }
        catch (FilterValidationException ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Expected a FilterValidationException, but none was thrown.");
    }

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static Expression<Func<T, bool>> Compile<T>(FilterNode node, FilterOrigin origin, ViewMetadata view) =>
        new FilterCompiler().Compile<T>(node, origin, view);
}

/// <summary>A minimal mutable row used to exercise <see cref="MaskApplier"/> fail-closed behavior.</summary>
internal sealed class SecretRow
{
    public string Secret { get; set; } = "";
}

/// <summary>The EF source entity for <see cref="OptInView"/> (POCO; not materialized in a metadata-only test).</summary>
internal sealed class OptInSource
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>The read projection for <see cref="OptInView"/>.</summary>
internal sealed class OptInRow
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>
/// A Style B read-only view exercising the D95 opt-in (R8.4): <c>Email</c> is masked with no opt-in
/// (→ non-filterable + non-searchable), <c>Phone</c> is masked but explicitly <c>Filterable()</c>
/// (→ filterable, still non-searchable), and <c>Note</c> is masked but explicitly <c>Searchable()</c>
/// (→ searchable, still non-filterable). <c>Id</c> is the unmasked primary key.
/// </summary>
internal sealed class OptInView : View<OptInRow>
{
    protected override void Configure(IViewBuilder<OptInRow> b) =>
        b.Named("mask-optin")
         .From<OptInSource>(s => new OptInRow { Id = s.Id, Email = s.Email, Phone = s.Phone, Note = s.Note })
         .Field(x => x.Id, f => f.PrimaryKey())
         .MaskField(x => x.Email, _ => true, _ => "***")
         .MaskField(x => x.Phone, _ => true, _ => "***")
         .MaskField(x => x.Note, _ => true, _ => "***")
         .Field(x => x.Phone, f => f.Filterable())
         .Field(x => x.Note, f => f.Searchable());
}
