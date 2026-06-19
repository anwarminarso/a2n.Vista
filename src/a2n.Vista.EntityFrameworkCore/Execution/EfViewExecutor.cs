using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using a2n.Vista.Contracts;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using Microsoft.EntityFrameworkCore;

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
/// <b>Write facet.</b> Create/Update/Delete are intentionally not implemented in Pilar 1's
/// list-focused executor; they require the compiled <c>TCrud → TEntity</c> mapping
/// (§7, §9) which has no task-9 sub-task. They throw <see cref="NotSupportedException"/> with a clear
/// message. See the class-level remarks and the method docs for the rationale.
/// </para>
/// </remarks>
public class EfViewExecutor : IViewExecutor
{
    private static readonly MethodInfo OrderByMethod = GetQueryableOrdering(nameof(Queryable.OrderBy));
    private static readonly MethodInfo OrderByDescendingMethod = GetQueryableOrdering(nameof(Queryable.OrderByDescending));
    private static readonly MethodInfo ThenByMethod = GetQueryableOrdering(nameof(Queryable.ThenBy));
    private static readonly MethodInfo ThenByDescendingMethod = GetQueryableOrdering(nameof(Queryable.ThenByDescending));

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
        : this(dbContext, services, planRegistry, new ProviderAwareFilterCompiler())
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
    [RequiresUnreferencedCode("View execution resolves sort/filter/projection from metadata at runtime; use the source generator path for AOT.")]
    public async Task<ViewListResult<TRow>> ListAsync<TRow>(
        ViewMetadata view,
        ViewQueryRequest request,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scope);

        // Clamp/reject paging up front so an invalid "return all" request fails before any DB round-trip (R10.3).
        var pageSize = ResolvePageSize(request.PageSize, view.Limits);
        var pageIndex = request.Page < 0 ? 0 : request.Page;

        // Task 9.2 seam: scope (server-trusted, pre-projection over TSource) is already AND-ed into this query.
        var scoped = ResolveScopedQueryable<TRow>(view, scope);

        // recordsTotal — scope applied, client filter/search NOT applied (R10.4).
        var totalRowsUnfiltered = await CountAsync(scoped, cancellationToken).ConfigureAwait(false);

        // Client filter/search path. The request carries the already-merged tree; per-origin compilation
        // of search/scope sub-trees is the adapter's responsibility in Pilar 2 (§8.1/§8.3), so here the
        // whole tree is validated and compiled under FilterOrigin.Filter. Documented assumption for 9.1.
        var filtered = scoped;
        if (request.Filter is not null)
        {
            var predicate = _filterCompiler.Compile<TRow>(request.Filter, FilterOrigin.Filter, view);
            filtered = filtered.Where(predicate);
        }

        // recordsFiltered — after the client filter (R10.4).
        var totalRows = await CountAsync(filtered, cancellationToken).ConfigureAwait(false);

        var ordered = ApplySort(filtered, request.Sort, view);

        // Compute the skip as long to avoid the int overflow DynData suffered on large page indexes (§10.1).
        var skipLong = (long)pageIndex * pageSize;
        var skip = skipLong > int.MaxValue ? int.MaxValue : (int)skipLong;
        var pageQuery = ordered.Skip(skip).Take(pageSize);

        var items = await MaterializeAsync(pageQuery, cancellationToken).ConfigureAwait(false);

        var totalPages = pageSize == 0 ? 0L : (totalRows + pageSize - 1) / pageSize;
        var page = new PagedResult<TRow>(items, totalRows, pageIndex, pageSize, totalPages);
        return new ViewListResult<TRow>(page, totalRowsUnfiltered);
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Detail key resolution and projection are built from metadata at runtime; use the source generator path for AOT.")]
    public async Task<TRow?> DetailAsync<TRow>(
        ViewMetadata view,
        object key,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(scope);

        // Detail = List projection filtered by primary key, with the server-trusted scope still applied
        // (Decision Log D49, §4.6). Reuse the same resolution seam as List.
        var scoped = ResolveScopedQueryable<TRow>(view, scope);

        var keyField = ResolveKeyField<TRow>(view);
        var predicate = BuildKeyEquality<TRow>(keyField, key);

        return await FirstOrDefaultAsync(scoped.Where(predicate), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Write mapping (TCrud to entity) is resolved from metadata at runtime; use the source generator path for AOT.")]
    public Task<object> CreateAsync<TCrud>(
        ViewMetadata view,
        TCrud model,
        IViewScope scope,
        CancellationToken cancellationToken)
        where TCrud : class =>
        throw WriteNotSupported(nameof(CreateAsync));

    /// <inheritdoc />
    [RequiresUnreferencedCode("Write mapping (TCrud to entity) is resolved from metadata at runtime; use the source generator path for AOT.")]
    public Task<bool> UpdateAsync<TCrud>(
        ViewMetadata view,
        object key,
        TCrud model,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken)
        where TCrud : class =>
        throw WriteNotSupported(nameof(UpdateAsync));

    /// <inheritdoc />
    [RequiresUnreferencedCode("Delete key resolution is built from metadata at runtime; use the source generator path for AOT.")]
    public Task<bool> DeleteAsync(
        ViewMetadata view,
        object key,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken) =>
        throw WriteNotSupported(nameof(DeleteAsync));

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

        var plan = _planRegistry.Get(view.Name)
            ?? throw new InvalidOperationException(
                $"No execution plan is registered for view '{view.Name}'. Register the view's plan (via " +
                "AddVista/RegisterTemplate/Register, Task 9.4) so its source query and projection can be resolved.");

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
        if (sort is null || sort.Count == 0)
        {
            return source;
        }

        IOrderedQueryable<TRow>? ordered = null;
        for (var i = 0; i < sort.Count; i++)
        {
            var spec = sort[i];
            var field = ResolveSortableField(view, spec.Field);

            var parameter = Expression.Parameter(typeof(TRow), "x");
            Expression member = Expression.Property(parameter, field.Name);
            var keySelector = Expression.Lambda(member, parameter);

            var openMethod = (i == 0, spec.Descending) switch
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
    /// Resolves the primary-key field used by Detail-by-key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Resolution order (Task 9.2).</b> First, the view's <see cref="IViewExecutionPlan.KeyFieldName"/>
    /// is consulted: when the authoring style captured the primary key (the §4.1-aligned
    /// <see cref="SplitViewExecutionPlan{TSource, TRow}"/> can carry it), the executor trusts it directly.
    /// This is the robust path and avoids the fragile name guessing below.
    /// </para>
    /// <para>
    /// <b>Known metadata gap (flagged for follow-up).</b> The authoring layer captures the primary key
    /// (<c>IFieldBuilder.PrimaryKey()</c> / <c>IFieldBuilderState.IsPrimaryKey</c>) but it is used only
    /// to validate write/detail facets at build time — it is <em>not</em> propagated into
    /// <see cref="FieldMetadata"/> or <see cref="ViewMetadata"/>. As a result, the Gaya A
    /// (central-template) path cannot supply <see cref="IViewExecutionPlan.KeyFieldName"/> today, and the
    /// executor must fall back to a convention for those views. <c>ViewMetadata</c>/<c>FieldMetadata</c>
    /// should carry the PK field name (for example a <c>FieldMetadata.IsPrimaryKey</c> flag or
    /// <c>ViewMetadata.KeyField</c>); once it does, both authoring styles can populate the plan's
    /// <see cref="IViewExecutionPlan.KeyFieldName"/> and this convention can be removed.
    /// </para>
    /// <para>
    /// Convention fallback, in order: a field named <c>Id</c>; then a field named
    /// <c>&lt;QueryType.Name&gt;Id</c> (for example <c>ProductId</c> when the row type is <c>Product</c>);
    /// otherwise the first projected field.
    /// </para>
    /// </remarks>
    /// <typeparam name="TRow">The projected (read) row type of the view.</typeparam>
    /// <param name="view">The metadata of the view.</param>
    /// <returns>The field treated as the primary key.</returns>
    /// <exception cref="InvalidOperationException">The view declares no projected fields.</exception>
    protected virtual FieldMetadata ResolveKeyField<TRow>(ViewMetadata view)
    {
        if (view.Fields.Count == 0)
        {
            throw new InvalidOperationException(
                $"View '{view.Name}' has no projected fields, so Detail-by-key cannot resolve a primary key.");
        }

        // 1. Trust the execution plan's captured PK when available (the robust, authoring-driven path).
        var planKeyName = _planRegistry?.Get(view.Name)?.KeyFieldName;
        if (planKeyName is not null)
        {
            var planKey = FindField(view, planKeyName);
            if (planKey is not null)
            {
                return planKey;
            }
        }

        // 2. Convention fallback (used while the PK is not surfaced into metadata; see remarks).
        var byId = FindField(view, "Id");
        if (byId is not null)
        {
            return byId;
        }

        var byTypeId = FindField(view, $"{typeof(TRow).Name}Id");
        if (byTypeId is not null)
        {
            return byTypeId;
        }

        return view.Fields[0];
    }

    /// <summary>
    /// Builds an equality predicate <c>x =&gt; x.&lt;keyField&gt; == key</c>, coercing
    /// <paramref name="key"/> to the field's CLR type. This is a server-side key lookup, not a client
    /// filter, so it intentionally bypasses the tri-whitelist (the PK may be opted out of client
    /// filtering yet must still resolve Detail).
    /// </summary>
    private static Expression<Func<TRow, bool>> BuildKeyEquality<TRow>(FieldMetadata keyField, object key)
    {
        var parameter = Expression.Parameter(typeof(TRow), "x");
        var member = Expression.Property(parameter, keyField.Name);
        var memberType = member.Type;
        var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;

        var coerced = CoerceKey(key, underlying, keyField.Name);
        Expression constant = Expression.Constant(coerced, underlying);
        if (constant.Type != memberType)
        {
            constant = Expression.Convert(constant, memberType);
        }

        var body = Expression.Equal(member, constant);
        return Expression.Lambda<Func<TRow, bool>>(body, parameter);
    }

    private static object CoerceKey(object key, Type underlying, string fieldName)
    {
        if (underlying.IsInstanceOfType(key))
        {
            return key;
        }

        try
        {
            if (underlying.IsEnum)
            {
                return key is string enumText
                    ? Enum.Parse(underlying, enumText, ignoreCase: true)
                    : Enum.ToObject(underlying, key);
            }

            if (underlying == typeof(Guid))
            {
                return key is string guidText ? Guid.Parse(guidText) : key;
            }

            return Convert.ChangeType(key, underlying, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new ArgumentException(
                $"The key value '{key}' could not be converted to type '{underlying.Name}' for field '{fieldName}'.",
                nameof(key),
                ex);
        }
    }

    private static NotSupportedException WriteNotSupported(string operation) =>
        new($"{operation} is not implemented in the Pilar 1 list-focused EF executor. Write execution " +
            "(compiled TCrud → TEntity mapping, concurrency tokens, SaveChanges) is a task-9 follow-up; " +
            "see docs/spec/01-view.md §7/§9.");

    private static MethodInfo GetQueryableOrdering(string name) =>
        typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name
                && m.GetParameters().Length == 2
                && m.GetGenericArguments().Length == 2);
}
