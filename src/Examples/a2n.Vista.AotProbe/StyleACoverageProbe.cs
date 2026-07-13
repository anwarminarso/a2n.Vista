// Licensed to the a2n.Vista project. Published artifact — English only.
//
// M9 Style A coverage AOT verification (spec style-a-coverage, Task 9.1, R9.1/R9.2/R9.3;
// Decision Log D129/D130).
//
// This probe drives the COVERED Style A slice and the D96 asymmetry case under the strict
// (IL2026/IL3050-as-error) trim/AOT analyzer, proving the coverage claim AND the permanent boundary
// mechanically — a green build IS the verification.
//
// The two fixtures (see StyleAProbeView.cs) are authored centrally in StyleACoverageProbeTemplate via
// ViewTemplate<TDbContext>.AddView<TRow> call sites, which the fifth incremental generator
// (StyleAShapeGenerator, D129) recognizes. It emits — INTO this assembly, keyed by the CONSTANT AddView
// name — an export accessor map + a read-DTO IJsonTypeInfoResolver for the named-row catalog view, and a
// TCrud-ONLY IJsonTypeInfoResolver for the anonymous-row audit view. Their [ModuleInitializer]s register
// those artifacts into a2n.Vista.Core's ViewAccessorRegistry (D117) and GeneratedJsonContextStore (D125)
// at module load. This probe resolves and drives those REAL generated artifacts.
//
// WHAT EACH PART VERIFIES:
//
//   Part 1 — COVERED NAMED-TRow READ VIEW (aotprobe-stylea-catalog), R9.1/R9.3:
//     * EXPORT: reads each StyleACatalogRow field through ExportColumns.Value(viewName, row, field) — the
//       AOT-clean 3-arg overload that consults ViewAccessorRegistry FIRST. The generated cast+member-read
//       accessor serves the value (never the RUC reflection Value(row, name) overload), and the read value
//       equals a direct member read (parity, R9.3 / Property 3 half).
//     * SERIALIZATION: serializes StyleACatalogRow, ViewListResult<StyleACatalogRow>, and
//       PagedResult<StyleACatalogRow> through the Serialization_Seam (VistaJsonWriter -> VistaJson.Options
//       + TypeInfoResolverChain, AOT-safe JsonTypeInfo overloads). Each type is asserted to resolve from a
//       Generated_View_Context (not reflection — the fallback was removed by HttpSurfaceProbe, and no
//       developer App_Json_Context is registered), so a successful, green resolve can only come from the
//       generated per-view context (R9.1/R9.3). The enum member (Status) rides the AOT-safe GENERIC
//       JsonStringEnumConverter<TEnum> the generated context bakes in, never the RUC non-generic converter.
//
//   Part 2 — WRITABLE ANONYMOUS-TRow VIEW (aotprobe-stylea-audit), the D96 ASYMMETRY, R9.2/R9.3:
//     * WRITE (AOT-clean): binds a write body to the named StyleAAuditCrud through the seam
//       (VistaWriteBinding.BindModel resolves TCrud's JsonTypeInfo via VistaJson.Options and deserializes
//       with the AOT-safe overload) and serializes it back through the seam — all AOT-clean. StyleAAuditCrud
//       is asserted to resolve from a Generated_View_Context, proving the write binding of a view whose read
//       row is anonymous is still AOT-clean (R9.2).
//     * READ (RUC by design): the audit view's read projection is an ANONYMOUS type — unnameable in
//       generated source — so the generator emits NO export accessor and NO read-DTO context for it
//       (asserted: ViewAccessorRegistry has no entry for the audit view). Its read row can therefore only
//       be serialized through the RUC reflection serializer, isolated here behind a narrowly-scoped
//       suppression to demonstrate that the read path WORKS but is NOT required to be AOT-clean (D96/D130).
//       This is the asymmetry WITHIN one view: the write body binds AOT-clean while the read row stays RUC.
//
// Keeping the analyzed surface honest (mirrors the sibling probes):
//   * ExportColumns.Value(viewName, row, field), VistaJsonWriter.Serialize/GetTypeInfo, and
//     VistaWriteBinding.BindModel are the ONLY Vista surface exercised here under the strict analyzer, and
//     none carries an unsuppressed [RequiresUnreferencedCode]/[RequiresDynamicCode] — so driving the
//     covered Style A artifacts THROUGH them is itself the member-level AOT proof.
//   * The single deliberately-RUC call (the anonymous read-row reflection serialize) is isolated behind a
//     narrowly-scoped suppression whose whole purpose is to demonstrate the D96 boundary; it is not part of
//     the covered path.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Export;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Results;

namespace a2n.Vista.AotProbe;

/// <summary>
/// Exercises the covered Style A slice (named-row read view: export accessor + read-DTO serialization) and
/// the D96 asymmetry case (writable anonymous-row view: AOT-clean TCrud write binding while the read row
/// stays RUC) for the M9 Style A coverage AOT verification (Task 9.1).
/// </summary>
internal static class StyleACoverageProbe
{
    /// <summary>
    /// Runs the Style A coverage probe. Assumes the Serialization_Seam is already configured with the
    /// reflection fallback removed (done by <see cref="HttpSurfaceProbe"/> earlier in the run), so a
    /// successful DTO resolve can only come from a Generated_View_Context — never reflection and never a
    /// developer <c>App_Json_Context</c> (none is registered).
    /// </summary>
    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("AOT probe: Style A coverage exercised (D129/D130 — covered slice + D96 asymmetry).");

        RunCoveredNamedRowRead();
        RunWritableAnonymousRowAsymmetry();
    }

    /// <summary>
    /// Part 1 (R9.1/R9.3): the covered named-row read view. Reads every field through the generated export
    /// accessor (parity with a direct member read) and serializes the read DTOs through the seam using the
    /// generated per-view context.
    /// </summary>
    private static void RunCoveredNamedRowRead()
    {
        const string viewName = StyleACoverageProbeTemplate.CatalogViewName;

        // A representative row spanning the emittable-shape spectrum: scalar, string, nullable value type,
        // enum, collection, and byte[].
        var row = new StyleACatalogRow
        {
            ItemId = 42,
            Name = "Reticulating Widget",
            ReorderLevel = 7,
            Status = StyleACatalogStatus.Active,
            Tags = new[] { "hardware", "reticulated" },
            Thumbnail = new byte[] { 0x01, 0x02, 0x03, 0x04 },
        };

        // 1a) The generated accessor map must be registered (by [ModuleInitializer] at module load). A miss
        //     means the StyleAShapeGenerator analyzer did not run/emit for the named-row view.
        if (!ViewAccessorRegistry.TryGetAccessor(viewName, nameof(StyleACatalogRow.ItemId), out _))
        {
            throw new InvalidOperationException(
                $"No generated export accessor map was found for the named-row Style A view '{viewName}'. " +
                "Ensure the source generator analyzer is referenced so the StyleAShapeGenerator emits the " +
                "accessor map and its [ModuleInitializer] registers it into ViewAccessorRegistry at module load.");
        }

        // 1b) EXPORT parity through the AOT-clean 3-arg ExportColumns.Value: it consults the registry FIRST,
        //     so the generated cast+member-read accessor serves each value. Compare to a direct member read
        //     (the oracle value) — value-for-value parity (R9.3 / Property 3 half). No reflection is on this
        //     surface: the RUC Value(row, name) overload is never called.
        AssertExportParity(viewName, row, nameof(StyleACatalogRow.ItemId), row.ItemId);
        AssertExportParity(viewName, row, nameof(StyleACatalogRow.Name), row.Name);
        AssertExportParity(viewName, row, nameof(StyleACatalogRow.ReorderLevel), row.ReorderLevel);
        AssertExportParity(viewName, row, nameof(StyleACatalogRow.Status), row.Status);
        AssertExportParity(viewName, row, nameof(StyleACatalogRow.Tags), row.Tags);
        AssertExportParity(viewName, row, nameof(StyleACatalogRow.Thumbnail), row.Thumbnail);
        Console.WriteLine(
            $"Export('{viewName}'): all fields read through the generated accessor with value parity (R9.3).");

        // 1c) The seam must resolve every read DTO from a Generated_View_Context (reflection fallback
        //     removed, no developer context) — R9.1/R9.3.
        AssertResolvedFromGeneratedContext(typeof(StyleACatalogRow));
        AssertResolvedFromGeneratedContext(typeof(ViewListResult<StyleACatalogRow>));
        AssertResolvedFromGeneratedContext(typeof(PagedResult<StyleACatalogRow>));

        // 1d) Serialize the read DTOs through the seam AOT-clean (AOT-safe JsonTypeInfo overloads).
        var rowJson = VistaJsonWriter.Serialize(row, typeof(StyleACatalogRow));

        var paged = new PagedResult<StyleACatalogRow>(
            Items: new[] { row },
            TotalRows: 1,
            PageIndex: 0,
            PageSize: 50,
            TotalPages: 1);
        var list = new ViewListResult<StyleACatalogRow>(paged, TotalRowsUnfiltered: 1);
        var listJson = VistaJsonWriter.Serialize(list, typeof(ViewListResult<StyleACatalogRow>));

        Console.WriteLine(
            $"Serialize('{viewName}'): read DTOs resolved from the Generated_View_Context and serialized " +
            $"AOT-clean (row {rowJson.Length} chars, list envelope {listJson.Length} chars) — R9.1.");
    }

    /// <summary>
    /// Part 2 (R9.2/R9.3): the D96 asymmetry view. Binds and serializes the named <c>TCrud</c> through the
    /// generated context AOT-clean, while the anonymous read row has no generated artifact and stays RUC by
    /// design.
    /// </summary>
    private static void RunWritableAnonymousRowAsymmetry()
    {
        const string viewName = StyleACoverageProbeTemplate.AuditViewName;

        // 2a) The anonymous read row is unnameable in generated source, so NO export accessor is emitted for
        //     this view — the read-side artifact is absent by design (D96/D130). Probing the members the
        //     anonymous projection selects (none of which can be keyed) must all miss, making the asymmetry
        //     mechanical: the write side is covered below while the read side is not.
        foreach (var candidateField in new[] { "EntryId", "Action", "OccurredAt" })
        {
            if (ViewAccessorRegistry.TryGetAccessor(viewName, candidateField, out _))
            {
                throw new InvalidOperationException(
                    $"The anonymous-row Style A view '{viewName}' must have NO generated export accessor " +
                    $"(field '{candidateField}'): an anonymous read row is unnameable in generated source and " +
                    "stays on the reflection path by design (D96/D130, VISTA0061).");
            }
        }

        // 2b) The named TCrud IS covered — its JsonTypeInfo must resolve from a Generated_View_Context even
        //     though the view's read row is anonymous (R4.2). With the reflection fallback removed and no
        //     developer context, a successful resolve proves the write model is served by the generated
        //     per-view context (R9.2).
        AssertResolvedFromGeneratedContext(typeof(StyleAAuditCrud));

        // 2c) Bind a write body to StyleAAuditCrud through the seam (AOT-clean) and serialize it back
        //     (round-trip) through the seam — the write path of a view whose read row is anonymous is
        //     AOT-clean (R9.2).
        var bound = (StyleAAuditCrud)BindAuditModel(
            "{\"action\":\"Login\",\"severity\":2,\"occurredAt\":null,\"isSensitive\":true}");
        var crudJson = VistaJsonWriter.Serialize(bound, typeof(StyleAAuditCrud));

        Console.WriteLine(
            $"Write('{viewName}'): TCrud (StyleAAuditCrud) bound + serialized through the generated context " +
            $"AOT-clean (action=\"{bound.Action}\", severity={bound.Severity}, sensitive={bound.IsSensitive}); " +
            $"serialized {crudJson.Length} chars — R9.2.");

        // 2d) The READ row stays RUC BY DESIGN. Its projection is anonymous, so it can only be serialized
        //     through the reflection serializer — isolated behind a narrowly-scoped suppression whose sole
        //     purpose is to DEMONSTRATE the D96 boundary. It is NOT required to be AOT-clean (R9.2/D130).
        var anonReadRowJson = SerializeAnonymousReadRowRuc();
        Console.WriteLine(
            $"Read('{viewName}'): anonymous read row serialized ONLY via the RUC reflection path " +
            $"(by design, D96/D130 — not required AOT-clean); {anonReadRowJson.Length} chars.");
    }

    /// <summary>
    /// Reads <paramref name="fieldName"/> from <paramref name="row"/> through the AOT-clean 3-arg
    /// <see cref="ExportColumns.Value(string, object?, string)"/> (which prefers the generated accessor in
    /// <see cref="ViewAccessorRegistry"/>) and asserts the value equals <paramref name="expected"/> (a
    /// direct member read — the Behavioral_Oracle), i.e. value-for-value parity (R9.3).
    /// </summary>
    private static void AssertExportParity(string viewName, object row, string fieldName, object? expected)
    {
        var actual = ExportColumns.Value(viewName, row, fieldName);
        if (!Equals(actual, expected))
        {
            throw new InvalidOperationException(
                $"Export accessor parity failed for '{viewName}'.'{fieldName}': the generated accessor " +
                $"read '{actual ?? "null"}' but the direct member read is '{expected ?? "null"}' (R9.3).");
        }
    }

    /// <summary>
    /// Asserts the Serialization_Seam resolves <paramref name="runtimeType"/> specifically from a
    /// <c>Generated_View_Context</c>. First confirms the seam resolves the type at all (a null resolve
    /// would mean nothing covers it), then confirms one of the generated per-view contexts drained from
    /// <see cref="GeneratedJsonContextStore"/> provides the <see cref="JsonTypeInfo"/> — draining the store
    /// through the very same opaque-handle → <see cref="IJsonTypeInfoResolver"/> cast the AspNetCore seam
    /// performs. With the reflection fallback removed and no developer <c>App_Json_Context</c>, this is the
    /// proof the covered Style A DTO is served by the generated context (R9.1/R9.3).
    /// </summary>
    private static void AssertResolvedFromGeneratedContext(Type runtimeType)
    {
        var typeInfo = VistaJsonWriter.GetTypeInfo(runtimeType);
        if (typeInfo is null)
        {
            throw new InvalidOperationException(
                $"The Serialization_Seam resolved no JsonTypeInfo for '{runtimeType}'. With the reflection " +
                "fallback removed, a covered Style A DTO must resolve from a generated per-view context " +
                "(R9.1/R9.3).");
        }

        foreach (var handle in GeneratedJsonContextStore.All)
        {
            if (((IJsonTypeInfoResolver)handle).GetTypeInfo(runtimeType, VistaJson.Options) is not null)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"No Generated_View_Context in GeneratedJsonContextStore provides a JsonTypeInfo for " +
            $"'{runtimeType}'. With no developer App_Json_Context registered and the reflection fallback " +
            "removed, the covered Style A DTO must be served by the source-generated per-view context " +
            "(R9.1/R9.3) — ensure the StyleAShapeGenerator analyzer emitted it and its [ModuleInitializer] " +
            "registered it into the store at module load.");
    }

    /// <summary>
    /// Binds a JSON write model to <see cref="StyleAAuditCrud"/> through the Serialization_Seam. The
    /// envelope is constructed directly (as a source-generated model binder would); only the model bind
    /// itself goes through <see cref="VistaWriteBinding.BindModel"/>, which resolves the AOT-clean
    /// <see cref="JsonTypeInfo"/> for <see cref="StyleAAuditCrud"/> from the generated per-view context.
    /// </summary>
    private static object BindAuditModel(string modelJson)
    {
        using var document = JsonDocument.Parse("{\"model\":" + modelJson + "}");
        var body = new VistaWriteRequestBody
        {
            Model = document.RootElement.GetProperty("model").Clone(),
        };
        return VistaWriteBinding.BindModel(body, typeof(StyleAAuditCrud));
    }

    /// <summary>
    /// Serializes a representative instance of the audit view's ANONYMOUS read projection
    /// (<c>new { EntryId, Action, OccurredAt }</c>) through the reflection-based serializer. This is the
    /// permanent D96 RUC read path an anonymous Style A row is confined to: the type is unnameable in
    /// generated source, so no <see cref="JsonTypeInfo"/> can be emitted for it and it cannot be served by
    /// a generated context. The call is deliberately RUC and isolated behind a narrowly-scoped suppression
    /// whose ONLY purpose is to demonstrate the boundary — it is NOT part of the covered, AOT-clean path
    /// and is NOT required to be AOT-clean (R9.2/D130).
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification = "Demonstrates the permanent D96 RUC boundary: an anonymous Style A read row is unnameable in generated source and stays on the reflection path by design; it is not the covered path under verification (R9.2/D130).")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Members annotated with 'RequiresDynamicCodeAttribute' may break when AOT compiling",
        Justification = "Demonstrates the permanent D96 RUC boundary: an anonymous Style A read row is unnameable in generated source and stays on the reflection path by design; it is not the covered path under verification (R9.2/D130).")]
    private static string SerializeAnonymousReadRowRuc()
    {
        // Mirrors the shape of StyleACoverageProbeTemplate's audit projection: new { EntryId, Action, OccurredAt }.
        var anonymousReadRow = new { EntryId = 1, Action = "Login", OccurredAt = (DateTime?)null };
        return JsonSerializer.Serialize(anonymousReadRow);
    }
}
