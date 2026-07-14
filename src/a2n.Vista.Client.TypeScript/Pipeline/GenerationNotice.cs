namespace a2n.Vista.Client.TypeScript.Pipeline;

/// <summary>
/// The category of a non-fatal <see cref="GenerationNotice"/> (design "Non-fatal notices" table).
/// </summary>
public enum GenerationNoticeKind
{
    /// <summary>
    /// A permissive/unconstrained object (<c>{}</c>) member was emitted as <c>unknown</c>
    /// (Requirement 3.6).
    /// </summary>
    PermissiveObjectMember,

    /// <summary>
    /// A scalar <c>type</c>/<c>format</c> combination the generator does not recognize was emitted
    /// as <c>unknown</c> (Requirement 3.7).
    /// </summary>
    UnrecognizedScalar,

    /// <summary>
    /// A <c>ViewListResult_*</c> component did not match the generic re-lifting template and was
    /// emitted as a plain named type (design robustness for Requirement 2.6).
    /// </summary>
    EnvelopeShapeFallback,
}

/// <summary>
/// A non-fatal notice recorded during generation. Notices never abort the run; they are collected,
/// ordered deterministically, printed to stderr, and summarized in the success report
/// (Requirements 3.6, 3.7, 10.6). The natural ordering is ordinal and total so notices do not
/// perturb byte-for-byte determinism (Requirement 9).
/// </summary>
/// <param name="Kind">The notice category.</param>
/// <param name="Message">The English, human-readable notice text.</param>
/// <param name="View">The view the notice relates to, when applicable.</param>
/// <param name="Property">The property the notice relates to, when applicable.</param>
public sealed record GenerationNotice(
    GenerationNoticeKind Kind,
    string Message,
    string? View = null,
    string? Property = null) : IComparable<GenerationNotice>
{
    /// <summary>
    /// Records that a permissive/unconstrained object member degraded to <c>unknown</c>
    /// (Requirement 3.6).
    /// </summary>
    public static GenerationNotice PermissiveObjectMember(string view, string property) => new(
        GenerationNoticeKind.PermissiveObjectMember,
        $"Property '{property}' on '{view}' is an unconstrained object; emitted as 'unknown'.",
        view,
        property);

    /// <summary>
    /// Records that an unrecognized scalar <c>type</c>/<c>format</c> degraded to <c>unknown</c>
    /// (Requirement 3.7).
    /// </summary>
    public static GenerationNotice UnrecognizedScalar(string view, string property, string? type, string? format)
    {
        var described = string.IsNullOrEmpty(format) ? (type ?? "(none)") : $"{type}/{format}";
        return new GenerationNotice(
            GenerationNoticeKind.UnrecognizedScalar,
            $"Property '{property}' on '{view}' declares an unrecognized scalar '{described}'; emitted as 'unknown'.",
            view,
            property);
    }

    /// <summary>
    /// Records that a <c>ViewListResult_*</c> component fell back to a plain named type because it
    /// did not match the generic re-lifting template (Requirement 2.6, robustness).
    /// </summary>
    public static GenerationNotice EnvelopeShapeFallback(string componentName) => new(
        GenerationNoticeKind.EnvelopeShapeFallback,
        $"Component '{componentName}' did not match the ViewListResult template; emitted as a plain named type.",
        componentName);

    /// <inheritdoc />
    public int CompareTo(GenerationNotice? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byKind = Kind.CompareTo(other.Kind);
        if (byKind != 0)
        {
            return byKind;
        }

        var byView = string.CompareOrdinal(View, other.View);
        if (byView != 0)
        {
            return byView;
        }

        var byProperty = string.CompareOrdinal(Property, other.Property);
        if (byProperty != 0)
        {
            return byProperty;
        }

        return string.CompareOrdinal(Message, other.Message);
    }
}
