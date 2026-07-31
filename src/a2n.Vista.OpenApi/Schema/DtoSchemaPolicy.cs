using a2n.Vista.Metadata;

namespace a2n.Vista.OpenApi.Schema;

/// <summary>
/// The per-view field-visibility policy applied when a view's row schema is generated: which projected
/// members are hidden (and therefore not described at all) and which are maskable (described, but annotated
/// as substitutable). Derived from <see cref="ViewMetadata.Fields"/> so the document agrees with the view's
/// own metadata facet, which already drops hidden fields.
/// </summary>
/// <remarks>
/// Without this policy the emitter reflects over every public property of the row type, so a field the
/// author marked <c>Hidden()</c> is absent from <c>GET {route}/metadata</c> yet present — name and type — in
/// <c>components.schemas</c>. That is a disclosure of exactly what the author chose to withhold, so the
/// policy is applied to the view's own row/write type members (never to nested types, whose members are not
/// view fields).
/// </remarks>
public sealed class DtoSchemaPolicy
{
    private readonly HashSet<string> _hidden;
    private readonly HashSet<string> _maskable;

    private DtoSchemaPolicy(string key, HashSet<string> hidden, HashSet<string> maskable)
    {
        Key = key;
        _hidden = hidden;
        _maskable = maskable;
    }

    /// <summary>
    /// The policy identity, used as part of the component key so one CLR type described under two different
    /// policies yields two distinct component schemas rather than the first one silently winning.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Builds the policy for <paramref name="view"/>, or returns <see langword="null"/> when the view hides
    /// and masks nothing (the common case), so the schema is generated exactly as the type serializes.
    /// </summary>
    /// <param name="view">The view whose field flags govern the emitted schema.</param>
    public static DtoSchemaPolicy? ForView(ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(view);

        HashSet<string>? hidden = null;
        HashSet<string>? maskable = null;

        foreach (var field in view.Fields)
        {
            if (field.IsHidden)
            {
                (hidden ??= new HashSet<string>(StringComparer.Ordinal)).Add(field.Name);
            }

            if (field.IsMaskable)
            {
                (maskable ??= new HashSet<string>(StringComparer.Ordinal)).Add(field.Name);
            }
        }

        if (hidden is null && maskable is null)
        {
            return null;
        }

        return new DtoSchemaPolicy(
            view.Name,
            hidden ?? new HashSet<string>(StringComparer.Ordinal),
            maskable ?? new HashSet<string>(StringComparer.Ordinal));
    }

    /// <summary>Whether the member named <paramref name="memberName"/> is hidden from the read surface.</summary>
    public bool IsHidden(string memberName) => _hidden.Contains(memberName);

    /// <summary>Whether the member named <paramref name="memberName"/> may be masked per request.</summary>
    public bool IsMaskable(string memberName) => _maskable.Contains(memberName);
}
