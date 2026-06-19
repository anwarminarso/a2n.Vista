using a2n.Vista.Metadata;

namespace a2n.Vista.Authoring;

/// <summary>
/// Non-generic accumulation surface for a configured field builder, consumed by the view
/// builders (central template / class-per-view) to collect fields without knowing each
/// field's <c>TProp</c> at compile time.
/// </summary>
/// <remarks>
/// Exposes the authoring-only signals that do not live on <see cref="FieldMetadata"/> itself
/// (the primary-key marker, used for Detail/Write by-key resolution and PK validation, and the
/// display <see cref="FormatString"/> hint), alongside <see cref="Build"/> which materializes
/// the immutable <see cref="FieldMetadata"/> snapshot.
/// </remarks>
internal interface IFieldBuilderState
{
    /// <summary>Whether the field was marked as the view's primary key.</summary>
    bool IsPrimaryKey { get; }

    /// <summary>The display/format string hint, or <see langword="null"/> when none was set.</summary>
    string? FormatString { get; }

    /// <summary>
    /// Materializes the accumulated state into an immutable <see cref="FieldMetadata"/>.
    /// </summary>
    /// <param name="name">The projected field name supplied by the view builder.</param>
    /// <returns>The field metadata snapshot.</returns>
    FieldMetadata Build(string name);
}
