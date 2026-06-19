using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Authoring;

/// <summary>
/// Fluent per-field authoring surface shared by both authoring styles
/// (central template / "Gaya A" and class-per-view / "Gaya B").
/// Each call mutates the field's pending state; when authoring finishes the
/// configured builder materializes a <see cref="FieldMetadata"/> for the view.
/// Authoritative shape: docs/spec/01-view.md §5.5.
/// </summary>
/// <typeparam name="TProp">The CLR type of the projected field.</typeparam>
/// <remarks>
/// Every option is optional and the defaults are safe-by-correct (Decision Log D42):
/// a field is filterable and sortable by default, string fields are searchable by
/// default, and a sensible per-type operator whitelist is derived automatically.
/// Contextual client scoping is opt-in only (<see cref="Scopable"/>, Decision Log D47).
/// </remarks>
public interface IFieldBuilder<TProp>
{
    /// <summary>
    /// Marks this field as the view's primary key. The primary key must be present in the
    /// List projection (it may also be <see cref="Hidden"/>) so the Detail and Write facets
    /// can resolve a row by key (docs/spec/01-view.md §4.6).
    /// </summary>
    /// <returns>The same builder, for fluent chaining.</returns>
    IFieldBuilder<TProp> PrimaryKey();

    /// <summary>
    /// Hides the field from transport and display (for example a technical primary key).
    /// A hidden field still exists in the projection and can be used to resolve rows by key.
    /// </summary>
    /// <returns>The same builder, for fluent chaining.</returns>
    IFieldBuilder<TProp> Hidden();

    /// <summary>
    /// Overrides the auto-derived display label (PascalCase → "Title Case").
    /// </summary>
    /// <param name="label">The explicit display label.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IFieldBuilder<TProp> Label(string label);

    /// <summary>
    /// Sets a display/format string hint for the field (for example a date or number format).
    /// This is a presentation hint consumed by adapters and code generators.
    /// </summary>
    /// <param name="format">The format string hint.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IFieldBuilder<TProp> Format(string format);

    /// <summary>
    /// Opts the field in or out of client filtering. Defaults to <see langword="true"/> for
    /// every projected field (default-allow, Decision Log D42); call with <see langword="false"/>
    /// to opt out (R5.1).
    /// </summary>
    /// <param name="allowed">Whether clients may filter on this field.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IFieldBuilder<TProp> Filterable(bool allowed = true);

    /// <summary>
    /// Opts the field in or out of client sorting. Defaults to <see langword="true"/>;
    /// call with <see langword="false"/> to opt out (R5.2).
    /// </summary>
    /// <param name="allowed">Whether clients may sort by this field.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IFieldBuilder<TProp> Sortable(bool allowed = true);

    /// <summary>
    /// Opts the field in or out of global search. Only affects <see cref="string"/> fields:
    /// numeric, date, and other non-string fields never participate in global search regardless
    /// of this flag (R5.3, R5.4, §4.4). Defaults to <see langword="true"/> for string fields.
    /// </summary>
    /// <param name="allowed">Whether the (string) field participates in global search.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IFieldBuilder<TProp> Searchable(bool allowed = true);

    /// <summary>
    /// Restricts the set of <see cref="FilterOperator"/> values clients may request against this
    /// field, overriding the auto-derived per-type default. Setting operators implies the field is
    /// filterable (§5.5).
    /// </summary>
    /// <param name="operators">The allowed operator whitelist.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IFieldBuilder<TProp> Operators(FilterOperator operators);

    /// <summary>
    /// Allows the field to be used as a contextual/lookup scope key supplied by the client
    /// (the equivalent of DynData's <c>externalFilter</c>). Defaults to <see langword="false"/>
    /// (opt-in, Decision Log D47). Server-trusted scoping flows through the authorizer instead.
    /// </summary>
    /// <param name="allowed">Whether clients may use this field as a scope key.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IFieldBuilder<TProp> Scopable(bool allowed = true);
}
