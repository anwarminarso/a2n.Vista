using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// Default <see cref="IViewTemplateBuilder{TDbContext}"/> implementation backing
/// <see cref="ViewTemplate{TDbContext}.BuildViews"/>. Collects each <see cref="AddView{TRow}"/> call as
/// a read-view builder, enforces unique view names within the template, and materializes the captured
/// views into <see cref="TemplateViewDefinition{TDbContext}"/> instances. Authoritative shape:
/// docs/spec/01-view.md §5.5.
/// </summary>
/// <typeparam name="TDbContext">The template's data-source type.</typeparam>
internal sealed class ViewTemplateBuilder<TDbContext> : IViewTemplateBuilder<TDbContext>
    where TDbContext : class
{
    private readonly List<ITemplateViewSource<TDbContext>> _views = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadViewBuilder<TRow> AddView<TRow>(
        string name,
        Func<TDbContext, IServiceProvider, IQueryable<TRow>> query)
        where TRow : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(query);

        if (!_names.Add(name))
        {
            // Duplicate names within a template fail fast (Requirement R1.3). Cross-template
            // uniqueness is additionally enforced by the registry (Task 4.3).
            throw new InvalidOperationException(
                $"A view named '{name}' is already registered in this template. View names must be unique.");
        }

        var builder = new ReadViewBuilder<TDbContext, TRow>(name, query);
        _views.Add(builder);
        return builder;
    }

    /// <inheritdoc />
    public IReadViewBuilder<TRow> AddView<TSource, TRow>(
        string name,
        Func<TDbContext, IServiceProvider, IQueryable<TSource>> source,
        Expression<Func<TSource, TRow>> projection)
        where TSource : class
        where TRow : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(projection);

        if (!_names.Add(name))
        {
            throw new InvalidOperationException(
                $"A view named '{name}' is already registered in this template. View names must be unique.");
        }

        var builder = ReadViewBuilder<TDbContext, TRow>.Split(name, source, projection);
        _views.Add(builder);
        return builder;
    }

    /// <summary>
    /// Materializes every registered view into its <see cref="TemplateViewDefinition{TDbContext}"/>, in
    /// registration order.
    /// </summary>
    [RequiresUnreferencedCode(ReadViewBuilder.ReflectionMessage)]
    internal IReadOnlyList<TemplateViewDefinition<TDbContext>> BuildDefinitions()
    {
        var definitions = new List<TemplateViewDefinition<TDbContext>>(_views.Count);
        foreach (var view in _views)
        {
            definitions.Add(view.Build());
        }

        return definitions;
    }
}
