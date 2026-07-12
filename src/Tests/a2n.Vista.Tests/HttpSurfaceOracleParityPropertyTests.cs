// Licensed to the a2n.Vista project. Published artifact — English only.
//
// MASTER oracle-parity property test for the Phase 4 HTTP SURFACE (spec source-generator-http-surface,
// task 10.2; Decision Log D123/D124). This is the verification backbone of the feature: for the
// representative compile-once typed Style B views and for randomly-generated request shapes, the
// GENERATED dispatch + serialization path must produce a response byte-for-byte equivalent to the
// REFLECTION oracle path. The reflection path is the Behavioral_Oracle.
//
// Feature: source-generator-http-surface, Property 1: HTTP dispatch + serialization parity with the
// reflection oracle (master, model-based).
//
// Validates: Requirements 2.1, 2.2, 2.5, 3.1, 3.2, 4.3, 4.4, 5.2, 6.2, 6.3, 10.1, 10.2, 10.3
//
// Strategy (design "Cost control for the master parity property", model-based): the three representative
// EF-aware typed Style B views in a2n.Vista.GeneratorHttpSurfaceSample are compiled ONCE; their generated
// dispatch invokers register into a2n.Vista.Ports.ViewInvokerStore at module load. Per iteration the test
// seeds a fresh, deterministic SQLite dataset and runs each request BOTH ways:
//   * GENERATED  — the source-generated IViewInvoker resolved from the store (closed-generic List/Detail/
//                  Create/Update, direct await, no MakeGenericMethod, no ViewListResult<TRow> reflection),
//                  then serialized through a resolver chain built from the shipped Static_Envelope_Context
//                  plus the sample App_Json_Contexts (source-gen JsonTypeInfo, no reflection fallback);
//   * REFLECTION — the IViewExecutor generic facet closed at runtime via MethodInfo.MakeGenericMethod
//                  (exactly the ViewRequestExecutor.*ReflectionAsync fallback), then serialized through a
//                  reflection-only resolver (DefaultJsonTypeInfoResolver).
// Both resolver chains mirror VistaJson.Options byte-for-byte (web defaults, case-insensitive matching,
// the enum + FilterNode converters); only the JsonTypeInfo resolution mechanism differs. The property
// asserts the two serialized responses are byte-for-byte identical for List (paging/filter/search/
// scope + both totals), Detail-by-key (including composite keys), Export row extraction, and the write
// facets (Create PK, Update/Delete bool outcomes, concurrency conflicts). Minimum 100 generated cases
// per property (CsCheck default). PBT library: CsCheck.
//
// Shared-static isolation: the property never mutates the process-wide VistaJson.Options static (its
// resolver chain freezes on first use and other tests depend on it). Instead it builds fresh options that
// mirror the seam chain exactly — the same construction VistaJson performs — following the established
// pattern of SeamResolutionOrderPropertyTests / SerializationSeamRoundTripTests. Because the mirror is the
// seam configuration, byte parity across the two mirrors proves R5.2/R10.1/R10.3 for the real seam.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.GeneratorHttpSurfaceSample;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property 1 — the master HTTP dispatch + serialization parity guard (task 10.2). The source-generated
/// invoker + source-gen serialization is the implementation under test; the reflection dispatch +
/// reflection serialization is the behavioral oracle. See the file header for the full strategy.
/// </summary>
/// <remarks>
/// Both <c>AddVista</c>/<c>Register&lt;TView&gt;</c> (runtime reflection authoring), the reflection
/// dispatch (<c>MakeGenericMethod</c>), and the reflection serialization resolver
/// (<see cref="DefaultJsonTypeInfoResolver"/>) are RUC-annotated; this test drives the reflection oracle
/// on purpose, so the trim/AOT diagnostic is suppressed at the class level (tests are never trimmed),
/// matching the sibling read/write parity property tests.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The parity test drives the RUC reflection oracle by design; trimming is not used for tests.")]
[SuppressMessage(
    "AOT",
    "IL3050:Calling members annotated with RequiresDynamicCodeAttribute may break functionality when AOT compiling",
    Justification = "The parity test drives the RUC reflection oracle by design; AOT is not used for tests.")]
public sealed class HttpSurfaceOracleParityPropertyTests
{
    private const string PropertyTag =
        "Feature: source-generator-http-surface, Property 1: HTTP dispatch + serialization parity with the reflection oracle (master, model-based)";

    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    // The data-independent parity infrastructure (built once, thread-safe static init): the DI graph, the
    // per-view metadata + resolved generated invokers, and the two mirrored serialization option sets.
    private static readonly Infrastructure Infra = Infrastructure.Build();

    // ---- cached IViewExecutor generic facets for the reflection oracle dispatch (as ViewRequestExecutor)

    private static readonly MethodInfo ListAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.ListAsync))!;

    private static readonly MethodInfo DetailAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.DetailAsync))!;

    private static readonly MethodInfo CreateAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.CreateAsync))!;

    private static readonly MethodInfo UpdateAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.UpdateAsync))!;

    // === Serialization helpers ========================================================================

    /// <summary>Serializes through the SOURCE-GEN chain (static + sample App_Json_Contexts, no fallback).</summary>
    private static byte[] SerializeSourceGen(object? value, Type runtimeType) =>
        JsonSerializer.SerializeToUtf8Bytes(value, Infra.SourceGenOptions.GetTypeInfo(runtimeType));

    /// <summary>Serializes through the REFLECTION oracle chain (DefaultJsonTypeInfoResolver).</summary>
    private static byte[] SerializeReflection(object? value, Type runtimeType) =>
        JsonSerializer.SerializeToUtf8Bytes(value, Infra.ReflectionOptions.GetTypeInfo(runtimeType));

    /// <summary>
    /// The master byte-parity assertion for a read response: the value produced by the GENERATED dispatch,
    /// serialized through the source-gen chain, must be byte-for-byte identical to the value produced by
    /// the REFLECTION dispatch, serialized through the reflection chain. The two intermediate assertions
    /// isolate a failure to dispatch parity (R2/R3/R4/R6) vs serialization parity (R5.2/R10.3).
    /// </summary>
    private static void AssertResponseParity(object? generatedValue, object? reflectionValue, Type runtimeType, string context)
    {
        var genViaSourceGen = SerializeSourceGen(generatedValue, runtimeType);
        var reflViaSourceGen = SerializeSourceGen(reflectionValue, runtimeType);
        var reflViaReflection = SerializeReflection(reflectionValue, runtimeType);

        // Dispatch parity: the generated and reflection dispatch produced the same data (same serializer).
        if (!genViaSourceGen.AsSpan().SequenceEqual(reflViaSourceGen))
        {
            throw new ParityException(
                $"{context}: dispatch parity mismatch — the generated invoker and the reflection oracle " +
                $"produced different data.\n  generated: {Utf8(genViaSourceGen)}\n  reflection: {Utf8(reflViaSourceGen)}");
        }

        // Serialization parity (R10.3): the source-gen JsonTypeInfo yields the same JSON as the reflection
        // resolver for the same options — no wire-visible drift.
        if (!reflViaSourceGen.AsSpan().SequenceEqual(reflViaReflection))
        {
            throw new ParityException(
                $"{context}: serialization parity mismatch — source-gen and reflection resolvers produced " +
                $"different JSON.\n  source-gen: {Utf8(reflViaSourceGen)}\n  reflection: {Utf8(reflViaReflection)}");
        }
    }

    private static string Utf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    /// <summary>A parity failure carrying a reproducible description of the offending case.</summary>
    private sealed class ParityException : Exception
    {
        public ParityException(string message)
            : base(PropertyTag + "\n" + message)
        {
        }
    }

    // === Request-shape generators =====================================================================

    private static readonly CancellationToken CT = CancellationToken.None;

    private static readonly string[] NameSubstrings = { "A", "err", "an", "e", "Fig", "xyz", "Ki" };
    private static readonly string[] Categories = { "Fruit", "Berry", "Tropical", "Citrus" };
    private static readonly string[] Descriptions = { "Alpha", "et", "a", "Zzz" };
    private static readonly string[] TerritoryIds = { "AA", "AB", "BA", "BB", "CA", "ZZ" };
    private static readonly string[] FullNames = { "Ann", "Zoe", "Bob", "New Hire" };
    private static readonly string[] Titles = { "Rep", "Manager", "Lead", "Intern" };

    // UnitPrice (decimal) is intentionally excluded from the sort fields: SQLite cannot ORDER BY a
    // decimal column (it throws before either dispatch path runs), so ordering on it would test the
    // provider, not dispatch parity. The sibling GeneratedRucParityPropertyTests avoids this the same way
    // (it orders on a double). UnitPrice is still projected, serialized, and Detail-returned.
    private static readonly string[] ProductSortFields = { "ProductId", "Name", "Category" };
    private static readonly string[] RegionSortFields = { "RegionId", "TerritoryId", "Description" };
    private static readonly string[] EmployeeSortFields = { "EmployeeId", "FullName", "Title", "ReportsTo" };

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    private static Gen<SortSpec> GenSort(string[] fields) =>
        from i in Gen.Int[0, fields.Length - 1]
        from desc in Gen.Bool
        select new SortSpec(fields[i], desc);

    private static Gen<IReadOnlyList<SortSpec>> GenSorts(string[] fields) =>
        from count in Gen.Int[0, 2]
        from sorts in GenSort(fields).Array[count]
        select (IReadOnlyList<SortSpec>)sorts;

    private static Gen<int> GenPage => Gen.Int[0, 3];

    private static Gen<int> GenPageSize => Gen.Int[1, 6];

    /// <summary>A list/export case: the request plus an optional server-trusted scope row filter (R6.3).</summary>
    private readonly record struct ListCase(ViewQueryRequest Request, bool ServerScope, int ScopeFloor);

    private static readonly Gen<ListCase> GenProductList =
        from shape in Gen.Int[0, 4]
        from id in Gen.Int[0, 12]
        from name in Pick(NameSubstrings)
        from hasSearch in Gen.Bool
        from searchName in Pick(NameSubstrings)
        from hasScope in Gen.Bool
        from cat in Pick(Categories)
        from sorts in GenSorts(ProductSortFields)
        from page in GenPage
        from pageSize in GenPageSize
        from serverScope in Gen.Bool
        from scopeFloor in Gen.Int[0, 6]
        select new ListCase(
            new ViewQueryRequest(
                Filter: shape switch
                {
                    0 => null,
                    1 => new FilterLeaf("ProductId", FilterOperator.Equals, id),
                    2 => new FilterLeaf("ProductId", FilterOperator.GreaterThanOrEqual, id),
                    3 => new FilterLeaf("Name", FilterOperator.Contains, name),
                    _ => new FilterLeaf("Name", FilterOperator.StartsWith, name),
                },
                Sort: sorts,
                Page: page,
                PageSize: pageSize,
                Search: hasSearch ? new FilterLeaf("Name", FilterOperator.Contains, searchName) : null,
                Scope: hasScope ? new FilterLeaf("Category", FilterOperator.Equals, cat) : null),
            serverScope,
            scopeFloor);

    private static readonly Gen<ViewQueryRequest> GenRegionList =
        from shape in Gen.Int[0, 3]
        from region in Gen.Int[0, 4]
        from desc in Pick(Descriptions)
        from hasSearch in Gen.Bool
        from searchDesc in Pick(Descriptions)
        from sorts in GenSorts(RegionSortFields)
        from page in GenPage
        from pageSize in GenPageSize
        select new ViewQueryRequest(
            Filter: shape switch
            {
                0 => null,
                1 => new FilterLeaf("RegionId", FilterOperator.Equals, region),
                2 => new FilterLeaf("RegionId", FilterOperator.GreaterThanOrEqual, region),
                _ => new FilterLeaf("Description", FilterOperator.Contains, desc),
            },
            Sort: sorts,
            Page: page,
            PageSize: pageSize,
            Search: hasSearch ? new FilterLeaf("Description", FilterOperator.Contains, searchDesc) : null);

    private static readonly Gen<ViewQueryRequest> GenEmployeeList =
        from shape in Gen.Int[0, 3]
        from id in Gen.Int[0, 6]
        from name in Pick(FullNames)
        from hasSearch in Gen.Bool
        from searchName in Pick(FullNames)
        from sorts in GenSorts(EmployeeSortFields)
        from page in GenPage
        from pageSize in GenPageSize
        select new ViewQueryRequest(
            Filter: shape switch
            {
                0 => null,
                1 => new FilterLeaf("EmployeeId", FilterOperator.LessThanOrEqual, id),
                2 => new FilterLeaf("FullName", FilterOperator.Contains, name),
                _ => new FilterLeaf("Title", FilterOperator.StartsWith, name),
            },
            Sort: sorts,
            Page: page,
            PageSize: pageSize,
            Search: hasSearch ? new FilterLeaf("FullName", FilterOperator.Contains, searchName) : null);

    private static readonly Gen<int> GenProductKey = Gen.Int[0, 12];

    private static readonly Gen<int> GenEmployeeKey = Gen.Int[0, 6];

    private static readonly Gen<IReadOnlyDictionary<string, object?>> GenRegionKey =
        from region in Gen.Int[0, 4]
        from territory in Pick(TerritoryIds)
        select (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["RegionId"] = region,
            ["TerritoryId"] = territory,
        };

    private static readonly Gen<EmployeeCrud> GenEmployeeCrud =
        from name in Pick(FullNames)
        from title in Pick(Titles)
        from reports in Gen.Int[0, 5]
        select new EmployeeCrud { FullName = name, Title = title, ReportsTo = reports };

    /// <summary>An update case: the target key, the write model, and whether to force a bad token.</summary>
    private readonly record struct UpdateCase(int Key, EmployeeCrud Model, bool WrongToken);

    private static readonly Gen<UpdateCase> GenUpdate =
        from key in GenEmployeeKey
        from model in GenEmployeeCrud
        from wrong in Gen.Bool
        select new UpdateCase(key, model, wrong);

    /// <summary>A delete case: the target key and whether to force a bad token.</summary>
    private readonly record struct DeleteCase(int Key, bool WrongToken);

    private static readonly Gen<DeleteCase> GenDelete =
        from key in GenEmployeeKey
        from wrong in Gen.Bool
        select new DeleteCase(key, wrong);

    // === Property 1 — List parity (paging, filter/search/scope, both totals) ==========================

    [Test]
    public void List_Parity_Product_SingleKey_ReadOnly()
    {
        // Feature: source-generator-http-surface, Property 1: for the read-only single-key ProductView and
        // any List request shape (filter/search/client-scope/sort/paging + a server-trusted scope), the
        // generated dispatch + serialization equals the reflection oracle (R2.1, R2.2, R4.3, R6.3, R10.1).
        GenProductList.Sample(
            testCase =>
            {
                using var harness = Infra.CreateHarness();
                var scope = new ViewScope();
                if (testCase.ServerScope)
                {
                    // Server-trusted row filter over the SOURCE entity, applied pre-projection (DR9). Uses
                    // the int PK (not the decimal UnitPrice) so SQLite can translate the comparison.
                    scope.AddRowFilter<ProductEntity>(p => p.ProductId >= testCase.ScopeFloor);
                }

                RunListParity(Infra.ProductMetadata, Infra.ProductInvoker, harness.Executor, testCase.Request, scope, "Product List");
            },
            iter: Iterations);
    }

    [Test]
    public void List_Parity_RegionTerritory_CompositeKey_ReadOnly()
    {
        // Feature: source-generator-http-surface, Property 1: the read-only composite-key RegionTerritoryView
        // List path is byte-parity with the reflection oracle across request shapes (R2.1, R4.3, R10.1).
        GenRegionList.Sample(
            request =>
            {
                using var harness = Infra.CreateHarness();
                RunListParity(Infra.RegionTerritoryMetadata, Infra.RegionTerritoryInvoker, harness.Executor, request, new ViewScope(), "RegionTerritory List");
            },
            iter: Iterations);
    }

    [Test]
    public void List_Parity_Employee_Writable()
    {
        // Feature: source-generator-http-surface, Property 1: the writable EmployeeView List (read facet of
        // a View<TRow, TCrud> invoker) is byte-parity with the reflection oracle (R2.1, R4.3, R10.1).
        GenEmployeeList.Sample(
            request =>
            {
                using var harness = Infra.CreateHarness();
                RunListParity(Infra.EmployeeMetadata, Infra.EmployeeInvoker, harness.Executor, request, new ViewScope(), "Employee List");
            },
            iter: Iterations);
    }

    // === Property 1 — Detail parity (single + composite key, 200/404) =================================

    [Test]
    public void Detail_Parity_Product_SingleKey()
    {
        // Feature: source-generator-http-surface, Property 1: Detail-by-single-key parity — the generated
        // invoker and the reflection oracle agree on the row (200) or its absence (404) and its body (R2.1,
        // R10.1, R10.2).
        GenProductKey.Sample(
            key =>
            {
                using var harness = Infra.CreateHarness();
                RunDetailParity(Infra.ProductMetadata, Infra.ProductInvoker, harness.Executor, key, "Product Detail key=" + key);
            },
            iter: Iterations);
    }

    [Test]
    public void Detail_Parity_RegionTerritory_CompositeKey()
    {
        // Feature: source-generator-http-surface, Property 1: Detail-by-COMPOSITE-key parity — the generated
        // read invoker resolves the composite (RegionId, TerritoryId) key identically to the oracle (R2.1,
        // R10.2).
        GenRegionKey.Sample(
            key =>
            {
                using var harness = Infra.CreateHarness();
                RunDetailParity(
                    Infra.RegionTerritoryMetadata,
                    Infra.RegionTerritoryInvoker,
                    harness.Executor,
                    key,
                    "RegionTerritory Detail key=(" + key["RegionId"] + "," + key["TerritoryId"] + ")");
            },
            iter: Iterations);
    }

    // === Property 1 — Export row extraction parity ====================================================

    [Test]
    public void Export_Parity_Product_RowExtraction()
    {
        // Feature: source-generator-http-surface, Property 1: Export row extraction parity — the generated
        // invoker's ViewInvocationListResult (rows + both totals, no ViewListResult<TRow> reflection) equals
        // the reflection ToAdapterResult extraction over the export-bounded request (R2.2, R10.2).
        GenProductList.Sample(
            testCase =>
            {
                using var harness = Infra.CreateHarness();
                var view = Infra.ProductMetadata;
                var exportRequest = testCase.Request with { Page = 0, PageSize = view.Limits.MaxExportRows };
                RunListParity(view, Infra.ProductInvoker, harness.Executor, exportRequest, new ViewScope(), "Product Export");
            },
            iter: Iterations);
    }

    // === Property 1 — Write parity (Create PK, Update/Delete bool, concurrency conflicts) =============

    [Test]
    public void Write_Create_Parity_Employee()
    {
        // Feature: source-generator-http-surface, Property 1: Create parity — the generated write invoker
        // (CreateAsync closing TCrud at compile time) returns the same primary key and persists the same
        // row as the reflection oracle for any write model (R3.1, R3.2, R4.3).
        GenEmployeeCrud.Sample(
            model =>
            {
                using var generated = Infra.CreateHarness();
                using var reflection = Infra.CreateHarness();
                var view = Infra.EmployeeMetadata;

                var pkGenerated = Infra.EmployeeInvoker
                    .CreateAsync(generated.Executor, view, model, new ViewScope(), CT)
                    .GetAwaiter().GetResult();
                var pkReflection = ReflectionCreateAsync(reflection.Executor, view, model, new ViewScope(), CT)
                    .GetAwaiter().GetResult();

                if (!KeyEqual(pkGenerated, pkReflection))
                {
                    throw new ParityException($"Create PK mismatch: generated '{pkGenerated}' vs reflection '{pkReflection}'.");
                }

                AssertEmployeesEqual(generated.Context, reflection.Context, "Create");
            },
            iter: Iterations);
    }

    [Test]
    public void Write_Update_Parity_Employee_Including_ConcurrencyConflict()
    {
        // Feature: source-generator-http-surface, Property 1: Update parity — the generated write invoker
        // (UpdateAsync closing TCrud, identity from the key only, token passed through unchanged) matches
        // the reflection oracle on the bool outcome, the 409 concurrency conflict, and the persisted state
        // (R3.1, R3.2, R4.3, R6.2).
        GenUpdate.Sample(
            testCase =>
            {
                using var generated = Infra.CreateHarness();
                using var reflection = Infra.CreateHarness();
                var view = Infra.EmployeeMetadata;
                var token = TokenFor(generated.Context, testCase.Key, testCase.WrongToken);

                var generatedOutcome = Capture(() => Infra.EmployeeInvoker
                    .UpdateAsync(generated.Executor, view, testCase.Key, testCase.Model, new ViewScope(), token, CT)
                    .GetAwaiter().GetResult());
                var reflectionOutcome = Capture(() =>
                    ReflectionUpdateAsync(reflection.Executor, view, testCase.Key, testCase.Model, new ViewScope(), token, CT)
                        .GetAwaiter().GetResult());

                AssertWriteOutcomeParity(generatedOutcome, reflectionOutcome, "Update key=" + testCase.Key);
                AssertEmployeesEqual(generated.Context, reflection.Context, "Update key=" + testCase.Key);
            },
            iter: Iterations);
    }

    [Test]
    public void Write_Delete_Parity_Employee_Including_ConcurrencyConflict()
    {
        // Feature: source-generator-http-surface, Property 1: Delete parity — Delete is a non-generic
        // executor call the generated and reflection paths share (no dispatch divergence), so the observable
        // bool outcome, the 409 concurrency conflict, and the persisted state must match across two
        // identically-seeded databases (R3.2, R6.2).
        GenDelete.Sample(
            testCase =>
            {
                using var generated = Infra.CreateHarness();
                using var reflection = Infra.CreateHarness();
                var view = Infra.EmployeeMetadata;
                var token = TokenFor(generated.Context, testCase.Key, testCase.WrongToken);

                var generatedOutcome = Capture(() => generated.Executor
                    .DeleteAsync(view, testCase.Key, new ViewScope(), token, CT)
                    .GetAwaiter().GetResult());
                var reflectionOutcome = Capture(() => reflection.Executor
                    .DeleteAsync(view, testCase.Key, new ViewScope(), token, CT)
                    .GetAwaiter().GetResult());

                AssertWriteOutcomeParity(generatedOutcome, reflectionOutcome, "Delete key=" + testCase.Key);
                AssertEmployeesEqual(generated.Context, reflection.Context, "Delete key=" + testCase.Key);
            },
            iter: Iterations);
    }

    // === Shared run/assert helpers ====================================================================

    private static void RunListParity(
        ViewMetadata view, IViewInvoker invoker, EfViewExecutor executor, ViewQueryRequest request, IViewScope scope, string context)
    {
        var generated = invoker.ListAsync(executor, view, request, scope, CT).GetAwaiter().GetResult();
        var reflection = ReflectionListAsync(executor, view, request, scope, CT).GetAwaiter().GetResult();

        var description = context + " " + Describe(request);
        AssertResponseParity(generated.BoxedResult, reflection, reflection.GetType(), description);

        // R2.2: the invoker's rows + both totals match the reflection ToAdapterResult extraction, byte-wise.
        var (reflectionRows, reflectionFiltered, reflectionUnfiltered) = ReflectExtract(reflection);
        if (generated.TotalRowsFiltered != reflectionFiltered || generated.TotalRowsUnfiltered != reflectionUnfiltered)
        {
            throw new ParityException(
                $"{description}: total mismatch — generated (filtered={generated.TotalRowsFiltered}, " +
                $"unfiltered={generated.TotalRowsUnfiltered}) vs reflection (filtered={reflectionFiltered}, " +
                $"unfiltered={reflectionUnfiltered}).");
        }

        AssertRowsParity(generated.Rows, reflectionRows, description);
    }

    private static void RunDetailParity(
        ViewMetadata view, IViewInvoker invoker, EfViewExecutor executor, object key, string context)
    {
        var generated = invoker.DetailAsync(executor, view, key, scope: new ViewScope(), CT).GetAwaiter().GetResult();
        var reflection = ReflectionDetailAsync(executor, view, key, scope: new ViewScope(), CT).GetAwaiter().GetResult();

        var generatedStatus = generated is null ? 404 : 200;
        var reflectionStatus = reflection is null ? 404 : 200;
        if (generatedStatus != reflectionStatus)
        {
            throw new ParityException($"{context}: status mismatch — generated {generatedStatus} vs reflection {reflectionStatus}.");
        }

        if (generated is not null)
        {
            AssertResponseParity(generated, reflection, generated.GetType(), context);
        }
    }

    private static void AssertRowsParity(IReadOnlyList<object?> generatedRows, IReadOnlyList<object?> reflectionRows, string context)
    {
        if (generatedRows.Count != reflectionRows.Count)
        {
            throw new ParityException($"{context}: row-count mismatch — generated {generatedRows.Count} vs reflection {reflectionRows.Count}.");
        }

        for (var i = 0; i < generatedRows.Count; i++)
        {
            var generatedRow = generatedRows[i];
            var reflectionRow = reflectionRows[i];
            if ((generatedRow is null) != (reflectionRow is null))
            {
                throw new ParityException($"{context}: row[{i}] nullability mismatch.");
            }

            if (generatedRow is null)
            {
                continue;
            }

            var type = generatedRow.GetType();
            var generatedJson = SerializeSourceGen(generatedRow, type);
            var reflectionJson = SerializeReflection(reflectionRow, type);
            if (!generatedJson.AsSpan().SequenceEqual(reflectionJson))
            {
                throw new ParityException(
                    $"{context}: row[{i}] mismatch.\n  generated: {Utf8(generatedJson)}\n  reflection: {Utf8(reflectionJson)}");
            }
        }
    }

    /// <summary>Reads the target's current concurrency token, or a deliberately wrong one.</summary>
    private static string? TokenFor(HttpSurfaceParityDbContext context, int key, bool wrong)
    {
        var current = context.Employees.AsNoTracking().FirstOrDefault(e => e.EmployeeId == key);
        if (current is null)
        {
            // No row → the executor returns false before the token check; the token value is irrelevant.
            return wrong ? "999999" : null;
        }

        return wrong
            ? (current.Version + 1000).ToString(CultureInfo.InvariantCulture)
            : current.Version.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Runs a write action, capturing its bool result or the typed write exception it raises.</summary>
    private static (bool Ok, Type? ExceptionType) Capture(Func<bool> action)
    {
        try
        {
            return (action(), null);
        }
        catch (VistaConcurrencyConflictException ex)
        {
            return (false, ex.GetType());
        }
        catch (VistaWriteException ex)
        {
            return (false, ex.GetType());
        }
    }

    private static void AssertWriteOutcomeParity((bool Ok, Type? ExceptionType) generated, (bool Ok, Type? ExceptionType) reflection, string context)
    {
        if (generated.ExceptionType != reflection.ExceptionType)
        {
            throw new ParityException(
                $"{context}: exception-type mismatch — generated '{generated.ExceptionType?.Name ?? "none"}' vs " +
                $"reflection '{reflection.ExceptionType?.Name ?? "none"}'.");
        }

        if (generated.ExceptionType is null && generated.Ok != reflection.Ok)
        {
            throw new ParityException($"{context}: outcome mismatch — generated {generated.Ok} vs reflection {reflection.Ok}.");
        }
    }

    /// <summary>Compares the full employees table of two databases row-by-row (post-write state parity).</summary>
    private static void AssertEmployeesEqual(HttpSurfaceParityDbContext generated, HttpSurfaceParityDbContext reflection, string context)
    {
        var generatedRows = generated.Employees.AsNoTracking().OrderBy(e => e.EmployeeId).ToList();
        var reflectionRows = reflection.Employees.AsNoTracking().OrderBy(e => e.EmployeeId).ToList();

        if (generatedRows.Count != reflectionRows.Count)
        {
            throw new ParityException($"{context}: employee count mismatch — generated {generatedRows.Count} vs reflection {reflectionRows.Count}.");
        }

        for (var i = 0; i < generatedRows.Count; i++)
        {
            var g = generatedRows[i];
            var r = reflectionRows[i];
            if (g.EmployeeId != r.EmployeeId
                || !string.Equals(g.FullName, r.FullName, StringComparison.Ordinal)
                || !string.Equals(g.Title, r.Title, StringComparison.Ordinal)
                || g.ReportsTo != r.ReportsTo
                || g.Version != r.Version)
            {
                throw new ParityException(
                    $"{context}: employee[{i}] state mismatch — generated (Id={g.EmployeeId}, {g.FullName}, {g.Title}, " +
                    $"{g.ReportsTo}, v{g.Version}) vs reflection (Id={r.EmployeeId}, {r.FullName}, {r.Title}, {r.ReportsTo}, v{r.Version}).");
            }
        }
    }

    private static bool KeyEqual(object a, object b) =>
        string.Equals(FormatKey(a), FormatKey(b), StringComparison.Ordinal);

    private static string FormatKey(object key) =>
        key is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : key.ToString() ?? "null";

    private static string Describe(ViewQueryRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("Page=").Append(request.Page).Append(", PageSize=").Append(request.PageSize);
        sb.Append(", Filter=").Append(DescribeNode(request.Filter));
        sb.Append(", Search=").Append(DescribeNode(request.Search));
        sb.Append(", Scope=").Append(DescribeNode(request.Scope));
        sb.Append(", Sort=[").Append(string.Join(", ", request.Sort.Select(s => $"{s.Field}{(s.Descending ? " desc" : " asc")}"))).Append(']');
        return sb.ToString();
    }

    private static string DescribeNode(FilterNode? node) =>
        node switch
        {
            null => "(none)",
            FilterLeaf leaf => $"{leaf.Field} {leaf.Op} '{leaf.Value}'",
            _ => node.GetType().Name,
        };

    // === Reflection oracle dispatch (mirrors ViewRequestExecutor.*ReflectionAsync) =====================

    /// <summary>Awaits a runtime-closed <c>Task&lt;TResult&gt;</c> and returns its boxed result.</summary>
    private static async Task<object?> AwaitResultAsync(Task task)
    {
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task);
    }

    private static async Task<object> ReflectionListAsync(
        IViewExecutor executor, ViewMetadata view, ViewQueryRequest request, IViewScope scope, CancellationToken ct)
    {
        var closed = ListAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, new object[] { view, request, scope, ct })!;
        return (await AwaitResultAsync(task).ConfigureAwait(false))!;
    }

    private static async Task<object?> ReflectionDetailAsync(
        IViewExecutor executor, ViewMetadata view, object key, IViewScope scope, CancellationToken ct)
    {
        var closed = DetailAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, new object[] { view, key, scope, ct })!;
        return await AwaitResultAsync(task).ConfigureAwait(false);
    }

    private static async Task<object> ReflectionCreateAsync(
        IViewExecutor executor, ViewMetadata view, object model, IViewScope scope, CancellationToken ct)
    {
        var closed = CreateAsyncMethod.MakeGenericMethod(view.CrudType!);
        var task = (Task)closed.Invoke(executor, new object[] { view, model, scope, ct })!;
        return (await AwaitResultAsync(task).ConfigureAwait(false))!;
    }

    private static async Task<bool> ReflectionUpdateAsync(
        IViewExecutor executor, ViewMetadata view, object key, object model, IViewScope scope, string? token, CancellationToken ct)
    {
        var closed = UpdateAsyncMethod.MakeGenericMethod(view.CrudType!);
        var task = (Task)closed.Invoke(executor, new object[] { view, key, model, scope, token!, ct })!;
        return (bool)(await AwaitResultAsync(task).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Extracts rows + both totals from a boxed <c>ViewListResult&lt;TRow&gt;</c> by reflection — the exact
    /// <c>ViewRequestExecutor.ToAdapterResult</c> the generated invoker replaces (R2.2). Used to confirm the
    /// generated <see cref="ViewInvocationListResult"/> matches the reflection extraction.
    /// </summary>
    private static (IReadOnlyList<object?> Rows, long Filtered, long Unfiltered) ReflectExtract(object boxed)
    {
        var resultType = boxed.GetType();
        var page = resultType.GetProperty("Page")!.GetValue(boxed)!;
        var unfiltered = (long)resultType.GetProperty("TotalRowsUnfiltered")!.GetValue(boxed)!;

        var pageType = page.GetType();
        var filtered = (long)pageType.GetProperty("TotalRows")!.GetValue(page)!;
        var items = (IEnumerable)pageType.GetProperty("Items")!.GetValue(page)!;

        var rows = new List<object?>();
        foreach (var item in items)
        {
            rows.Add(item);
        }

        return (rows, filtered, unfiltered);
    }
}

/// <summary>
/// Minimal EF context exposing the three representative sample entities so a real
/// <see cref="EfViewExecutor"/> can root each view's queryable on <c>DbContext.Set&lt;TSource&gt;()</c>
/// (Decision Log D11). The composite key of <see cref="RegionTerritoryEntity"/> is configured explicitly;
/// the two single-key entities use the EF convention (an identity integer key).
/// </summary>
internal sealed class HttpSurfaceParityDbContext : DbContext
{
    public HttpSurfaceParityDbContext(DbContextOptions<HttpSurfaceParityDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProductEntity> Products => Set<ProductEntity>();

    public DbSet<RegionTerritoryEntity> RegionTerritories => Set<RegionTerritoryEntity>();

    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductEntity>().HasKey(e => e.ProductId);
        modelBuilder.Entity<RegionTerritoryEntity>().HasKey(e => new { e.RegionId, e.TerritoryId });
        modelBuilder.Entity<EmployeeEntity>().HasKey(e => e.EmployeeId);
    }

    /// <summary>
    /// Seeds a deterministic dataset. The data is fixed across iterations — the property quantifies over
    /// random <em>request shapes</em>, not random data (design "Cost control for the master parity
    /// property"). Overlapping names/categories make Contains/StartsWith and scope filters meaningful.
    /// </summary>
    public static void Seed(HttpSurfaceParityDbContext context)
    {
        context.Products.AddRange(
            new ProductEntity { Name = "Apple", Category = "Fruit", UnitPrice = 1.50m },
            new ProductEntity { Name = "Apricot", Category = "Fruit", UnitPrice = 2.25m },
            new ProductEntity { Name = "Banana", Category = "Tropical", UnitPrice = 0.75m },
            new ProductEntity { Name = "Blueberry", Category = "Berry", UnitPrice = 4.00m },
            new ProductEntity { Name = "Cherry", Category = "Berry", UnitPrice = 5.10m },
            new ProductEntity { Name = "Date", Category = "Tropical", UnitPrice = 6.30m },
            new ProductEntity { Name = "Elderberry", Category = "Berry", UnitPrice = 3.80m },
            new ProductEntity { Name = "Fig", Category = "Fruit", UnitPrice = 2.90m },
            new ProductEntity { Name = "Grape", Category = "Fruit", UnitPrice = 1.20m },
            new ProductEntity { Name = "Kiwi", Category = "Tropical", UnitPrice = 3.40m });

        context.RegionTerritories.AddRange(
            new RegionTerritoryEntity { RegionId = 1, TerritoryId = "AA", Description = "Alpha" },
            new RegionTerritoryEntity { RegionId = 1, TerritoryId = "AB", Description = "Beta" },
            new RegionTerritoryEntity { RegionId = 2, TerritoryId = "BA", Description = "Gamma" },
            new RegionTerritoryEntity { RegionId = 2, TerritoryId = "BB", Description = "Delta" },
            new RegionTerritoryEntity { RegionId = 3, TerritoryId = "CA", Description = "Epsilon" });

        context.Employees.AddRange(
            new EmployeeEntity { FullName = "Ann", Title = "Manager", ReportsTo = 0, Version = 1 },
            new EmployeeEntity { FullName = "Bob", Title = "Rep", ReportsTo = 1, Version = 2 },
            new EmployeeEntity { FullName = "Cara", Title = "Rep", ReportsTo = 1, Version = 3 },
            new EmployeeEntity { FullName = "Dan", Title = "Lead", ReportsTo = 1, Version = 4 },
            new EmployeeEntity { FullName = "Eve", Title = "Rep", ReportsTo = 2, Version = 5 });

        context.SaveChanges();
    }
}

/// <summary>
/// The data-independent parity infrastructure, built once (thread-safe static initialization): the DI
/// graph produced by <c>AddVista</c> for the three sample views, each view's <see cref="ViewMetadata"/>
/// and resolved generated <see cref="IViewInvoker"/>, and the two mirrored serialization option sets. A
/// fresh, seeded SQLite-backed <see cref="EfViewExecutor"/> is created per iteration via
/// <see cref="CreateHarness"/> (CsCheck runs cases in parallel and a <c>DbContext</c> is not
/// thread-safe, so the store/graph is shared but the context is per-iteration).
/// </summary>
internal sealed class Infrastructure
{
    public required ServiceProvider Services { get; init; }

    public required IViewExecutionPlanRegistry PlanRegistry { get; init; }

    public required ViewMetadata ProductMetadata { get; init; }

    public required ViewMetadata RegionTerritoryMetadata { get; init; }

    public required ViewMetadata EmployeeMetadata { get; init; }

    public required IViewInvoker ProductInvoker { get; init; }

    public required IViewInvoker RegionTerritoryInvoker { get; init; }

    public required IViewInvoker EmployeeInvoker { get; init; }

    /// <summary>Source-gen serialization chain: static envelope + sample App_Json_Contexts, no fallback.</summary>
    public required JsonSerializerOptions SourceGenOptions { get; init; }

    /// <summary>Reflection oracle serialization chain: the reflection resolver only.</summary>
    public required JsonSerializerOptions ReflectionOptions { get; init; }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification = "AddVista/Register<TView> (reflection authoring) is exercised on purpose; tests are not trimmed.")]
    public static Infrastructure Build()
    {
        // Force the sample assembly's modules to load so the generated [ModuleInitializer]s register each
        // view's IViewInvoker into ViewInvokerStore before we resolve them.
        _ = new ProductView().Name;
        _ = new RegionTerritoryView().Name;
        _ = new EmployeeView().Name;

        var services = new ServiceCollection();
        services.AddVista(v =>
        {
            v.Register<ProductView>();
            v.Register<RegionTerritoryView>();
            v.Register<EmployeeView>();
        });
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IViewRegistry>();
        var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();

        var productMeta = registry.Get(ProductView.ViewName)
            ?? throw new InvalidOperationException($"View '{ProductView.ViewName}' was not registered.");
        var regionMeta = registry.Get(RegionTerritoryView.ViewName)
            ?? throw new InvalidOperationException($"View '{RegionTerritoryView.ViewName}' was not registered.");
        var employeeMeta = registry.Get(EmployeeView.ViewName)
            ?? throw new InvalidOperationException($"View '{EmployeeView.ViewName}' was not registered.");

        return new Infrastructure
        {
            Services = provider,
            PlanRegistry = planRegistry,
            ProductMetadata = productMeta,
            RegionTerritoryMetadata = regionMeta,
            EmployeeMetadata = employeeMeta,
            ProductInvoker = ResolveInvoker(productMeta.Name),
            RegionTerritoryInvoker = ResolveInvoker(regionMeta.Name),
            EmployeeInvoker = ResolveInvoker(employeeMeta.Name),
            SourceGenOptions = BuildSourceGenOptions(),
            ReflectionOptions = BuildReflectionOptions(),
        };
    }

    /// <summary>Resolves a generated invoker from the process-wide store, failing fast when absent.</summary>
    private static IViewInvoker ResolveInvoker(string viewName)
    {
        if (!ViewInvokerStore.TryGet(viewName, out var invoker))
        {
            throw new InvalidOperationException(
                $"No generated IViewInvoker is registered for '{viewName}'. The ViewInvokerGenerator must " +
                "emit an invoker + [ModuleInitializer] for this representative view.");
        }

        return invoker!;
    }

    /// <summary>
    /// Builds a fresh <see cref="JsonSerializerOptions"/> mirroring the Vista seam configuration with the
    /// shipped <see cref="VistaStaticJsonContext"/> and the three sample <c>App_Json_Context</c>s chained
    /// ahead of NO reflection fallback — the AOT-clean shape the generated path serializes through.
    /// </summary>
    private static JsonSerializerOptions BuildSourceGenOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());
        options.TypeInfoResolverChain.Add(VistaStaticJsonContext.Default);
        options.TypeInfoResolverChain.Add(ProductJsonContext.Default);
        options.TypeInfoResolverChain.Add(RegionTerritoryJsonContext.Default);
        options.TypeInfoResolverChain.Add(EmployeeJsonContext.Default);
        return options;
    }

    /// <summary>
    /// Builds a fresh <see cref="JsonSerializerOptions"/> mirroring the Vista seam configuration with only
    /// the reflection fallback (<see cref="DefaultJsonTypeInfoResolver"/>) — the Behavioral_Oracle
    /// serialization path.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification = "The reflection resolver is the deliberate serialization oracle; tests are not trimmed.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Calling members annotated with RequiresDynamicCodeAttribute may break functionality when AOT compiling",
        Justification = "The reflection resolver is the deliberate serialization oracle; AOT is not used for tests.")]
    private static JsonSerializerOptions BuildReflectionOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        return options;
    }

    /// <summary>Creates a fresh, seeded SQLite-backed executor harness (one per parallel iteration).</summary>
    public ParityHarness CreateHarness() => ParityHarness.Create(Services, PlanRegistry);
}

/// <summary>
/// A disposable per-iteration harness owning an open in-memory SQLite connection, a seeded
/// <see cref="HttpSurfaceParityDbContext"/>, and an <see cref="EfViewExecutor"/> over it wired to the
/// shared DI graph (write-facet registry + plan registry). SQLite in-memory databases live only while the
/// connection is open, so the connection is disposed last.
/// </summary>
internal sealed class ParityHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    private ParityHarness(SqliteConnection connection, HttpSurfaceParityDbContext context, EfViewExecutor executor)
    {
        _connection = connection;
        Context = context;
        Executor = executor;
    }

    public HttpSurfaceParityDbContext Context { get; }

    public EfViewExecutor Executor { get; }

    public static ParityHarness Create(IServiceProvider services, IViewExecutionPlanRegistry planRegistry)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<HttpSurfaceParityDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new HttpSurfaceParityDbContext(options);
        context.Database.EnsureCreated();
        HttpSurfaceParityDbContext.Seed(context);

        // The base (ordinal) FilterCompiler + DefaultQueryDialect is used by the SQLite-backed parity
        // tests (matching GeneratedRucParityPropertyTests); the dispatch mechanism under test is identical
        // regardless, so this only governs the shared query translation.
        var executor = new EfViewExecutor(context, services, planRegistry, new FilterCompiler(new DefaultQueryDialect()));
        return new ParityHarness(connection, context, executor);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
