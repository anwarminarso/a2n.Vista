using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using a2n.Vista.Authoring;
using a2n.Vista.Metadata;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Source-generator Phase 2 / D118, Requirement R7.1 — the authoring builder captures BOTH the
/// request-scoped <c>shouldMask</c> predicate (previously discarded) AND the <c>masker</c> transform for
/// each <c>MaskField</c> declaration, one ordered <see cref="MaskSpec"/> per declaration, so both reach
/// runtime.
/// <para>
/// The internal <c>ViewBuilder&lt;TQuery&gt;</c> and its <c>MaskSpecs</c> property are intentionally not
/// surfaced to the test assembly (Core exposes internals only to <c>a2n.Vista.EntityFrameworkCore</c>;
/// the documented decision in <see cref="TypingInvariantTests"/> avoids broad <c>InternalsVisibleTo</c>
/// because it would force the <c>protected internal Configure</c> overrides to change accessibility).
/// These tests therefore reach the internal builder and its <c>MaskSpecs</c> through reflection while
/// asserting against the <b>public</b> <see cref="MaskSpec"/> record (FieldName, ShouldMask, Masker).
/// </para>
/// Assertions:
/// <list type="bullet">
/// <item>both delegates are captured and retrievable per field;</item>
/// <item>specs are ordered by declaration order;</item>
/// <item>each field keeps its own predicate (not swapped, not discarded, not shared);</item>
/// <item>the boxed, non-generic masker applies the original typed masker to the pre-mask value.</item>
/// </list>
/// </summary>
public sealed class MaskAuthoringCaptureTests
{
    /// <summary>
    /// R7.1: two <c>MaskField</c> declarations produce two <see cref="MaskSpec"/>s, in declaration order,
    /// each carrying the field name plus both runtime delegates.
    /// </summary>
    [Test]
    public async Task MaskField_Captures_Predicate_And_Masker_Per_Field_In_Order()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var builder = NewBuilder<CaptureRow>();
        ((IViewBuilder<CaptureRow>)builder)
            .Named("capture")
            .From<CaptureSource>(s => new CaptureRow { Id = s.Id, Email = s.Email, Phone = s.Phone })
            // Distinct predicates and maskers per field so a swap/discard would be detected.
            .MaskField(x => x.Email, _ => true, value => "EMAIL:" + value)
            .MaskField(x => x.Phone, _ => false, value => "PHONE:" + value);

        var specs = MaskSpecsOf(builder);

        await Assert.That(specs.Count).IsEqualTo(2);

        // Declaration order is preserved (R7.1).
        await Assert.That(specs[0].FieldName).IsEqualTo(nameof(CaptureRow.Email));
        await Assert.That(specs[1].FieldName).IsEqualTo(nameof(CaptureRow.Phone));

        // The predicate is captured (no longer discarded) and is the per-field one.
        await Assert.That(specs[0].ShouldMask(services)).IsTrue();
        await Assert.That(specs[1].ShouldMask(services)).IsFalse();

        // The masker is captured and applies the ORIGINAL transform to the pre-mask value.
        await Assert.That(specs[0].Masker("a@b.com")).IsEqualTo("EMAIL:a@b.com");
        await Assert.That(specs[1].Masker("555-1234")).IsEqualTo("PHONE:555-1234");
    }

    /// <summary>
    /// R7.1: the boxed, non-generic masker round-trips a typed (non-string) transform correctly —
    /// the original masker runs on the unboxed value and the result is re-boxed.
    /// </summary>
    [Test]
    public async Task Boxed_Masker_Applies_Original_Typed_Masker()
    {
        var builder = NewBuilder<CaptureRow>();
        ((IViewBuilder<CaptureRow>)builder)
            .Named("capture-typed")
            .From<CaptureSource>(s => new CaptureRow { Id = s.Id, Email = s.Email, Phone = s.Phone })
            // An int field masker that doubles the value; proves the boxed wrapper casts to TProp,
            // applies the original masker, and re-boxes the result.
            .MaskField(x => x.Id, _ => true, value => value * 2);

        var spec = MaskSpecsOf(builder).Single();

        await Assert.That(spec.FieldName).IsEqualTo(nameof(CaptureRow.Id));
        await Assert.That(spec.Masker(21)).IsEqualTo((object)42);
    }

    /// <summary>A view with no <c>MaskField</c> declarations captures no specs.</summary>
    [Test]
    public async Task No_MaskField_Captures_No_Specs()
    {
        var builder = NewBuilder<CaptureRow>();
        ((IViewBuilder<CaptureRow>)builder)
            .Named("capture-none")
            .From<CaptureSource>(s => new CaptureRow { Id = s.Id, Email = s.Email, Phone = s.Phone });

        await Assert.That(MaskSpecsOf(builder).Count).IsEqualTo(0);
    }

    // --- Reflection bridge to the internal ViewBuilder<TQuery>.MaskSpecs surface (R7.1) ---

    private static object NewBuilder<TQuery>()
        where TQuery : class
    {
        // ViewBuilder<TQuery> is internal to a2n.Vista.Core; construct it via reflection so this test
        // needs no broad InternalsVisibleTo (see class remarks). The fluent surface is the public
        // IViewBuilder<TQuery>.
        var openType = typeof(IViewBuilder<>).Assembly.GetType("a2n.Vista.Authoring.ViewBuilder`1")
            ?? throw new InvalidOperationException("Could not locate the internal ViewBuilder<TQuery> type.");
        var closedType = openType.MakeGenericType(typeof(TQuery));
        return Activator.CreateInstance(closedType, nonPublic: true)
            ?? throw new InvalidOperationException("Could not construct ViewBuilder<TQuery>.");
    }

    private static IReadOnlyList<MaskSpec> MaskSpecsOf(object builder)
    {
        var property = builder.GetType().GetProperty(
            "MaskSpecs",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ViewBuilder<TQuery>.MaskSpecs was not found.");
        var value = (IEnumerable)property.GetValue(builder)!;
        return value.Cast<MaskSpec>().ToList();
    }
}

/// <summary>The EF source entity for the capture tests (POCO; never materialized — authoring-only).</summary>
internal sealed class CaptureSource
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
}

/// <summary>The read projection for the capture tests.</summary>
internal sealed class CaptureRow
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
}
