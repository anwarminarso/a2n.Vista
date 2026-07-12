// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Phase 4 AOT verification (spec source-generator-http-surface, Task 11.1, R8.1/R8.2/R8.3; D123/D124).
//
// This probe drives a full typed Style B HTTP round-trip AOT-cleanly, exercising BOTH halves the phase
// makes trim/AOT-clean:
//
//   1) DISPATCH (D123). The generated IViewInvoker for each covered view — emitted into this assembly by
//      the ViewInvokerGenerator and registered into a2n.Vista.Core's ViewInvokerStore via a
//      [ModuleInitializer] — is resolved from the store and used to dispatch List + Detail (the read-only
//      ProbeWidgetView) and Create + Update (the writable ProbeMemoView). The invoker closes TRow/TCrud at
//      compile time and rides the executor's NON-RUC facets (IViewExecutor.ListAsync<TRow> etc.), so no
//      MakeGenericMethod / Task<TResult>.Result / ViewListResult<TRow> reflection is reached (R2, R3).
//
//   2) SERIALIZATION (D124). A write body is bound through the seam (VistaWriteBinding.BindModel resolves
//      TCrud's JsonTypeInfo via VistaJson.Options and deserializes with the AOT-safe overload), and every
//      response (the List envelope, the Detail row, the write response) is serialized through the
//      Serialization_Seam (VistaJsonWriter -> VistaJson.Options + TypeInfoResolverChain). The seam is
//      configured with the shipped VistaStaticJsonContext (Static_Envelope_Context) plus a probe-authored
//      App_Json_Context (ProbeHttpJsonContext), and the reflection fallback resolver is REMOVED
//      (VistaJson.DisableReflectionFallback — what IVistaEndpointBuilder.DisableVistaReflectionSerialization
//      Fallback() calls). With no reflection resolver in the chain, a successful GetTypeInfo for a view DTO
//      proves the seam resolved it from a source-gen context, not DefaultJsonTypeInfoResolver (R8.1/R8.2).
//
// Keeping the analyzed surface honest (mirrors the Phase 2 / Phase 3 probes):
//   * The generated invoker and the seam helpers (VistaJsonWriter, VistaWriteBinding.BindModel,
//     VistaJson.Options.GetTypeInfo) are the ONLY Vista HTTP surface under the strict (warning-as-error)
//     trim/AOT analyzer. None carry an unsuppressed [RequiresUnreferencedCode]/[RequiresDynamicCode], so
//     dispatching THROUGH the invoker and serializing THROUGH the seam on this non-suppressed surface is
//     itself the member-level RUC proof: a RUC member here would raise IL2026 and fail the build.
//   * EF Core provider wire-up, schema creation, seeding, and SaveChanges are framework infrastructure
//     documented as not trim/AOT compatible; they are NOT the generated dispatch / seam serialization
//     path, so they stay isolated behind narrowly-scoped suppressions. Nothing on the verified surface is
//     suppressed.
//   * The Style A view's central-template build is the permanent D96 RUC path; it is isolated behind a
//     narrowly-scoped suppression to demonstrate the coexistence boundary (R8.3) — it is NOT required to
//     be AOT-clean, and it has no generated invoker in the store.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using a2n.Vista.Write;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace a2n.Vista.AotProbe;

/// <summary>
/// Developer-authored, source-generated <c>App_Json_Context</c> for the Phase 4 HTTP-surface probe. Lists
/// exactly the <c>[JsonSerializable]</c> types the generator's VISTA0041 guidance names for the two covered
/// probe views: for the read-only <see cref="ProbeWidgetView"/> and the writable <see cref="ProbeMemoView"/>
/// their <c>TRow</c>, <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>, and — for the
/// writable view — <c>TCrud</c>. Because this is real source, the built-in System.Text.Json source
/// generator produces AOT-clean metadata for these types; chaining it into the Serialization_Seam via
/// <c>VistaJson.AddContext</c> makes per-view (de)serialization AOT-clean.
/// </summary>
[JsonSerializable(typeof(ProbeWidgetRow))]
[JsonSerializable(typeof(ViewListResult<ProbeWidgetRow>))]
[JsonSerializable(typeof(PagedResult<ProbeWidgetRow>))]
[JsonSerializable(typeof(ProbeMemoRow))]
[JsonSerializable(typeof(ViewListResult<ProbeMemoRow>))]
[JsonSerializable(typeof(PagedResult<ProbeMemoRow>))]
[JsonSerializable(typeof(ProbeMemoCrud))]
internal sealed partial class ProbeHttpJsonContext : JsonSerializerContext
{
}

/// <summary>
/// A public probe-local bridge from a source-generated <see cref="ICompiledViewExecutionPlan"/> (obtained
/// from <see cref="GeneratedExecutionPlanStore"/>) into the <see cref="IViewExecutionPlanRegistry"/>, which
/// stores <see cref="IViewExecutionPlan"/>. It mirrors the EF layer's internal
/// <c>CompiledExecutionPlanAdapter</c> using only the two public plan interfaces, so the probe can register
/// the generated read plan and have the public <see cref="EfViewExecutor.ListAsync{TRow}"/> /
/// <see cref="EfViewExecutor.DetailAsync{TRow}"/> facets (which the generated invoker rides) resolve it and
/// take the AOT-clean compiled read path at runtime.
/// </summary>
internal sealed class ProbeCompiledPlanAdapter : IViewExecutionPlan, ICompiledViewExecutionPlan
{
    private readonly ICompiledViewExecutionPlan _inner;

    public ProbeCompiledPlanAdapter(ICompiledViewExecutionPlan inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc cref="ICompiledViewExecutionPlan.ViewName" />
    public string ViewName => _inner.ViewName;

    /// <inheritdoc cref="ICompiledViewExecutionPlan.RowType" />
    public Type RowType => _inner.RowType;

    /// <inheritdoc cref="ICompiledViewExecutionPlan.SourceType" />
    public Type SourceType => _inner.SourceType;

    /// <inheritdoc cref="ICompiledViewExecutionPlan.IsSingleSource" />
    public bool IsSingleSource => _inner.IsSingleSource;

    /// <inheritdoc cref="ICompiledViewExecutionPlan.MaskAccessors" />
    public System.Collections.Generic.IReadOnlyList<MaskAccessor> MaskAccessors => _inner.MaskAccessors;

    /// <summary>
    /// Satisfies both interfaces' identically-shaped member. The executor only ever reaches this via the
    /// non-RUC <see cref="ICompiledViewExecutionPlan"/> reference (in <c>ResolveCompiledScopedQueryable</c>),
    /// so the compiled read path stays warning-free; the body delegates to the generated, AOT-clean build.
    /// </summary>
    [SuppressMessage("Trimming", "IL2046:RequiresUnreferencedCode mismatch on override/interface",
        Justification = "The compiled plan builds the queryable from generated expression nodes; it is AOT-clean and the executor only calls it via the non-RUC ICompiledViewExecutionPlan facet (mirrors CompiledExecutionPlanAdapter).")]
    public System.Linq.IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope)
        => _inner.CreateScopedQueryable(dbContext, services, scope);

    /// <inheritdoc cref="ICompiledViewExecutionPlan.TryGetMemberAccess" />
    public bool TryGetMemberAccess(string fieldName, out LambdaExpression accessor)
        => _inner.TryGetMemberAccess(fieldName, out accessor);

    /// <inheritdoc cref="ICompiledViewExecutionPlan.ApplyPrimarySort" />
    public System.Linq.IOrderedQueryable ApplyPrimarySort(System.Linq.IQueryable source, string fieldName, bool descending)
        => _inner.ApplyPrimarySort(source, fieldName, descending);

    /// <inheritdoc cref="ICompiledViewExecutionPlan.ApplyThenSort" />
    public System.Linq.IOrderedQueryable ApplyThenSort(System.Linq.IOrderedQueryable source, string fieldName, bool descending)
        => _inner.ApplyThenSort(source, fieldName, descending);
}

/// <summary>
/// A Style A (central-template, anonymous projection) view for the Phase 4 coexistence demonstration
/// (R8.3 / D96). It derives <see cref="a2n.Vista.Authoring.ViewTemplate{TDbContext}"/>, not
/// <c>View&lt;TQuery&gt;</c>, so the <c>ViewInvokerGenerator</c> never recognizes it and emits no
/// dispatch invoker for it — it stays permanently on the reflection path and is not required to be
/// AOT-clean. Building its metadata is the documented RUC Style A authoring path.
/// </summary>
internal sealed class ProbeStyleATemplate : a2n.Vista.Authoring.ViewTemplate<ProbeDbContext>
{
    /// <summary>The Style A view's globally-unique name (never keys a generated invoker).</summary>
    public const string ViewName = "aotprobe-stylea-widgets";

    /// <inheritdoc />
    protected override void Configure(a2n.Vista.Authoring.IViewTemplateBuilder<ProbeDbContext> views) =>
        views.AddView(ViewName, (db, sp) =>
                from w in db.Widgets
                select new { w.Id, w.Name })
            .Field(x => x.Id, f => f.PrimaryKey());
}

/// <summary>
/// Exercises the generated typed Style B HTTP-surface round-trip for AOT verification (Task 11.1):
/// dispatch List/Detail and a write through the generated <see cref="IViewInvoker"/>, bind a write body
/// and serialize responses through the Serialization_Seam, and demonstrate the Style A coexistence
/// boundary.
/// </summary>
internal static class HttpSurfaceProbe
{
    /// <summary>
    /// Runs the Phase 4 probe: configures the seam (chain a probe App_Json_Context, remove the reflection
    /// fallback), resolves the generated invokers, asserts they carry no AOT-barrier attributes and that
    /// the seam resolves view DTO metadata from source-gen contexts, then dispatches read + write through
    /// the invokers and serializes every response through the seam.
    /// </summary>
    public static async Task RunAsync()
    {
        // --- 1) Configure the Serialization_Seam BEFORE the first (de)serialization (the options freeze
        //        their resolver chain on first use). Chain the probe App_Json_Context ahead of the
        //        reflection fallback, then REMOVE the reflection fallback so the chain is exactly
        //        { VistaStaticJsonContext (shipped), ProbeHttpJsonContext (probe) } — no RUC resolver.
        //        VistaJson.AddContext / DisableReflectionFallback are what IVistaEndpointBuilder's
        //        AddVistaJsonContext(...) / DisableVistaReflectionSerializationFallback() call. ---
        VistaJson.AddContext(ProbeHttpJsonContext.Default);
        VistaJson.DisableReflectionFallback();

        Console.WriteLine();
        Console.WriteLine("AOT probe: generated typed Style B HTTP-surface round-trip exercised.");

        // --- 2) Resolve the generated invokers from the Core ViewInvokerStore. Their [ModuleInitializer]s
        //        registered them at module load; a miss means the ViewInvokerGenerator analyzer did not
        //        run or emit them. ---
        if (!ViewInvokerStore.TryGet(ProbeWidgetView.ViewName, out var widgetInvoker))
        {
            throw new InvalidOperationException(
                $"No generated IViewInvoker was found for '{ProbeWidgetView.ViewName}'. Ensure the source " +
                "generator analyzer is referenced so the ViewInvokerGenerator emits the invoker and its " +
                "[ModuleInitializer] registers it into ViewInvokerStore at module load.");
        }

        if (!ViewInvokerStore.TryGet(ProbeMemoView.ViewName, out var memoInvoker))
        {
            throw new InvalidOperationException(
                $"No generated IViewInvoker was found for '{ProbeMemoView.ViewName}'. Ensure the source " +
                "generator analyzer is referenced so the ViewInvokerGenerator emits the invoker and its " +
                "[ModuleInitializer] registers it into ViewInvokerStore at module load.");
        }

        // Arity is reflected by IsWritable: the read-only view's invoker is not writable; the writable
        // view's invoker is (R2/R3).
        if (widgetInvoker.IsWritable)
        {
            throw new InvalidOperationException(
                $"The generated invoker for read-only view '{ProbeWidgetView.ViewName}' must report " +
                "IsWritable == false.");
        }

        if (!memoInvoker.IsWritable)
        {
            throw new InvalidOperationException(
                $"The generated invoker for writable view '{ProbeMemoView.ViewName}' must report " +
                "IsWritable == true.");
        }

        // --- 3) Assert (R8.2) the generated invoker types AND their members carry no
        //        [RequiresUnreferencedCode]/[RequiresDynamicCode]. Method infos are read from delegates
        //        (Delegate.Method) so no GetType().GetMethods() reflection is needed — the check is
        //        itself AOT-clean. Dispatching through these members below (on this non-suppressed
        //        surface) is the complementary member-level RUC proof. ---
        AssertInvokerCarriesNoAotBarrierAttributes(widgetInvoker);
        AssertInvokerCarriesNoAotBarrierAttributes(memoInvoker);
        Console.WriteLine(
            "Generated invoker types/members carry no [RequiresUnreferencedCode]/[RequiresDynamicCode] (R8.2).");

        // --- 4) Assert (R8.1/R8.2) the seam resolves each covered view DTO's JsonTypeInfo from a
        //        source-gen context. The reflection fallback was removed above, so a successful resolve
        //        can only come from VistaStaticJsonContext or the probe App_Json_Context — never the
        //        DefaultJsonTypeInfoResolver. ---
        AssertResolvedFromSourceGen(typeof(ViewListResult<ProbeWidgetRow>));
        AssertResolvedFromSourceGen(typeof(ProbeWidgetRow));
        AssertResolvedFromSourceGen(typeof(ProbeMemoCrud));
        AssertResolvedFromSourceGen(typeof(VistaWriteResponse)); // shipped Static_Envelope_Context
        Console.WriteLine(
            "Serialization_Seam resolves view DTO JsonTypeInfo from source-gen contexts, reflection " +
            "fallback removed (R8.1/R8.2).");

        // --- 5) READ dispatch through the generated invoker (ProbeWidgetView). ---
        await RunReadRoundTripAsync(widgetInvoker).ConfigureAwait(false);

        // --- 6) WRITE dispatch through the generated invoker (ProbeMemoView), body bound via the seam. ---
        await RunWriteRoundTripAsync(memoInvoker).ConfigureAwait(false);

        // --- 7) Style A coexistence boundary (R8.3 / D96). ---
        RunStyleACoexistence();
    }

    /// <summary>
    /// Drives List + Detail through the generated read invoker over a SQLite-backed dataset and serializes
    /// the responses through the seam. The invoker calls the executor's non-RUC facets; the generated
    /// compiled plan (registered via <see cref="ProbeCompiledPlanAdapter"/>) makes the runtime read path
    /// AOT-clean.
    /// </summary>
    private static async Task RunReadRoundTripAsync(IViewInvoker widgetInvoker)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var context = BuildSeededWidgetContext(connection);

        var view = BuildWidgetViewMetadata();

        // Register the generated compiled read plan into the registry (via the public-interface adapter)
        // so the public ListAsync/DetailAsync facets the invoker rides resolve it and take the compiled
        // (non-RUC) path.
        if (!GeneratedExecutionPlanStore.TryGet(ProbeWidgetView.ViewName, out var compiledPlan))
        {
            throw new InvalidOperationException(
                $"No generated compiled plan was found for '{ProbeWidgetView.ViewName}'.");
        }

        var planRegistry = new ViewExecutionPlanRegistry();
        planRegistry.Add(new ProbeCompiledPlanAdapter(compiledPlan));

        var provider = new ServiceCollection().BuildServiceProvider();
        var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler(new DefaultQueryDialect()));
        var scope = new ViewScope();

        var request = new ViewQueryRequest(
            Filter: new FilterLeaf(nameof(ProbeWidgetRow.Region), FilterOperator.Equals, "EU"),
            Sort: new[] { new SortSpec(nameof(ProbeWidgetRow.Name), Descending: false) },
            Page: 0,
            PageSize: 50,
            Search: null,
            Scope: null);

        // List through the generated invoker: closes ListAsync<ProbeWidgetRow>, awaits directly, and
        // returns the boxed ViewListResult plus the extracted rows/totals (no ViewListResult reflection).
        var listResult = await widgetInvoker
            .ListAsync(executor, view, request, scope, CancellationToken.None)
            .ConfigureAwait(false);

        // Serialize the List envelope through the seam using the source-gen context.
        var listJson = VistaJsonWriter.Serialize(listResult.BoxedResult, typeof(ViewListResult<ProbeWidgetRow>));

        // Detail-by-key through the generated invoker: closes DetailAsync<ProbeWidgetRow>.
        var detail = await widgetInvoker
            .DetailAsync(executor, view, 1, scope, CancellationToken.None)
            .ConfigureAwait(false);

        var detailJson = detail is null
            ? "(none)"
            : VistaJsonWriter.Serialize(detail, typeof(ProbeWidgetRow));

        Console.WriteLine(
            $"List(\"{ProbeWidgetView.ViewName}\", Region=EU): {listResult.Rows.Count} row(s), " +
            $"filtered {listResult.TotalRowsFiltered}, unfiltered {listResult.TotalRowsUnfiltered}; " +
            $"serialized envelope {listJson.Length} chars.");
        Console.WriteLine($"Detail(Id=1) serialized => {detailJson}");
    }

    /// <summary>
    /// Binds a write body through the seam (<see cref="VistaWriteBinding.BindModel"/>) and drives Create +
    /// Update through the generated write invoker (ProbeMemoView) over a SQLite-backed dataset, then
    /// serializes the write response through the seam. Row identity comes from the request key, never the
    /// body (D25/D120); the concurrency token is passed through unchanged.
    /// </summary>
    private static async Task RunWriteRoundTripAsync(IViewInvoker memoInvoker)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var context = BuildSeededMemoContext(connection);

        var view = BuildMemoViewMetadata();

        // DI the write path consults: WriteMapperResolver (generated-preferred) and the write-facet
        // registry (Update's concurrency gate consults it). Both are AOT-clean to resolve.
        var facetRegistry = BuildMemoWriteFacetRegistry();
        var services = new ServiceCollection();
        services.AddSingleton<IWriteFacetRegistry>(facetRegistry);
        services.AddSingleton<WriteMapperResolver>();
        var provider = services.BuildServiceProvider();

        var planRegistry = new ViewExecutionPlanRegistry();
        var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler(new DefaultQueryDialect()));
        var scope = new ViewScope();

        // Bind the CREATE body through the seam: BindModel resolves ProbeMemoCrud's JsonTypeInfo via
        // VistaJson.Options (the probe App_Json_Context) and deserializes with the AOT-safe overload —
        // AOT-clean, never on the (removed) reflection fallback.
        var createModel = BindCrud("{\"text\":\"First memo\",\"priority\":3,\"payload\":null}");

        // Create through the generated invoker: closes CreateAsync<ProbeMemoCrud>, returns the boxed PK.
        var key = await memoInvoker
            .CreateAsync(executor, view, createModel, scope, CancellationToken.None)
            .ConfigureAwait(false);

        // Bind and dispatch an UPDATE through the generated invoker: closes UpdateAsync<ProbeMemoCrud>;
        // identity is the request key, the model body never sets it; a null concurrency token is a no-op
        // for this tokenless view.
        var updateModel = BindCrud("{\"text\":\"Updated memo\",\"priority\":9,\"payload\":null}");
        var updated = await memoInvoker
            .UpdateAsync(executor, view, key, updateModel, scope, concurrencyToken: null, CancellationToken.None)
            .ConfigureAwait(false);

        // Serialize the write response through the seam (VistaWriteResponse is covered by the shipped
        // Static_Envelope_Context).
        var writeJson = VistaJsonWriter.Serialize(new VistaWriteResponse(key), typeof(VistaWriteResponse));

        Console.WriteLine(
            $"Create(\"{ProbeMemoView.ViewName}\") => key {key}; Update => {(updated ? "applied" : "no row")}; " +
            $"write response serialized => {writeJson}");
    }

    /// <summary>
    /// Demonstrates the Style A coexistence boundary (R8.3 / D96): the central-template, anonymous view has
    /// no generated dispatch invoker in the store (so it rides the permanent reflection path), and its
    /// metadata build is the documented RUC Style A authoring path — isolated behind a narrowly-scoped
    /// suppression, proving it works but is NOT required to be AOT-clean.
    /// </summary>
    private static void RunStyleACoexistence()
    {
        if (ViewInvokerStore.TryGet(ProbeStyleATemplate.ViewName, out _))
        {
            throw new InvalidOperationException(
                $"The Style A view '{ProbeStyleATemplate.ViewName}' must NOT have a generated invoker; it " +
                "rides the permanent reflection path (D96 coexistence boundary, R8.3).");
        }

        var styleAViewCount = BuildStyleAViews();
        Console.WriteLine(
            $"Style A view '{ProbeStyleATemplate.ViewName}' present: no generated invoker (reflection path, " +
            $"RUC, D96), built {styleAViewCount} view(s) via the permanent Style A authoring path (R8.3).");
    }

    /// <summary>
    /// Binds a JSON write model to <see cref="ProbeMemoCrud"/> through the Serialization_Seam. The
    /// envelope is constructed directly (as a source-generated model binder would); only the model bind
    /// itself goes through <see cref="VistaWriteBinding.BindModel"/>, which resolves the AOT-clean
    /// JsonTypeInfo for <see cref="ProbeMemoCrud"/> from the probe App_Json_Context.
    /// </summary>
    private static object BindCrud(string modelJson)
    {
        using var document = JsonDocument.Parse("{\"model\":" + modelJson + "}");
        var body = new VistaWriteRequestBody
        {
            Model = document.RootElement.GetProperty("model").Clone(),
        };
        return VistaWriteBinding.BindModel(body, typeof(ProbeMemoCrud));
    }

    /// <summary>
    /// Asserts the seam resolves <paramref name="runtimeType"/>'s <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo"/>
    /// with the reflection fallback removed, i.e. from a chained source-gen context (R8.1/R8.2).
    /// </summary>
    private static void AssertResolvedFromSourceGen(Type runtimeType)
    {
        var typeInfo = VistaJsonWriter.GetTypeInfo(runtimeType);
        if (typeInfo is null)
        {
            throw new InvalidOperationException(
                $"The Serialization_Seam resolved no JsonTypeInfo for '{runtimeType}'. With the reflection " +
                "fallback removed, a covered view DTO must resolve from a source-gen context (R8.1/R8.2).");
        }
    }

    /// <summary>
    /// Asserts, without <c>GetType().GetMethods()</c> reflection, that a generated <see cref="IViewInvoker"/>'s
    /// declaring type and each dispatch member carry neither <see cref="RequiresUnreferencedCodeAttribute"/>
    /// nor <see cref="RequiresDynamicCodeAttribute"/> (R8.2). Method infos come from delegates
    /// (<see cref="System.Delegate.Method"/>), and <see cref="Attribute.IsDefined(MemberInfo, Type)"/> is
    /// AOT-safe.
    /// </summary>
    private static void AssertInvokerCarriesNoAotBarrierAttributes(IViewInvoker invoker)
    {
        Func<IViewExecutor, ViewMetadata, ViewQueryRequest, IViewScope, CancellationToken, Task<ViewInvocationListResult>> list = invoker.ListAsync;
        Func<IViewExecutor, ViewMetadata, object, IViewScope, CancellationToken, Task<object?>> detail = invoker.DetailAsync;
        Func<IViewExecutor, ViewMetadata, object, IViewScope, CancellationToken, Task<object>> create = invoker.CreateAsync;
        Func<IViewExecutor, ViewMetadata, object, object, IViewScope, string?, CancellationToken, Task<bool>> update = invoker.UpdateAsync;

        var members = new MemberInfo[]
        {
            list.Method,
            detail.Method,
            create.Method,
            update.Method,
        };

        // The concrete generated invoker type (the `file sealed` class) must be clean. Attribute.IsDefined
        // over the runtime type is AOT-safe (it reads metadata, never instantiates or enumerates members).
        var invokerType = invoker.GetType();
        if (HasBarrier(invokerType))
        {
            throw new InvalidOperationException(
                $"The generated invoker type '{invokerType.FullName}' must carry no " +
                "[RequiresUnreferencedCode]/[RequiresDynamicCode] (R8.2).");
        }

        foreach (var member in members)
        {
            if (HasBarrier(member))
            {
                throw new InvalidOperationException(
                    $"The generated invoker member '{invokerType.FullName}.{member.Name}' must carry no " +
                    "[RequiresUnreferencedCode]/[RequiresDynamicCode] (R8.2).");
            }
        }

        static bool HasBarrier(MemberInfo member) =>
            Attribute.IsDefined(member, typeof(RequiresUnreferencedCodeAttribute)) ||
            Attribute.IsDefined(member, typeof(RequiresDynamicCodeAttribute));
    }

    /// <summary>Builds the read-only widget view metadata (AOT-clean, hand-built), mirroring the Phase 2 probe.</summary>
    private static ViewMetadata BuildWidgetViewMetadata()
    {
        var fields = new[]
        {
            FieldMetadata.Create("Id", typeof(int), isPrimaryKey: true),
            FieldMetadata.Create("Name", typeof(string)),
            FieldMetadata.Create("Region", typeof(string), allowedOperators: FilterOperator.Equals),
            FieldMetadata.Create("Quantity", typeof(int)),
        };

        return new ViewMetadata(
            Name: ProbeWidgetView.ViewName,
            Route: "/api/views/" + ProbeWidgetView.ViewName,
            QueryType: typeof(ProbeWidgetRow),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: true)
        {
            KeyFields = new[] { "Id" },
        };
    }

    /// <summary>Builds the writable memo view metadata (AOT-clean, hand-built), mirroring the Phase 3 probe.</summary>
    private static ViewMetadata BuildMemoViewMetadata()
    {
        var fields = new[]
        {
            FieldMetadata.Create("Id", typeof(int), isPrimaryKey: true),
            FieldMetadata.Create("Text", typeof(string)),
            FieldMetadata.Create("Priority", typeof(int)),
        };

        return new ViewMetadata(
            Name: ProbeMemoView.ViewName,
            Route: "/api/views/" + ProbeMemoView.ViewName,
            QueryType: typeof(ProbeMemoRow),
            CrudType: typeof(ProbeMemoCrud),
            CrudEntityType: typeof(ProbeMemo),
            Fields: fields,
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: false)
        {
            KeyFields = new[] { "Id" },
        };
    }

    /// <summary>
    /// Builds the memo write-facet registry with the three whitelisted scalar mappings (AOT-clean
    /// expression literals). The write path prefers the generated mapper; this facet backs the Update
    /// concurrency gate (a tokenless no-op here) and any reflection fallback.
    /// </summary>
    private static IWriteFacetRegistry BuildMemoWriteFacetRegistry()
    {
        var mappings = new[]
        {
            new WritableFieldMapping(
                CrudMember: nameof(ProbeMemoCrud.Text),
                EntityMember: nameof(ProbeMemo.Text),
                From: (Expression<Func<ProbeMemoCrud, string>>)(c => c.Text),
                To: (Expression<Func<ProbeMemo, string>>)(e => e.Text)),
            new WritableFieldMapping(
                CrudMember: nameof(ProbeMemoCrud.Priority),
                EntityMember: nameof(ProbeMemo.Priority),
                From: (Expression<Func<ProbeMemoCrud, int>>)(c => c.Priority),
                To: (Expression<Func<ProbeMemo, int>>)(e => e.Priority)),
            new WritableFieldMapping(
                CrudMember: nameof(ProbeMemoCrud.Payload),
                EntityMember: nameof(ProbeMemo.Payload),
                From: (Expression<Func<ProbeMemoCrud, byte[]?>>)(c => c.Payload),
                To: (Expression<Func<ProbeMemo, byte[]?>>)(e => e.Payload)),
        };

        var facet = new CrudFacetDefinition(
            CrudType: typeof(ProbeMemoCrud),
            EntityType: typeof(ProbeMemo),
            WritableFields: mappings,
            ConcurrencyToken: null,
            AllowsBulk: false);

        var registry = new WriteFacetRegistry();
        registry.Register(ProbeMemoView.ViewName, facet);
        return registry;
    }

    /// <summary>
    /// Builds and seeds the widget SQLite context. EF Core provider/schema/seed setup is framework
    /// infrastructure documented as not trim/AOT compatible — it is NOT the generated dispatch/seam path
    /// under verification, so it is isolated here behind narrowly-scoped suppressions.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification = "EF Core provider/schema/seed setup is framework infrastructure, not the generated dispatch/seam path under AOT verification (R8.1).")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Members annotated with 'RequiresDynamicCodeAttribute' may break when AOT compiling",
        Justification = "EF Core provider/schema/seed setup is framework infrastructure, not the generated dispatch/seam path under AOT verification (R8.1).")]
    private static ProbeDbContext BuildSeededWidgetContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ProbeDbContext(options);
        context.Database.EnsureCreated();

        context.Widgets.AddRange(
            new ProbeWidget { Id = 1, Name = "Anchor", Region = "EU", Quantity = 10 },
            new ProbeWidget { Id = 2, Name = "Bolt", Region = "EU", Quantity = 5 },
            new ProbeWidget { Id = 3, Name = "Cog", Region = "US", Quantity = 7 },
            new ProbeWidget { Id = 4, Name = "Dowel", Region = "EU", Quantity = 3 });
        context.SaveChanges();

        return context;
    }

    /// <summary>
    /// Builds the memo SQLite context and creates the schema. Isolated behind narrowly-scoped suppressions
    /// for the same reason as the widget context: EF Core infrastructure is not the verified generated path.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification = "EF Core provider/schema setup is framework infrastructure, not the generated dispatch/seam path under AOT verification (R8.1).")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Members annotated with 'RequiresDynamicCodeAttribute' may break when AOT compiling",
        Justification = "EF Core provider/schema setup is framework infrastructure, not the generated dispatch/seam path under AOT verification (R8.1).")]
    private static ProbeMemoDbContext BuildSeededMemoContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ProbeMemoDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ProbeMemoDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Builds the Style A view metadata via the permanent Style A (central-template) authoring path, which
    /// enumerates the anonymous projection via reflection (RUC / D96). Isolated behind a narrowly-scoped
    /// suppression to demonstrate the coexistence boundary (R8.3): Style A stays RUC and is not required to
    /// be AOT-clean.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification = "Style A (central-template, anonymous projection) authoring is permanently RUC by design (D96, R8.3); it is not required to be AOT-clean and is walled off from the verified generated surface.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Members annotated with 'RequiresDynamicCodeAttribute' may break when AOT compiling",
        Justification = "Style A (central-template, anonymous projection) authoring is permanently RUC by design (D96, R8.3); it is not required to be AOT-clean and is walled off from the verified generated surface.")]
    private static int BuildStyleAViews() => new ProbeStyleATemplate().BuildViews().Count;
}
