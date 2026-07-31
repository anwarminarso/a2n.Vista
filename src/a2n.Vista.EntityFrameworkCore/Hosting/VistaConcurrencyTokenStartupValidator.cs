// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace a2n.Vista.EntityFrameworkCore.Hosting;

/// <summary>
/// A startup-time hosted service that verifies every declared optimistic-concurrency token is <b>also</b> a
/// concurrency token in the EF Core model (Decision Log D146). Fails closed at startup when it is not.
/// </summary>
/// <remarks>
/// <para>
/// <c>WithConcurrencyToken(e =&gt; e.Version)</c> is a Vista-level selector with no coupling to the EF model.
/// Vista's own pre-check (load the row, compare the formatted token to <c>If-Match</c>) runs in application
/// code and is therefore <b>not</b> atomic: two concurrent requests can both pass it and both save, losing an
/// update. The only atomic guard is the database's own <c>UPDATE ... WHERE token = @original</c>, and EF Core
/// emits that predicate <em>only</em> when the property is configured <c>IsRowVersion()</c> /
/// <c>IsConcurrencyToken()</c>.
/// </para>
/// <para>
/// Without this validator a view could declare a token, satisfy every Vista-level check, and still have no
/// database-level protection at all — the weakest possible outcome, because the declaration reads as if
/// concurrency were handled. The check is a startup concern (it needs the built model, and it must not run on
/// the request hot path), and it mirrors the shape of the D105 key-derivation hook.
/// </para>
/// <para>
/// Registered by <c>AddVista</c> via <c>TryAddEnumerable</c>, so it is added at most once across repeat
/// <c>AddVista</c> calls. When no <see cref="DbContext"/> can be resolved the check is skipped rather than
/// failing: a host with no context cannot execute a write in the first place.
/// </para>
/// </remarks>
public sealed class VistaConcurrencyTokenStartupValidator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IViewRegistry _viewRegistry;
    private readonly IWriteFacetRegistry _writeFacets;
    private readonly VistaDbContextAccessor _contextAccessor;

    /// <summary>Initializes a new <see cref="VistaConcurrencyTokenStartupValidator"/>.</summary>
    /// <param name="serviceProvider">The root provider, used to open a startup scope and resolve the <see cref="DbContext"/>.</param>
    /// <param name="viewRegistry">The registry whose writable views are inspected.</param>
    /// <param name="writeFacets">The write-facet registry carrying each view's token selector.</param>
    /// <param name="contextAccessor">Records the captured concrete <see cref="DbContext"/> type, if any.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public VistaConcurrencyTokenStartupValidator(
        IServiceProvider serviceProvider,
        IViewRegistry viewRegistry,
        IWriteFacetRegistry writeFacets,
        VistaDbContextAccessor contextAccessor)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(viewRegistry);
        ArgumentNullException.ThrowIfNull(writeFacets);
        ArgumentNullException.ThrowIfNull(contextAccessor);
        _serviceProvider = serviceProvider;
        _viewRegistry = viewRegistry;
        _writeFacets = writeFacets;
        _contextAccessor = contextAccessor;
    }

    /// <summary>
    /// Validates every declared token against the EF model, aborting startup on the first view whose token
    /// member is missing from the model or is not configured as a concurrency token.
    /// </summary>
    /// <param name="cancellationToken">A token tied to host startup (the work is synchronous).</param>
    /// <returns>A completed task when every declared token is model-backed.</returns>
    /// <exception cref="InvalidOperationException">
    /// A declared token is not a concurrency token in the model, or its member cannot be resolved.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        DbContext? dbContext = null;
        IServiceScope? scope = null;

        try
        {
            foreach (var view in _viewRegistry.All)
            {
                if (view.IsReadOnly
                    || !_writeFacets.TryGet(view.Name, out var facet)
                    || facet.ConcurrencyToken is not { } selector)
                {
                    continue;
                }

                var memberName = TryGetMemberName(selector)
                    ?? throw new InvalidOperationException(
                        $"View '{view.Name}' declares a concurrency token whose selector is not a simple member " +
                        "access (expected 'e => e.Version'), so it cannot be matched against the EF model " +
                        "(Decision Log D146).");

                scope ??= _serviceProvider.CreateScope();
                dbContext ??= ResolveDbContext(scope.ServiceProvider);
                if (dbContext is null)
                {
                    // No context is resolvable, so no write can execute either. Nothing to validate against.
                    return Task.CompletedTask;
                }

                var entityType = dbContext.Model.FindEntityType(facet.EntityType);
                var property = entityType?.FindProperty(memberName);

                if (property is null)
                {
                    throw new InvalidOperationException(
                        $"View '{view.Name}' declares '{memberName}' as its concurrency token, but entity " +
                        $"'{facet.EntityType.FullName}' has no such property in the DbContext model " +
                        "(Decision Log D146).");
                }

                if (!property.IsConcurrencyToken)
                {
                    throw new InvalidOperationException(
                        $"View '{view.Name}' declares '{facet.EntityType.Name}.{memberName}' as its " +
                        "optimistic-concurrency token, but that property is not a concurrency token in the " +
                        "DbContext model, so the database performs no atomic check and two concurrent writes " +
                        "can both succeed (a lost update). Configure it with .IsRowVersion() or " +
                        ".IsConcurrencyToken() in OnModelCreating, or remove WithConcurrencyToken(...) from the " +
                        "view (Decision Log D146).");
                }
            }

            return Task.CompletedTask;
        }
        finally
        {
            scope?.Dispose();
        }
    }

    /// <summary>Does nothing; this service holds no resources.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Returns the member name selected by a simple <c>e =&gt; e.Member</c> lambda (unwrapping a
    /// compiler-inserted conversion), or <see langword="null"/> for any other shape.
    /// </summary>
    internal static string? TryGetMemberName(LambdaExpression selector)
    {
        var body = selector.Body;

        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        return body is MemberExpression { Expression: ParameterExpression } member ? member.Member.Name : null;
    }

    private DbContext? ResolveDbContext(IServiceProvider scopedProvider)
        => _contextAccessor.ContextType is null
            ? scopedProvider.GetService<DbContext>()
            : scopedProvider.GetService(_contextAccessor.ContextType) as DbContext;
}
