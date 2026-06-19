using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Authoring;

/// <summary>
/// Default <see cref="IFieldBuilder{TProp}"/> implementation. Accumulates per-field authoring
/// state with default-allow semantics (Decision Log D42, D47) and materializes a
/// <see cref="FieldMetadata"/> via <see cref="Build"/>.
/// </summary>
/// <typeparam name="TProp">The CLR type of the projected field.</typeparam>
/// <remarks>
/// Defaults derived from <typeparamref name="TProp"/>:
/// <list type="bullet">
/// <item><description>Filterable / Sortable default <see langword="true"/> for every field.</description></item>
/// <item><description>Searchable defaults <see langword="true"/> only for <see cref="string"/> fields;
/// for any other type the searchable flag is forced off because global search is string-only
/// (R5.3, R5.4, §4.4).</description></item>
/// <item><description>Scopable defaults <see langword="false"/> (opt-in, D47).</description></item>
/// <item><description>Writable defaults <see langword="false"/> (default-deny write, R3.4); write is
/// opted in only via the typed CRUD facet's <c>MapWritable</c>, never here.</description></item>
/// <item><description>Allowed operators default to a coherent per-type whitelist (see
/// <see cref="DefaultOperatorsFor"/>) unless <see cref="Operators"/> is called explicitly.</description></item>
/// </list>
/// </remarks>
public sealed class FieldBuilder<TProp> : IFieldBuilder<TProp>, IFieldBuilderState
{
    private static readonly bool IsStringField =
        (Nullable.GetUnderlyingType(typeof(TProp)) ?? typeof(TProp)) == typeof(string);

    private bool _isPrimaryKey;
    private bool _isHidden;
    private string? _label;
    private string? _format;
    private bool _isFilterable = true;
    private bool _isSortable = true;
    private bool _isSearchable = true;
    private bool _isScopable;
    private FilterOperator _allowedOperators = DefaultOperatorsFor(typeof(TProp));

    /// <inheritdoc />
    public bool IsPrimaryKey => _isPrimaryKey;

    /// <inheritdoc />
    public string? FormatString => _format;

    /// <inheritdoc />
    public IFieldBuilder<TProp> PrimaryKey()
    {
        _isPrimaryKey = true;
        return this;
    }

    /// <inheritdoc />
    public IFieldBuilder<TProp> Hidden()
    {
        _isHidden = true;
        return this;
    }

    /// <inheritdoc />
    public IFieldBuilder<TProp> Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _label = label;
        return this;
    }

    /// <inheritdoc />
    public IFieldBuilder<TProp> Format(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        _format = format;
        return this;
    }

    /// <inheritdoc />
    public IFieldBuilder<TProp> Filterable(bool allowed = true)
    {
        _isFilterable = allowed;
        return this;
    }

    /// <inheritdoc />
    public IFieldBuilder<TProp> Sortable(bool allowed = true)
    {
        _isSortable = allowed;
        return this;
    }

    /// <inheritdoc />
    public IFieldBuilder<TProp> Searchable(bool allowed = true)
    {
        _isSearchable = allowed;
        return this;
    }

    /// <inheritdoc />
    public IFieldBuilder<TProp> Operators(FilterOperator operators)
    {
        // Setting an operator whitelist implies the field is filterable (§5.5);
        // a later explicit Filterable(false) still wins.
        _allowedOperators = operators;
        return this;
    }

    /// <inheritdoc />
    public IFieldBuilder<TProp> Scopable(bool allowed = true)
    {
        _isScopable = allowed;
        return this;
    }

    /// <inheritdoc />
    public FieldMetadata Build(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return FieldMetadata.Create(
            name: name,
            clrType: typeof(TProp),
            label: _label,
            isFilterable: _isFilterable,
            isSortable: _isSortable,
            // Global search is string-only: non-string fields never participate
            // regardless of the searchable flag (R5.3, R5.4, §4.4).
            isSearchable: IsStringField && _isSearchable,
            isScopable: _isScopable,
            isHidden: _isHidden,
            isWritable: false,
            isMaskable: false,
            allowedOperators: _allowedOperators);
    }

    /// <summary>
    /// Derives a coherent default operator whitelist for <paramref name="type"/>.
    /// </summary>
    /// <remarks>
    /// Resolves the open "operator default per type" decision noted in tasks.md:
    /// <list type="bullet">
    /// <item><description><see cref="string"/>: the <see cref="FilterOperator.Text"/> group plus
    /// <see cref="FilterOperator.In"/>.</description></item>
    /// <item><description>numeric and date/time types: equality, the four comparisons,
    /// <see cref="FilterOperator.Between"/>, <see cref="FilterOperator.In"/> and
    /// <see cref="FilterOperator.IsNull"/>.</description></item>
    /// <item><description><see cref="bool"/>: equality and <see cref="FilterOperator.IsNull"/>.</description></item>
    /// <item><description>enums and <see cref="Guid"/>: equality, <see cref="FilterOperator.In"/>
    /// and <see cref="FilterOperator.IsNull"/>.</description></item>
    /// <item><description>any other type: equality and <see cref="FilterOperator.IsNull"/> only.</description></item>
    /// </list>
    /// </remarks>
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

    /// <summary>
    /// Determines whether <paramref name="type"/> supports ordered comparisons
    /// (numeric or date/time), and therefore range/comparison operators by default.
    /// </summary>
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
