// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Feature: style-a-coverage
//
// Generator-driver EMITTED-STRUCTURE + INCREMENTAL-REUSE examples for the Style A coverage generator
// (StyleAShapeGenerator, the fifth phase — M9, D129/D130, style-a-coverage) — task 5.5; requirements R2.1,
// R3.1, R4.2, R4.4, R7.2, R7.5. The generator is driven directly via CSharpGeneratorDriver over in-memory
// source (see StyleAShapeGeneratorTestHarness), and these examples assert the SHAPE of the emitted
// <Template>_<View>_VistaAccessors.g.cs accessor map and <Template>_<View>_VistaJsonContext.g.cs per-view
// IJsonTypeInfoResolver, plus the incremental caching contract — mirroring the Phase 1/2/3/4/5
// emitted-structure and cache-reuse tests (ViewAccessorGeneratorTests / WriteMapperEmissionTests /
// ViewInvokerEmissionTests / ViewJsonContextEmissionTests). These are EXAMPLE (fact) tests documenting the
// concrete emitted shape; the companion reflection-free / determinism PROPERTY tests are tasks 5.3/5.4 and
// the recognition + coverage MATRIX examples are task 2.5 (StyleARecognitionAndCoverageMatrixTests).
//
// WHAT IS ASSERTED (design.md "Testing Strategy"; the emitters landed in tasks 5.1/5.2):
//   1. Covered named read-only view — emits an accessor file AND a context file; EACH generated file
//      contains EXACTLY ONE [ModuleInitializer]; the accessor one calls ViewAccessorRegistry.Register and
//      the context one calls GeneratedJsonContextStore.Register — both keyed by the CONSTANT view-name
//      LITERAL (the D129 difference from D125's `new <View>().Name`); the context is built by hand via
//      JsonMetadataServices with NO [JsonSerializable]; the accessor map is cast-only
//      (((global::...)row).Member) with no reflection; nothing binds to ASP.NET Core (R7.5).
//   2. Named writable view — the context additionally carries the TCrud JsonTypeInfo factory.
//   3. Anonymous-TRow + named-TCrud view (the D96 asymmetry) — emits ONLY the TCrud context (NO accessor
//      file); the context provides TCrud but NOT TRow / ViewListResult / PagedResult; and VISTA0061 is
//      reported (read stays RUC by design).
//   4. A [JsonPropertyName("custom_name")] member + an enum member — the emitted context carries the custom
//      JSON-name literal and an enum handled via the AOT-safe generic JsonStringEnumConverter<TEnum>, and
//      the emitted JSON names match the web-default + enum-converter reflection oracle (a source-structure
//      assertion; runtime byte-parity is the master property, task 8.2).
//   5. Incremental reuse — after an UNRELATED edit (an appended, non-candidate class) the tagged
//      StyleAViewModel step is served from cache (IncrementalStepRunReason.Cached/Unchanged), proving the
//      equatable value model (R7.2).
//
// Case 5 needs TWO compilations on one reused driver; the shared StyleAShapeGeneratorTestHarness.Run(source)
// runs a single compilation and must not be modified (tasks 5.3/5.4/6.x may read it concurrently), so this
// file carries a SELF-CONTAINED incremental driver (RunIncremental) reusing only the harness's public
// VistaStubs. Only the generated source TEXT and the run diagnostics / tracked steps are inspected; no
// emitted [ModuleInitializer] is executed.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class StyleAEmittedStructureAndIncrementalTests
{
    // ---- Case 1 / 5 source: a covered named-TRow read-only Style A view with a constant name ----------

    private const string NamedReadOnlyView = @"
using System.Linq;

namespace App
{
    public enum CustomerKind { Regular, Premium }

    public sealed class CustomerRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Rank { get; set; }
        public CustomerKind Kind { get; set; }
    }

    public class CustomerReadOnlyTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView<CustomerRow>(""customers"", (db, sp) => new CustomerRow[0].AsQueryable());
        }
    }
}
";

    // ---- Case 2 source: a covered named-TRow + named-TCrud writable Style A view ----------------------

    private const string NamedWritableView = @"
using System.Linq;

namespace App
{
    public sealed class CustomerRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class CustomerCrud
    {
        public string Name { get; set; } = string.Empty;
        public int? Rank { get; set; }
    }

    public sealed class CustomerEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Rank { get; set; }
    }

    public class CustomerWritableTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView<CustomerRow>(""customers"", (db, sp) => new CustomerRow[0].AsQueryable())
                 .WithCrud<CustomerCrud, CustomerEntity>();
        }
    }
}
";

    // ---- Case 3 source: the D96 asymmetry — anonymous read TRow + named TCrud -------------------------

    private const string AnonymousRowWithNamedCrudView = @"
using System.Linq;

namespace App
{
    public sealed class OrderCrud
    {
        public string Reference { get; set; } = string.Empty;
        public int? Quantity { get; set; }
    }

    public sealed class OrderEntity
    {
        public int Id { get; set; }
        public string Reference { get; set; } = string.Empty;
        public int? Quantity { get; set; }
    }

    public class OrderWritableTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView(""orders"", (db, sp) => new[] { new { Id = 1, Reference = ""x"" } }.AsQueryable())
                 .WithCrud<OrderCrud, OrderEntity>();
        }
    }
}
";

    // ---- Case 4 source: a non-default [JsonPropertyName] member and an enum member --------------------

    private const string CustomJsonNameAndEnumView = @"
using System.Linq;

namespace App
{
    public enum Priority { Low, High }

    public sealed class TicketRow
    {
        public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName(""custom_name"")]
        public string Title { get; set; } = string.Empty;
        public Priority Level { get; set; }
    }

    public class TicketTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView<TicketRow>(""tickets"", (db, sp) => new TicketRow[0].AsQueryable());
        }
    }
}
";

    // An unrelated edit appended to the view's syntax tree for the incremental case: a plain class with NO
    // AddView call site, so it is not a Style A candidate. It changes the tree text (forcing the semantic
    // transform to re-run) but leaves the AddView call site and its equatable StyleAViewModel identical, so
    // the tagged model stage is served from cache (R7.2).
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

    // ---- Case 1: accessor + context files, each with one [ModuleInitializer] keyed by the constant -----

    [Test]
    public async Task Named_ReadOnly_Emits_Accessor_And_Context_Each_With_One_Initializer_Keyed_By_Constant()
    {
        // R2.1, R2.2, R3.1, R5.1, R5.2, R7.5 — a covered named-TRow read-only view emits BOTH the export
        // accessor map and the per-view JsonTypeInfo context into the consumer assembly, each keyed by the
        // CONSTANT AddView name literal.
        var result = StyleAShapeGeneratorTestHarness.Run(NamedReadOnlyView);

        await Assert.That(result.HasGeneratedSourceContaining("_VistaAccessors")).IsTrue();
        await Assert.That(result.HasGeneratedSourceContaining("_VistaJsonContext")).IsTrue();

        // -- accessor file -----------------------------------------------------------------------------
        var accessors = result.GeneratedSourceContaining("_VistaAccessors");

        // Exactly one [ModuleInitializer], registering the map into the EXISTING ViewAccessorRegistry.
        await Assert.That(CountOccurrences(accessors, ModuleInitializerAttribute)).IsEqualTo(1);
        await Assert.That(accessors.Contains(
            "global::a2n.Vista.Metadata.ViewAccessorRegistry.Register(", StringComparison.Ordinal)).IsTrue();

        // Keyed by the CONSTANT view-name LITERAL (the D129 difference from D125's `new <View>().Name`).
        await Assert.That(accessors.Contains("\"customers\", Map);", StringComparison.Ordinal)).IsTrue();
        await Assert.That(accessors.Contains("().Name", StringComparison.Ordinal)).IsFalse();

        // Cast-only accessors: ((global::App.CustomerRow)row).Member — never reflection.
        await Assert.That(accessors.Contains(
            "((global::App.CustomerRow)row).Id", StringComparison.Ordinal)).IsTrue();
        await Assert.That(accessors.Contains(
            "((global::App.CustomerRow)row).Name", StringComparison.Ordinal)).IsTrue();
        await AssertNoReflection(accessors);

        // -- context file ------------------------------------------------------------------------------
        var context = result.GeneratedSourceContaining("_VistaJsonContext");

        // A file-local sealed resolver over the BCL IJsonTypeInfoResolver — STJ from the shared framework,
        // no Vista/ASP.NET Core dependency in the template assembly (R7.5).
        await Assert.That(context.Contains(
            "file sealed class CustomerReadOnlyTemplate_customers_VistaJsonContext : "
            + "global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(context.Contains("Microsoft.AspNetCore", StringComparison.Ordinal)).IsFalse();

        // Exactly one [ModuleInitializer], registering the context into the EXISTING GeneratedJsonContextStore
        // keyed by the CONSTANT view-name literal (NOT `new <View>().Name`).
        await Assert.That(CountOccurrences(context, ModuleInitializerAttribute)).IsEqualTo(1);
        await Assert.That(context.Contains(
            "global::a2n.Vista.Metadata.GeneratedJsonContextStore.Register(", StringComparison.Ordinal)).IsTrue();
        await Assert.That(context.Contains(
            "\"customers\", new CustomerReadOnlyTemplate_customers_VistaJsonContext());",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(context.Contains("().Name", StringComparison.Ordinal)).IsFalse();

        // Every DTO is built BY HAND via JsonMetadataServices — never the [JsonSerializable] attribute route
        // (the generator-of-generator constraint).
        await Assert.That(context.Contains(
            "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateObjectInfo<global::App.CustomerRow>",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(context.Contains(
            "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreatePropertyInfo<",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(context.Contains("JsonSerializable", StringComparison.Ordinal)).IsFalse();

        await AssertNoErrors(result);
    }

    // ---- Case 2: a named writable view's context includes the TCrud JsonTypeInfo factory --------------

    [Test]
    public async Task Named_Writable_Context_Includes_The_TCrud_JsonTypeInfo_Factory()
    {
        // R4.1 — a writable view adds the write-model TCrud to the Serializable_DTO_Set, so the emitted
        // context carries a JsonMetadataServices object-info factory (and a dispatch arm) for TCrud in
        // addition to the read-side TRow.
        var result = StyleAShapeGeneratorTestHarness.Run(NamedWritableView);

        var context = result.GeneratedSourceContaining("_VistaJsonContext");

        // The read-side TRow is present ...
        await Assert.That(context.Contains(
            "JsonMetadataServices.CreateObjectInfo<global::App.CustomerRow>", StringComparison.Ordinal)).IsTrue();

        // ... and the write model TCrud is added to the set (its own object-info factory + dispatch arm).
        await Assert.That(context.Contains(
            "JsonMetadataServices.CreateObjectInfo<global::App.CustomerCrud>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(context.Contains(
            "typeof(global::App.CustomerCrud)", StringComparison.Ordinal)).IsTrue();

        await AssertNoErrors(result);
    }

    // ---- Case 3: anonymous TRow + named TCrud -> ONLY the TCrud context, no accessor, + VISTA0061 ------

    [Test]
    public async Task Anonymous_Row_With_Named_TCrud_Emits_Only_The_TCrud_Context_And_Reports_VISTA0061()
    {
        // R4.2, R4.4, R8.2 — the D96 asymmetry within one view: the anonymous read row is unnameable in
        // generated source (no accessor map, no read-DTO JsonTypeInfo — VISTA0061), while the always-named
        // TCrud (D38) is still covered, so the emitted context provides ONLY TCrud.
        var result = StyleAShapeGeneratorTestHarness.Run(AnonymousRowWithNamedCrudView);

        // NO accessor file (the anonymous row is unnameable).
        await Assert.That(result.HasGeneratedSourceContaining("_VistaAccessors")).IsFalse();

        // A context file IS emitted for the write side.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaJsonContext")).IsTrue();
        var context = result.GeneratedSourceContaining("_VistaJsonContext");

        // The context provides the named TCrud ...
        await Assert.That(context.Contains(
            "JsonMetadataServices.CreateObjectInfo<global::App.OrderCrud>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(context.Contains(
            "typeof(global::App.OrderCrud)", StringComparison.Ordinal)).IsTrue();

        // ... and ONLY TCrud: exactly one object-info factory, so no read-row (or envelope) DTO leaked in.
        await Assert.That(CountOccurrences(context, "JsonMetadataServices.CreateObjectInfo<")).IsEqualTo(1);

        // ... but NOT the read row nor the read envelopes (the read side is not emitted for an anonymous row).
        await Assert.That(context.Contains("ViewListResult", StringComparison.Ordinal)).IsFalse();
        await Assert.That(context.Contains("PagedResult", StringComparison.Ordinal)).IsFalse();

        // Keyed by the constant literal.
        await Assert.That(context.Contains(
            "\"orders\", new OrderWritableTemplate_orders_VistaJsonContext());", StringComparison.Ordinal)).IsTrue();

        // The read side stays on the reflection path by design (D96): exactly one VISTA0061 (Info).
        var anonymousRow = DiagnosticsWithId(result, "VISTA0061");
        await Assert.That(anonymousRow.Length).IsEqualTo(1);
        await Assert.That(anonymousRow[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(Message(anonymousRow[0]).Contains("'orders'", StringComparison.Ordinal)).IsTrue();

        await AssertNoErrors(result);
    }

    // ---- Case 4: custom [JsonPropertyName] literal + enum via JsonStringEnumConverter (oracle parity) --

    [Test]
    public async Task Custom_Json_Name_And_Enum_Emit_The_Custom_Literal_And_The_String_Enum_Converter_Arm()
    {
        // R3.1, R3.3 (parity with the web-default + enum-converter oracle) — the emitted context carries the
        // [JsonPropertyName("custom_name")] literal verbatim and the enum member rides the AOT-safe generic
        // JsonStringEnumConverter<TEnum>, so its wire form matches the reflection oracle. This is the
        // source-structure assertion (runtime byte-parity is the master property, task 8.2).
        var result = StyleAShapeGeneratorTestHarness.Run(CustomJsonNameAndEnumView);

        var context = result.GeneratedSourceContaining("_VistaJsonContext");

        // The non-default [JsonPropertyName] override wins over the naming policy and emits verbatim.
        await Assert.That(context.Contains(
            "JsonPropertyName = \"custom_name\",", StringComparison.Ordinal)).IsTrue();

        // The enum member rides the seam's string-enum converter, built directly from the AOT-safe generic
        // factory (no bespoke per-property converter, no numeric GetEnumConverter).
        await Assert.That(context.Contains(
            "JsonMetadataServices.CreateValueInfo<global::App.Priority>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(context.Contains(
            "new global::System.Text.Json.Serialization.JsonStringEnumConverter<global::App.Priority>()",
            StringComparison.Ordinal)).IsTrue();

        // Parity with the reflection oracle's property-name set under the seam's JsonSerializerOptions
        // (Web defaults: camelCase; JsonStringEnumConverter). Naming is value-independent, so serializing a
        // default mirror instance yields the exact JSON keys the oracle produces; each must appear as a
        // `JsonPropertyName = "..."` literal in the emitted source (the [JsonPropertyName] override and the
        // camelCase defaults alike).
        var oracleNames = OracleJsonPropertyNames(new OracleTicketRow());
        await Assert.That(oracleNames.Contains("custom_name")).IsTrue();
        foreach (var name in oracleNames)
        {
            await Assert.That(context.Contains(
                $"JsonPropertyName = \"{name}\",", StringComparison.Ordinal)).IsTrue();
        }

        await AssertNoErrors(result);
    }

    // ---- Case 5: an unrelated edit serves the tagged StyleAViewModel step from cache (R7.2) -----------

    [Test]
    public async Task UnrelatedEdit_Reuses_The_Cached_StyleAViewModel_Step()
    {
        var result = RunIncremental(NamedReadOnlyView, UnrelatedEdit);

        // The tagged equatable-model stage must be present in the tracked steps of the second run.
        var trackedSteps = result.Results.Single().TrackedSteps;
        await Assert.That(trackedSteps.ContainsKey(TrackingNames.StyleAViewModel)).IsTrue();

        // On the unrelated edit, every output of the model stage must be served from cache: either Cached
        // (input node unchanged, not re-executed) or Unchanged (re-executed because the tree text changed,
        // but the equatable StyleAViewModel compared equal so no new value flowed downstream). It must NOT
        // be New/Modified — that would mean the unrelated edit regenerated the unchanged view's artifacts
        // (R7.2).
        var outcomes = trackedSteps[TrackingNames.StyleAViewModel]
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToArray();

        await Assert.That(outcomes.Length).IsGreaterThan(0);
        await Assert.That(outcomes.All(static reason =>
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged))
            .IsTrue();
    }

    // ---- oracle mirror types (case 4) -----------------------------------------------------------------

    private enum OracleTicketPriority
    {
        Low,
        High,
    }

    // Shaped identically to App.TicketRow (same members, same [JsonPropertyName]) so the reflection oracle's
    // property-name set under the seam's options equals the wire names the generated metadata must match.
    private sealed class OracleTicketRow
    {
        public int Id { get; set; }

        [JsonPropertyName("custom_name")]
        public string Title { get; set; } = string.Empty;

        public OracleTicketPriority Level { get; set; }
    }

    // ---- helpers --------------------------------------------------------------------------------------

    // The [ModuleInitializer] attribute the generated files carry (counted to assert exactly one per file).
    private const string ModuleInitializerAttribute =
        "[global::System.Runtime.CompilerServices.ModuleInitializer]";

    // All framework reference assemblies for the running TFM (TRUSTED_PLATFORM_ASSEMBLIES) — the standard
    // way to give the self-contained incremental compilation a complete reference closure, matching the
    // shared harness. Kept local to this file so the shared StyleAShapeGeneratorTestHarness is untouched.
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(static p => !string.IsNullOrEmpty(p))
        .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToArray();

    /// <summary>
    /// Drives <see cref="StyleAShapeGenerator"/> over the same Style A source twice on ONE reused driver to
    /// prove incremental cache reuse of the equatable <see cref="StyleAViewModel"/> (R7.2), mirroring the
    /// sibling generators' <c>RunIncremental</c> helpers. The first run establishes the baseline cache; the
    /// second run sees a compilation where the view's syntax tree has an UNRELATED edit appended
    /// (<paramref name="unrelatedEdit"/>) that leaves the <c>AddView</c> call site identical. Because the
    /// tree text changed, Roslyn re-executes the semantic transform — but the resulting model compares
    /// equal, so the tagged <see cref="TrackingNames.StyleAViewModel"/> stage is served from cache
    /// (<see cref="IncrementalStepRunReason.Unchanged"/>/<see cref="IncrementalStepRunReason.Cached"/>). The
    /// returned result is the SECOND run's. Self-contained here (reusing only the harness's public
    /// <see cref="StyleAShapeGeneratorTestHarness.VistaStubs"/>) so the shared single-run harness stays
    /// unmodified for the concurrently-authored sibling tasks.
    /// </summary>
    private static GeneratorDriverRunResult RunIncremental(string viewSource, string unrelatedEdit)
    {
        var stubsTree = CSharpSyntaxTree.ParseText(StyleAShapeGeneratorTestHarness.VistaStubs);
        var viewTree = CSharpSyntaxTree.ParseText(viewSource);

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.StyleAShapeGeneratorTests.Incremental.InMemory",
            syntaxTrees: new[] { stubsTree, viewTree },
            references: References,
            options: options);

        var generator = new StyleAShapeGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        // First run: establishes the baseline cache for every pipeline stage.
        driver = driver.RunGenerators(compilation);

        // Second run: replace ONLY the view tree with one carrying an unrelated edit appended after the
        // view. The appended content is not a candidate (no AddView call site), so the view's own model is
        // unchanged and the tagged model stage is served from cache.
        var modifiedViewTree = CSharpSyntaxTree.ParseText(viewSource + unrelatedEdit);
        var modifiedCompilation = compilation.ReplaceSyntaxTree(viewTree, modifiedViewTree);
        driver = driver.RunGenerators(modifiedCompilation);

        return driver.GetRunResult();
    }

    /// <summary>
    /// The ordered set of JSON property names the reflection oracle produces for <paramref name="value"/>
    /// under the seam's <see cref="JsonSerializerOptions"/> (Web defaults + <see cref="JsonStringEnumConverter"/>).
    /// Property naming is value-independent, so the keys of the serialized default instance are exactly the
    /// wire names the generated metadata must match (R3.3).
    /// </summary>
    private static IReadOnlyList<string> OracleJsonPropertyNames<T>(T value)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var json = JsonSerializer.Serialize(value, options);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(static p => p.Name).ToArray();
    }

    /// <summary>All diagnostics the generator reported with the given id.</summary>
    private static Diagnostic[] DiagnosticsWithId(GeneratorDriverRunResult result, string id)
        => result.Diagnostics.Where(d => d.Id == id).ToArray();

    /// <summary>The invariant-culture rendered message of a diagnostic (for substring/name assertions).</summary>
    private static string Message(Diagnostic diagnostic)
        => diagnostic.GetMessage(CultureInfo.InvariantCulture);

    /// <summary>Asserts the generated accessor source uses cast + member access only — never reflection.</summary>
    private static async Task AssertNoReflection(string generated)
    {
        await Assert.That(generated.Contains("System.Reflection", StringComparison.Ordinal)).IsFalse();
        await Assert.That(generated.Contains("PropertyInfo", StringComparison.Ordinal)).IsFalse();
        await Assert.That(generated.Contains("GetValue", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>Asserts the generator reported no Error-severity diagnostic (the family is non-blocking).</summary>
    private static async Task AssertNoErrors(GeneratorDriverRunResult result)
        => await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            .IsFalse();

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
