using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using a2n.Vista.Metadata;

namespace a2n.Vista.Authoring;

/// <summary>
/// Internal contract that lets <see cref="ViewTemplateBuilder{TDbContext}"/> collect a finished
/// <see cref="TemplateViewDefinition{TDbContext}"/> from a read-view builder without knowing its
/// <c>TRow</c> type argument.
/// </summary>
/// <typeparam name="TDbContext">The template's data-source type.</typeparam>
internal interface ITemplateViewSource<TDbContext>
    where TDbContext : class
{
    /// <summary>Materializes the authored view into its definition (metadata + captured state).</summary>
    [RequiresUnreferencedCode(ReadViewBuilder.ReflectionMessage)]
    TemplateViewDefinition<TDbContext> Build();
}

/// <summary>Shared constants for the Gaya A read-view builder.</summary>
internal static class ReadViewBuilder
{
    /// <summary>Reason text for the reflection-based field enumeration used by Gaya A authoring.</summary>
    internal const string ReflectionMessage =
        "Gaya A authoring enumerates the (possibly anonymous) projection row type via reflection to derive field metadata; use the source generator path for AOT.";
}

/// <summary>
/// Default <see cref="IReadViewBuilder{TRow}"/> implementation for the Gaya A (central template) style.
/// Captures the projection and per-field/limit/CRUD configuration, then produces an equivalent
/// <see cref="ViewMetadata"/> with the typing invariant applied (anonymous projection ⇒ read-only
/// unless a typed Write facet is attached). Authoritative shape: docs/spec/01-view.md §5.5.
/// </summary>
/// <typeparam name="TDbContext">The template's data-source type.</typeparam>
/// <typeparam name="TRow">The projected (read) row type, possibly anonymous.</typeparam>
internal sealed class ReadViewBuilder<TDbContext, TRow> : IReadViewBuilder<TRow>, ITemplateViewSource<TDbContext>
    where TDbContext : class
    where TRow : class
{
    private readonly string _name;
    private readonly string _routeRoot;
    private readonly Func<TDbContext, IServiceProvider, IQueryable<TRow>> _query;

    // Per-field overrides keyed by projected member name (ordinal). Stored as the non-generic
    // accumulation surface so this builder can read PK/format and materialize FieldMetadata without
    // tracking each field's TProp.
    private readonly Dictionary<string, IFieldBuilderState> _fieldOverrides = new(StringComparer.Ordinal);
    private readonly List<TemplateRowFilter> _rowFilters = [];

    private int? _maxPageSize;
    private int? _maxExportRows;
    private ICrudFacetDefinitionSource? _crud;

    internal ReadViewBuilder(string name, string routeRoot, Func<TDbContext, IServiceProvider, IQueryable<TRow>> query)
    {
        _name = name;
        _routeRoot = routeRoot;
        _query = query;
    }

    /// <inheritdoc />
    public IReadViewBuilder<TRow> MaxPageSize(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        _maxPageSize = rows;
        return this;
    }

    /// <inheritdoc />
    public IReadViewBuilder<TRow> MaxExportRows(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        _maxExportRows = rows;
        return this;
    }

    /// <inheritdoc />
    public IReadViewBuilder<TRow> Field<TProp>(
        Expression<Func<TRow, TProp>> field,
        Action<IFieldBuilder<TProp>> configure)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(configure);

        var name = CentralTemplateExpressions.GetMemberName(field);
        var fieldBuilder = new FieldBuilder<TProp>();
        configure(fieldBuilder);
        _fieldOverrides[name] = fieldBuilder;
        return this;
    }

    /// <inheritdoc />
    public IReadViewBuilder<TRow> WithRowFilter<TSource>(
        Func<IServiceProvider, Expression<Func<TSource, bool>>> filterFactory)
        where TSource : class
    {
        ArgumentNullException.ThrowIfNull(filterFactory);
        _rowFilters.Add(new TemplateRowFilter(typeof(TSource), services => filterFactory(services)));
        return this;
    }

    /// <inheritdoc />
    public ICrudFacetBuilder<TCrud, TEntity> WithCrud<TCrud, TEntity>()
        where TCrud : class
        where TEntity : class
    {
        if (_crud is not null)
        {
            throw new InvalidOperationException(
                $"View '{_name}' already declares a CRUD facet. WithCrud may be called only once per view.");
        }

        var crudBuilder = new CrudFacetBuilder<TCrud, TEntity>();
        _crud = crudBuilder;
        return crudBuilder;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode(ReadViewBuilder.ReflectionMessage)]
    public TemplateViewDefinition<TDbContext> Build()
    {
        var crudDefinition = _crud?.Build();

        // The typing invariant (Requirements R3.1, R3.3, Decision Log D38): a view authored from an
        // anonymous projection is read-only unless a typed Write facet (WithCrud) is attached. When a
        // facet is present the view is writable and the metadata carries the CRUD types.
        var isReadOnly = crudDefinition is null;

        var metadata = new ViewMetadata(
            Name: _name,
            Route: CombineRoute(_routeRoot, _name),
            QueryType: typeof(TRow),
            CrudType: crudDefinition?.CrudType,
            CrudEntityType: crudDefinition?.EntityType,
            Fields: BuildFields(),
            Authorization: null,
            Limits: new HardLimits(
                _maxPageSize ?? HardLimits.DefaultMaxPageSize,
                _maxExportRows ?? HardLimits.DefaultMaxExportRows),
            IsReadOnly: isReadOnly);

        // Erase TRow to the non-generic IQueryable so the EF layer can consume the projection without
        // naming an anonymous type (Decision Log D11). IQueryable<TRow> is an IQueryable.
        Func<TDbContext, IServiceProvider, IQueryable> queryFactory = (db, services) => _query(db, services);

        return new TemplateViewDefinition<TDbContext>(metadata, queryFactory, _rowFilters, crudDefinition);
    }

    /// <summary>
    /// Builds the projected field set: every readable public instance property of
    /// <typeparamref name="TRow"/> in projection order, applying any per-field override and otherwise
    /// using the default-allow defaults (Decision Log D42).
    /// </summary>
    [RequiresUnreferencedCode(ReadViewBuilder.ReflectionMessage)]
    private IReadOnlyList<FieldMetadata> BuildFields()
    {
        var properties = typeof(TRow).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var fields = new List<FieldMetadata>(properties.Length);

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            fields.Add(_fieldOverrides.TryGetValue(property.Name, out var overridden)
                ? overridden.Build(property.Name)
                : BuildDefaultField(property));
        }

        return fields;
    }

    /// <summary>
    /// Builds the default <see cref="FieldMetadata"/> for a projected property that the author did not
    /// customize, reusing <see cref="FieldBuilder{TProp}"/> so the default-allow rules (including the
    /// per-type operator whitelist) have a single source of truth.
    /// </summary>
    [RequiresUnreferencedCode(ReadViewBuilder.ReflectionMessage)]
    private static FieldMetadata BuildDefaultField(PropertyInfo property)
    {
        var builderType = typeof(FieldBuilder<>).MakeGenericType(property.PropertyType);
        var builder = Activator.CreateInstance(builderType)!;
        var buildMethod = builderType.GetMethod(nameof(FieldBuilder<object>.Build), [typeof(string)])!;
        return (FieldMetadata)buildMethod.Invoke(builder, [property.Name])!;
    }

    private static string CombineRoute(string routeRoot, string name) =>
        $"{routeRoot.TrimEnd('/')}/{name}";
}
