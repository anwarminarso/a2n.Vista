using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;

namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// The envelope generic re-lifting step (design "Envelope recognition / generic re-lifting (Requirement
/// 2.6)"). M18 monomorphizes the row-parameterized list envelope as one named component per distinct row —
/// <c>ViewListResult_{Row}</c> — whose <c>page</c> object inlines the <c>PagedResult</c> shape with the row
/// <c>$ref</c> bound. Requirement 2.6 nonetheless requires a <b>single generic</b>
/// <c>ViewListResult&lt;TRow&gt;</c> / <c>PagedResult&lt;TRow&gt;</c> TypeScript type. This step collapses
/// each monomorphized component back into that single generic by structurally matching it against the fixed
/// <see cref="EnvelopeCatalog"/> templates and extracting the row <c>$ref</c> as the type parameter, binding
/// <c>TRow</c> per view.
/// </summary>
/// <remarks>
/// <para>
/// The re-lifter is pure: it performs no I/O and mutates nothing except the supplied
/// <see cref="NoticeCollector"/>, to which it records a non-fatal fallback notice for any
/// <c>ViewListResult_*</c> component whose shape does not fit the template. A mismatch is <b>never</b> fatal
/// (design "Error Handling" — <c>ViewListResult_*</c> shape not matching the template → emit as plain named
/// type, record notice); an unexpected server shape degrades gracefully rather than crashing, and the plain
/// named type preserves whatever extra members the component carries.
/// </para>
/// <para>
/// <b>Single generic declaration (Requirements 2.6, 2.5).</b> The step records only a per-component row-type
/// binding (component name → row type name); the downstream emitter (task 9.2) emits the
/// <c>ViewListResult&lt;TRow&gt;</c> and <c>PagedResult&lt;TRow&gt;</c> generics <b>once</b> when
/// <see cref="EnvelopeReLiftResult.GenericEnvelopesNeeded"/> is set, and each view's list-success type
/// references that single generic bound to the view's row type. The per-view DTO step (task 7.4) and the
/// operation-graph step (task 7.5) consume <see cref="EnvelopeReLiftResult"/> to bind each view's row type.
/// </para>
/// <para>
/// <b>Discovery.</b> Candidates are found by the M18 naming convention (the <see cref="MonomorphizedPrefix"/>
/// name prefix). Restricting to the prefix avoids accidentally re-lifting an unrelated component that merely
/// happens to share the structural shape; every list envelope M18 emits carries the prefix.
/// </para>
/// </remarks>
public sealed class EnvelopeReLifter
{
    /// <summary>
    /// The name prefix M18 gives every monomorphized list envelope (<c>ViewListResult_{Row}</c>). Discovery
    /// is keyed off this prefix (see the type-level remarks).
    /// </summary>
    public const string MonomorphizedPrefix = "ViewListResult_";

    /// <summary>The name of the single generic list envelope the emitter declares once (<c>ViewListResult&lt;TRow&gt;</c>).</summary>
    public const string GenericViewListResultName = "ViewListResult";

    /// <summary>The name of the single generic paged-result envelope the emitter declares once (<c>PagedResult&lt;TRow&gt;</c>).</summary>
    public const string GenericPagedResultName = "PagedResult";

    private readonly EnvelopeCatalog _catalog;

    /// <summary>Creates a re-lifter that matches against the templates held by <paramref name="catalog"/>.</summary>
    /// <param name="catalog">The catalog holding the fixed <c>ViewListResult</c>/<c>PagedResult</c> structural templates.</param>
    public EnvelopeReLifter(EnvelopeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>
    /// Scans the resolved document for monomorphized <c>ViewListResult_*</c> components, structurally matches
    /// each against the <c>ViewListResult</c> + <c>PagedResult</c> templates, and returns the re-lifting
    /// outcome: the recognized components (each bound to its extracted row type), the components that fell
    /// back to a plain named type, and whether the generic envelopes are needed at all. Fallbacks are
    /// recorded as non-fatal notices on <paramref name="notices"/>; this method never throws for an
    /// unrecognized shape and is never fatal (Requirement 2.6, robustness).
    /// </summary>
    /// <param name="document">The resolved document whose schema graph is scanned.</param>
    /// <param name="notices">The collector that receives a fallback notice per unmatched component.</param>
    /// <returns>The deterministic <see cref="EnvelopeReLiftResult"/>.</returns>
    public EnvelopeReLiftResult ReLift(ResolvedDocument document, NoticeCollector notices)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(notices);

        // Deterministic scan order so the notices and bindings are stable across runs and operating systems
        // (Requirement 9.2). Discovery is by the M18 naming convention.
        var candidates = DeterministicOrder.OrderNames(
            document.Schemas.Keys.Where(name => name.StartsWith(MonomorphizedPrefix, StringComparison.Ordinal)));

        var reLifted = new List<ReLiftedEnvelope>();
        var fallbacks = new List<string>();
        var rowByComponent = new Dictionary<string, string>(DeterministicOrder.Comparer);

        foreach (var name in candidates)
        {
            var component = document.Schemas[name];
            var rowType = TryMatchViewListResult(component);

            if (rowType is not null)
            {
                // Match: bind the view's list-success type to the single generic ViewListResult<rowType>.
                reLifted.Add(new ReLiftedEnvelope(name, rowType));
                rowByComponent[name] = rowType;
            }
            else
            {
                // Mismatch: fall back to a plain named type and record a non-fatal notice (never fatal).
                fallbacks.Add(name);
                notices.AddEnvelopeShapeFallback(name);
            }
        }

        return new EnvelopeReLiftResult(reLifted, fallbacks, rowByComponent);
    }

    // Matches a candidate against the ViewListResult template and, on success, returns the extracted row
    // component name (the type parameter TRow); returns null on any structural mismatch.
    private string? TryMatchViewListResult(OpenApiSchema component)
    {
        if (!TryGetInlineObject(component, out var properties))
        {
            return null;
        }

        var template = _catalog.ViewListResultTemplate;

        // Exact member-set match: a component carrying extra members is intentionally rejected, because
        // re-lifting it to the fixed generic shape would silently drop those members. Falling back to a
        // plain named type preserves them.
        if (properties.Count != template.Members.Count)
        {
            return null;
        }

        string? rowType = null;

        foreach (var member in template.Members)
        {
            if (!properties.TryGetValue(member.Name, out var memberSchema))
            {
                return null;
            }

            switch (member.Kind)
            {
                case EnvelopeTemplateMemberKind.NestedTemplate:
                    // The `page` object must itself match the fixed PagedResult template and yield the row.
                    rowType = TryMatchPagedResult(memberSchema);
                    if (rowType is null)
                    {
                        return null;
                    }

                    break;

                case EnvelopeTemplateMemberKind.Scalar:
                    if (!ScalarMatches(memberSchema, member))
                    {
                        return null;
                    }

                    break;

                default:
                    // The outer template declares no RowArray member; any other kind is a template authoring
                    // error, treated conservatively as a mismatch.
                    return null;
            }
        }

        return rowType;
    }

    // Matches the inlined `page` object against the PagedResult template and, on success, returns the row
    // component name extracted from `items`' element `$ref`; returns null on any structural mismatch.
    private string? TryMatchPagedResult(OpenApiSchema page)
    {
        if (!TryGetInlineObject(page, out var properties))
        {
            return null;
        }

        var template = _catalog.PagedResultTemplate;

        if (properties.Count != template.Members.Count)
        {
            return null;
        }

        string? rowType = null;

        foreach (var member in template.Members)
        {
            if (!properties.TryGetValue(member.Name, out var memberSchema))
            {
                return null;
            }

            switch (member.Kind)
            {
                case EnvelopeTemplateMemberKind.RowArray:
                    // `items` must be an array whose element is a named component reference; that reference
                    // target is the row type parameter TRow.
                    rowType = TryExtractRowRef(memberSchema);
                    if (rowType is null)
                    {
                        return null;
                    }

                    break;

                case EnvelopeTemplateMemberKind.Scalar:
                    if (!ScalarMatches(memberSchema, member))
                    {
                        return null;
                    }

                    break;

                default:
                    // The PagedResult template declares no NestedTemplate member; treat as a mismatch.
                    return null;
            }
        }

        return rowType;
    }

    // Extracts the row component name from the `items` array's element `$ref`. Returns null when `items` is
    // not an array, has no item schema, or its element is not a local component reference (an inline element
    // has no nameable row type to re-lift, so it degrades to a fallback).
    private static string? TryExtractRowRef(OpenApiSchema items)
    {
        if (!IsType(items, "array") || items.Items is null)
        {
            return null;
        }

        var element = items.Items;
        if (string.IsNullOrEmpty(element.Ref))
        {
            return null;
        }

        return ResolvedDocument.TryGetComponentName(element.Ref, ResolvedDocument.SchemaRefPrefix, out var name)
            ? name
            : null;
    }

    // A schema is a matchable inline object when it declares object properties and is not itself a $ref.
    private static bool TryGetInlineObject(
        OpenApiSchema schema,
        out IReadOnlyDictionary<string, OpenApiSchema> properties)
    {
        if (string.IsNullOrEmpty(schema.Ref) && schema.Properties is { } props)
        {
            properties = props;
            return true;
        }

        properties = EmptyProperties;
        return false;
    }

    // A scalar member matches when it is not a $ref, its OpenAPI `type` equals the template's expected type,
    // and — when the template pins a `format` — its `format` matches too. Formats are constants in the M18
    // shape, so pinning them keeps the match faithful; a format drift degrades to a fallback, never fatal.
    private static bool ScalarMatches(OpenApiSchema schema, EnvelopeTemplateMember member)
    {
        if (!string.IsNullOrEmpty(schema.Ref))
        {
            return false;
        }

        if (!string.Equals(schema.Type, member.ExpectedType, StringComparison.Ordinal))
        {
            return false;
        }

        return string.IsNullOrEmpty(member.ExpectedFormat)
            || string.Equals(schema.Format, member.ExpectedFormat, StringComparison.Ordinal);
    }

    private static bool IsType(OpenApiSchema schema, string type) =>
        string.Equals(schema.Type, type, StringComparison.Ordinal);

    private static readonly IReadOnlyDictionary<string, OpenApiSchema> EmptyProperties =
        new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
}

/// <summary>
/// One recognized monomorphized <c>ViewListResult_{Row}</c> component collapsed back to the single generic
/// <c>ViewListResult&lt;TRow&gt;</c>, recording the extracted row type parameter.
/// </summary>
/// <param name="ComponentName">The verbatim monomorphized component name (e.g. <c>ViewListResult_CustomerRow</c>).</param>
/// <param name="RowTypeName">The extracted row component name bound as <c>TRow</c> (e.g. <c>CustomerRow</c>).</param>
public sealed record ReLiftedEnvelope(string ComponentName, string RowTypeName);

/// <summary>
/// The outcome of the envelope generic re-lifting step (task 7.2). Captures the recognized components (each
/// bound to its row type), the components that fell back to a plain named type, and whether the single
/// generic <c>ViewListResult&lt;TRow&gt;</c>/<c>PagedResult&lt;TRow&gt;</c> declarations are needed at all.
/// Consumed by the per-view DTO step (task 7.4) and the operation-graph step (task 7.5); the emitter
/// (task 9.2) emits the generics once when <see cref="GenericEnvelopesNeeded"/> is set.
/// </summary>
/// <param name="ReLifted">
/// The recognized components in deterministic ordinal order by component name (Requirement 9.2). A single
/// generic pair covers all of them; each entry supplies the per-view row-type binding.
/// </param>
/// <param name="FallbackComponents">
/// The <c>ViewListResult_*</c> components whose shape did not match the template, in deterministic ordinal
/// order. Each is emitted as a plain named type and carries a non-fatal notice (Requirement 2.6 robustness).
/// </param>
/// <param name="RowTypeByComponent">
/// The component-name → row-type-name lookup for the recognized components, keyed ordinally.
/// </param>
public sealed record EnvelopeReLiftResult(
    IReadOnlyList<ReLiftedEnvelope> ReLifted,
    IReadOnlyList<string> FallbackComponents,
    IReadOnlyDictionary<string, string> RowTypeByComponent)
{
    /// <summary>
    /// Whether any monomorphized component was recognized, and therefore whether the emitter should declare
    /// the single generic <c>ViewListResult&lt;TRow&gt;</c>/<c>PagedResult&lt;TRow&gt;</c> pair once
    /// (Requirements 2.6, 2.5). A document with no recognized list envelope needs no generic declaration.
    /// </summary>
    public bool GenericEnvelopesNeeded => ReLifted.Count > 0;

    /// <summary>
    /// Attempts to read the row type bound to a recognized monomorphized component. Returns <c>true</c> and
    /// sets <paramref name="rowTypeName"/> when the component was recognized; otherwise <c>false</c>.
    /// </summary>
    /// <param name="componentName">The verbatim monomorphized component name.</param>
    /// <param name="rowTypeName">The bound row type name when recognized.</param>
    public bool TryGetRowType(string componentName, out string rowTypeName)
    {
        if (RowTypeByComponent.TryGetValue(componentName, out var found))
        {
            rowTypeName = found;
            return true;
        }

        rowTypeName = string.Empty;
        return false;
    }
}
