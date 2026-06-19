using System.Diagnostics.CodeAnalysis;

namespace a2n.Vista.Authoring;

/// <summary>
/// Base class for the Gaya A (central template) authoring style: a developer derives from this type
/// and registers many views in one place by overriding <see cref="Configure"/>. This is the
/// view-first ergonomics carried over from DynData (Decision Log D37). Both authoring styles emit an
/// equivalent <see cref="a2n.Vista.Metadata.ViewMetadata"/>. Authoritative shape: docs/spec/01-view.md §5.5.
/// </summary>
/// <typeparam name="TDbContext">
/// The developer's data-source type that projections are expressed against (the source of the
/// <see cref="IQueryable{T}"/> in <see cref="IViewTemplateBuilder{TDbContext}.AddView{TRow}"/>).
/// </typeparam>
/// <remarks>
/// <para>
/// <b>Type-parameter constraint (intentional deviation from §5.5).</b> The authoritative spec writes
/// <c>where TDbContext : DbContext</c>. Core, however, must not reference EF Core or ASP.NET
/// (Requirement R11.1, Decision Log D48), so this library constrains <typeparamref name="TDbContext"/>
/// to <c>class</c> instead. The real EF <c>DbContext</c> requirement is enforced where it belongs — at
/// the EF composition root (Task 9), which resolves the concrete context and materializes the captured
/// projection (Decision Log D11). Constraining to <c>class</c> here keeps the Core surface EF-free
/// while remaining source-compatible for developers who pass an actual <c>DbContext</c> subclass.
/// </para>
/// <para>
/// <b>AOT hygiene.</b> <see cref="BuildViews"/> enumerates the (often anonymous) projection row types
/// via reflection to derive default field metadata, so it is marked
/// <see cref="RequiresUnreferencedCodeAttribute"/>. For full Native AOT use the class-per-view style
/// (Gaya B) or the source generator (Pilar 3), per the §5.5 AOT note.
/// </para>
/// </remarks>
public abstract class ViewTemplate<TDbContext>
    where TDbContext : class
{
    /// <summary>The default global route root applied when a caller does not supply one (§5.6).</summary>
    public const string DefaultRouteRoot = "/api/views";

    /// <summary>
    /// Registers this template's views against the supplied builder. Implementations call
    /// <see cref="IViewTemplateBuilder{TDbContext}.AddView{TRow}"/> once per view.
    /// </summary>
    /// <param name="views">The registration surface to add views to.</param>
    protected internal abstract void Configure(IViewTemplateBuilder<TDbContext> views);

    /// <summary>
    /// Runs <see cref="Configure"/> against a fresh builder and returns the produced view definitions
    /// (metadata plus the captured projection/row-filter/CRUD state the EF layer consumes). This is the
    /// entry point the DI registration path (<c>RegisterTemplate&lt;T&gt;</c>, Task 9.4) invokes.
    /// </summary>
    /// <param name="routeRoot">
    /// The global route root to prefix view names with (<c>{root}/{viewName}</c>, §5.6). Defaults to
    /// <see cref="DefaultRouteRoot"/>.
    /// </param>
    /// <returns>The view definitions produced by this template, in registration order.</returns>
    /// <exception cref="ArgumentException"><paramref name="routeRoot"/> is <see langword="null"/> or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Two views are registered under the same name, or a view declares a CRUD facet without any
    /// <c>MapWritable</c> mapping (Decision Log D38/D1).
    /// </exception>
    [RequiresUnreferencedCode("Gaya A authoring enumerates the (possibly anonymous) projection row type via reflection to derive field metadata; use the source generator path for AOT.")]
    public IReadOnlyList<TemplateViewDefinition<TDbContext>> BuildViews(string routeRoot = DefaultRouteRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeRoot);

        var builder = new ViewTemplateBuilder<TDbContext>(routeRoot);
        Configure(builder);
        return builder.BuildDefinitions();
    }
}
