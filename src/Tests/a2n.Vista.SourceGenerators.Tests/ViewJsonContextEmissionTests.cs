// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Generator-driver EMISSION-STRUCTURE examples for the Phase 5 (M9, D125/D126, source-generator-json-typeinfo)
// ViewJsonContextGenerator emitter (task 5.4; requirements R2.1, R2.3, R4.1, R4.5, R7.2, R7.5). The
// generator is driven directly via CSharpGeneratorDriver over in-memory source (see
// ViewJsonContextGeneratorTestHarness), and these examples assert the SHAPE of the emitted
// <View>_VistaJsonContext.g.cs per-view IJsonTypeInfoResolver plus the incremental caching contract,
// mirroring the Phase 1/2/3/4 emitted-structure and cache-reuse tests
// (ViewAccessorGeneratorTests / WriteMapperEmissionTests / ViewInvokerEmissionTests). These are EXAMPLE
// (fact) tests — the companion reflection-free / determinism property tests are tasks 5.2/5.3.
//
//   * Emitted structure (R2.1, R4.1, R7.5) — a covered view with a public parameterless constructor emits a
//     `file sealed` resolver implementing global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver
//     with EXACTLY ONE [ModuleInitializer], keyed by the view's runtime Name obtained from `new <View>().Name`,
//     that registers a singleton into a2n.Vista.Metadata.GeneratedJsonContextStore. The context is emitted
//     into the consumer assembly (the assembly that declares the view), never a Vista/ASP.NET Core assembly.
//   * Every DTO via JsonMetadataServices, no [JsonSerializable] (R2.1) — each type in the Serializable_DTO_Set
//     ({ TRow, ViewListResult<TRow>, PagedResult<TRow> } plus TCrud for a writable view) is built by hand via
//     JsonMetadataServices.CreateObjectInfo + CreatePropertyInfo; the source uses NO [JsonSerializable]
//     attribute route (the generator-of-generator workaround).
//   * Options-honoring / oracle parity (R2.3, R6.4) — a member's JSON property name follows the seam's
//     JsonSerializerOptions: a [JsonPropertyName("...")] member emits that literal, a plain member emits the
//     Web-default camelCase name. Parity is checked against the reflection oracle: the exact property-name
//     set the reflection serializer produces under the same options is asserted to appear as
//     `JsonPropertyName = "..."` literals in the emitted source (a lighter assertion than executing the
//     generated code, per task 5.4). An enum member rides the shared JsonStringEnumConverter — the generated
//     metadata captures `options` and emits no bespoke enum converter.
//   * No-ctor view emits nothing (R4.5) — a covered SHAPE with no public parameterless constructor emits
//     NEITHER the resolver NOR the initializer (the [ModuleInitializer] could not instantiate the view to
//     read its Name), and claims no coverage (no VISTA0050).
//   * Incremental cache reuse (R7.2) — an unrelated edit that leaves a view's equatable ViewJsonContextModel
//     unchanged serves the tagged ViewJsonContextModel stage from cache
//     (IncrementalStepRunReason.Cached/Unchanged) rather than regenerating the unchanged view's context.
//
// Only the generated source TEXT and the run diagnostics/tracked steps are inspected; no generated
// [ModuleInitializer] is executed.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class ViewJsonContextEmissionTests
{
    // Shared contract types reused across the examples so each view source stays small. `Row` carries only
    // EMITTABLE members (a scalar, a string with a non-default [JsonPropertyName], a nullable value type,
    // and an enum), so a view over it is a covered serialization candidate. `WriteCrud` is a named write
    // model so a writable view over it adds TCrud to the Serializable_DTO_Set.
    private const string SharedTypes = @"
namespace App
{
    public enum RowKind { Alpha, Beta }

    public sealed class Row
    {
        public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName(""display_name"")]
        public string Name { get; set; } = string.Empty;
        public int? Score { get; set; }
        public RowKind Kind { get; set; }
    }

    public sealed class WriteCrud
    {
        public string Name { get; set; } = string.Empty;
        public int? Score { get; set; }
    }
}
";

    // ---- covered read-only candidate: named View<TRow>, implicit public parameterless ctor ----------

    private const string ReadOnlyNamedView = SharedTypes + @"
namespace App
{
    public partial class ReadOnlyNamedView : a2n.Vista.Authoring.View<Row>
    {
    }
}
";

    // ---- covered writable candidate: named View<TRow, TCrud>, implicit public parameterless ctor ----

    private const string WritableNamedView = SharedTypes + @"
namespace App
{
    public partial class WritableNamedView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
    }
}
";

    // ---- NO-CTOR candidate: a covered shape that declares ONLY a parameterized constructor, so it has no
    // public parameterless constructor and cannot be instantiated by the module initializer to read its
    // Name (R4.5). No context and no initializer must be emitted, and no coverage is claimed.
    private const string NoCtorNamedView = SharedTypes + @"
namespace App
{
    public partial class NoCtorNamedView : a2n.Vista.Authoring.View<Row>
    {
        public NoCtorNamedView(int seed)
        {
        }
    }
}
";

    // An unrelated edit appended to the view's syntax tree: a plain class with NO base list, so it is not a
    // serialization candidate. It changes the tree text (forcing the semantic transform to re-run) but
    // leaves the view declaration and its Serializable_DTO_Set identical, so the equatable
    // ViewJsonContextModel compares equal and the downstream model stage is served from cache.
    private const string UnrelatedEdit = @"
namespace App
{
    public sealed class UnrelatedThing
    {
        public int Value { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}
";

    // The mirror row type in THIS assembly, shaped identically to App.Row (same members, same
    // [JsonPropertyName]), used to compute the reflection oracle's property-name set under the seam's
    // JsonSerializerOptions. Because naming is value-independent, a default instance is enough to read the
    // JSON keys the oracle would produce.
    private enum OracleRowKind
    {
        Alpha,
        Beta,
    }

    private sealed class OracleRow
    {
        public int Id { get; set; }

        [JsonPropertyName("display_name")]
        public string Name { get; set; } = string.Empty;

        public int? Score { get; set; }

        public OracleRowKind Kind { get; set; }
    }

    // ---- emitted structure + one [ModuleInitializer] keyed by new View().Name (R2.1, R4.1, R7.5) -----

    [Test]
    public async Task Covered_View_Emits_Exactly_One_ModuleInitializer_Keyed_By_New_View_Name()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(ReadOnlyNamedView);

        // R7.5: exactly one per-view JsonTypeInfo context source is emitted into the consumer assembly.
        await Assert.That(result.HasGeneratedSourceContaining("ReadOnlyNamedView_VistaJsonContext")).IsTrue();
        var generated = result.GeneratedSourceContaining("ReadOnlyNamedView_VistaJsonContext");

        // The emitted artifact is a file-local sealed resolver implementing the BCL IJsonTypeInfoResolver
        // (STJ from the shared framework — no Vista/ASP.NET Core dependency in the view assembly, R7.5).
        await Assert.That(generated.Contains(
            "file sealed class ReadOnlyNamedView_VistaJsonContext : "
            + "global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains("Microsoft.AspNetCore", StringComparison.Ordinal)).IsFalse();

        // R4.1: EXACTLY ONE [ModuleInitializer] is emitted.
        var initializerCount = CountOccurrences(
            generated, "[global::System.Runtime.CompilerServices.ModuleInitializer]");
        await Assert.That(initializerCount).IsEqualTo(1);

        // R4.1: the initializer keys the context off the view's RUNTIME Name, obtained by instantiating the
        // view via its public parameterless constructor and reading `.Name` — `new <View>().Name` — and
        // registers a singleton into the Core-resident, serializer-neutral GeneratedJsonContextStore.
        await Assert.That(generated.Contains(
            "global::a2n.Vista.Metadata.GeneratedJsonContextStore.Register(", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            "new global::App.ReadOnlyNamedView().Name, new ReadOnlyNamedView_VistaJsonContext());",
            StringComparison.Ordinal)).IsTrue();
    }

    // ---- every DTO built via JsonMetadataServices; no [JsonSerializable] (R2.1) ----------------------

    [Test]
    public async Task ReadOnly_View_Builds_Every_Read_Dto_Via_JsonMetadataServices_Without_JsonSerializable()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(ReadOnlyNamedView);

        var generated = result.GeneratedSourceContaining("ReadOnlyNamedView_VistaJsonContext");

        // The generator-of-generator workaround: metadata is built BY HAND via JsonMetadataServices, never
        // through the [JsonSerializable] attribute route the built-in generator would need (R2.1).
        await Assert.That(generated.Contains("[JsonSerializable", StringComparison.Ordinal)).IsFalse();
        await Assert.That(generated.Contains("JsonSerializable", StringComparison.Ordinal)).IsFalse();

        // Each type in the read Serializable_DTO_Set — { TRow, ViewListResult<TRow>, PagedResult<TRow> } —
        // is built via one JsonMetadataServices.CreateObjectInfo<...> factory (3 for a read-only view).
        var createObjectInfoCount = CountOccurrences(
            generated, "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateObjectInfo<");
        await Assert.That(createObjectInfoCount).IsEqualTo(3);

        // Each object-info names its DTO type, and the GetTypeInfo dispatch has an arm per DTO type.
        await Assert.That(generated.Contains(
            "CreateObjectInfo<global::App.Row>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            "typeof(global::a2n.Vista.Ports.ViewListResult<global::App.Row>)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            "typeof(global::a2n.Vista.Results.PagedResult<global::App.Row>)", StringComparison.Ordinal)).IsTrue();

        // Property metadata is built via CreatePropertyInfo<TMember> (compile-time getters/setters), not
        // reflection.
        await Assert.That(generated.Contains(
            "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreatePropertyInfo<",
            StringComparison.Ordinal)).IsTrue();
    }

    // ---- writable view adds a TCrud JsonTypeInfo to the set (R2.1) -----------------------------------

    [Test]
    public async Task Writable_View_Builds_The_TCrud_Dto_Via_JsonMetadataServices()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(WritableNamedView);

        var generated = result.GeneratedSourceContaining("WritableNamedView_VistaJsonContext");

        // A writable view's Serializable_DTO_Set adds TCrud, so FOUR object-info factories are emitted:
        // { TRow, ViewListResult<TRow>, PagedResult<TRow>, TCrud }.
        var createObjectInfoCount = CountOccurrences(
            generated, "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateObjectInfo<");
        await Assert.That(createObjectInfoCount).IsEqualTo(4);

        await Assert.That(generated.Contains(
            "CreateObjectInfo<global::App.WriteCrud>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            "typeof(global::App.WriteCrud)", StringComparison.Ordinal)).IsTrue();
    }

    // ---- options-honoring: JSON property names match the reflection oracle (R2.3, R6.4) --------------

    [Test]
    public async Task Emitted_Json_Property_Names_Match_The_Reflection_Oracle()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(ReadOnlyNamedView);

        var generated = result.GeneratedSourceContaining("ReadOnlyNamedView_VistaJsonContext");

        // The reflection oracle's property-name set for TRow under the seam's JsonSerializerOptions
        // (Web defaults: camelCase policy; case-insensitive; JsonStringEnumConverter). Naming is
        // value-independent, so serializing a default instance yields the exact JSON keys the oracle
        // produces — the parity target the generated metadata must match byte-for-byte.
        var oracleNames = OracleJsonPropertyNames(new OracleRow());

        // The generated metadata emits every member's JSON name as a `JsonPropertyName = "..."` literal.
        // Assert each oracle name appears verbatim — a [JsonPropertyName] override (display_name) and the
        // camelCase defaults (id, score, kind) alike (R2.3, R6.4). This is the lighter parity assertion
        // task 5.4 permits over executing the generated code.
        foreach (var name in oracleNames)
        {
            await Assert.That(generated.Contains(
                $"JsonPropertyName = \"{name}\",", StringComparison.Ordinal)).IsTrue();
        }

        // Spot-check the two interesting cases explicitly: the non-default [JsonPropertyName] wins over the
        // policy, and a plain member is camel-cased.
        await Assert.That(oracleNames.Contains("display_name")).IsTrue();
        await Assert.That(oracleNames.Contains("id")).IsTrue();
        await Assert.That(generated.Contains("JsonPropertyName = \"display_name\",", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains("JsonPropertyName = \"id\",", StringComparison.Ordinal)).IsTrue();
    }

    // ---- options-honoring: an enum member rides the shared converter, not a bespoke one (R2.3) -------

    [Test]
    public async Task Enum_Member_Rides_The_Seam_Converter_Via_Captured_Options()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(ReadOnlyNamedView);

        var generated = result.GeneratedSourceContaining("ReadOnlyNamedView_VistaJsonContext");

        // The enum member is modeled as a strongly-typed property over the enum type; its property metadata
        // captures the resolver's `options` and synthesizes NO per-property bespoke converter.
        await Assert.That(generated.Contains(
            "CreatePropertyInfo<global::App.RowKind>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains("JsonPropertyName = \"kind\",", StringComparison.Ordinal)).IsTrue();

        // The property metadata itself does not carry a strongly-typed JsonConverter<...> — the property
        // rides the captured options, not a per-property converter.
        await Assert.That(generated.Contains("JsonConverter<", StringComparison.Ordinal)).IsFalse();

        // Corrected emitter behavior (R8.1 / no reflection fallback): a covered DTO with an enum member also
        // needs the enum's JsonTypeInfo resolvable from THIS resolver, because System.Text.Json resolves an
        // enum property's JsonTypeInfo from the chain when the reflection fallback is removed. The generator
        // therefore emits a per-view enum LEAF ARM built via JsonMetadataServices.CreateValueInfo using the
        // AOT-safe GENERIC JsonStringEnumConverter<TEnum> — a string enum converter whose defaults match the
        // seam's `new JsonStringEnumConverter()`, so the enum's wire form stays byte-for-byte identical to
        // the reflection oracle (R2.3, R6.4) while being no-fallback-clean.
        await Assert.That(generated.Contains(
            "JsonMetadataServices.CreateValueInfo<global::App.RowKind>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            "new global::System.Text.Json.Serialization.JsonStringEnumConverter<global::App.RowKind>()",
            StringComparison.Ordinal)).IsTrue();
    }

    // ---- no-ctor skip: emits nothing and claims no coverage (R4.5) -----------------------------------

    [Test]
    public async Task Covered_View_Without_Public_Parameterless_Ctor_Emits_Nothing()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(NoCtorNamedView);

        // R4.5: a view the [ModuleInitializer] cannot instantiate emits NEITHER the resolver NOR the
        // initializer, so the GeneratedJsonContextStore is left untouched for it.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaJsonContext")).IsFalse();
        await Assert.That(result.HasGeneratedContextFor("NoCtorNamedView")).IsFalse();

        // It is a covered SHAPE but coverage is not CLAIMED (no VISTA0050), and it is not a non-emittable
        // failure either (no VISTA0051). The build stays green.
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0050")).IsFalse();
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0051")).IsFalse();
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    // ---- incremental cache reuse on an unrelated edit (equatable model, R7.2) ------------------------

    [Test]
    public async Task UnrelatedEdit_Reuses_Cached_ViewJsonContextModel_Step()
    {
        var result = ViewJsonContextGeneratorTestHarness.RunIncremental(ReadOnlyNamedView, UnrelatedEdit);

        // The tagged equatable-model stage must be present in the tracked steps of the second run.
        var trackedSteps = result.Results.Single().TrackedSteps;
        await Assert.That(trackedSteps.ContainsKey(TrackingNames.ViewJsonContextModel)).IsTrue();

        // On the unrelated edit, every output of the model stage must be served from cache: either Cached
        // (input node unchanged, not re-executed) or Unchanged (re-executed because the tree text changed,
        // but the equatable ViewJsonContextModel compared equal so no new value flowed downstream). It must
        // NOT be New/Modified — that would mean the unrelated edit regenerated the unchanged view's context
        // (R7.2).
        var outcomes = trackedSteps[TrackingNames.ViewJsonContextModel]
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToArray();

        await Assert.That(outcomes.Length).IsGreaterThan(0);
        await Assert.That(outcomes.All(static reason =>
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged))
            .IsTrue();
    }

    // ---- helpers -------------------------------------------------------------------------------------

    /// <summary>
    /// The ordered set of JSON property names the reflection oracle produces for <paramref name="value"/>
    /// under the seam's <see cref="JsonSerializerOptions"/> (Web defaults + <see cref="JsonStringEnumConverter"/>).
    /// Property naming is value-independent, so the keys of the serialized default instance are exactly the
    /// wire names the generated metadata must match (R2.3, R6.4).
    /// </summary>
    private static IReadOnlyList<string> OracleJsonPropertyNames<T>(T value)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var json = JsonSerializer.Serialize(value, options);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(static p => p.Name).ToArray();
    }

    // Counts non-overlapping occurrences of <paramref name="value"/> in <paramref name="text"/>.
    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
