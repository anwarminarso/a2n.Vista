// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// Bridges a source-generated <see cref="ICompiledViewExecutionPlan"/> into the existing
/// <see cref="IViewExecutionPlanRegistry"/>, which stores <see cref="IViewExecutionPlan"/>
/// (source-generator Phase 2 / Decision Log D118). The adapter implements <b>both</b> interfaces and
/// delegates every member to the wrapped compiled plan: <see cref="IViewExecutionPlan"/> lets it be
/// stored and lets <see cref="ViewName"/>/<see cref="RowType"/> satisfy the registry; the
/// <see cref="ICompiledViewExecutionPlan"/> facet lets the executor detect it (<c>is</c>) and route
/// through the AOT-clean compiled read path without ever calling the RUC
/// <see cref="IViewExecutionPlan.CreateScopedQueryable"/> member.
/// </summary>
internal sealed class CompiledExecutionPlanAdapter : IViewExecutionPlan, ICompiledViewExecutionPlan
{
    private readonly ICompiledViewExecutionPlan _inner;

    public CompiledExecutionPlanAdapter(ICompiledViewExecutionPlan inner)
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
    public IReadOnlyList<MaskAccessor> MaskAccessors => _inner.MaskAccessors;

    /// <summary>
    /// Satisfies both interfaces' identically-shaped member. The compiled facet is non-RUC; the
    /// executor reaches this only via the <see cref="ICompiledViewExecutionPlan"/> reference, so the
    /// compiled read path stays warning-free. The body delegates to the generated, AOT-clean build.
    /// </summary>
    [SuppressMessage("Trimming", "IL2046:RequiresUnreferencedCode mismatch on override/interface",
        Justification = "The compiled plan builds the queryable from generated expression nodes; it is AOT-clean and the executor only calls it via the non-RUC ICompiledViewExecutionPlan facet.")]
    public IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope)
        => _inner.CreateScopedQueryable(dbContext, services, scope);

    /// <inheritdoc cref="ICompiledViewExecutionPlan.TryGetMemberAccess" />
    public bool TryGetMemberAccess(string fieldName, out LambdaExpression accessor)
        => _inner.TryGetMemberAccess(fieldName, out accessor);

    /// <inheritdoc cref="ICompiledViewExecutionPlan.ApplyPrimarySort" />
    public IOrderedQueryable ApplyPrimarySort(IQueryable source, string fieldName, bool descending)
        => _inner.ApplyPrimarySort(source, fieldName, descending);

    /// <inheritdoc cref="ICompiledViewExecutionPlan.ApplyThenSort" />
    public IOrderedQueryable ApplyThenSort(IOrderedQueryable source, string fieldName, bool descending)
        => _inner.ApplyThenSort(source, fieldName, descending);
}
