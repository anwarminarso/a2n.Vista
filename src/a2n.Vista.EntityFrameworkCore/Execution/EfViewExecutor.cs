using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using a2n.Vista.Write;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// Entity Framework Core implementation of <see cref="IViewExecutor"/>. This type owns the single
/// execution path for a view's facets so that whitelist validation, server-trusted scope, and hard
/// limits cannot be bypassed (Requirement R11.2, Decision Log D48).
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope of this implementation (Task 9.1).</b> This class implements the <b>List</b> path end to
/// end — server-trusted scope, client filter, sort, paging, and the filtered/unfiltered totals
/// (Requirements R9.3, R10.3, R10.4, R11.2) — plus a working <b>Detail</b> path built on the same
/// resolution seam. The remaining task-9 sub-tasks slot in through clearly marked extension points:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Source resolution (Task 9.2 — implemented).</b> <see cref="ResolveScopedQueryable{TRow}"/> is
///     the single seam that turns a <see cref="ViewMetadata"/> + <see cref="IViewScope"/> into a
///     post-projection <see cref="IQueryable{T}"/> with the server-trusted scope already AND-ed in
///     <em>pre-projection</em> over the EF source entity. It resolves the per-view
///     <see cref="IViewExecutionPlan"/> from the injected <see cref="IViewExecutionPlanRegistry"/>; the
///     plan obtains <c>IQueryable&lt;TSource&gt;</c> via the <c>DbContext.Set&lt;TSource&gt;()</c>
///     convention (Decision Log D11) and applies scope/row-filters/projection. Subclasses (and tests,
///     Task 12) may still override the seam to inject a queryable directly.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Provider-aware text matching (Task 9.3 — implemented).</b> The production constructors
///     default the <see cref="FilterCompiler"/> to <see cref="ProviderAwareFilterCompiler"/>, which
///     overrides <c>BuildContains</c>/<c>BuildStartsWith</c>/<c>BuildEndsWith</c> to emit SQL
///     <c>LIKE</c> whose case-sensitivity is decided by the active provider/collation (R9.3, D17, §8.2).
///     A <see cref="FilterCompiler"/> can still be injected through the constructor (for the EF Core
///     InMemory provider, which does not translate <c>EF.Functions.Like</c>). The clamp of
///     <see cref="ViewQueryRequest.PageSize"/> to <see cref="HardLimits.MaxPageSize"/> and the rejection
///     of "return all" requests (<c>length=-1</c>) live here (<see cref="ResolvePageSize"/>).
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>DI wiring (Task 9.4).</b> <c>AddVista(...)</c> registers this executor and the queryable
///     resolver; it does not change the contract implemented here.
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>AOT hygiene (R11.4).</b> List/Detail drive sorting, filtering, and key resolution from
/// string/metadata at runtime (reflection), so they carry <see cref="RequiresUnreferencedCodeAttribute"/>
/// consistent with <see cref="IViewExecutor"/>. The AOT-clean route is the source generator (Pilar 3).
/// </para>
/// <para>
/// <b>Write facet.</b> Create/Update/Delete are implemented (Tasks 4.2–4.4): each public method bridges
/// the port signature to the view's runtime entity type (<see cref="ViewMetadata.CrudEntityType"/>) by
/// closing a private generic <c>*CoreAsync&lt;TEntity&gt;</c> helper via <c>MakeGenericMethod</c>. The
/// write bodies resolve targets within the server-trusted scope, apply the whitelisted
/// <c>WriteMapper</c> (Create/Update only), enforce the optimistic-concurrency precondition, and persist
/// with a single <c>SaveChanges</c> (§7, §9). See the method docs for the per-operation contract.
/// </para>
/// </remarks>
public class EfViewExecutor : IViewExecutor
{
    private static readonly MethodInfo OrderByMethod = GetQueryableOrdering(nameof(Queryable.OrderBy));
    private static readonly MethodInfo OrderByDescendingMethod = GetQueryableOrdering(nameof(Queryable.OrderByDescending));
    private static readonly MethodInfo ThenByMethod = GetQueryableOrdering(nameof(Queryable.ThenBy));
    private static readonly MethodInfo ThenByDescendingMethod = GetQueryableOrdering(nameof(Queryable.ThenByDescending));

    // Write facet bridge (Tasks 4.2–4.4): CreateAsync/UpdateAsync/DeleteAsync are generic over TCrud (or
    // non-generic) at the port, but their bodies must operate on the view's runtime entity type
    // (ViewMetadata.CrudEntityType). Each public method closes the matching private generic *CoreAsync
    // helper over that runtime type via MakeGenericMethod — the same deferred-reflection pattern the read
    // bridge uses (see ViewRequestExecutor). The MethodInfo is resolved once and cached here.
    private static readonly MethodInfo CreateCoreAsyncMethod =
        typeof(EfViewExecutor).GetMethod(nameof(CreateCoreAsync), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{nameof(EfViewExecutor)}.{nameof(CreateCoreAsync)} was not found.");

    private static readonly MethodInfo UpdateCoreAsyncMethod =
        typeof(EfViewExecutor).GetMethod(nameof(UpdateCoreAsync), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{nameof(EfViewExecutor)}.{nameof(UpdateCoreAsync)} was not found.");

    private static readonly MethodInfo DeleteCoreAsyncMethod =
        typeof(EfViewExecutor).GetMethod(nameof(DeleteCoreAsync), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{nameof(EfViewExecutor)}.{nameof(DeleteCoreAsync)} was not found.");

    private readonly FilterCompiler _filterCompiler;
    private readonly DbContext? _dbContext;
    private readonly IServiceProvider? _services;
    private readonly IViewExecutionPlanRegistry? _planRegistry;

    /// <summary>
    /// Initializes a new <see cref="EfViewExecutor"/> with the default <see cref="FilterCompiler"/> and
    /// <b>no</b> source-resolution dependencies. This constructor is for subclasses (and tests, Task 12)
    /// that override <see cref="ResolveScopedQueryable{TRow}"/> to supply their own queryable; the base
    /// <see cref="ResolveScopedQueryable{TRow}"/> cannot run without an execution-plan registry and
    /// throws to make that explicit.
    /// </summary>
    public EfViewExecutor()
        : this(new FilterCompiler())
    {
    }

    /// <summary>
    /// Initializes a new <see cref="EfViewExecutor"/> with a supplied <see cref="FilterCompiler"/> and
    /// <b>no</b> source-resolution dependencies. As with the parameterless constructor, this is for
    /// subclasses/tests that override <see cref="ResolveScopedQueryable{TRow}"/>. Task 9.3 injects a
    /// provider-aware <see cref="FilterCompiler"/> subclass here so client text operators translate to
    /// the correct SQL for the active EF provider (for example <c>EF.Functions.ILike</c> on Npgsql).
    /// </summary>
    /// <param name="filterCompiler">The compiler used to turn the neutral filter tree into a predicate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="filterCompiler"/> is <see langword="null"/>.</exception>
    public EfViewExecutor(FilterCompiler filterCompiler)
    {
        ArgumentNullException.ThrowIfNull(filterCompiler);
        _filterCompiler = filterCompiler;
    }

    /// <summary>
    /// Initializes a new <see cref="EfViewExecutor"/> with the source-resolution dependencies and the
    /// default <see cref="FilterCompiler"/>. This is the production constructor the DI wiring (Task 9.4)
    /// uses: the executor obtains each view's base <c>IQueryable&lt;TSource&gt;</c> from
    /// <paramref name="dbContext"/> via the <c>Set&lt;TSource&gt;()</c> convention (Decision Log D11) and
    /// looks up the per-view <see cref="IViewExecutionPlan"/> from <paramref name="planRegistry"/>.
    /// </summary>
    /// <param name="dbContext">The active EF context used to resolve each view's source set (D11).</param>
    /// <param name="services">The request <see cref="IServiceProvider"/> used to build deferred row filters.</param>
    /// <param name="planRegistry">The registry mapping view name to its execution plan.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// This overload defaults the <see cref="FilterCompiler"/> to <see cref="ProviderAwareFilterCompiler"/>
    /// (Task 9.3) so the client text operators (<c>Contains</c>/<c>StartsWith</c>/<c>EndsWith</c>)
    /// translate to SQL <c>LIKE</c> and the case-sensitivity is decided by the active provider/collation
    /// (Requirement R9.3, Decision Log D17). Use the overload that takes a <see cref="FilterCompiler"/>
    /// to inject the base (in-memory/ordinal) compiler, which is required by the EF Core <b>InMemory</b>
    /// provider since it does not translate <c>EF.Functions.Like</c>.
    /// </remarks>
    public EfViewExecutor(DbContext dbContext, IServiceProvider services, IViewExecutionPlanRegistry planRegistry)
        : this(dbContext, services, planRegistry, new FilterCompiler(services.GetService<IQueryDialect>()))
    {
    }

    /// <summary>
    /// Initializes a new <see cref="EfViewExecutor"/> with the source-resolution dependencies and a
    /// supplied <see cref="FilterCompiler"/> (the full production constructor).
    /// </summary>
    /// <param name="dbContext">The active EF context used to resolve each view's source set (D11).</param>
    /// <param name="services">The request <see cref="IServiceProvider"/> used to build deferred row filters.</param>
    /// <param name="planRegistry">The registry mapping view name to its execution plan.</param>
    /// <param name="filterCompiler">The compiler used to turn the neutral filter tree into a predicate.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public EfViewExecutor(
        DbContext dbContext,
        IServiceProvider services,
        IViewExecutionPlanRegistry planRegistry,
        FilterCompiler filterCompiler)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(planRegistry);
        ArgumentNullException.ThrowIfNull(filterCompiler);

        _dbContext = dbContext;
        _services = services;
        _planRegistry = planRegistry;
        _filterCompiler = filterCompiler;
    }

    /// <summary>The filter compiler used by this executor; exposed to subclasses (Task 9.3).</summary>
    protected FilterCompiler FilterCompiler => _filterCompiler;

    /// <inheritdoc />
    /// <remarks>
    /// AOT boundary (Decision Log D123): this facet prefers the AOT-clean compiled read path
    /// (<see cref="ListCompiledAsync{TRow}"/>, Phase 2 / D118) and confines the reflection fallback to the
    /// private <see cref="ListReflectionAsync{TRow}"/> helper, mirroring <c>WriteMapperResolver</c>. The
    /// single call from here into that RUC helper is suppressed with the justification that it is
    /// unreachable under trim/AOT once a compiled plan is registered, so the source-generated dispatch
    /// invoker — which only rides the compiled branch — is not forced onto an RUC method (R2.4, R4.2).
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification =
            "The reflection fallback is reached only when no source-generated compiled plan is registered " +
            "for the view. The AOT-clean read path registers a compiled plan, so the RUC branch is " +
            "unreachable under trim/AOT and the generated read path stays warning-free (Decision Log D123, R2.4).")]
    public async Task<ViewListResult<TRow>> ListAsync<TRow>(
        ViewMetadata view,
        ViewQueryRequest request,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scope);

        // Source-generator Phase 2 (D118): when the resolved plan is a generated, non-RUC compiled plan,
        // route through the AOT-clean compiled read path; otherwise take the reflection fallback.
        if (TryResolveCompiledPlan(view) is { } compiledPlan)
        {
            return await ListCompiledAsync<TRow>(compiledPlan, view, request, scope, cancellationToken).ConfigureAwait(false);
        }

        // No compiled plan: the reflection fallback, isolated in a RUC helper (Decision Log D123).
        return await ListReflectionAsync<TRow>(view, request, scope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The reflection (RUC) List path: clamps paging, applies the server-trusted scope and the client
    /// scope/filter/search channels, sorts, pages, and materializes one page, resolving sort/filter/
    /// projection from metadata at runtime. Kept separate from <see cref="ListAsync{TRow}"/> so the
    /// <see cref="RequiresUnreferencedCodeAttribute"/> stays confined to the reflection fallback branch
    /// (Decision Log D123). Behavior is identical to the former inline fallback.
    /// </summary>
    [RequiresUnreferencedCode("View execution resolves sort/filter/projection from metadata at runtime; use the source generator path for AOT.")]
    private async Task<ViewListResult<TRow>> ListReflectionAsync<TRow>(
        ViewMetadata view,
        ViewQueryRequest request,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        // Clamp/reject paging up front so an invalid "return all" request fails before any DB round-trip (R10.3).
        var pageSize = ResolvePageSize(request.PageSize, view.Limits);
        var pageIndex = request.Page < 0 ? 0 : request.Page;

        // Task 9.2 seam: scope (server-trusted, pre-projection over TSource) is already AND-ed into this query.
        var scoped = ResolveScopedQueryable<TRow>(view, scope);

        // Client contextual scope sub-tree (externalFilter equivalent) defines the working context, so it
        // is applied to the baseline and counts toward recordsTotal (Decision Log D111, FilterOrigin.Scope).
        var baseline = ApplyChannel(scoped, request.Scope, FilterOrigin.Scope, view);

        // recordsTotal — server-trusted scope + client Scope applied, client filter/search NOT applied (R10.4, D111).
        var totalRowsUnfiltered = await CountAsync(baseline, cancellationToken).ConfigureAwait(false);

        // Client filter + global search, each validated under its own origin whitelist (Decision Log D111).
        var filtered = ApplyChannel(baseline, request.Filter, FilterOrigin.Filter, view);
        filtered = ApplyChannel(filtered, request.Search, FilterOrigin.Search, view);

        // recordsFiltered — after the client filter/search (R10.4).
        var totalRows = await CountAsync(filtered, cancellationToken).ConfigureAwait(false);

        var ordered = ApplySort(filtered, request.Sort, view);

        // Compute the skip as long to avoid the int overflow DynData suffered on large page indexes (§10.1).
        var skipLong = (long)pageIndex * pageSize;
        var skip = skipLong > int.MaxValue ? int.MaxValue : (int)skipLong;
        var pageQuery = ordered.Skip(skip).Take(pageSize);

        var items = await MaterializeAsync(pageQuery, cancellationToken).ConfigureAwait(false);

        // Masking runtime (Decision Log D118 / R7) on the RUC path: reflection supplies the read/write
        // accessors for Style A / non-generated views. A no-op when the view masks nothing.
        items = ApplyMaskRuc<TRow>(items, view);

        var totalPages = pageSize == 0 ? 0L : (totalRows + pageSize - 1) / pageSize;
        var page = new PagedResult<TRow>(items, totalRows, pageIndex, pageSize, totalPages);
        return new ViewListResult<TRow>(page, totalRowsUnfiltered);
    }

    /// <summary>
    /// Applies one filter channel to <paramref name="source"/>: compiles <paramref name="node"/> under
    /// <paramref name="origin"/> (so each leaf is validated against that channel's whitelist, Decision Log
    /// D111) and AND-s it via <c>Where</c>. A <see langword="null"/> <paramref name="node"/> is a no-op.
    /// </summary>
    [RequiresUnreferencedCode("Compiles a filter sub-tree over TRow at runtime; use the source generator path for AOT.")]
    private IQueryable<TRow> ApplyChannel<TRow>(
        IQueryable<TRow> source,
        FilterNode? node,
        FilterOrigin origin,
        ViewMetadata view)
    {
        if (node is null)
        {
            return source;
        }

        var predicate = _filterCompiler.Compile<TRow>(node, origin, view);
        return source.Where(predicate);
    }

    /// <inheritdoc />
    /// <remarks>
    /// AOT boundary (Decision Log D123): this facet prefers the AOT-clean compiled Detail path
    /// (<see cref="DetailCompiledAsync{TRow}"/>, Phase 2 / D118) and confines the reflection fallback to
    /// the private <see cref="DetailReflectionAsync{TRow}"/> helper, mirroring <c>WriteMapperResolver</c>.
    /// The single call into that RUC helper is suppressed because it is unreachable under trim/AOT once a
    /// compiled plan is registered (R2.4, R4.2).
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification =
            "The reflection fallback is reached only when no source-generated compiled plan is registered " +
            "for the view. The AOT-clean read path registers a compiled plan, so the RUC branch is " +
            "unreachable under trim/AOT and the generated read path stays warning-free (Decision Log D123, R2.4).")]
    public async Task<TRow?> DetailAsync<TRow>(
        ViewMetadata view,
        object key,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(scope);

        // Source-generator Phase 2 (D118): route a generated compiled plan through the AOT-clean path.
        if (TryResolveCompiledPlan(view) is { } compiledPlan)
        {
            return await DetailCompiledAsync<TRow>(compiledPlan, view, key, scope, cancellationToken).ConfigureAwait(false);
        }

        // No compiled plan: the reflection fallback, isolated in a RUC helper (Decision Log D123).
        return await DetailReflectionAsync<TRow>(view, key, scope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The reflection (RUC) Detail-by-key path: reads the single row matching the view's key within the
    /// server-trusted scope, building key resolution and projection from metadata at runtime. Kept
    /// separate from <see cref="DetailAsync{TRow}"/> so the <see cref="RequiresUnreferencedCodeAttribute"/>
    /// stays confined to the reflection fallback branch (Decision Log D123). Behavior is identical to the
    /// former inline fallback.
    /// </summary>
    [RequiresUnreferencedCode("Detail key resolution and projection are built from metadata at runtime; use the source generator path for AOT.")]
    private async Task<TRow?> DetailReflectionAsync<TRow>(
        ViewMetadata view,
        object key,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        // Detail = List projection filtered by the view's key, with the server-trusted scope still
        // applied (Decision Log D49, §4.6). Reuse the same resolution seam as List.
        var scoped = ResolveScopedQueryable<TRow>(view, scope);

        var predicate = BuildKeyPredicate<TRow>(view, key);

        var row = await FirstOrDefaultAsync(scoped.Where(predicate), cancellationToken).ConfigureAwait(false);

        // Masking runtime (Decision Log D118 / R7) on the RUC path: mask the single row via reflection
        // accessors before it leaves the executor.
        return row is null ? row : ApplyMaskRucRow<TRow>(row, view);
    }

    // ---------------------------------------------------------------------------------------------
    // Source-generator Phase 2 (Decision Log D118) — non-RUC compiled read path.
    //
    // These helpers run when the resolved plan is an ICompiledViewExecutionPlan (a generated Style B
    // plan, wrapped by CompiledExecutionPlanAdapter). They build filtering and ordering from the plan's
    // generated member-access lambdas and strongly-typed sort appliers, so neither
    // Expression.Property(string) nor MethodInfo.MakeGenericMethod is reached on the member-access / sort
    // path (R2, R3, R5). Behavioral parity with the reflection path above is the central guard
    // (Property 1). None of these members carry [RequiresUnreferencedCode].
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the per-view plan and returns it as an <see cref="ICompiledViewExecutionPlan"/> when one
    /// is registered for the view, or <see langword="null"/> otherwise (in which case the caller keeps
    /// the reflection path). A plan that is a plain <see cref="IViewExecutionPlan"/> (for example a
    /// hand-built <see cref="SplitViewExecutionPlan{TSource, TRow}"/>) yields <see langword="null"/>.
    /// </summary>
    private ICompiledViewExecutionPlan? TryResolveCompiledPlan(ViewMetadata view) =>
        _planRegistry?.Get(view.Name) as ICompiledViewExecutionPlan;

    /// <summary>
    /// The compiled (non-RUC) List path. Mirrors <see cref="ListAsync{TRow}"/>'s semantics — clamped
    /// page size, unfiltered total ignoring the page window (DR6), client filter/search under their own
    /// whitelist, sort with the <see cref="ViewMetadata.KeyFields"/> tiebreaker (D106) — but resolves
    /// every member-access and sort step from <paramref name="plan"/> instead of reflection.
    /// </summary>
    internal async Task<ViewListResult<TRow>> ListCompiledAsync<TRow>(
        ICompiledViewExecutionPlan plan,
        ViewMetadata view,
        ViewQueryRequest request,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        // Clamp/reject paging up front so an invalid "return all" request fails before any DB round-trip (R10.3).
        var pageSize = ResolvePageSize(request.PageSize, view.Limits);
        var pageIndex = request.Page < 0 ? 0 : request.Page;

        var scoped = ResolveCompiledScopedQueryable<TRow>(plan, view, scope);

        // Compile every client-supplied predicate and resolve every sort step (each under its own origin
        // whitelist, D111) BEFORE any query executes. A disallowed/non-projected/masked-without-opt-in
        // field therefore throws via the FilterCompiler whitelist path before the unfiltered-total COUNT
        // round-trip — no SQL is emitted for a rejected request (Property 4, R2.4, R8.1).
        var scopePredicate = CompileChannelCompiled<TRow>(request.Scope, FilterOrigin.Scope, view, plan);
        var filterPredicate = CompileChannelCompiled<TRow>(request.Filter, FilterOrigin.Filter, view, plan);
        var searchPredicate = CompileChannelCompiled<TRow>(request.Search, FilterOrigin.Search, view, plan);
        var sortSteps = ResolveSortSteps(request.Sort, view);

        // Client contextual scope sub-tree defines the working context and counts toward recordsTotal (D111).
        var baseline = ApplyPredicate(scoped, scopePredicate);

        // recordsTotal — server-trusted scope + client Scope applied, client filter/search NOT applied (DR6, D111).
        var totalRowsUnfiltered = await CountAsync(baseline, cancellationToken).ConfigureAwait(false);

        // Client filter + global search applied (already validated above).
        var filtered = ApplyPredicate(baseline, filterPredicate);
        filtered = ApplyPredicate(filtered, searchPredicate);

        // recordsFiltered — after the client filter/search.
        var totalRows = await CountAsync(filtered, cancellationToken).ConfigureAwait(false);

        var ordered = ApplySortStepsCompiled(filtered, sortSteps, plan);

        var skipLong = (long)pageIndex * pageSize;
        var skip = skipLong > int.MaxValue ? int.MaxValue : (int)skipLong;
        var pageQuery = ordered.Skip(skip).Take(pageSize);

        var items = await MaterializeAsync(pageQuery, cancellationToken).ConfigureAwait(false);

        // Masking runtime (Decision Log D118 / R7): apply the view's masks at materialization, post
        // projection and in memory, using the generated AOT-clean MaskAccessors. ShouldMask is evaluated
        // once per request; rows are masked in place (or rebuilt for record rows). No SQL is touched.
        items = ApplyMaskCompiled(items, plan, view);

        var totalPages = pageSize == 0 ? 0L : (totalRows + pageSize - 1) / pageSize;
        var page = new PagedResult<TRow>(items, totalRows, pageIndex, pageSize, totalPages);
        return new ViewListResult<TRow>(page, totalRowsUnfiltered);
    }

    /// <summary>
    /// The compiled (non-RUC) Detail-by-key path: builds the key predicate from the plan's generated
    /// member-access lambdas, returns the at-most-one matching row (composite keys supported), and
    /// returns <see langword="null"/> without throwing when no row matches (R3.2/R3.3). The materialized
    /// row is masked at materialization (R7) before it leaves the executor.
    /// </summary>
    internal async Task<TRow?> DetailCompiledAsync<TRow>(
        ICompiledViewExecutionPlan plan,
        ViewMetadata view,
        object key,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        var scoped = ResolveCompiledScopedQueryable<TRow>(plan, view, scope);
        var predicate = BuildKeyPredicateCompiled<TRow>(view, key, plan);
        var row = await FirstOrDefaultAsync(scoped.Where(predicate), cancellationToken).ConfigureAwait(false);

        // Masking runtime (Decision Log D118 / R7): mask the single materialized row via the generated
        // AOT-clean MaskAccessors before it leaves the executor.
        return row is null ? row : ApplyMaskCompiledRow(row, plan, view);
    }

    /// <summary>
    /// Applies the view's masks (Decision Log D118 / R7) to a materialized List page on the compiled
    /// (AOT-clean) path using the plan's generated <see cref="MaskAccessor"/>s. A no-op when the view has
    /// no active masked field for the request.
    /// </summary>
    private IReadOnlyList<TRow> ApplyMaskCompiled<TRow>(
        IReadOnlyList<TRow> items,
        ICompiledViewExecutionPlan plan,
        ViewMetadata view)
    {
        var applier = MaskApplier.Create(view.Name, plan.MaskAccessors, _services);
        return ApplyMaskToList(items, applier);
    }

    /// <summary>
    /// Applies the view's masks (Decision Log D118 / R7) to a single materialized row on the compiled
    /// (AOT-clean) path. Returns the masked (possibly rebuilt) row, or the input unchanged when no mask
    /// is active.
    /// </summary>
    private TRow ApplyMaskCompiledRow<TRow>(TRow row, ICompiledViewExecutionPlan plan, ViewMetadata view)
    {
        var applier = MaskApplier.Create(view.Name, plan.MaskAccessors, _services);
        return applier.HasWork ? (TRow)applier.Apply(row!) : row;
    }

    /// <summary>
    /// Threads <paramref name="applier"/> over each materialized row, returning a new list of masked rows
    /// (record rows are rebuilt). A no-op (returns the input list) when no mask is active. Null rows pass
    /// through unchanged.
    /// </summary>
    private static IReadOnlyList<TRow> ApplyMaskToList<TRow>(IReadOnlyList<TRow> items, MaskApplier applier)
    {
        if (!applier.HasWork)
        {
            return items;
        }

        var masked = new List<TRow>(items.Count);
        foreach (var item in items)
        {
            masked.Add(item is null ? item : (TRow)applier.Apply(item));
        }

        return masked;
    }

    /// <summary>
    /// Applies the view's masks (Decision Log D118 / R7) to a materialized List page on the reflection
    /// (RUC) path, using reflection-built accessors for the masked fields. A no-op when the view has no
    /// active masked field for the request.
    /// </summary>
    [RequiresUnreferencedCode("Masking on the RUC path reads/writes masked fields via reflection; use the source generator path for AOT.")]
    private IReadOnlyList<TRow> ApplyMaskRuc<TRow>(IReadOnlyList<TRow> items, ViewMetadata view)
    {
        var applier = MaskApplier.CreateWithReflectionFallback(
            view.Name, typeof(TRow), Array.Empty<MaskAccessor>(), _services);
        return ApplyMaskToList(items, applier);
    }

    /// <summary>
    /// Applies the view's masks (Decision Log D118 / R7) to a single materialized row on the reflection
    /// (RUC) path. Returns the masked (possibly rebuilt) row, or the input unchanged when no mask is
    /// active.
    /// </summary>
    [RequiresUnreferencedCode("Masking on the RUC path reads/writes masked fields via reflection; use the source generator path for AOT.")]
    private TRow ApplyMaskRucRow<TRow>(TRow row, ViewMetadata view)
    {
        var applier = MaskApplier.CreateWithReflectionFallback(
            view.Name, typeof(TRow), Array.Empty<MaskAccessor>(), _services);
        return applier.HasWork ? (TRow)applier.Apply(row!) : row;
    }

    /// <summary>
    /// Obtains the scoped, projected <see cref="IQueryable{T}"/> from a compiled plan — the compiled
    /// counterpart of <see cref="ResolveScopedQueryable{TRow}"/>. The plan AND-s the authored
    /// server-trusted row filters and the per-request scope predicates pre-projection (R1.4), then
    /// projects to <typeparamref name="TRow"/>. Overridable for testing.
    /// </summary>
    protected virtual IQueryable<TRow> ResolveCompiledScopedQueryable<TRow>(
        ICompiledViewExecutionPlan plan,
        ViewMetadata view,
        IViewScope scope)
    {
        if (_dbContext is null || _services is null)
        {
            throw new InvalidOperationException(
                $"This {nameof(EfViewExecutor)} was constructed without an EF context and service provider, " +
                $"so it cannot resolve a compiled queryable for view '{view.Name}'. Use the DI constructor " +
                $"(the one taking a {nameof(DbContext)}, {nameof(IServiceProvider)}, and " +
                $"{nameof(IViewExecutionPlanRegistry)}), or override ResolveCompiledScopedQueryable<TRow>.");
        }

        var queryable = plan.CreateScopedQueryable(_dbContext, _services, scope);
        if (queryable is IQueryable<TRow> typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"The compiled execution plan for view '{view.Name}' produced a queryable of element type " +
            $"'{queryable.ElementType}', but the caller requested rows of type '{typeof(TRow)}'. The TRow " +
            "type argument must match the view's projected row type (ViewMetadata.QueryType).");
    }

    /// <summary>
    /// Compiles <paramref name="node"/> under <paramref name="origin"/> (tri-whitelist enforced, D111)
    /// resolving each field's member-access from <paramref name="plan"/> rather than reflecting over
    /// <typeparamref name="TRow"/>. Returns <see langword="null"/> for a <see langword="null"/> node.
    /// This performs validation only — it executes no query — so a disallowed field throws
    /// <see cref="FilterValidationException"/> before any SQL is emitted (R2.4, R8.1).
    /// </summary>
    private Expression<Func<TRow, bool>>? CompileChannelCompiled<TRow>(
        FilterNode? node,
        FilterOrigin origin,
        ViewMetadata view,
        ICompiledViewExecutionPlan plan)
    {
        if (node is null)
        {
            return null;
        }

        return _filterCompiler.Compile<TRow>(
            node,
            origin,
            view,
            fieldName => plan.TryGetMemberAccess(fieldName, out var accessor) ? accessor : null);
    }

    /// <summary>
    /// AND-s a previously-compiled <paramref name="predicate"/> onto <paramref name="source"/> via
    /// <c>Where</c>. A <see langword="null"/> predicate is a no-op. Applying a pre-compiled predicate keeps
    /// all whitelist validation ahead of any query execution.
    /// </summary>
    private static IQueryable<TRow> ApplyPredicate<TRow>(
        IQueryable<TRow> source,
        Expression<Func<TRow, bool>>? predicate) =>
        predicate is null ? source : source.Where(predicate);

    /// <summary>
    /// Resolves and validates the ordered sort steps — the client sort (each field validated sortable
    /// against the view metadata, D111) followed by the <see cref="ViewMetadata.KeyFields"/> tiebreaker
    /// (D106). A disallowed / non-projected sort field is rejected by <see cref="ResolveSortableField"/>
    /// here, before any SQL executes (R2.4). No query runs in this method.
    /// </summary>
    private static List<(FieldMetadata Field, bool Descending)> ResolveSortSteps(
        IReadOnlyList<SortSpec> sort,
        ViewMetadata view)
    {
        var steps = new List<(FieldMetadata Field, bool Descending)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (sort is not null)
        {
            foreach (var spec in sort)
            {
                var field = ResolveSortableField(view, spec.Field);
                if (seen.Add(field.Name))
                {
                    steps.Add((field, spec.Descending));
                }
            }
        }

        foreach (var keyName in view.KeyFields)
        {
            if (!seen.Add(keyName))
            {
                continue;
            }

            var keyField = FindField(view, keyName)
                ?? throw new InvalidOperationException(
                    $"View '{view.Name}' declares key field '{keyName}', which is not part of the projection.");
            steps.Add((keyField, false));
        }

        return steps;
    }

    /// <summary>
    /// Compiled counterpart of <c>ApplySort</c>: applies each pre-resolved sort step via the plan's
    /// strongly-typed appliers (no <c>MakeGenericMethod</c>). Steps are validated by
    /// <see cref="ResolveSortSteps"/> ahead of any query execution.
    /// </summary>
    private static IQueryable<TRow> ApplySortStepsCompiled<TRow>(
        IQueryable<TRow> source,
        List<(FieldMetadata Field, bool Descending)> steps,
        ICompiledViewExecutionPlan plan)
    {
        if (steps.Count == 0)
        {
            // No client sort and no key fields — deterministic paging cannot be guaranteed; registration
            // fail-fast (D106) prevents this for registered views, so this is only reachable for hand-built
            // metadata in tests.
            return source;
        }

        IOrderedQueryable ordered = plan.ApplyPrimarySort(source, steps[0].Field.Name, steps[0].Descending);
        for (var i = 1; i < steps.Count; i++)
        {
            ordered = plan.ApplyThenSort(ordered, steps[i].Field.Name, steps[i].Descending);
        }

        return (IQueryable<TRow>)ordered;
    }

    /// <summary>
    /// Compiled counterpart of <see cref="BuildKeyPredicate{TRow}"/>: the conjunction of
    /// <c>x.&lt;keyField&gt; == coerce(value)</c> over each <see cref="ViewMetadata.KeyFields"/> entry,
    /// resolving each key member from the plan's generated member-access lambdas (no
    /// <c>Expression.Property(string)</c>). This is a server-side key lookup, not a client filter, so it
    /// bypasses the tri-whitelist (D104/D109).
    /// </summary>
    private static Expression<Func<TRow, bool>> BuildKeyPredicateCompiled<TRow>(
        ViewMetadata view,
        object key,
        ICompiledViewExecutionPlan plan)
    {
        if (view.KeyFields.Count == 0)
        {
            throw new InvalidOperationException(
                $"View '{view.Name}' has no key fields, so Detail-by-key cannot resolve a row. Declare a " +
                "primary key with .PrimaryKey() or Key(...).");
        }

        var values = NormalizeKey(view, key);

        var parameter = Expression.Parameter(typeof(TRow), "x");
        Expression? body = null;
        foreach (var keyName in view.KeyFields)
        {
            var field = FindField(view, keyName)
                ?? throw new InvalidOperationException(
                    $"View '{view.Name}' declares key field '{keyName}', which is not part of the projection.");

            if (!plan.TryGetMemberAccess(keyName, out var accessor))
            {
                throw new InvalidOperationException(
                    $"The compiled execution plan for view '{view.Name}' exposes no generated member-access " +
                    $"for key field '{keyName}', so Detail-by-key cannot be resolved on the compiled path.");
            }

            var member = ParameterReplaceVisitor.Replace(accessor.Body, accessor.Parameters[0], parameter);
            var memberType = member.Type;
            var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;

            var coerced = FilterCompiler.CoerceValue(values[keyName], underlying, field.Name);
            Expression constant = Expression.Constant(coerced, underlying);
            if (constant.Type != memberType)
            {
                constant = Expression.Convert(constant, memberType);
            }

            var equality = Expression.Equal(member, constant);
            body = body is null ? equality : Expression.AndAlso(body, equality);
        }

        return Expression.Lambda<Func<TRow, bool>>(body!, parameter);
    }

    /// <inheritdoc />
    /// <remarks>
    /// AOT boundary (Decision Log D123): the port facet itself is not
    /// <see cref="RequiresUnreferencedCodeAttribute"/> so the source-generated write dispatch invoker can
    /// call it without inheriting an <c>IL2026</c> warning. The runtime-entity bridge (which closes
    /// <see cref="CreateCoreAsync{TEntity}"/> over <see cref="ViewMetadata.CrudEntityType"/> and resolves
    /// the write mapper) is confined to the private <see cref="CreateReflectionAsync"/> helper, reached
    /// through a justified suppression, mirroring <c>WriteMapperResolver</c>. The AOT-clean write path
    /// rides the source-generated <c>WriteMapper</c> resolved by <c>WriteMapperResolver</c> (R3.4, R4.2).
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification =
            "The runtime-entity bridge is the reflection fallback for write dispatch; the AOT-clean write " +
            "path resolves a source-generated write mapper, so the RUC branch is unreachable under " +
            "trim/AOT and the generated write path stays warning-free (Decision Log D123, R3.4).")]
    public Task<object> CreateAsync<TCrud>(
        ViewMetadata view,
        TCrud model,
        IViewScope scope,
        CancellationToken cancellationToken)
        where TCrud : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(scope);

        return CreateReflectionAsync(view, model, scope, cancellationToken);
    }

    /// <summary>
    /// The reflection (RUC) Create bridge: closes <see cref="CreateCoreAsync{TEntity}"/> over the view's
    /// runtime <see cref="ViewMetadata.CrudEntityType"/> (known only at runtime) and invokes it — mirroring
    /// how the read bridge closes over <see cref="ViewMetadata.QueryType"/> (R1.1, R1.3). Kept separate
    /// from <see cref="CreateAsync{TCrud}"/> so the <see cref="RequiresUnreferencedCodeAttribute"/> stays
    /// confined to the reflection fallback branch (Decision Log D123). Behavior is identical to the former
    /// inline bridge.
    /// </summary>
    [RequiresUnreferencedCode("Write mapping (TCrud to entity) is resolved from metadata at runtime; use the source generator path for AOT.")]
    private Task<object> CreateReflectionAsync(
        ViewMetadata view,
        object model,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        // Bridge from the port's generic-over-TCrud signature to the runtime entity type. The write body
        // works on ViewMetadata.CrudEntityType (the EF entity), which is known only at runtime, so close
        // CreateCoreAsync<TEntity> over it and invoke.
        var entityType = RequireCrudEntityType(view);
        var closed = CreateCoreAsyncMethod.MakeGenericMethod(entityType);
        return (Task<object>)closed.Invoke(this, new object[] { view, model, scope, cancellationToken })!;
    }

    /// <summary>
    /// The runtime-entity-typed core of <see cref="CreateAsync{TCrud}"/> (Task 4.2). Instantiates a fresh
    /// <typeparamref name="TEntity"/>, applies the resolved <see cref="WriteMapper"/> (the only channel
    /// client values reach the entity, whitelisted-only — Requirement R4/R5), <c>Add</c>s it, and persists
    /// with a single <c>SaveChanges</c> (Requirements R1.3, R11.1). After the save the store-assigned
    /// primary key is read back from the entity via the view's ordered <see cref="ViewMetadata.KeyFields"/>
    /// and returned: a scalar for a single-field key, or a name→value map for a composite key. The key
    /// must be non-<see langword="null"/> (Requirements R1.1, R1.2); the AspNetCore layer wraps the return
    /// value in a <c>VistaWriteResponse</c>.
    /// </summary>
    /// <typeparam name="TEntity">The view's write entity type (<see cref="ViewMetadata.CrudEntityType"/>).</typeparam>
    /// <param name="view">The writable view being created into.</param>
    /// <param name="model">The bound <c>TCrud</c> payload (passed as <see cref="object"/> to the seam mapper).</param>
    /// <param name="scope">The server-trusted scope (unused for Create; carried for signature symmetry).</param>
    /// <param name="cancellationToken">Token used to cancel the persistence round-trip.</param>
    /// <returns>The store-assigned primary key: a scalar value, or a name→value map for a composite key.</returns>
    [RequiresUnreferencedCode("Create resolves the write mapper and reads back the key from metadata at runtime; use the source generator path for AOT.")]
    private async Task<object> CreateCoreAsync<TEntity>(
        ViewMetadata view,
        object model,
        IViewScope scope,
        CancellationToken cancellationToken)
        where TEntity : class, new()
    {
        var dbContext = RequireDbContext(view, nameof(CreateAsync));

        // Resolve the write mapper once for this write (generated preferred, reflection fallback) so the
        // executor never branches on which implementation produced it (Requirements R13.1, R13.2).
        var mapper = RequireServices(view, nameof(CreateAsync))
            .GetRequiredService<WriteMapperResolver>()
            .Resolve(view);

        // A fresh entity: client values flow in only through the whitelisted mapper — key/token targets are
        // skipped by the mapper itself (defense in depth), so identity is store-assigned, not client-set.
        var entity = new TEntity();
        mapper(model, entity);

        dbContext.Add(entity);

        // Exactly one SaveChanges for the operation (Requirements R1.3, R11.1). A provider persistence
        // failure (constraint or concurrency violation) is translated to a typed, leak-free Vista write
        // exception and the implicit transaction leaves no partial row (Requirements R1.7, R9.4, R11.3).
        await SaveWriteChangesAsync(dbContext, cancellationToken).ConfigureAwait(false);

        // Read back the store-assigned PK from the tracked entity (Requirements R1.1, R1.2).
        return ReadEntityKey(view, entity);
    }

    /// <summary>
    /// Reads the primary-key value(s) from a persisted <typeparamref name="TEntity"/> via the view's
    /// ordered <see cref="ViewMetadata.KeyFields"/>: a single scalar for a one-field key, or a
    /// name→value map (ordinal, key-order) for a composite key. Every key component must be
    /// non-<see langword="null"/> after the write (Requirements R1.1, R1.2); a <see langword="null"/>
    /// component is an internal error (the store did not assign identity) and throws.
    /// </summary>
    [RequiresUnreferencedCode("Reads entity key members by name via reflection; use the source generator path for AOT.")]
    private static object ReadEntityKey<TEntity>(ViewMetadata view, TEntity entity)
        where TEntity : class
    {
        if (view.KeyFields.Count == 0)
        {
            throw new InvalidOperationException(
                $"View '{view.Name}' has no key fields, so the created row's primary key cannot be read " +
                "back. Declare a primary key with .PrimaryKey() or Key(...).");
        }

        if (view.KeyFields.Count == 1)
        {
            return ReadKeyComponent(view, entity, view.KeyFields[0]);
        }

        var map = new Dictionary<string, object?>(view.KeyFields.Count, StringComparer.Ordinal);
        foreach (var name in view.KeyFields)
        {
            map[name] = ReadKeyComponent(view, entity, name);
        }

        return map;
    }

    /// <summary>
    /// Reads a single non-<see langword="null"/> key component named <paramref name="keyFieldName"/> from
    /// <paramref name="entity"/>, resolving the member by name (an authoring error if absent). A
    /// <see langword="null"/> value after the write is an internal error and throws (Requirement R1.1).
    /// </summary>
    [RequiresUnreferencedCode("Reads an entity key member by name via reflection; use the source generator path for AOT.")]
    private static object ReadKeyComponent<TEntity>(ViewMetadata view, TEntity entity, string keyFieldName)
    {
        var member = ResolveEntityKeyMember<TEntity>(view, keyFieldName);
        return member.GetValue(entity)
            ?? throw new InvalidOperationException(
                $"The created row for view '{view.Name}' has a null value for key field '{keyFieldName}' " +
                "after persistence. A writable view's primary key must be assigned by the store or the " +
                "write mapper before the created identity can be returned.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// AOT boundary (Decision Log D123): the port facet itself is not
    /// <see cref="RequiresUnreferencedCodeAttribute"/> so the source-generated write dispatch invoker can
    /// call it without inheriting an <c>IL2026</c> warning. The runtime-entity bridge (which closes
    /// <see cref="UpdateCoreAsync{TEntity}"/> over <see cref="ViewMetadata.CrudEntityType"/> and resolves
    /// the write mapper) is confined to the private <see cref="UpdateReflectionAsync"/> helper, reached
    /// through a justified suppression, mirroring <c>WriteMapperResolver</c>. The AOT-clean write path
    /// rides the source-generated <c>WriteMapper</c> resolved by <c>WriteMapperResolver</c> (R3.4, R4.2).
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification =
            "The runtime-entity bridge is the reflection fallback for write dispatch; the AOT-clean write " +
            "path resolves a source-generated write mapper, so the RUC branch is unreachable under " +
            "trim/AOT and the generated write path stays warning-free (Decision Log D123, R3.4).")]
    public Task<bool> UpdateAsync<TCrud>(
        ViewMetadata view,
        object key,
        TCrud model,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken)
        where TCrud : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(scope);

        return UpdateReflectionAsync(view, key, model, scope, concurrencyToken, cancellationToken);
    }

    /// <summary>
    /// The reflection (RUC) Update bridge: closes <see cref="UpdateCoreAsync{TEntity}"/> over the view's
    /// runtime <see cref="ViewMetadata.CrudEntityType"/> (known only at runtime) and invokes it, exactly as
    /// the Create bridge does (R2.1, R2.5). Kept separate from <see cref="UpdateAsync{TCrud}"/> so the
    /// <see cref="RequiresUnreferencedCodeAttribute"/> stays confined to the reflection fallback branch
    /// (Decision Log D123). Behavior is identical to the former inline bridge.
    /// </summary>
    [RequiresUnreferencedCode("Write mapping (TCrud to entity) is resolved from metadata at runtime; use the source generator path for AOT.")]
    private Task<bool> UpdateReflectionAsync(
        ViewMetadata view,
        object key,
        object model,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken)
    {
        // Bridge from the port's generic-over-TCrud signature to the runtime entity type, exactly as
        // CreateAsync does. The update body operates on ViewMetadata.CrudEntityType (the EF entity), known
        // only at runtime, so close UpdateCoreAsync<TEntity> over it and invoke (R2.1, R2.5).
        var entityType = RequireCrudEntityType(view);
        var closed = UpdateCoreAsyncMethod.MakeGenericMethod(entityType);
        return (Task<bool>)closed.Invoke(
            this,
            new object[] { view, key, model, scope, concurrencyToken!, cancellationToken })!;
    }

    /// <summary>
    /// The runtime-entity-typed core of <see cref="UpdateAsync{TCrud}"/> (Task 4.3). Resolves the target
    /// row <b>within the server-trusted scope</b> by the request key
    /// (<see cref="ResolveEntityForWriteAsync{TEntity}"/>); a <see langword="null"/> result — no in-scope
    /// row — returns <see langword="false"/> (HTTP 404, Requirements R2.3, R8.2). It then enforces the
    /// optimistic-concurrency precondition <em>before</em> any mutation (Requirement R6.3): a token
    /// mismatch throws <see cref="VistaConcurrencyConflictException"/> (HTTP 409) with the row untouched.
    /// The whitelisted <see cref="WriteMapper"/> is the only channel client values reach the tracked
    /// entity; the mapper skips key/token targets, so the row's identity always comes from the loaded
    /// entity's key and never from the request body (Requirements R2.5, R5.2). A single
    /// <c>SaveChanges</c> persists the change to the already-tracked entity (Requirements R2.4, R11.1).
    /// </summary>
    /// <typeparam name="TEntity">The view's write entity type (<see cref="ViewMetadata.CrudEntityType"/>).</typeparam>
    /// <param name="view">The writable view being updated.</param>
    /// <param name="key">The request key: a scalar (single key) or a name→value map (composite key).</param>
    /// <param name="model">The bound <c>TCrud</c> payload (passed as <see cref="object"/> to the seam mapper).</param>
    /// <param name="scope">The server-trusted scope AND-ed into the target resolution (R8.2).</param>
    /// <param name="concurrencyToken">The client-supplied expected token (<c>If-Match</c>), or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token used to cancel the load and the persistence round-trip.</param>
    /// <returns><see langword="true"/> when a row was updated; <see langword="false"/> when none matched in scope.</returns>
    [RequiresUnreferencedCode("Update resolves the write mapper, key, and concurrency token from metadata at runtime; use the source generator path for AOT.")]
    private async Task<bool> UpdateCoreAsync<TEntity>(
        ViewMetadata view,
        object key,
        object model,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var services = RequireServices(view, nameof(UpdateAsync));

        // Load the keyed target within the authorized scope on the request-scoped DbContext, so the entity
        // is tracked and a later SaveChanges persists the mutation (R11.5). A null result — out-of-scope or
        // unknown key — is an indistinguishable 404 (R2.3, R8.2).
        var entity = await ResolveEntityForWriteAsync<TEntity>(view, key, scope, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        // Enforce the optimistic-concurrency precondition BEFORE any mutation: a mismatch aborts with 409
        // and leaves the row untouched (R6.3). A tokenless view is a no-op here (R6.6).
        var facet = RequireWriteFacet(services, view);
        EnforceConcurrencyToken<TEntity>(facet, entity, concurrencyToken);

        // Apply the whitelisted mapper — the only channel client values reach the entity. Key/token targets
        // are skipped by the mapper, so the row identity stays the loaded entity's key, never the body
        // (R2.5, R5.2). Resolve the mapper once (generated preferred, reflection fallback — R13.1, R13.2).
        var mapper = services.GetRequiredService<WriteMapperResolver>().Resolve(view);
        mapper(model, entity);

        // Exactly one SaveChanges for the operation on the tracked entity (R2.4, R11.1). A provider
        // persistence failure (constraint or concurrency violation) is translated to a typed, leak-free
        // Vista write exception; the implicit transaction leaves the row unchanged (R6.5, R9.4, R11.3).
        await SaveWriteChangesAsync(RequireDbContext(view, nameof(UpdateAsync)), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Delete key resolution is built from metadata at runtime; use the source generator path for AOT.")]
    public Task<bool> DeleteAsync(
        ViewMetadata view,
        object key,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(scope);

        // Bridge from the port's non-generic signature to the runtime entity type, exactly as
        // Create/Update do. The delete body operates on ViewMetadata.CrudEntityType (the EF entity), known
        // only at runtime, so close DeleteCoreAsync<TEntity> over it and invoke (R3.1).
        var entityType = RequireCrudEntityType(view);
        var closed = DeleteCoreAsyncMethod.MakeGenericMethod(entityType);
        return (Task<bool>)closed.Invoke(
            this,
            new object[] { view, key, scope, concurrencyToken!, cancellationToken })!;
    }

    /// <summary>
    /// The runtime-entity-typed core of <see cref="DeleteAsync"/> (Task 4.4). Resolves the target row
    /// <b>within the server-trusted scope</b> by the request key
    /// (<see cref="ResolveEntityForWriteAsync{TEntity}"/>); a <see langword="null"/> result — no in-scope
    /// row — returns <see langword="false"/> (HTTP 404, Requirements R3.3, R8.2). It then enforces the
    /// optimistic-concurrency precondition <em>before</em> removal (Requirement R6.3): a token mismatch
    /// throws <see cref="VistaConcurrencyConflictException"/> (HTTP 409) with the row untouched. A single
    /// <c>Remove</c> + <c>SaveChanges</c> deletes the tracked entity (Requirements R3.4, R11.1). No write
    /// mapper is involved — delete carries no body.
    /// </summary>
    /// <typeparam name="TEntity">The view's write entity type (<see cref="ViewMetadata.CrudEntityType"/>).</typeparam>
    /// <param name="view">The writable view being deleted from.</param>
    /// <param name="key">The request key: a scalar (single key) or a name→value map (composite key).</param>
    /// <param name="scope">The server-trusted scope AND-ed into the target resolution (R8.2).</param>
    /// <param name="concurrencyToken">The client-supplied expected token (<c>If-Match</c>), or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token used to cancel the load and the persistence round-trip.</param>
    /// <returns><see langword="true"/> when a row was deleted; <see langword="false"/> when none matched in scope.</returns>
    [RequiresUnreferencedCode("Delete resolves the key and concurrency token from metadata at runtime; use the source generator path for AOT.")]
    private async Task<bool> DeleteCoreAsync<TEntity>(
        ViewMetadata view,
        object key,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var services = RequireServices(view, nameof(DeleteAsync));

        // Load the keyed target within the authorized scope on the request-scoped DbContext, so the entity
        // is tracked and a later SaveChanges deletes it (R11.5). A null result — out-of-scope or unknown
        // key — is an indistinguishable 404 (R3.3, R8.2).
        var entity = await ResolveEntityForWriteAsync<TEntity>(view, key, scope, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        // Enforce the optimistic-concurrency precondition BEFORE removal: a mismatch aborts with 409 and
        // leaves the row untouched (R6.3). A tokenless view is a no-op here (R6.6).
        var facet = RequireWriteFacet(services, view);
        EnforceConcurrencyToken<TEntity>(facet, entity, concurrencyToken);

        // Remove the tracked entity and persist with exactly one SaveChanges (R3.4, R11.1). A provider
        // persistence failure (constraint or concurrency violation) is translated to a typed, leak-free
        // Vista write exception; the implicit transaction leaves the row unchanged (R3.8, R6.5, R11.3).
        var deleteContext = RequireDbContext(view, nameof(DeleteAsync));
        deleteContext.Remove(entity);
        await SaveWriteChangesAsync(deleteContext, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Persists a single write operation through exactly one <c>SaveChanges</c> and translates a provider
    /// persistence failure into a typed, leak-free Vista write exception (Decision Log D120):
    /// a <see cref="DbUpdateConcurrencyException"/> — a genuine optimistic-concurrency violation detected
    /// at save time — becomes a <see cref="VistaConcurrencyConflictException"/> (HTTP 409, rolled back,
    /// Requirement R6.5); any other <see cref="DbUpdateException"/> — a database constraint violation such
    /// as a unique-index, NOT NULL, or foreign-key breach — becomes a <see cref="VistaWriteConflictException"/>
    /// (HTTP 409 write-conflict, Requirement R9.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// EF Core wraps a single <c>SaveChanges</c> in an implicit transaction, so a failed save commits no
    /// rows: the pre-operation persisted state is preserved and no partial row remains (Requirements R1.7,
    /// R3.8, R11.3, R11.4). The caller therefore never sees a success result for a failed persistence.
    /// </para>
    /// <para>
    /// The original provider exception is attached only as the <see cref="Exception.InnerException"/> of
    /// the typed Vista exception. The Vista message is fixed, Vista-authored text; the AspNetCore mapper
    /// builds the client response from that message and never from the inner exception, so no SQL text,
    /// schema/object name, or connection detail can leak to the client (Requirement R9.6). The inner
    /// exception remains available for server-side logging by a host error handler.
    /// </para>
    /// <para>
    /// <see cref="DbUpdateConcurrencyException"/> derives from <see cref="DbUpdateException"/>, so it is
    /// caught first to keep the concurrency (R6.5) and constraint (R9.4) branches distinct.
    /// </para>
    /// </remarks>
    /// <param name="dbContext">The request-scoped context whose tracked changes are persisted.</param>
    /// <param name="cancellationToken">Token used to cancel the persistence round-trip.</param>
    /// <exception cref="VistaConcurrencyConflictException">A concurrency violation was detected at save time (R6.5).</exception>
    /// <exception cref="VistaWriteConflictException">A database constraint violation occurred (R9.4).</exception>
    private static async Task SaveWriteChangesAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Genuine optimistic-concurrency violation detected by the provider at save time (R6.5). The
            // implicit transaction rolled back; surface a fixed, safe message and keep the provider detail
            // as the inner exception only (server-side; never surfaced to clients — R9.6).
            throw new VistaConcurrencyConflictException(innerException: ex);
        }
        catch (DbUpdateException ex)
        {
            // Database constraint violation (unique index, NOT NULL, foreign key, CHECK, ...). The implicit
            // transaction rolled back, leaving no partial row (R1.7, R11.3). Translate to a 409 write-conflict
            // with a Vista-authored message; the provider detail stays on the inner exception only (R9.4, R9.6).
            throw new VistaWriteConflictException(innerException: ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Write facet helpers (Task 4.1) — the shared building blocks the Create/Update/Delete bodies
    // (Tasks 4.2–4.4) compose. Three concerns live here and nothing else:
    //   1. Entity resolution within the server-trusted scope (ResolveScopedEntitySet / ResolveEntityForWriteAsync).
    //   2. Composite-capable key normalization + coercion against the ordered KeyFields (NormalizeWriteKey),
    //      reusing FilterCompiler.CoerceValue exactly as DetailAsync does.
    //   3. The optimistic-concurrency pre-check (EnforceConcurrencyToken) + token read-back for the ETag
    //      round-trip (ReadConcurrencyToken).
    //
    // None of these persist anything: they build queryables/predicates, load a candidate row, and
    // validate the precondition. TEntity is the view's CrudEntityType; the write facet methods bridge to
    // these generic helpers over that runtime type (Tasks 4.2–4.4). Because they resolve entity members
    // and target types from metadata at runtime and compile captured selectors, they carry
    // [RequiresUnreferencedCode] consistent with the rest of the RUC write path (R13.5); the AOT-clean
    // route is the source generator (Pilar 3).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the <b>pre-projection</b> <see cref="IQueryable{T}"/> over the write entity
    /// <typeparamref name="TEntity"/> (the view's <see cref="ViewMetadata.CrudEntityType"/>), rooted on
    /// the <c>DbContext.Set&lt;TEntity&gt;()</c> convention (Decision Log D11) with the server-trusted
    /// per-request <see cref="IViewScope"/> AND-ed in. This is the write-side counterpart of the read
    /// seam <see cref="ResolveScopedQueryable{TRow}"/>: it stops <em>before</em> projection so writes load
    /// and mutate the tracked entity, not the read row.
    /// </summary>
    /// <typeparam name="TEntity">The EF entity type the view writes to.</typeparam>
    /// <param name="view">The writable view being resolved.</param>
    /// <param name="scope">The server-trusted row-filter scope to apply pre-projection.</param>
    /// <returns>A not-yet-enumerated, scoped queryable over <typeparamref name="TEntity"/>.</returns>
    /// <remarks>
    /// The scope predicates come from <c>IViewAuthorizer.ShapeQuery</c> (tenant isolation, ownership,
    /// ...). They are <b>server-trusted</b> and are <b>not</b> subjected to the client-filter whitelist
    /// validation (Requirement R8.4, Decision Log DR9). Applying them pre-projection excludes every
    /// out-of-scope row <em>before</em> any row is loaded, so an out-of-scope key is indistinguishable
    /// from a missing key at load time (Requirements R8.1–R8.3). Overridable so tests can inject an
    /// in-memory/SQLite queryable, mirroring the read seam.
    /// </remarks>
    protected virtual IQueryable<TEntity> ResolveScopedEntitySet<TEntity>(ViewMetadata view, IViewScope scope)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(scope);

        if (_dbContext is null)
        {
            throw new InvalidOperationException(
                $"This {nameof(EfViewExecutor)} was constructed without an EF context, so it cannot resolve " +
                $"the write entity set for view '{view.Name}'. Use the DI constructor (the one taking a " +
                $"{nameof(DbContext)}, {nameof(IServiceProvider)}, and {nameof(IViewExecutionPlanRegistry)}), " +
                "or override ResolveScopedEntitySet<TEntity>.");
        }

        IQueryable<TEntity> source = _dbContext.Set<TEntity>();

        // Server-trusted per-request scope (R8.4/DR9): AND-ed pre-projection, never whitelist-validated.
        var scopeFilters = scope.GetRowFilters<TEntity>();
        for (var i = 0; i < scopeFilters.Count; i++)
        {
            source = source.Where(scopeFilters[i]);
        }

        return source;
    }

    /// <summary>
    /// Loads the single <typeparamref name="TEntity"/> matching <paramref name="key"/> <b>within the
    /// authorized scope</b>, or <see langword="null"/> when no in-scope row matches. Composes
    /// <see cref="NormalizeWriteKey{TEntity}"/>, <see cref="BuildEntityKeyPredicate{TEntity}"/>, and
    /// <see cref="ResolveScopedEntitySet{TEntity}"/>. A <see langword="null"/> result is the caller's
    /// signal to return <see langword="false"/> (HTTP 404) — the same response an out-of-scope key
    /// produces (Requirements R2.3, R3.3, R8.2, R8.3).
    /// </summary>
    /// <typeparam name="TEntity">The EF entity type the view writes to.</typeparam>
    /// <param name="view">The writable view being resolved.</param>
    /// <param name="key">The request key: a scalar (single key) or a name→value map (composite key).</param>
    /// <param name="scope">The server-trusted row-filter scope.</param>
    /// <param name="cancellationToken">Token used to cancel the load.</param>
    /// <returns>The matched, tracked entity, or <see langword="null"/> when none matches in scope.</returns>
    /// <exception cref="VistaWriteKeyException">
    /// The key is missing/incomplete or a value cannot be coerced to the key member's type (HTTP 400).
    /// </exception>
    [RequiresUnreferencedCode("Write entity resolution builds a key predicate over TEntity from metadata at runtime; use the source generator path for AOT.")]
    private protected virtual async Task<TEntity?> ResolveEntityForWriteAsync<TEntity>(
        ViewMetadata view,
        object key,
        IViewScope scope,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var coercedKey = NormalizeWriteKey<TEntity>(view, key);
        var predicate = BuildEntityKeyPredicate<TEntity>(view, coercedKey);
        var scoped = ResolveScopedEntitySet<TEntity>(view, scope);
        return await SingleOrDefaultAsync(scoped.Where(predicate), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Normalizes and coerces the request <paramref name="key"/> into a name→value map keyed by the
    /// view's ordered <see cref="ViewMetadata.KeyFields"/>, with each value coerced to its
    /// <typeparamref name="TEntity"/> member's CLR type via <see cref="FilterCompiler.CoerceValue"/>
    /// (the same coercion DetailAsync uses). A scalar is accepted only for a single-field key; a
    /// composite key must arrive as a name→value map supplying <em>exactly</em> the key fields
    /// (order-independent, Requirement R3.6).
    /// </summary>
    /// <typeparam name="TEntity">The EF entity type the view writes to.</typeparam>
    /// <param name="view">The writable view being resolved.</param>
    /// <param name="key">The request key: a scalar or a name→value map.</param>
    /// <returns>An ordinal-keyed map of coerced key values, one per <see cref="ViewMetadata.KeyFields"/>.</returns>
    /// <exception cref="VistaWriteKeyException">
    /// A composite key omits a field or names a field absent from <c>KeyFields</c>
    /// (<see cref="WriteErrorCode.IncompleteKey"/>), or a value cannot be coerced
    /// (<see cref="WriteErrorCode.KeyTypeCoercion"/>).
    /// </exception>
    /// <exception cref="InvalidOperationException">The view declares no key fields (authoring error).</exception>
    [RequiresUnreferencedCode("Write key coercion resolves entity key members and target types at runtime; use the source generator path for AOT.")]
    private static IReadOnlyDictionary<string, object?> NormalizeWriteKey<TEntity>(ViewMetadata view, object key)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);

        if (view.KeyFields.Count == 0)
        {
            throw new InvalidOperationException(
                $"View '{view.Name}' has no key fields, so a write cannot resolve a target row. Declare a " +
                "primary key with .PrimaryKey() or Key(...).");
        }

        var raw = ShapeWriteKey(view, key);

        var coerced = new Dictionary<string, object?>(view.KeyFields.Count, StringComparer.Ordinal);
        foreach (var name in view.KeyFields)
        {
            var member = ResolveEntityKeyMember<TEntity>(view, name);
            var underlying = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;

            try
            {
                coerced[name] = FilterCompiler.CoerceValue(raw[name], underlying, name);
            }
            catch (FilterValidationException ex)
            {
                // Translate the read-path coercion failure into the write vocabulary (R9.3). The message
                // is Vista-authored and leak-free; the original is kept only as the (never-surfaced) inner.
                throw new VistaWriteKeyException(
                    WriteErrorCode.KeyTypeCoercion,
                    $"The key value for field '{name}' could not be interpreted as the expected type.",
                    name,
                    ex);
            }
        }

        return coerced;
    }

    /// <summary>
    /// Shapes the incoming <paramref name="key"/> into a name→raw-value map covering exactly the view's
    /// <see cref="ViewMetadata.KeyFields"/>, enforcing completeness and rejecting unknown field names
    /// (Requirement R3.7). Values are not yet coerced. Order-independent (Requirement R3.6).
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ShapeWriteKey(ViewMetadata view, object key)
    {
        var keyFields = view.KeyFields;

        if (key is IReadOnlyDictionary<string, object?> map)
        {
            // Reject any supplied field that is not part of the key (R3.7 "field name not present").
            foreach (var supplied in map.Keys)
            {
                if (!ContainsOrdinal(keyFields, supplied))
                {
                    throw new VistaWriteKeyException(
                        WriteErrorCode.IncompleteKey,
                        $"The key names field '{supplied}', which is not part of the view's key.",
                        supplied);
                }
            }

            var result = new Dictionary<string, object?>(keyFields.Count, StringComparer.Ordinal);
            foreach (var name in keyFields)
            {
                if (!map.TryGetValue(name, out var value))
                {
                    throw new VistaWriteKeyException(
                        WriteErrorCode.IncompleteKey,
                        $"The key is missing a value for the field '{name}'.",
                        name);
                }

                result[name] = value;
            }

            return result;
        }

        // A scalar is only valid for a single-field key; a composite key requires a name→value map (R3.6).
        if (keyFields.Count != 1)
        {
            throw new VistaWriteKeyException(
                WriteErrorCode.IncompleteKey,
                $"The view has a composite key ({keyFields.Count} fields); supply a value for every key " +
                "field by name, not a single scalar value.");
        }

        return new Dictionary<string, object?>(1, StringComparer.Ordinal) { [keyFields[0]] = key };
    }

    /// <summary>
    /// Builds the entity-side key predicate <c>e =&gt; e.&lt;Key1&gt; == v1 &amp;&amp; ...</c> over
    /// <typeparamref name="TEntity"/> from a pre-coerced key map, resolving each key member by
    /// <see cref="ViewMetadata.KeyFields"/> name. This is a server-side key lookup, not a client filter,
    /// so it bypasses the tri-whitelist (a key field may be opted out of client filtering yet must still
    /// resolve a write target).
    /// </summary>
    [RequiresUnreferencedCode("Write key predicate resolves entity key members from metadata at runtime; use the source generator path for AOT.")]
    private static Expression<Func<TEntity, bool>> BuildEntityKeyPredicate<TEntity>(
        ViewMetadata view,
        IReadOnlyDictionary<string, object?> coercedKey)
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        Expression? body = null;
        foreach (var name in view.KeyFields)
        {
            var member = ResolveEntityKeyMember<TEntity>(view, name);
            Expression access = Expression.Property(parameter, member);
            var memberType = member.PropertyType;
            var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;

            Expression constant = Expression.Constant(coercedKey[name], underlying);
            if (constant.Type != memberType)
            {
                constant = Expression.Convert(constant, memberType);
            }

            var equality = Expression.Equal(access, constant);
            body = body is null ? equality : Expression.AndAlso(body, equality);
        }

        return Expression.Lambda<Func<TEntity, bool>>(body!, parameter);
    }

    /// <summary>
    /// Resolves the <typeparamref name="TEntity"/> public instance property named by
    /// <paramref name="keyFieldName"/>. Write key resolution maps each <see cref="ViewMetadata.KeyFields"/>
    /// entry by name onto the entity; a missing member is an authoring error (not a client error).
    /// </summary>
    [RequiresUnreferencedCode("Resolves an entity key member by name via reflection; use the source generator path for AOT.")]
    private static PropertyInfo ResolveEntityKeyMember<TEntity>(ViewMetadata view, string keyFieldName)
    {
        return typeof(TEntity).GetProperty(keyFieldName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"View '{view.Name}' declares key field '{keyFieldName}', but the write entity type " +
                $"'{typeof(TEntity).Name}' has no matching public instance property. Ensure the projected " +
                "key field name matches the entity member for single-source writable views.");
    }

    /// <summary>
    /// Enforces the optimistic-concurrency precondition (Requirement R6.3): when the view declares a
    /// concurrency token, the stored row's current token must exactly equal <paramref name="expectedToken"/>
    /// (the client's <c>If-Match</c> value); a mismatch throws <see cref="VistaConcurrencyConflictException"/>
    /// (HTTP 409) before any change is persisted. A tokenless view is a no-op and ignores
    /// <paramref name="expectedToken"/> (Requirement R6.6).
    /// </summary>
    /// <typeparam name="TEntity">The EF entity type the view writes to.</typeparam>
    /// <param name="facet">The captured write facet, whose <see cref="CrudFacetDefinition.ConcurrencyToken"/> selects the token.</param>
    /// <param name="entity">The loaded, tracked entity whose current token is compared.</param>
    /// <param name="expectedToken">The client-supplied expected token (the <c>If-Match</c> value), or <see langword="null"/>.</param>
    /// <exception cref="VistaConcurrencyConflictException">The stored token does not match <paramref name="expectedToken"/>.</exception>
    [RequiresUnreferencedCode("Reading the concurrency token compiles the captured selector at runtime; use the source generator path for AOT.")]
    private static void EnforceConcurrencyToken<TEntity>(
        CrudFacetDefinition facet,
        TEntity entity,
        string? expectedToken)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(facet);
        ArgumentNullException.ThrowIfNull(entity);

        if (facet.ConcurrencyToken is null)
        {
            // R6.6: no token → no precondition; any If-Match is ignored.
            return;
        }

        var storedToken = ReadConcurrencyToken(facet, entity);
        if (!string.Equals(storedToken, expectedToken, StringComparison.Ordinal))
        {
            throw new VistaConcurrencyConflictException();
        }
    }

    /// <summary>
    /// Reads and formats the current concurrency-token value of <paramref name="entity"/> as a wire
    /// string, or <see langword="null"/> when the view declares no token. Used both by the pre-check
    /// (<see cref="EnforceConcurrencyToken{TEntity}"/>) and by the write bodies to round-trip the token
    /// into the <c>ETag</c> response header on success (Requirement R6.4).
    /// </summary>
    /// <typeparam name="TEntity">The EF entity type the view writes to.</typeparam>
    /// <param name="facet">The captured write facet whose token selector is evaluated.</param>
    /// <param name="entity">The entity to read the token from.</param>
    /// <returns>The formatted token, or <see langword="null"/> for a tokenless view.</returns>
    [RequiresUnreferencedCode("Reading the concurrency token compiles the captured selector at runtime; use the source generator path for AOT.")]
    private static string? ReadConcurrencyToken<TEntity>(CrudFacetDefinition facet, TEntity entity)
        where TEntity : class
    {
        if (facet.ConcurrencyToken is not { } selector)
        {
            return null;
        }

        var value = selector.Compile().DynamicInvoke(entity);
        return FormatToken(value);
    }

    /// <summary>
    /// Formats a concurrency-token value into a stable wire string for comparison and the <c>ETag</c>
    /// header: a <c>byte[]</c> rowversion becomes Base64, any other value uses its invariant-culture
    /// representation. <see langword="null"/> maps to <see langword="null"/>.
    /// </summary>
    internal static string? FormatToken(object? value) => value switch
    {
        null => null,
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static bool ContainsOrdinal(IReadOnlyList<string> names, string candidate)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the single row of <paramref name="source"/> (or <see langword="null"/>) using EF's async
    /// pipeline, honoring <paramref name="cancellationToken"/>. Used by the write path to resolve a
    /// keyed target within scope. Overridable for testing.
    /// </summary>
    /// <typeparam name="TEntity">The queried entity type.</typeparam>
    /// <param name="source">The key-filtered, scoped queryable to read from.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The single matching entity, or <see langword="null"/> when none matches.</returns>
    protected virtual Task<TEntity?> SingleOrDefaultAsync<TEntity>(IQueryable<TEntity> source, CancellationToken cancellationToken) =>
        EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(source, cancellationToken);

    /// <summary>
    /// Resolves the post-projection <see cref="IQueryable{T}"/> for a view, with the server-trusted
    /// scope AND-ed in <em>pre-projection</em> over the EF source entity. <b>This is the Task 9.2 seam.</b>
    /// </summary>
    /// <typeparam name="TRow">The projected (read) row type of the view.</typeparam>
    /// <param name="view">The metadata of the view to resolve.</param>
    /// <param name="scope">The server-trusted row-filter scope to apply pre-projection.</param>
    /// <returns>A not-yet-enumerated, scoped, projected queryable over <typeparamref name="TRow"/>.</returns>
    /// <remarks>
    /// <para>
    /// The integration contract Task 9.2 must satisfy:
    /// </para>
    /// <list type="number">
    ///   <item><description>Obtain <c>IQueryable&lt;TSource&gt;</c> for the view's source entity via the
    ///   <c>DbContext.Set&lt;TSource&gt;()</c> convention (Decision Log D11).</description></item>
    ///   <item><description>AND-in the authored row filters (<c>TemplateRowFilter</c>) <em>and</em> the
    ///   per-request predicates from <see cref="IViewScope.GetRowFilters{TSource}"/> — both
    ///   server-trusted, neither subject to client whitelist validation (R6.3) — over
    ///   <c>TSource</c> before projecting.</description></item>
    ///   <item><description>Apply the captured projection to produce <c>IQueryable&lt;TRow&gt;</c> and
    ///   return it.</description></item>
    /// </list>
    /// <para>
    /// Applying scope pre-projection keeps row-level security push-down friendly (translated to SQL)
    /// and guarantees the unfiltered total never counts rows outside the authorized scope. Keeping
    /// this the <em>only</em> source seam lets the List/Detail logic stay provider-agnostic and unit
    /// testable: a test (Task 12) overrides this method to return an EF InMemory/SQLite queryable.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("Source/projection resolution builds queries from metadata at runtime; use the source generator path for AOT.")]
    protected virtual IQueryable<TRow> ResolveScopedQueryable<TRow>(ViewMetadata view, IViewScope scope)
    {
        if (_planRegistry is null || _dbContext is null || _services is null)
        {
            throw new InvalidOperationException(
                $"This {nameof(EfViewExecutor)} was constructed without an execution-plan registry, so it " +
                $"cannot resolve a queryable for view '{view.Name}'. Use the constructor that takes a " +
                $"{nameof(DbContext)}, {nameof(IServiceProvider)}, and {nameof(IViewExecutionPlanRegistry)} " +
                "(the DI wiring path, Task 9.4), or override ResolveScopedQueryable<TRow> in a subclass.");
        }

        // Metadata-only fail-fast (R4.4 / DR5): a typed Style B view stays metadata-only when no plan was
        // generated for it. Throw before any query work — no source query is built, no count or
        // materialization runs — so no partial result is ever produced. The message names the view, states
        // that no generated execution plan exists, and instructs referencing the source generator (D118).
        var plan = _planRegistry.Get(view.Name)
            ?? throw new InvalidOperationException(
                $"View '{view.Name}' has no generated execution plan, so it is metadata-only (DR5) and " +
                "cannot be executed. Reference the a2n.Vista source generator (a2n.Vista.SourceGenerators) " +
                "so a compiled execution plan is generated for this typed Style B view, making it " +
                "executable; until then List and Detail fail fast here before any query runs and no " +
                "result is produced.");

        var queryable = plan.CreateScopedQueryable(_dbContext, _services, scope);
        if (queryable is IQueryable<TRow> typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"The execution plan for view '{view.Name}' produced a queryable of element type " +
            $"'{queryable.ElementType}', but the caller requested rows of type '{typeof(TRow)}'. The TRow type " +
            "argument must match the view's projected row type (ViewMetadata.QueryType).");
    }

    /// <summary>
    /// Counts the rows of <paramref name="source"/> as a <see langword="long"/> (R10.1, overflow-safe),
    /// honoring <paramref name="cancellationToken"/>. Overridable for testing or alternative providers.
    /// </summary>
    /// <typeparam name="TRow">The queried row type.</typeparam>
    /// <param name="source">The queryable to count.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of matching rows.</returns>
    protected virtual Task<long> CountAsync<TRow>(IQueryable<TRow> source, CancellationToken cancellationToken) =>
        EntityFrameworkQueryableExtensions.LongCountAsync(source, cancellationToken);

    /// <summary>
    /// Materializes <paramref name="source"/> into a list using EF's async pipeline, honoring
    /// <paramref name="cancellationToken"/>. Overridable for testing or alternative providers.
    /// </summary>
    /// <typeparam name="TRow">The queried row type.</typeparam>
    /// <param name="source">The (already paged) queryable to materialize.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The materialized rows.</returns>
    protected virtual async Task<IReadOnlyList<TRow>> MaterializeAsync<TRow>(
        IQueryable<TRow> source,
        CancellationToken cancellationToken) =>
        await EntityFrameworkQueryableExtensions.ToListAsync(source, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Reads the first row of <paramref name="source"/> (or <see langword="null"/>) using EF's async
    /// pipeline, honoring <paramref name="cancellationToken"/>. Overridable for testing.
    /// </summary>
    /// <typeparam name="TRow">The queried row type.</typeparam>
    /// <param name="source">The queryable to read from.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The first matching row, or <see langword="null"/>.</returns>
    protected virtual Task<TRow?> FirstOrDefaultAsync<TRow>(IQueryable<TRow> source, CancellationToken cancellationToken) =>
        EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(source, cancellationToken);

    /// <summary>
    /// Clamps the requested page size to <see cref="HardLimits.MaxPageSize"/> and rejects "return all"
    /// requests (<see cref="ViewQueryRequest.PageSize"/> &lt;= 0, e.g. DataTables <c>length=-1</c>),
    /// which are never honored (R10.3, §7, §12.2).
    /// </summary>
    /// <param name="requested">The client-requested page size.</param>
    /// <param name="limits">The view's hard limits.</param>
    /// <returns>The effective page size to use (1..<see cref="HardLimits.MaxPageSize"/>).</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="requested"/> is &lt;= 0. The AspNetCore layer (Task 10) maps this to HTTP 400.
    /// </exception>
    protected static int ResolvePageSize(int requested, HardLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (requested <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                requested,
                "A positive page size is required; \"return all\" requests (for example length=-1) are not permitted (R10.3).");
        }

        return Math.Min(requested, limits.MaxPageSize);
    }

    /// <summary>
    /// Applies the requested ordering to <paramref name="source"/>, validating each field is sortable
    /// against <paramref name="view"/> metadata before building the key selector.
    /// </summary>
    [RequiresUnreferencedCode("Sorting builds key selectors and closed Queryable generics from metadata at runtime; use the source generator path for AOT.")]
    private static IQueryable<TRow> ApplySort<TRow>(IQueryable<TRow> source, IReadOnlyList<SortSpec> sort, ViewMetadata view)
    {
        // Build the combined ordering: the client sort (validated sortable) followed by the view's
        // KeyFields as the deterministic tiebreaker — appended ascending, skipping any already used as a
        // sort key. An empty client sort therefore orders by KeyFields ascending (Decision Log D106, §11).
        var steps = new List<(FieldMetadata Field, bool Descending)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (sort is not null)
        {
            foreach (var spec in sort)
            {
                var field = ResolveSortableField(view, spec.Field);
                if (seen.Add(field.Name))
                {
                    steps.Add((field, spec.Descending));
                }
            }
        }

        foreach (var keyName in view.KeyFields)
        {
            if (!seen.Add(keyName))
            {
                continue;
            }

            var keyField = FindField(view, keyName)
                ?? throw new InvalidOperationException(
                    $"View '{view.Name}' declares key field '{keyName}', which is not part of the projection.");
            steps.Add((keyField, false));
        }

        if (steps.Count == 0)
        {
            // No client sort and no key fields. Deterministic paging cannot be guaranteed; registration
            // fail-fast (Decision Log D106) prevents this for registered views, so this is only reachable
            // for hand-built metadata in tests.
            return source;
        }

        IOrderedQueryable<TRow>? ordered = null;
        for (var i = 0; i < steps.Count; i++)
        {
            var (field, descending) = steps[i];
            var parameter = Expression.Parameter(typeof(TRow), "x");
            Expression member = Expression.Property(parameter, field.Name);
            var keySelector = Expression.Lambda(member, parameter);

            var openMethod = (i == 0, descending) switch
            {
                (true, false) => OrderByMethod,
                (true, true) => OrderByDescendingMethod,
                (false, false) => ThenByMethod,
                (false, true) => ThenByDescendingMethod,
            };
            var method = openMethod.MakeGenericMethod(typeof(TRow), member.Type);

            var current = i == 0 ? (object)source : ordered!;
            ordered = (IOrderedQueryable<TRow>)method.Invoke(null, [current, keySelector])!;
        }

        return ordered!;
    }

    private static FieldMetadata ResolveSortableField(ViewMetadata view, string fieldName)
    {
        var field = FindField(view, fieldName);
        if (field is null)
        {
            throw new FilterValidationException(
                FilterErrorCode.UnknownField,
                $"Sort field '{fieldName}' does not exist in the view projection.",
                fieldName);
        }

        if (!field.IsSortable)
        {
            throw new FilterValidationException(
                FilterErrorCode.FieldNotAllowed,
                $"Field '{fieldName}' is not sortable.",
                fieldName);
        }

        return field;
    }

    private static FieldMetadata? FindField(ViewMetadata view, string fieldName)
    {
        foreach (var field in view.Fields)
        {
            if (string.Equals(field.Name, fieldName, StringComparison.Ordinal))
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the Detail-by-key predicate for the view's <see cref="ViewMetadata.KeyFields"/>
    /// (Decision Log D104/D109): the conjunction of <c>x.&lt;keyField&gt; == coerce(value)</c> over each
    /// key field, resolving values <b>by field name</b> (order-independent). This is a server-side key
    /// lookup, not a client filter, so it bypasses the tri-whitelist (a key field may be opted out of
    /// client filtering yet must still resolve Detail).
    /// </summary>
    /// <typeparam name="TRow">The projected (read) row type of the view.</typeparam>
    /// <param name="view">The metadata of the view.</param>
    /// <param name="key">The key: a scalar (single key) or a name→value map (composite key).</param>
    /// <returns>The conjunction predicate identifying the row.</returns>
    /// <exception cref="InvalidOperationException">The view declares no key fields.</exception>
    /// <exception cref="FilterValidationException">The key shape is wrong or a value cannot be coerced.</exception>
    private static Expression<Func<TRow, bool>> BuildKeyPredicate<TRow>(ViewMetadata view, object key)
    {
        if (view.KeyFields.Count == 0)
        {
            throw new InvalidOperationException(
                $"View '{view.Name}' has no key fields, so Detail-by-key cannot resolve a row. Declare a " +
                "primary key with .PrimaryKey() or Key(...).");
        }

        var values = NormalizeKey(view, key);

        var parameter = Expression.Parameter(typeof(TRow), "x");
        Expression? body = null;
        foreach (var keyName in view.KeyFields)
        {
            var field = FindField(view, keyName)
                ?? throw new InvalidOperationException(
                    $"View '{view.Name}' declares key field '{keyName}', which is not part of the projection.");

            var member = Expression.Property(parameter, field.Name);
            var memberType = member.Type;
            var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;

            var coerced = FilterCompiler.CoerceValue(values[keyName], underlying, field.Name);
            Expression constant = Expression.Constant(coerced, underlying);
            if (constant.Type != memberType)
            {
                constant = Expression.Convert(constant, memberType);
            }

            var equality = Expression.Equal(member, constant);
            body = body is null ? equality : Expression.AndAlso(body, equality);
        }

        return Expression.Lambda<Func<TRow, bool>>(body!, parameter);
    }

    /// <summary>
    /// Normalizes the <see cref="object"/> key into a name→value map keyed by <see cref="ViewMetadata.KeyFields"/>:
    /// a scalar is accepted only for a single-field key; a composite key must arrive as an
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> providing exactly the key fields by name
    /// (Decision Log D109).
    /// </summary>
    private static IReadOnlyDictionary<string, object?> NormalizeKey(ViewMetadata view, object key)
    {
        var keyFields = view.KeyFields;

        if (key is IReadOnlyDictionary<string, object?> map)
        {
            var result = new Dictionary<string, object?>(keyFields.Count, StringComparer.Ordinal);
            foreach (var name in keyFields)
            {
                if (!map.TryGetValue(name, out var value))
                {
                    throw new FilterValidationException(
                        FilterErrorCode.InvalidValue,
                        $"The key for view '{view.Name}' is missing the field '{name}'.",
                        name);
                }

                result[name] = value;
            }

            return result;
        }

        if (keyFields.Count != 1)
        {
            throw new FilterValidationException(
                FilterErrorCode.InvalidValue,
                $"View '{view.Name}' has a composite key ({keyFields.Count} fields); supply a key object " +
                "with a member per key field, not a scalar value.");
        }

        return new Dictionary<string, object?>(1, StringComparer.Ordinal) { [keyFields[0]] = key };
    }

    // ---------------------------------------------------------------------------------------------
    // Write facet dependency guards (Tasks 4.2–4.4). The write bodies persist through the request-scoped
    // DbContext and resolve the WriteMapper from the request IServiceProvider, so they require the DI
    // constructor. These surface a clear authoring/wiring error (never a NullReferenceException) when the
    // executor was constructed without those dependencies, and centralize the CrudEntityType lookup used
    // by the reflection bridge.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the view's write entity type (<see cref="ViewMetadata.CrudEntityType"/>), or throws when
    /// the view is read-only / carries no entity type. The write bridge closes its <c>*CoreAsync</c>
    /// helper over this type at runtime.
    /// </summary>
    private static Type RequireCrudEntityType(ViewMetadata view) =>
        view.CrudEntityType
        ?? throw new InvalidOperationException(
            $"View '{view.Name}' declares no CRUD entity type, so a write cannot be executed against it. " +
            "This indicates a read-only view reached the write path; writable views are authored with " +
            "WithCrud<TCrud, TEntity>().");

    /// <summary>
    /// Returns the request-scoped <see cref="DbContext"/>, or throws a clear wiring error when this
    /// executor was constructed without one (the subclass/test constructors). The same
    /// <see cref="DbContext"/> instance backs read-for-write and persistence (Requirement R11.5).
    /// </summary>
    private DbContext RequireDbContext(ViewMetadata view, string operation) =>
        _dbContext
        ?? throw new InvalidOperationException(
            $"This {nameof(EfViewExecutor)} was constructed without an EF context, so it cannot execute " +
            $"{operation} for view '{view.Name}'. Use the DI constructor (the one taking a " +
            $"{nameof(DbContext)}, {nameof(IServiceProvider)}, and {nameof(IViewExecutionPlanRegistry)}).");

    /// <summary>
    /// Returns the request <see cref="IServiceProvider"/>, or throws a clear wiring error when this
    /// executor was constructed without one. The provider supplies the <see cref="WriteMapperResolver"/>
    /// the write path resolves its mapper from.
    /// </summary>
    private IServiceProvider RequireServices(ViewMetadata view, string operation) =>
        _services
        ?? throw new InvalidOperationException(
            $"This {nameof(EfViewExecutor)} was constructed without a service provider, so it cannot " +
            $"resolve the write mapper for {operation} on view '{view.Name}'. Use the DI constructor (the " +
            $"one taking a {nameof(DbContext)}, {nameof(IServiceProvider)}, and {nameof(IViewExecutionPlanRegistry)}).");

    /// <summary>
    /// Resolves the captured <see cref="CrudFacetDefinition"/> for <paramref name="view"/> from the
    /// <see cref="IWriteFacetRegistry"/>, or throws a clear wiring error when none is registered. The
    /// write bodies need the facet to enforce the concurrency precondition
    /// (<see cref="EnforceConcurrencyToken{TEntity}"/>); a writable view that reached the executor must
    /// have a registered facet, so a miss is an internal wiring fault, not a client error.
    /// </summary>
    private static CrudFacetDefinition RequireWriteFacet(IServiceProvider services, ViewMetadata view)
    {
        var registry = services.GetRequiredService<IWriteFacetRegistry>();
        if (registry.TryGet(view.Name, out var facet))
        {
            return facet;
        }

        throw new InvalidOperationException(
            $"View '{view.Name}' reached the write executor but has no CRUD facet registered in the " +
            $"{nameof(IWriteFacetRegistry)}. This indicates a registration wiring fault; writable views " +
            "are authored with WithCrud<TCrud, TEntity>() and register a facet at startup.");
    }

    private static MethodInfo GetQueryableOrdering(string name) =>
        typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name
                && m.GetParameters().Length == 2
                && m.GetGenericArguments().Length == 2);
}
