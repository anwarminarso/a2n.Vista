namespace a2n.Vista.Metadata;

/// <summary>
/// Declarative snapshot of a View after its authoring builder has run. Both authoring styles
/// (central template and class-per-view) emit an equivalent <see cref="ViewMetadata"/>, which is
/// the primary input for the executor, UI adapters, the source generator, and the TypeScript client.
/// Authoritative shape: docs/spec/01-view.md §5.4.
/// </summary>
/// <param name="Name">The unique view name used for registration and routing.</param>
/// <param name="Route">
/// The full route at which the view is served, composed at registration from the route group prefix
/// (or the default root <c>/api/views</c>) plus the view name — e.g. <c>/api/views/customers</c> or
/// <c>/internal/orders</c> (Decision Log D101/D103). The AspNetCore mapper maps the view at this route
/// verbatim. Core authoring builders emit the bare name here; the registration layer composes the
/// final full route.
/// </param>
/// <param name="QueryType">The CLR type of the projected (read) row.</param>
/// <param name="CrudType">
/// The typed CRUD/write contract, or <see langword="null"/> for a read-only view.
/// </param>
/// <param name="CrudEntityType">
/// The underlying entity type targeted by write operations, or <see langword="null"/> when the
/// view is read-only.
/// </param>
/// <param name="Fields">The projected fields and their per-field metadata, in projection order.</param>
/// <param name="Authorization">
/// Optional per-view authorization override; <see langword="null"/> means the view defers to the
/// central authorizer (§5.6).
/// </param>
/// <param name="Limits">The hard limits (page size, export rows) enforced for this view.</param>
/// <param name="IsReadOnly">
/// <see langword="true"/> when the view exposes only read facets (anonymous projection); write
/// endpoints are not generated for read-only views (Decision Log D38, §4.5).
/// </param>
public sealed record ViewMetadata(
    string Name,
    string Route,
    Type QueryType,
    Type? CrudType,
    Type? CrudEntityType,
    IReadOnlyList<FieldMetadata> Fields,
    AuthorizationRequirement? Authorization,
    HardLimits Limits,
    bool IsReadOnly)
{
    private readonly object _keyFieldsGate = new();
    private IReadOnlyList<string> _keyFields = [];
    private bool _keyFieldsCompleted;

    /// <summary>
    /// The ordered list of projected field names that uniquely identify a row <b>of this view</b>
    /// (single-element for a simple key, multi-element for a composite key). This is the view-level
    /// source of truth for deterministic paging tiebreakers and Detail-by-key; it defaults from the
    /// fields marked <see cref="FieldMetadata.IsPrimaryKey"/> and may be overridden during authoring
    /// (Decision Log D104). Empty only transiently before the registration fail-fast (Decision Log D106)
    /// or, for a single-source executable view that declared no key, until the startup model hook
    /// completes it from <c>DbContext.Model</c> via <see cref="CompleteKeyFields"/> (Decision Log D105).
    /// </summary>
    public IReadOnlyList<string> KeyFields
    {
        get => _keyFields;
        init => _keyFields = value ?? [];
    }

    /// <summary>
    /// Completes the view's <see cref="KeyFields"/> from the EF model at startup for a single-source
    /// executable view that declared no key (Decision Log D105 / M11). This is a <b>startup-only</b>,
    /// run-at-most-once mutation performed by the model hook before any request is served; it never runs
    /// on the request hot path and never changes request-time behavior beyond making the (previously
    /// empty) key resolvable. A declared key is never overridden (Requirement R6.3): the method refuses
    /// to run when <see cref="KeyFields"/> is already non-empty.
    /// </summary>
    /// <param name="keyFields">
    /// The derived key field names, in the source entity's declared primary-key column order
    /// (Requirement R6.2). Composite keys list every column.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="keyFields"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The key has already been completed, or a key was already declared (Requirement R6.3): the startup
    /// hook must derive a key only for a key-less view and only once.
    /// </exception>
    internal void CompleteKeyFields(IReadOnlyList<string> keyFields)
    {
        ArgumentNullException.ThrowIfNull(keyFields);

        lock (_keyFieldsGate)
        {
            if (_keyFieldsCompleted)
            {
                throw new InvalidOperationException(
                    $"KeyFields for view '{Name}' have already been completed; the startup primary-key " +
                    "derivation runs at most once per application start (Decision Log D105).");
            }

            if (_keyFields.Count != 0)
            {
                throw new InvalidOperationException(
                    $"View '{Name}' already declares a key, so the startup primary-key derivation must " +
                    "not override, merge, or supplement it from the EF model (Decision Log D105, R6.3).");
            }

            _keyFields = [.. keyFields];
            _keyFieldsCompleted = true;
        }
    }

    /// <summary>
    /// Compares two snapshots by their declarative content: name, route, row/write types, field list
    /// (element-wise), authorization, limits, and read-only flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is hand-written.</b> A record's synthesized equality compares <em>every</em> instance
    /// field, including the ones declared outside the primary constructor. This type declares three
    /// (<c>_keyFieldsGate</c>, <c>_keyFields</c>, <c>_keyFieldsCompleted</c>), and the gate is a fresh
    /// <see cref="object"/> per instance — so the synthesized <c>Equals</c> could never return
    /// <see langword="true"/> for two distinct instances and the synthesized <c>GetHashCode</c> was an
    /// identity hash, unstable across runs. It also compared <see cref="Fields"/> by reference, because
    /// <see cref="IReadOnlyList{T}"/> has no structural equality. Both are fixed here.
    /// </para>
    /// <para>
    /// <b>Why the key is excluded.</b> <see cref="KeyFields"/> is completed after construction by the
    /// startup model hook (<see cref="CompleteKeyFields"/>, Decision Log D105), so including it would make
    /// equality and the hash code change during an instance's lifetime — the one property that makes a type
    /// unsafe to put in a hash-based collection. Excluding it costs nothing in practice: view names are
    /// globally unique (D101/D103) and <see cref="Name"/> is compared, so two snapshots that compare equal
    /// describe the same view and therefore resolve the same key.
    /// </para>
    /// </remarks>
    /// <param name="other">The snapshot to compare with.</param>
    /// <returns><see langword="true"/> when both describe the same view shape.</returns>
    public bool Equals(ViewMetadata? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Route, other.Route, StringComparison.Ordinal)
            && QueryType == other.QueryType
            && CrudType == other.CrudType
            && CrudEntityType == other.CrudEntityType
            && IsReadOnly == other.IsReadOnly
            && Authorization == other.Authorization
            && Limits == other.Limits
            && FieldsEqual(Fields, other.Fields);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Route, StringComparer.Ordinal);
        hash.Add(QueryType);
        hash.Add(CrudType);
        hash.Add(CrudEntityType);
        hash.Add(IsReadOnly);
        hash.Add(Authorization);
        hash.Add(Limits);

        // Fields is immutable once the authoring builder has run, so it is safe to hash; KeyFields is not
        // (see the Equals remarks).
        hash.Add(Fields.Count);
        foreach (var field in Fields)
        {
            hash.Add(field);
        }

        return hash.ToHashCode();
    }

    private static bool FieldsEqual(IReadOnlyList<FieldMetadata> left, IReadOnlyList<FieldMetadata> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
