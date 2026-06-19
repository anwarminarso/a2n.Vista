using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Metadata;

namespace a2n.Vista.Ports;

/// <summary>
/// Registration and resolution surface for Views. The registry is the single source of truth for
/// "which views exist": a request only resolves to a view when that view was registered explicitly
/// (Requirement R1.2). There is no auto-expose of <c>DbSet</c>s — an unregistered name resolves to
/// nothing, which the AspNetCore layer maps to HTTP 404 (R1.1, Decision Log D2).
/// Authoritative shape: docs/spec/01-view.md §5.3 and §5.5.
/// </summary>
/// <remarks>
/// <para>
/// <b>Metadata-keyed.</b> Both authoring styles — Gaya A (<c>ViewTemplate&lt;TDbContext&gt;</c>) and
/// Gaya B (<c>View&lt;TQuery&gt;</c>) — ultimately produce a <see cref="ViewMetadata"/>. The registry
/// stores and returns <see cref="ViewMetadata"/> keyed by <see cref="ViewMetadata.Name"/>, so it is
/// independent of how a view was authored. The authoring builders are introduced in later tasks
/// (Tasks 6 and 7); they (and the DI layer, Task 9.4) call into the entry points defined here once a
/// <see cref="ViewMetadata"/> has been produced.
/// </para>
/// <para>
/// <b>Duplicate names fail (R1.3).</b> Registering two views under the same name is a startup error:
/// <see cref="Add(ViewMetadata)"/> throws so the application fails fast rather than silently shadowing
/// a view.
/// </para>
/// <para>
/// <b>Resolution returns null on miss (R1.1).</b> <see cref="Get(string)"/> returns
/// <see langword="null"/> for an unknown name instead of throwing. This keeps the 404 mapping at the
/// endpoint a simple null check and avoids using exceptions for ordinary "not found" control flow.
/// This intentionally refines the authoritative spec sketch (§5.3), which shows a non-nullable
/// <c>Get</c>; the nullable form is the cleaner contract for the endpoint described by R1.1.
/// </para>
/// <para>
/// <b>AOT hygiene (R11.4, §9).</b> The reflection-based registration entry,
/// <see cref="Register{TView}"/>, is marked <see cref="RequiresUnreferencedCodeAttribute"/> because
/// it introspects the view type at runtime to build its <see cref="ViewMetadata"/>. The AOT-clean
/// route is the source generator (Pilar 3), which emits explicit <see cref="Add(ViewMetadata)"/>
/// calls. The type-based and assembly-scan overloads from the full spec (§5.3) — and the
/// <c>RegisterTemplate&lt;T&gt;</c> DI sugar (§5.5) — land with the reflection authoring path and DI
/// wiring (Task 9.4); they are not part of this Core-only surface.
/// </para>
/// </remarks>
public interface IViewRegistry
{
    /// <summary>
    /// Adds an already-built <see cref="ViewMetadata"/> to the registry. This is the concrete metadata
    /// sink that authoring (Gaya A/B) and the source generator call once a view's metadata has been
    /// produced.
    /// </summary>
    /// <param name="view">The view metadata to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A view with the same <see cref="ViewMetadata.Name"/> is already registered (R1.3). Comparison is
    /// ordinal and case-sensitive.
    /// </exception>
    void Add(ViewMetadata view);

    /// <summary>
    /// Registers a class-per-view (Gaya B) view type by introspecting it at runtime to build its
    /// <see cref="ViewMetadata"/>, then adding it via <see cref="Add(ViewMetadata)"/>.
    /// </summary>
    /// <typeparam name="TView">The view type to register.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// A view with the same name is already registered (R1.3).
    /// </exception>
    /// <remarks>
    /// This is the reflection registration path. It is AOT-unsafe (hence
    /// <see cref="RequiresUnreferencedCodeAttribute"/>); the source generator emits the equivalent
    /// <see cref="Add(ViewMetadata)"/> call on the AOT-clean route.
    /// </remarks>
    [RequiresUnreferencedCode("View registration introspects the view type at runtime to build its metadata; use the source generator path for AOT.")]
    void Register<TView>() where TView : class;

    /// <summary>
    /// Resolves the metadata of a registered view by name.
    /// </summary>
    /// <param name="name">The view name. Compared ordinally and case-sensitively.</param>
    /// <returns>
    /// The matching <see cref="ViewMetadata"/>, or <see langword="null"/> when no view is registered
    /// under <paramref name="name"/> (the endpoint maps a null result to HTTP 404, R1.1).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    ViewMetadata? Get(string name);

    /// <summary>
    /// All registered views. The collection reflects only views registered explicitly (R1.2); there is
    /// no implicit/auto-exposed entry.
    /// </summary>
    IReadOnlyCollection<ViewMetadata> All { get; }
}
