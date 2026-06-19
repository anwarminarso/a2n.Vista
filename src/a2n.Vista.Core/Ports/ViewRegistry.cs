using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Metadata;

namespace a2n.Vista.Ports;

/// <summary>
/// Default in-memory <see cref="IViewRegistry"/>. Stores <see cref="ViewMetadata"/> in a dictionary
/// keyed by view name (ordinal, case-sensitive) and rejects duplicate names at registration time
/// (Requirement R1.3). This is pure Core logic with no EF/HTTP dependency, which keeps DI wiring in
/// later layers a single line. Authoritative shape: docs/spec/01-view.md §5.3.
/// </summary>
/// <remarks>
/// <para>
/// The registry is built once at startup (composition root) and then read concurrently while serving
/// requests. It is therefore <b>not</b> designed for registration after startup: <see cref="Add"/>
/// and <see cref="Register{TView}"/> are not synchronized for concurrent writers. Reads via
/// <see cref="Get"/> and <see cref="All"/> over a fully-populated registry are safe.
/// </para>
/// </remarks>
public sealed class ViewRegistry : IViewRegistry
{
    private readonly Dictionary<string, ViewMetadata> _views = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Add(ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (_views.ContainsKey(view.Name))
        {
            throw new InvalidOperationException(
                $"A view named '{view.Name}' is already registered. View names must be unique.");
        }

        _views.Add(view.Name, view);
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("View registration introspects the view type at runtime to build its metadata; use the source generator path for AOT.")]
    public void Register<TView>() where TView : class
    {
        // The reflection-based introspection that turns TView into ViewMetadata depends on the
        // authoring builders (Gaya A/B), which are implemented in later tasks (Tasks 6 and 7). Until
        // that path exists, callers register views through Add(ViewMetadata) — the sink the authoring
        // layer and source generator emit into. This member is part of the contract now so consumers
        // and the DI layer (Task 9.4) can bind to a stable surface.
        throw new NotSupportedException(
            "Register<TView>() requires the reflection authoring path, which is not implemented yet. " +
            "Register views by adding their built metadata via Add(ViewMetadata) until the authoring " +
            "builders (Tasks 6/7) or the source generator (Pilar 3) are available.");
    }

    /// <inheritdoc />
    public ViewMetadata? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _views.TryGetValue(name, out var view) ? view : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ViewMetadata> All => _views.Values;
}
