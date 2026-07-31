using a2n.Vista.Contracts;

namespace a2n.Vista.Metadata;

/// <summary>
/// Declarative snapshot of a single projected view field, produced by the authoring builders
/// and consumed by the executor, adapters, and code generators.
/// Authoritative shape: docs/spec/01-view.md §5.4.
/// </summary>
/// <param name="Name">The projected field name (typically the projection property name).</param>
/// <param name="Label">
/// Human-friendly display label. Auto-derived from <paramref name="Name"/> via
/// <see cref="LabelHelper.ToTitleCase"/> (for example <c>"ProductName"</c> → <c>"Product Name"</c>)
/// unless overridden during authoring with <c>.Field(..., f =&gt; f.Label(...))</c>.
/// </param>
/// <param name="ClrType">The CLR type of the projected value.</param>
/// <param name="IsFilterable">
/// Whether clients may filter on this field. Defaults to <see langword="true"/> for every
/// projected field (default-allow, Decision Log D42); opt out with <c>.Filterable(false)</c>.
/// </param>
/// <param name="IsSortable">
/// Whether clients may sort by this field. Defaults to <see langword="true"/>;
/// opt out with <c>.Sortable(false)</c>.
/// </param>
/// <param name="IsSearchable">
/// Whether this field participates in global search. Defaults to <see langword="true"/> for
/// string fields only; numeric/date fields are excluded from global search (§5.1).
/// </param>
/// <param name="IsScopable">
/// Whether clients may use this field as a contextual/lookup scope key. Defaults to
/// <see langword="false"/> (opt-in, Decision Log D47).
/// </param>
/// <param name="IsHidden">
/// Whether the field is hidden from transport/display (for example a technical primary key).
/// Defaults to <see langword="false"/>.
/// </param>
/// <param name="IsWritable">
/// Whether the field can be written by clients. Write is default-deny; only fields opted in via
/// <c>MapWritable(...)</c> on a typed CRUD facet are writable (Decision Log D25, §7).
/// </param>
/// <param name="IsMaskable">Whether the field value is masked in read responses.</param>
/// <param name="AllowedOperators">
/// The set of <see cref="FilterOperator"/> values a client may request against this field.
/// </param>
public sealed record FieldMetadata(
    string Name,
    string Label,
    Type ClrType,
    bool IsFilterable,
    bool IsSortable,
    bool IsSearchable,
    bool IsScopable,
    bool IsHidden,
    bool IsWritable,
    bool IsMaskable,
    FilterOperator AllowedOperators)
{
    /// <summary>
    /// Whether this field is (part of) the underlying entity/table primary key. This is the
    /// <b>entity-level</b> truth, surfaced from authoring (<c>.PrimaryKey()</c>) or single-source
    /// EF-model derivation; <see cref="ViewMetadata.KeyFields"/> is the <b>view-level</b> key that
    /// defaults from the fields marked here. Defaults to <see langword="false"/> (Decision Log D104).
    /// </summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>
    /// An optional display-format hint for this field (for example <c>"N2"</c> or <c>"yyyy-MM-dd"</c>), or
    /// <see langword="null"/> when the author set none.
    /// </summary>
    /// <remarks>
    /// <b>Published, never applied (Decision Log D149).</b> The server emits this string on the metadata
    /// endpoint for a client — a grid, a report, a generated UI — to apply when rendering the column. Vista
    /// itself never interprets it: filtering, sorting, and export all operate on raw values, so a format hint
    /// can never change what a query matches or what an export contains. That keeps the wire contract and the
    /// data fidelity of exports independent of presentation. It is the successor of DynData's
    /// <c>DataFormatString</c>.
    /// </remarks>
    public string? Format { get; init; }

    /// <summary>
    /// Creates a <see cref="FieldMetadata"/>, auto-deriving the display label from
    /// <paramref name="name"/> when <paramref name="label"/> is not supplied.
    /// </summary>
    /// <param name="name">The projected field name.</param>
    /// <param name="clrType">The CLR type of the projected value.</param>
    /// <param name="label">
    /// Explicit display label. When <see langword="null"/>, the label is derived from
    /// <paramref name="name"/> via <see cref="LabelHelper.ToTitleCase"/>.
    /// </param>
    /// <param name="isFilterable">Whether clients may filter on this field.</param>
    /// <param name="isSortable">Whether clients may sort by this field.</param>
    /// <param name="isSearchable">Whether this field participates in global search.</param>
    /// <param name="isScopable">Whether this field may be used as a contextual scope key.</param>
    /// <param name="isHidden">Whether the field is hidden from transport/display.</param>
    /// <param name="isWritable">Whether the field can be written by clients.</param>
    /// <param name="isMaskable">Whether the field value is masked in read responses.</param>
    /// <param name="allowedOperators">The filter operators allowed on this field.</param>
    /// <param name="isPrimaryKey">Whether the field is (part of) the entity primary key (Decision Log D104).</param>
    /// <param name="format">The optional display-format hint published to clients (Decision Log D149).</param>
    /// <returns>A new <see cref="FieldMetadata"/> instance.</returns>
    public static FieldMetadata Create(
        string name,
        Type clrType,
        string? label = null,
        bool isFilterable = true,
        bool isSortable = true,
        bool isSearchable = true,
        bool isScopable = false,
        bool isHidden = false,
        bool isWritable = false,
        bool isMaskable = false,
        FilterOperator allowedOperators = FilterOperator.None,
        bool isPrimaryKey = false,
        string? format = null) =>
        new(
            name,
            label ?? LabelHelper.ToTitleCase(name),
            clrType,
            isFilterable,
            isSortable,
            isSearchable,
            isScopable,
            isHidden,
            isWritable,
            isMaskable,
            allowedOperators)
        {
            IsPrimaryKey = isPrimaryKey,
            Format = format,
        };
}
