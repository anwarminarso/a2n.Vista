using System.Linq.Expressions;
using System.Reflection;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Authoring;

/// <summary>
/// Default <see cref="IViewBuilder{TQuery}"/> implementation for the class-per-view ("Gaya B")
/// read-only authoring path. It accumulates the projection, per-field overrides, masks, and limits,
/// then materializes a <see cref="ViewMetadata"/> via <see cref="Build"/>.
/// Authoritative shape: docs/spec/01-view.md §5.2.
/// </summary>
/// <typeparam name="TQuery">The projected (read) row type sent to clients.</typeparam>
/// <remarks>
/// <para>
/// The field set is derived from the projection passed to <c>From</c>/<c>FromQuery</c>: it must be an
/// object initializer (<c>new TQuery { ... }</c>) or a named-member constructor projection (anonymous
/// type or record). Each projected field defaults to filterable + sortable, string fields are also
/// searchable, and a per-type operator whitelist is derived (mirroring <see cref="FieldBuilder{TProp}"/>).
/// Per-field overrides supplied through <see cref="Field{TProp}"/> win.
/// </para>
/// <para>
/// The class is internal and not designed for concurrent use; one instance is created per
/// <see cref="View{TQuery}"/> build.
/// </para>
/// </remarks>
internal class ViewBuilder<TQuery> : IViewBuilder<TQuery>
    where TQuery : class
{
    private readonly Dictionary<string, IFieldBuilderState> _fieldOverrides = new(StringComparer.Ordinal);
    private readonly HashSet<string> _maskedFields = new(StringComparer.Ordinal);
    private readonly List<object> _rowFilterFactories = [];
    private readonly List<object> _projectedRowFilterFactories = [];
    private readonly List<object> _maskFactories = [];

    private string? _viewName;
    private int? _maxPageSize;
    private int? _maxExportRows;
    private Type? _sourceType;
    private LambdaExpression? _projection;
    private object? _sourceFactory;

    /// <summary>The configured view name, or <see langword="null"/> when <c>Named</c> was not called.</summary>
    internal string? ViewName => _viewName;

    /// <summary>The source entity type captured from <c>From</c>/<c>FromQuery</c>, for the executor (D11).</summary>
    internal Type? SourceType => _sourceType;

    /// <summary>The captured projection expression, for the executor/source generator.</summary>
    internal LambdaExpression? Projection => _projection;

    /// <summary>The optional source-query factory captured from <c>FromQuery</c>; <see langword="null"/> for <c>From</c>.</summary>
    internal object? SourceFactory => _sourceFactory;

    /// <summary>The accumulated pre-projection row-filter factories (server-trusted, D28).</summary>
    internal IReadOnlyList<object> RowFilterFactories => _rowFilterFactories;

    /// <summary>The accumulated post-projection row-filter factories (D28).</summary>
    internal IReadOnlyList<object> ProjectedRowFilterFactories => _projectedRowFilterFactories;

    /// <summary>The accumulated field-mask factories (D29).</summary>
    internal IReadOnlyList<object> MaskFactories => _maskFactories;

    /// <inheritdoc />
    public IViewBuilder<TQuery> Named(string viewName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);
        _viewName = viewName;
        return this;
    }

    /// <inheritdoc />
    public IViewBuilder<TQuery> From<TSource>(Expression<Func<TSource, TQuery>> projection)
        where TSource : class
    {
        ArgumentNullException.ThrowIfNull(projection);
        EnsureSourceNotConfigured();
        _sourceType = typeof(TSource);
        _projection = projection;
        return this;
    }

    /// <inheritdoc />
    public IViewBuilder<TQuery> FromQuery<TSource>(
        Func<IServiceProvider, IQueryable<TSource>> source,
        Expression<Func<TSource, TQuery>> projection)
        where TSource : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(projection);
        EnsureSourceNotConfigured();
        _sourceType = typeof(TSource);
        _projection = projection;
        _sourceFactory = source;
        return this;
    }

    /// <inheritdoc />
    public IViewBuilder<TQuery> Field<TProp>(
        Expression<Func<TQuery, TProp>> field,
        Action<IFieldBuilder<TProp>> configure)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(configure);

        var name = GetMemberName(field);
        var builder = new FieldBuilder<TProp>();
        configure(builder);
        _fieldOverrides[name] = builder;
        return this;
    }

    /// <inheritdoc />
    public IViewBuilder<TQuery> MaxPageSize(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        _maxPageSize = rows;
        return this;
    }

    /// <inheritdoc />
    public IViewBuilder<TQuery> MaxExportRows(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        _maxExportRows = rows;
        return this;
    }

    /// <inheritdoc />
    public IViewBuilder<TQuery> WithRowFilter<TSource>(
        Func<IServiceProvider, Expression<Func<TSource, bool>>> filterFactory)
        where TSource : class
    {
        ArgumentNullException.ThrowIfNull(filterFactory);
        _rowFilterFactories.Add(filterFactory);
        return this;
    }

    /// <inheritdoc />
    public IViewBuilder<TQuery> WithProjectedRowFilter(
        Func<IServiceProvider, Expression<Func<TQuery, bool>>> filterFactory)
    {
        ArgumentNullException.ThrowIfNull(filterFactory);
        _projectedRowFilterFactories.Add(filterFactory);
        return this;
    }

    /// <inheritdoc />
    public IViewBuilder<TQuery> MaskField<TProp>(
        Expression<Func<TQuery, TProp>> field,
        Func<IServiceProvider, bool> shouldMask,
        Func<TProp, TProp> masker)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(shouldMask);
        ArgumentNullException.ThrowIfNull(masker);

        var name = GetMemberName(field);
        _maskedFields.Add(name);
        _maskFactories.Add(masker);
        return this;
    }

    IViewBuilderCore IViewBuilderCore.Named(string viewName) => Named(viewName);

    IViewBuilderCore IViewBuilderCore.MaxPageSize(int rows) => MaxPageSize(rows);

    IViewBuilderCore IViewBuilderCore.MaxExportRows(int rows) => MaxExportRows(rows);

    /// <summary>
    /// Materializes the accumulated authoring state into an immutable <see cref="ViewMetadata"/>,
    /// running all build-time validation (Requirements R2.2, R2.3, R3.2, R4.3, R4.4).
    /// </summary>
    /// <param name="routeRoot">Optional global route root; when set the route is <c>{root}/{viewName}</c>.</param>
    /// <returns>The built metadata.</returns>
    /// <exception cref="InvalidOperationException">Authoring is incomplete or inconsistent.</exception>
    internal ViewMetadata Build(string? routeRoot)
    {
        if (string.IsNullOrWhiteSpace(_viewName))
        {
            throw new InvalidOperationException(
                "A view name is required; call Named(\"...\") in Configure.");
        }

        if (_projection is null)
        {
            throw new InvalidOperationException(
                $"View '{_viewName}' has no source projection; call From<TSource>(...) or " +
                "FromQuery<TSource>(...) in Configure (R2.2).");
        }

        var projectedFields = ExtractProjectedFields(_viewName, _projection);
        var projectedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, _) in projectedFields)
        {
            projectedNames.Add(name);
        }

        EnsureConfiguredFieldsAreProjected(_viewName, _fieldOverrides.Keys, projectedNames, "configured");
        EnsureConfiguredFieldsAreProjected(_viewName, _maskedFields, projectedNames, "masked");

        var fields = new List<FieldMetadata>(projectedFields.Count);
        var hasPrimaryKey = false;

        foreach (var (name, type) in projectedFields)
        {
            FieldMetadata field;
            if (_fieldOverrides.TryGetValue(name, out var state))
            {
                field = state.Build(name);
                hasPrimaryKey |= state.IsPrimaryKey;
            }
            else
            {
                field = FieldMetadata.Create(
                    name: name,
                    clrType: type,
                    isSearchable: IsStringType(type),
                    allowedOperators: DefaultOperatorsFor(type));
            }

            if (_maskedFields.Contains(name))
            {
                field = field with { IsMaskable = true };
            }

            fields.Add(field);
        }

        ValidateWriteFacet(_viewName, hasPrimaryKey);

        var limits = new HardLimits(
            _maxPageSize ?? HardLimits.DefaultMaxPageSize,
            _maxExportRows ?? HardLimits.DefaultMaxExportRows);

        return new ViewMetadata(
            Name: _viewName,
            Route: ComposeRoute(routeRoot, _viewName),
            QueryType: typeof(TQuery),
            CrudType: GetCrudType(),
            CrudEntityType: GetCrudEntityType(),
            Fields: fields,
            Authorization: null,
            Limits: limits,
            IsReadOnly: IsReadOnlyView());
    }

    /// <summary>The write contract type, or <see langword="null"/> for a read-only view.</summary>
    private protected virtual Type? GetCrudType() => null;

    /// <summary>The write target entity type, or <see langword="null"/> for a read-only view.</summary>
    private protected virtual Type? GetCrudEntityType() => null;

    /// <summary>Whether the view exposes only read facets. <see langword="true"/> for the read-only builder.</summary>
    private protected virtual bool IsReadOnlyView() => true;

    /// <summary>
    /// Validates the write facet. The read-only builder has none, so this is a no-op; the write-capable
    /// builder overrides it to enforce R3.2 and R4.4.
    /// </summary>
    /// <param name="viewName">The view name, for diagnostics.</param>
    /// <param name="hasPrimaryKey">Whether a projected field was marked as the primary key.</param>
    private protected virtual void ValidateWriteFacet(string viewName, bool hasPrimaryKey)
    {
        // Read-only views require no primary key (they expose only a List facet; Detail is an optional
        // by-PK fallback). The write-capable builder overrides this to enforce the facet invariants.
    }

    private void EnsureSourceNotConfigured()
    {
        if (_projection is not null)
        {
            throw new InvalidOperationException(
                "The view source is already configured; call From/FromQuery exactly once.");
        }
    }

    private static void EnsureConfiguredFieldsAreProjected(
        string viewName,
        IEnumerable<string> names,
        HashSet<string> projectedNames,
        string kind)
    {
        foreach (var name in names)
        {
            if (!projectedNames.Contains(name))
            {
                throw new InvalidOperationException(
                    $"View '{viewName}' {kind} field '{name}', which is not part of the projection. " +
                    "Only projected fields can be configured.");
            }
        }
    }

    /// <summary>
    /// Extracts the projected field set (name and CLR type, in projection order) from the projection
    /// body. Supports object initializers and named-member constructor projections; identity and other
    /// shapes are rejected with a clear message.
    /// </summary>
    private static IReadOnlyList<(string Name, Type Type)> ExtractProjectedFields(
        string viewName,
        LambdaExpression projection)
    {
        var body = Unwrap(projection.Body);

        if (body is MemberInitExpression init)
        {
            var fields = new List<(string, Type)>();
            CollectNewExpressionMembers(init.NewExpression, fields);

            foreach (var binding in init.Bindings)
            {
                if (binding is MemberAssignment assignment)
                {
                    fields.Add((assignment.Member.Name, GetMemberType(assignment.Member)));
                }
            }

            if (fields.Count > 0)
            {
                return fields;
            }
        }
        else if (body is NewExpression newExpression)
        {
            var fields = new List<(string, Type)>();
            CollectNewExpressionMembers(newExpression, fields);
            if (fields.Count > 0)
            {
                return fields;
            }
        }

        throw new NotSupportedException(
            $"View '{viewName}' projection must be an object initializer (new {typeof(TQuery).Name} " +
            "{ ... }) or a named-member constructor/anonymous projection. Identity and other projection " +
            "shapes are not supported in this release.");
    }

    private static void CollectNewExpressionMembers(NewExpression newExpression, List<(string, Type)> fields)
    {
        if (newExpression.Members is null)
        {
            return;
        }

        foreach (var member in newExpression.Members)
        {
            fields.Add((member.Name, GetMemberType(member)));
        }
    }

    private static Type GetMemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => throw new NotSupportedException(
            $"Unsupported projection member '{member.Name}' of kind {member.MemberType}."),
    };

    private static string GetMemberName(LambdaExpression selector)
    {
        var body = Unwrap(selector.Body);
        if (body is MemberExpression member)
        {
            return member.Member.Name;
        }

        throw new ArgumentException(
            "Expected a simple member access expression, for example x => x.Field.", nameof(selector));
    }

    private static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression unary
            && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static string ComposeRoute(string? routeRoot, string viewName) =>
        string.IsNullOrWhiteSpace(routeRoot)
            ? viewName
            : $"{routeRoot.TrimEnd('/')}/{viewName}";

    private static bool IsStringType(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type) == typeof(string);

    /// <summary>
    /// Derives the per-type default operator whitelist for a projected field that has no explicit
    /// <see cref="IFieldBuilder{TProp}.Operators"/> override. Mirrors the logic in
    /// <see cref="FieldBuilder{TProp}"/> so defaulted and overridden fields stay consistent.
    /// </summary>
    private static FilterOperator DefaultOperatorsFor(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
        {
            return FilterOperator.Text | FilterOperator.In;
        }

        if (underlying == typeof(bool))
        {
            return FilterOperator.Equals | FilterOperator.NotEquals | FilterOperator.IsNull;
        }

        if (underlying.IsEnum || underlying == typeof(Guid))
        {
            return FilterOperator.Equals | FilterOperator.NotEquals
                | FilterOperator.In | FilterOperator.IsNull;
        }

        if (IsComparable(underlying))
        {
            return FilterOperator.Equals | FilterOperator.NotEquals
                | FilterOperator.GreaterThan | FilterOperator.GreaterThanOrEqual
                | FilterOperator.LessThan | FilterOperator.LessThanOrEqual
                | FilterOperator.Between | FilterOperator.In | FilterOperator.IsNull;
        }

        return FilterOperator.Equals | FilterOperator.NotEquals | FilterOperator.IsNull;
    }

    private static bool IsComparable(Type type) =>
        type == typeof(sbyte) || type == typeof(byte)
        || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong)
        || type == typeof(float) || type == typeof(double) || type == typeof(decimal)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset)
        || type == typeof(DateOnly) || type == typeof(TimeOnly)
        || type == typeof(TimeSpan);
}
