// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using a2n.Vista.Export;
using a2n.Vista.GeneratorStyleASample;
using a2n.Vista.Metadata;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the export-value parity of the source-generated read accessors emitted for the
/// covered <b>named-<c>TRow</c></b> Style A (central-template) views (spec style-a-coverage, task 8.4;
/// Decision Log D129/D130; Property 3; Requirements 2.3, 6.4).
/// <para>
/// The referenced <c>a2n.Vista.GeneratorStyleASample</c> assembly hosts three representative Style A views
/// authored as <c>AddView&lt;TRow&gt;(name, projection)</c> call sites in a single
/// <c>ViewTemplate&lt;TDbContext&gt;</c>. For each view whose read row is a <b>named</b> type, the fifth
/// incremental generator (<c>StyleAShapeGenerator</c>, D129) emits a reflection-free field-accessor map
/// (<c>fieldName → Func&lt;object, object?&gt;</c>, a compile-time cast + member read) and registers it into
/// the Core-resident <see cref="ViewAccessorRegistry"/> from a <c>[ModuleInitializer]</c>, keyed by the
/// <b>constant</b> <c>AddView</c> name. The covered named-<c>TRow</c> views this property quantifies over are
/// therefore:
/// </para>
/// <list type="bullet">
///   <item><description><c>stylea-catalog-items</c> (read-only) → <see cref="CatalogItemRow"/>, whose
///   members span the shape spectrum: a scalar (<c>int</c>), a nullable value type (<c>int?</c>), an enum,
///   a collection (<c>IReadOnlyList&lt;string&gt;</c>), and a <c>byte[]</c>;</description></item>
///   <item><description><c>stylea-subscriptions</c> (writable) → <see cref="SubscriptionRow"/> (a scalar,
///   a string, a value-type scalar, a nullable <c>DateTime</c>, and an enum).</description></item>
/// </list>
/// <para>
/// The <c>stylea-audit-entries</c> view is deliberately excluded: its read projection is an <b>anonymous</b>
/// type, unnameable in generated source, so no read accessor exists for it and its export read stays on the
/// reflection path by design (D96/D130). It is therefore not a covered named-<c>TRow</c> view.
/// </para>
/// <para>
/// <b>Behavioral_Oracle.</b> The reference read is a reflection <see cref="PropertyInfo.GetValue(object)"/>
/// of the same member on the same row — the exact reflection read the export path falls back to when no
/// generated accessor is registered. The property proves that, for every covered named-<c>TRow</c> view and
/// for any row value, the value read through the generated accessor — both directly from
/// <see cref="ViewAccessorRegistry"/> and through the public export seam
/// <see cref="ExportColumns.Value(string, object?, string)"/> (which prefers a registered accessor) — equals
/// the reflection-oracle read for the same field (R2.3, R6.4).
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Model-based, resolution-then-value.</b> Which read path serves a field depends only on the (view,
/// field) pair, not on the row value, so the resolution half — every covered view's every public readable
/// member has a <b>generated</b> accessor registered — is asserted once, up front, over the fixed member
/// set. Asserting the accessor is present is what makes the value half meaningful: <c>ExportColumns.Value</c>
/// only reaches the reflection fallback when no accessor is registered, so without this guard a missing
/// accessor would let the value comparison degrade to reflection-vs-reflection and pass vacuously. The
/// value half — the accessor read equals the reflection read — then depends on the row, so it is sampled
/// over random row values (minimum 100 iterations).
/// </para>
/// <para>
/// <b>Fixture module load.</b> The generated accessor maps are registered by the fixture assembly's
/// <c>[ModuleInitializer]</c>s at module load, keyed by the constant <c>AddView</c> name. Referencing a
/// fixture type via <c>typeof</c> alone does not guarantee the module <c>.cctor</c> has run, so the static
/// constructor runs it explicitly — mirroring <see cref="StyleASeamCoexistenceTests"/> — so the stores are
/// populated before any case reads them, deterministic whether this class runs in isolation or as part of
/// the full suite.
/// </para>
/// <para>
/// <b>Value comparison.</b> The generated accessor and the reflection read return the very same member
/// value of the very same row object, so reference-type members (the collection and the <c>byte[]</c>)
/// compare reference-equal and value-type members (the scalar, nullable, enum, <c>DateTime?</c>) compare
/// value-equal once boxed. The comparison honors both and adds a structural fallback for enumerables purely
/// as a defensive, self-documenting guard.
/// </para>
/// </remarks>
public sealed class StyleAExportAccessorValueParityPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    // The constant AddView names the generated Style A accessor maps are keyed under (D129).
    private const string CatalogItemsView = GeneratorStyleASampleViews.CatalogItemsViewName;   // "stylea-catalog-items"
    private const string SubscriptionsView = GeneratorStyleASampleViews.SubscriptionsViewName; // "stylea-subscriptions"

    static StyleAExportAccessorValueParityPropertyTests()
    {
        // Force the fixture assembly's [ModuleInitializer]s to run so the generated Style A export accessor
        // maps are registered into ViewAccessorRegistry before any case reads them. Referencing a type via
        // typeof alone does not guarantee the module .cctor has run, so run it explicitly — mirroring
        // StyleASeamCoexistenceTests / JsonContextLayeringGuardTests.
        RuntimeHelpers.RunModuleConstructor(typeof(CatalogItemRow).Assembly.ManifestModule.ModuleHandle);
    }

    // Feature: style-a-coverage, Property 3: Export-accessor value parity with the reflection oracle
    // (model-based).
    //
    // For any covered named-TRow Style A view and for any row value, the value read for a field through the
    // generated accessor (via ViewAccessorRegistry / ExportColumns.Value(view.Name, row, field)) equals the
    // value read through the Behavioral_Oracle reflection read (PropertyInfo.GetValue) for the same field.
    //
    // Validates: Requirements 2.3, 6.4
    [Test]
    public void Generated_Export_Accessor_Read_Equals_Reflection_Oracle_For_Covered_NamedRow_Views()
    {
        // Feature: style-a-coverage, Property 3: Export-accessor value parity with the reflection oracle
        // (model-based).

        // Half 1 (resolution — (view, field)-based, deterministic): every public readable member of each
        // covered named-TRow view has a GENERATED accessor registered under the constant view name. This
        // fails fast, with a clear message, if the fixture assembly's [ModuleInitializer] did not register a
        // view's accessor map — and guarantees the value half below exercises the generated path rather than
        // silently degrading to the reflection fallback inside ExportColumns.Value.
        AssertEveryMemberHasGeneratedAccessor(CatalogItemsView, typeof(CatalogItemRow));
        AssertEveryMemberHasGeneratedAccessor(SubscriptionsView, typeof(SubscriptionRow));

        // Half 2 (parity — value-based): for random row values, the value read through the generated accessor
        // equals the reflection-oracle read for every field, across the full member-shape spectrum.
        var genCase =
            from catalogRow in GenCatalogRow
            from subscriptionRow in GenSubRow
            select (catalogRow, subscriptionRow);

        genCase.Sample(
            tuple =>
            {
                var (catalogRow, subscriptionRow) = tuple;
                AssertMemberValueParity(CatalogItemsView, catalogRow);
                AssertMemberValueParity(SubscriptionsView, subscriptionRow);
            },
            iter: Iterations);
    }

    // -- Resolution (Half 1) ----------------------------------------------------------------------------

    /// <summary>
    /// Asserts that every public readable member of <paramref name="rowType"/> has a generated accessor
    /// registered in <see cref="ViewAccessorRegistry"/> under <paramref name="viewName"/>, so the value
    /// half reads through the generated accessor (not the reflection fallback).
    /// </summary>
    private static void AssertEveryMemberHasGeneratedAccessor(string viewName, Type rowType)
    {
        foreach (var member in PublicReadableProperties(rowType))
        {
            if (!ViewAccessorRegistry.TryGetAccessor(viewName, member.Name, out _))
            {
                throw new Exception(
                    $"Covered named-TRow Style A view '{viewName}' has no generated export accessor for the " +
                    $"public readable member '{rowType.Name}.{member.Name}'. Its accessor map must be " +
                    "registered by the a2n.Vista.GeneratorStyleASample [ModuleInitializer] into " +
                    "ViewAccessorRegistry, keyed by the constant AddView name (D129); without it the export " +
                    "read would silently fall back to reflection (R2.3, R6.4).");
            }
        }
    }

    // -- Parity (Half 2) --------------------------------------------------------------------------------

    /// <summary>
    /// For every public readable member of <paramref name="row"/>, asserts that the value read through the
    /// generated accessor — both directly from <see cref="ViewAccessorRegistry"/> and through the public
    /// export seam <see cref="ExportColumns.Value(string, object?, string)"/> — equals the reflection-oracle
    /// read of the same member on the same row.
    /// </summary>
    private static void AssertMemberValueParity(string viewName, object row)
    {
        foreach (var member in PublicReadableProperties(row.GetType()))
        {
            // Behavioral_Oracle: the reflection read the export path falls back to for an uncovered view.
            var oracle = member.GetValue(row);

            // The generated accessor read through the public export seam (prefers a registered accessor).
            var throughExportSeam = ExportColumns.Value(viewName, row, member.Name);

            // The generated accessor read directly from the registry — proves the value came from the
            // generated cast + member read, not the reflection fallback.
            if (!ViewAccessorRegistry.TryGetAccessor(viewName, member.Name, out var accessor))
            {
                throw new Exception(
                    $"Generated accessor for '{viewName}'.'{member.Name}' vanished between resolution and " +
                    "value read; the store is first-wins and never removes entries, so this should be " +
                    "impossible.");
            }

            var throughAccessor = accessor(row);

            if (!ValuesEqual(throughExportSeam, oracle))
            {
                throw new Exception(
                    $"Export seam value for '{viewName}'.'{member.Name}' ({Describe(member.PropertyType)}) " +
                    $"differed from the reflection oracle.\n  accessor (seam): {Render(throughExportSeam)}\n" +
                    $"  oracle:          {Render(oracle)}");
            }

            if (!ValuesEqual(throughAccessor, oracle))
            {
                throw new Exception(
                    $"Generated accessor value for '{viewName}'.'{member.Name}' " +
                    $"({Describe(member.PropertyType)}) differed from the reflection oracle.\n" +
                    $"  accessor: {Render(throughAccessor)}\n  oracle:   {Render(oracle)}");
            }
        }
    }

    /// <summary>
    /// The public, readable, non-indexer instance properties of <paramref name="rowType"/> — the exact
    /// member set the generated accessor map is emitted over (R2.1) and the reflection oracle reads.
    /// </summary>
    private static IEnumerable<PropertyInfo> PublicReadableProperties(Type rowType) =>
        rowType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

    /// <summary>
    /// Value equality that matches how the generated accessor and the reflection read return values: both
    /// return the same member of the same row, so reference-type members (collection, <c>byte[]</c>) are
    /// reference-equal and value-type members (scalar, nullable, enum, <c>DateTime?</c>) are value-equal
    /// once boxed. A structural pass over enumerables is a defensive fallback (never actually needed while
    /// both paths return the same instance).
    /// </summary>
    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (ReferenceEquals(a, b) || a.Equals(b))
        {
            return true;
        }

        // Defensive structural comparison for collections / byte[] (both paths return the same instance, so
        // this only guards against a hypothetical future copy on either read path). Strings are IEnumerable
        // but are already settled by Equals above, so they never reach here.
        if (a is IEnumerable left and not string && b is IEnumerable right and not string)
        {
            var leftItems = left.Cast<object?>().ToList();
            var rightItems = right.Cast<object?>().ToList();
            if (leftItems.Count != rightItems.Count)
            {
                return false;
            }

            for (var i = 0; i < leftItems.Count; i++)
            {
                if (!ValuesEqual(leftItems[i], rightItems[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private static string Describe(Type type) => type.Name;

    /// <summary>Renders a value for a readable counterexample message (collections as bracketed lists).</summary>
    private static string Render(object? value) =>
        value switch
        {
            null => "(null)",
            string s => $"\"{s}\"",
            IEnumerable e => "[" + string.Join(", ", e.Cast<object?>().Select(Render)) + "]",
            _ => value.ToString() ?? "(null)",
        };

    // -- Value generators (mirror the covered named-TRow fixtures' member shapes) -----------------------

    private static readonly string[] TextPool =
        { "", "Alice", "Bob", "naïve café", "a\"quoted\"b", "back\\slash", "tab\tend", "  spaced  " };

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    private static readonly Gen<byte[]> GenBytes =
        Gen.Int[0, 255].Select(i => (byte)i).Array[0, 8];

    // An optional DateTime with a fixed Kind (Unspecified) so the value space is well-defined; the accessor
    // and the reflection read return the same instant regardless, so parity holds for any concrete value.
    private static readonly Gen<DateTime?> GenOptionalDate =
        from present in Gen.Bool
        from minutes in Gen.Int[0, 5_000_000]
        select present ? new DateTime(2000, 1, 1).AddMinutes(minutes) : (DateTime?)null;

    private static readonly Gen<int?> GenOptionalInt =
        from present in Gen.Bool
        from value in Gen.Int[-100_000, 100_000]
        select present ? value : (int?)null;

    private static readonly Gen<CatalogItemStatus> GenCatalogStatus =
        Gen.Int[0, 2].Select(i => (CatalogItemStatus)i);

    private static readonly Gen<SubscriptionTier> GenSubscriptionTier =
        Gen.Int[0, 2].Select(i => (SubscriptionTier)i);

    private static readonly Gen<CatalogItemRow> GenCatalogRow =
        from itemId in Gen.Int[-100_000, 100_000]
        from name in Pick(TextPool)
        from reorderLevel in GenOptionalInt
        from status in GenCatalogStatus
        from tags in Pick(TextPool).List[0, 4]
        from thumbnail in GenBytes
        select new CatalogItemRow
        {
            ItemId = itemId,
            Name = name,
            ReorderLevel = reorderLevel,
            Status = status,
            Tags = tags,
            Thumbnail = thumbnail,
        };

    private static readonly Gen<SubscriptionRow> GenSubRow =
        from id in Gen.Int[-100_000, 100_000]
        from planName in Pick(TextPool)
        from seatCount in Gen.Int[0, 10_000]
        from renewsOn in GenOptionalDate
        from tier in GenSubscriptionTier
        select new SubscriptionRow
        {
            SubscriptionId = id,
            PlanName = planName,
            SeatCount = seatCount,
            RenewsOn = renewsOn,
            Tier = tier,
        };
}
