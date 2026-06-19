using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.EntityFrameworkCore;

/// <summary>
/// Records the concrete <see cref="DbContext"/> type the registered views are authored against, so the
/// request-scoped <see cref="a2n.Vista.Ports.IViewExecutor"/> can resolve <em>that exact</em> context
/// from DI. This closes the gap between <c>AddDbContext&lt;TContext&gt;</c> — which registers only
/// <c>TContext</c>, never the <see cref="DbContext"/> base — and the executor, which is constructed
/// with a <see cref="DbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// The accessor is a startup-populated singleton: <c>RegisterTemplate&lt;TTemplate, TDbContext&gt;</c>
/// records <c>TDbContext</c> here, and the executor factory reads it back per request to resolve the
/// scoped context via <c>IServiceProvider.GetRequiredService(contextType)</c>. When no template
/// captured a context type (for example a test that registers <see cref="DbContext"/> directly, or a
/// Gaya B-only setup), the executor factory falls back to resolving <see cref="DbContext"/> itself.
/// </para>
/// <para>
/// <b>Single-context assumption (Pilar 1).</b> The Northwind sample (Task 11) and Pilar 1 in general
/// assume one application <see cref="DbContext"/>. Capturing two <em>different</em> context types is a
/// composition mistake (the single scoped executor cannot pick one per view), so
/// <see cref="Capture"/> fails fast with a clear message rather than silently using whichever was
/// recorded last. Multi-context support is a deliberate follow-up.
/// </para>
/// </remarks>
public sealed class VistaDbContextAccessor
{
    /// <summary>
    /// The captured concrete <see cref="DbContext"/> type, or <see langword="null"/> when no template
    /// recorded one (the executor then resolves the <see cref="DbContext"/> base type directly).
    /// </summary>
    public Type? ContextType { get; private set; }

    /// <summary>
    /// Records the context type a view template is authored against. Idempotent for the same type;
    /// rejects a conflicting second type (Pilar 1 single-context assumption).
    /// </summary>
    /// <param name="contextType">The concrete <see cref="DbContext"/>-derived type to capture.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contextType"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A different context type was already captured (more than one <see cref="DbContext"/> across the
    /// registered templates is not supported in Pilar 1).
    /// </exception>
    public void Capture(Type contextType)
    {
        ArgumentNullException.ThrowIfNull(contextType);

        if (ContextType is null)
        {
            ContextType = contextType;
            return;
        }

        if (ContextType != contextType)
        {
            throw new InvalidOperationException(
                $"Vista templates were registered against more than one DbContext type " +
                $"('{ContextType}' and '{contextType}'). Pilar 1 supports a single application DbContext per " +
                "AddVista call; register the templates of each context with their own composition root.");
        }
    }
}
