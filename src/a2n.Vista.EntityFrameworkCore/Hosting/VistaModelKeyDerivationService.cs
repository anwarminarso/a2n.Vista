// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace a2n.Vista.EntityFrameworkCore.Hosting;

/// <summary>
/// A startup-time hosted service that completes <see cref="ViewMetadata.KeyFields"/> for single-source
/// <b>executable</b> views that declared no key, by deriving the key from the application
/// <c>DbContext.Model</c> (Decision Log D105 / M11, Requirement R6). It runs once at host start, never on
/// the request hot path (R6.7/R6.8).
/// </summary>
/// <remarks>
/// <para>
/// Only views backed by a source-generated <see cref="ICompiledViewExecutionPlan"/> can reach this hook
/// without a declared key: <c>Register&lt;TView&gt;()</c> defers the D106 key fail-fast for them so the
/// model can supply the key. Metadata-only views and hand-built plans still require a declared key at
/// registration, so they are already keyed when observed here.
/// </para>
/// <para>
/// For each such key-less view:
/// <list type="bullet">
/// <item><b>Single-source</b> (R6.1): read the primary key of its <see cref="ICompiledViewExecutionPlan.SourceType"/>
/// from <c>DbContext.Model</c> and complete <see cref="ViewMetadata.KeyFields"/> in the model's declared
/// key-column order, composite keys included (R6.2).</item>
/// <item><b>Single-source with no model primary key</b> (R6.4): fail closed, aborting startup with a
/// message naming the view and the source entity.</item>
/// <item><b>Not single-source</b> (R6.5/R6.6): model derivation is not attempted; a non-single-source
/// view that declared no key fails closed, aborting startup with a message naming the view.</item>
/// </list>
/// A view that already declares a key is never touched (R6.3).
/// </para>
/// <para>
/// Registered by <c>AddVista</c> via <c>TryAddEnumerable</c> as an <see cref="IHostedService"/>, so it is
/// added at most once regardless of repeat <c>AddVista</c> calls (R6.7). It resolves the application
/// <see cref="DbContext"/> from a startup scope using the captured concrete context type — the same rule
/// <see cref="VistaDialectStartupValidator"/> uses.
/// </para>
/// </remarks>
public sealed class VistaModelKeyDerivationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IViewRegistry _viewRegistry;
    private readonly IViewExecutionPlanRegistry _planRegistry;
    private readonly VistaDbContextAccessor _contextAccessor;

    /// <summary>
    /// Initializes a new <see cref="VistaModelKeyDerivationService"/>.
    /// </summary>
    /// <param name="serviceProvider">The root provider, used to open a startup scope and resolve the <see cref="DbContext"/>.</param>
    /// <param name="viewRegistry">The metadata registry whose key-less single-source views are completed.</param>
    /// <param name="planRegistry">The execution-plan registry, used to read each view's compiled plan facet.</param>
    /// <param name="contextAccessor">Records the captured concrete <see cref="DbContext"/> type, if any.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public VistaModelKeyDerivationService(
        IServiceProvider serviceProvider,
        IViewRegistry viewRegistry,
        IViewExecutionPlanRegistry planRegistry,
        VistaDbContextAccessor contextAccessor)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(viewRegistry);
        ArgumentNullException.ThrowIfNull(planRegistry);
        ArgumentNullException.ThrowIfNull(contextAccessor);
        _serviceProvider = serviceProvider;
        _viewRegistry = viewRegistry;
        _planRegistry = planRegistry;
        _contextAccessor = contextAccessor;
    }

    /// <summary>
    /// Completes the key of every single-source executable view that declared none, deriving it from the
    /// EF model in declared key-column order (R6.1/R6.2), and fails closed for a key-less source (R6.4) or
    /// a non-single-source key-less view (R6.6). Runs at most once per application start (R6.7).
    /// </summary>
    /// <param name="cancellationToken">A token tied to host startup (the work is synchronous).</param>
    /// <returns>A completed task when every eligible view has a resolvable key.</returns>
    /// <exception cref="InvalidOperationException">
    /// A single-source view's source entity has no primary key in the model (R6.4); a non-single-source
    /// view declared no key (R6.6); or the application <see cref="DbContext"/> cannot be resolved while a
    /// view still needs a model-derived key.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Find the views that reached startup without a key. Only generated (compiled) plans can do so;
        // the rest are already keyed. Reads are over the fully-populated startup registries.
        var pending = new List<(ViewMetadata View, ICompiledViewExecutionPlan Plan)>();
        foreach (var view in _viewRegistry.All)
        {
            if (view.KeyFields.Count != 0)
            {
                // Declared (or already-derived) key: never override (R6.3).
                continue;
            }

            if (_planRegistry.Get(view.Name) is not ICompiledViewExecutionPlan compiled)
            {
                // No compiled plan but no key either — registration should have failed fast for these
                // (Decision Log D106). Guard defensively so a key-less metadata-only view cannot slip
                // through to request time.
                throw new InvalidOperationException(
                    $"View '{view.Name}' has no key fields and no generated execution plan from which to " +
                    "derive one. Declare the key with .PrimaryKey() or .Key(...), or reference the source " +
                    "generator so a single-source key can be derived from the model (Decision Log D105/D106).");
            }

            pending.Add((view, compiled));
        }

        if (pending.Count == 0)
        {
            return Task.CompletedTask;
        }

        // A non-single-source view without a declared key cannot be model-derived (R6.5) and is a hard
        // misconfiguration (R6.6) — fail closed before touching the DbContext, naming the view.
        var multiSource = pending.FirstOrDefault(p => !p.Plan.IsSingleSource).View;
        if (multiSource is not null)
        {
            throw new InvalidOperationException(
                $"View '{multiSource.Name}' projects from more than one source entity, so its key cannot " +
                "be derived from the EF model (Decision Log D105). A multi-source view must declare its " +
                "key explicitly with .PrimaryKey() or .Key(...) (Requirement R6.6).");
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = ResolveDbContext(scope.ServiceProvider);
        if (dbContext is null)
        {
            // Single-source views remain that need a model key, but no DbContext is resolvable to read it.
            throw new InvalidOperationException(
                $"View '{pending[0].View.Name}' relies on single-source primary-key derivation from the " +
                "application DbContext (Decision Log D105), but no DbContext could be resolved at startup. " +
                "Register the DbContext used by your views, or declare the view key explicitly.");
        }

        foreach (var (view, plan) in pending)
        {
            var entityType = dbContext.Model.FindEntityType(plan.SourceType);
            var primaryKey = entityType?.FindPrimaryKey();
            if (primaryKey is null)
            {
                // R6.4: a single-source view whose source has no model primary key fails closed at
                // startup, naming the view and the source entity.
                throw new InvalidOperationException(
                    $"View '{view.Name}' is single-source over entity '{plan.SourceType.FullName}', but that " +
                    "entity has no primary key in the DbContext model, so a key cannot be derived " +
                    "(Decision Log D105). Configure a primary key on the entity, or declare the view key " +
                    "explicitly with .PrimaryKey() or .Key(...) (Requirement R6.4).");
            }

            // R6.2: list every key column in the model's declared key order (composite keys included).
            var keyFields = primaryKey.Properties.Select(static p => p.Name).ToArray();
            view.CompleteKeyFields(keyFields);
        }

        return Task.CompletedTask;
    }

    /// <summary>Does nothing; this service holds no resources.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Resolves the application <see cref="DbContext"/> from the startup scope using the captured concrete
    /// context type when known (the same rule the executor and dialect validator use), falling back to the
    /// <see cref="DbContext"/> base registration. Returns <see langword="null"/> when none can be resolved.
    /// </summary>
    private DbContext? ResolveDbContext(IServiceProvider scopedProvider)
        => _contextAccessor.ContextType is null
            ? scopedProvider.GetService<DbContext>()
            : scopedProvider.GetService(_contextAccessor.ContextType) as DbContext;
}
