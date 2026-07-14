using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;

namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// The canonical catalog of the fixed Vista request/response envelopes the model builder binds to by name
/// (design §A.5 step 2 "fixed-envelope binding", and the "fixed Vista schema catalog" table). It holds the
/// exact component names the M18 emitter produces, the fixed structural templates for the row-parameterized
/// envelopes (consumed later by the generic re-lifting step, task 7.2), and the <see cref="Bind"/> method
/// that locates each required envelope by name in a <see cref="ResolvedDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// The catalog is pure: it performs no I/O and holds only constant, deterministic data. Binding is a lookup
/// against the resolved document's name-keyed schema graph; a missing required envelope is a fatal
/// <see cref="GenerationError.MissingSchema"/> (Requirement 2.7), returned rather than thrown so the
/// buffered pipeline routes it through the single abort path.
/// </para>
/// <para>
/// <b>One declaration per name (Requirement 2.1).</b> The bindings are keyed by envelope name, so the same
/// name can never be bound twice; the downstream emitter (task 9.2) emits exactly one declaration per bound
/// name and references it by name from every use.
/// </para>
/// <para>
/// <b>Write-envelope required-ness (flagged for review).</b> The design §A.5 step 2 lists all eight
/// envelopes — including <c>VistaWriteRequestBody</c> and <c>VistaWriteResponse</c> — under "fixed-envelope
/// binding", but Requirement 5 makes the write surface an explicit opt-in (off by default), and a target API
/// built before the write path landed (M12) simply will not document the write envelopes. Binding the write
/// envelopes unconditionally would therefore make generation fail against a legitimate read-only document.
/// To reconcile the two, this catalog treats the <b>read-surface envelopes as always required</b> and the
/// <b>write-surface envelopes as required only when write-facet generation is enabled</b>
/// (<c>GenerationConfig.EmitWriteFacets</c>), surfaced through the <c>includeWriteEnvelopes</c> parameter of
/// <see cref="Bind"/>. This split should be confirmed at review.
/// </para>
/// <para>
/// Scope note: the <c>FilterNode</c> family (<c>FilterNode</c>/<c>FilterLeaf</c>/<c>FilterAnd</c>/
/// <c>FilterOr</c>/<c>FilterNot</c>) and the per-view DTO/row components are bound by their own modeling
/// steps (tasks 7.3 and 7.4); this catalog is limited to the fixed request/response envelopes.
/// </para>
/// </remarks>
public sealed class EnvelopeCatalog
{
    /// <summary>The list request envelope (<c>filter</c>/<c>scope</c>/<c>sort</c>/<c>page</c>/…).</summary>
    public const string VistaListRequestBody = "VistaListRequestBody";

    /// <summary>The sort clause envelope (<c>{ field, desc }</c>), referenced by <see cref="VistaListRequestBody"/>.</summary>
    public const string VistaSortBody = "VistaSortBody";

    /// <summary>The detail request envelope (a scalar or composite <c>key</c>).</summary>
    public const string VistaDetailRequestBody = "VistaDetailRequestBody";

    /// <summary>The write request envelope (shared permissive <c>model</c>/<c>key</c>). Write surface only.</summary>
    public const string VistaWriteRequestBody = "VistaWriteRequestBody";

    /// <summary>The write response envelope (the created row's <c>key</c>). Write surface only.</summary>
    public const string VistaWriteResponse = "VistaWriteResponse";

    /// <summary>The view metadata response envelope (<c>fields</c>, <c>keyFields</c>, limits, …).</summary>
    public const string VistaMetadataResponse = "VistaMetadataResponse";

    /// <summary>The single field-metadata projection, referenced by <see cref="VistaMetadataResponse"/>.</summary>
    public const string VistaFieldMetadataResponse = "VistaFieldMetadataResponse";

    /// <summary>The RFC 7807 problem-details envelope (plus the Vista <c>code</c> extension).</summary>
    public const string ProblemDetails = "ProblemDetails";

    /// <summary>
    /// The read-surface envelopes that are <b>always</b> required to be present in the document. Absence of
    /// any of these is a fatal <see cref="GenerationError.MissingSchema"/> regardless of configuration.
    /// </summary>
    public static IReadOnlyList<string> ReadSurfaceEnvelopeNames { get; } =
    [
        VistaListRequestBody,
        VistaSortBody,
        VistaDetailRequestBody,
        VistaMetadataResponse,
        VistaFieldMetadataResponse,
        ProblemDetails,
    ];

    /// <summary>
    /// The write-surface envelopes required <b>only</b> when write-facet generation is enabled
    /// (<c>GenerationConfig.EmitWriteFacets</c>). See the type-level note on write-envelope required-ness.
    /// </summary>
    public static IReadOnlyList<string> WriteSurfaceEnvelopeNames { get; } =
    [
        VistaWriteRequestBody,
        VistaWriteResponse,
    ];

    /// <summary>
    /// The fixed structural template for <c>PagedResult&lt;TRow&gt;</c>, matching the shape M18 inlines under
    /// the <c>page</c> member of each monomorphized <c>ViewListResult_{Row}</c>. Held here so the generic
    /// re-lifting step (task 7.2) can structurally match a document component against it and extract the row
    /// type parameter. This catalog does not itself perform the matching.
    /// </summary>
    public StructuralTemplate PagedResultTemplate { get; } = new(
        "PagedResult",
        [
            // `items` is the array of the row type parameter (TRow[]) — the sole row-bearing member.
            new EnvelopeTemplateMember("items", EnvelopeTemplateMemberKind.RowArray, Required: true),
            new EnvelopeTemplateMember(
                "totalRows", EnvelopeTemplateMemberKind.Scalar, Required: true,
                ExpectedType: "integer", ExpectedFormat: "int64"),
            new EnvelopeTemplateMember(
                "pageIndex", EnvelopeTemplateMemberKind.Scalar, Required: true,
                ExpectedType: "integer", ExpectedFormat: "int32"),
            new EnvelopeTemplateMember(
                "pageSize", EnvelopeTemplateMemberKind.Scalar, Required: true,
                ExpectedType: "integer", ExpectedFormat: "int32"),
            new EnvelopeTemplateMember(
                "totalPages", EnvelopeTemplateMemberKind.Scalar, Required: true,
                ExpectedType: "integer", ExpectedFormat: "int64"),
        ]);

    /// <summary>
    /// The fixed structural template for <c>ViewListResult&lt;TRow&gt;</c>, matching the outer shape of each
    /// monomorphized <c>ViewListResult_{Row}</c> component: a <c>page</c> object (the nested
    /// <see cref="PagedResultTemplate"/>) plus a fixed <c>totalRowsUnfiltered</c> scalar. Held for the
    /// generic re-lifting step (task 7.2).
    /// </summary>
    public StructuralTemplate ViewListResultTemplate { get; } = new(
        "ViewListResult",
        [
            new EnvelopeTemplateMember(
                "page", EnvelopeTemplateMemberKind.NestedTemplate, Required: true,
                NestedTemplateName: "PagedResult"),
            new EnvelopeTemplateMember(
                "totalRowsUnfiltered", EnvelopeTemplateMemberKind.Scalar, Required: true,
                ExpectedType: "integer", ExpectedFormat: "int64"),
        ]);

    /// <summary>
    /// Returns the required envelope names for the requested surface, in a deterministic ordinal order: the
    /// read-surface envelopes always, plus the write-surface envelopes when
    /// <paramref name="includeWriteEnvelopes"/> is <c>true</c>. The order is fixed so a "first missing"
    /// report is deterministic across runs and operating systems (Requirement 9).
    /// </summary>
    /// <param name="includeWriteEnvelopes">
    /// Whether the write-surface envelopes are required (mirrors <c>GenerationConfig.EmitWriteFacets</c>).
    /// </param>
    public static IReadOnlyList<string> RequiredEnvelopeNames(bool includeWriteEnvelopes)
    {
        var names = includeWriteEnvelopes
            ? ReadSurfaceEnvelopeNames.Concat(WriteSurfaceEnvelopeNames)
            : ReadSurfaceEnvelopeNames;

        return DeterministicOrder.OrderNames(names);
    }

    /// <summary>
    /// Locates every required envelope by name in the resolved document's <c>components.schemas</c> and
    /// returns the bound set, or a fatal <see cref="GenerationError.MissingSchema"/> for the first missing
    /// envelope (Requirement 2.7). The read-surface envelopes are always required; the write-surface
    /// envelopes are required only when <paramref name="includeWriteEnvelopes"/> is <c>true</c> (see the
    /// type-level note on write-envelope required-ness).
    /// </summary>
    /// <param name="document">The resolved document to bind against.</param>
    /// <param name="includeWriteEnvelopes">
    /// Whether the write-surface envelopes must be present (mirrors <c>GenerationConfig.EmitWriteFacets</c>).
    /// </param>
    /// <returns>
    /// <see cref="Result{T, E}.Ok"/> carrying the <see cref="EnvelopeBindings"/> when every required envelope
    /// is present; otherwise <see cref="Result{T, E}.Err"/> carrying the first
    /// <see cref="GenerationError.MissingSchema"/>.
    /// </returns>
    public Result<EnvelopeBindings, GenerationError> Bind(ResolvedDocument document, bool includeWriteEnvelopes)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Keyed by name so a name can never bind twice — the "declared once" invariant (Requirement 2.1) is
        // structural, not merely enforced downstream. The ordinal comparer matches the emit-stage ordering.
        var bound = new Dictionary<string, OpenApiSchema>(DeterministicOrder.Comparer);

        foreach (var name in RequiredEnvelopeNames(includeWriteEnvelopes))
        {
            if (!document.Schemas.TryGetValue(name, out var schema))
            {
                // First missing required envelope aborts generation, naming it (Requirement 2.7).
                return Result<EnvelopeBindings, GenerationError>.Err(new GenerationError.MissingSchema(name));
            }

            // Idempotent by name: a duplicate would simply overwrite the identical binding, never a second
            // declaration.
            bound[name] = schema;
        }

        return Result<EnvelopeBindings, GenerationError>.Ok(new EnvelopeBindings(bound));
    }
}

/// <summary>
/// The located fixed Vista envelopes, keyed by their verbatim schema name. Keying by name guarantees each
/// envelope is represented exactly once (Requirement 2.1); the emitter walks <see cref="BoundNames"/> to
/// emit one declaration per name in deterministic order.
/// </summary>
/// <param name="Envelopes">The bound envelopes, keyed by verbatim schema name.</param>
public sealed record EnvelopeBindings(IReadOnlyDictionary<string, OpenApiSchema> Envelopes)
{
    /// <summary>Gets the bound schema for <paramref name="name"/>. Throws if the name was not bound.</summary>
    /// <param name="name">The verbatim envelope name.</param>
    public OpenApiSchema this[string name] => Envelopes[name];

    /// <summary>Returns whether an envelope of the given name was bound.</summary>
    /// <param name="name">The verbatim envelope name.</param>
    public bool Contains(string name) => Envelopes.ContainsKey(name);

    /// <summary>
    /// Attempts to read a bound envelope without throwing. Returns <c>true</c> and sets
    /// <paramref name="schema"/> when present; otherwise <c>false</c>.
    /// </summary>
    /// <param name="name">The verbatim envelope name.</param>
    /// <param name="schema">The bound schema when present.</param>
    public bool TryGet(string name, out OpenApiSchema? schema) => Envelopes.TryGetValue(name, out schema);

    /// <summary>
    /// The bound envelope names in deterministic ordinal order — the order the emitter uses to emit exactly
    /// one declaration per name (Requirements 2.1, 9.2).
    /// </summary>
    public IReadOnlyList<string> BoundNames => DeterministicOrder.OrderNames(Envelopes.Keys);
}

/// <summary>
/// A fixed structural template describing the expected members of a row-parameterized envelope. Held by the
/// <see cref="EnvelopeCatalog"/> for the generic re-lifting step (task 7.2) to match a monomorphized
/// document component against, extract the row type parameter, and collapse it back into a single generic
/// TypeScript type (Requirement 2.6). This is descriptive data only; matching lives in the re-lifting step.
/// </summary>
/// <param name="Name">The logical template name (e.g. <c>ViewListResult</c>, <c>PagedResult</c>).</param>
/// <param name="Members">The expected members, in document order.</param>
public sealed record StructuralTemplate(string Name, IReadOnlyList<EnvelopeTemplateMember> Members);

/// <summary>
/// How a <see cref="EnvelopeTemplateMember"/> is expected to be shaped when matching a document component
/// against a <see cref="StructuralTemplate"/>.
/// </summary>
public enum EnvelopeTemplateMemberKind
{
    /// <summary>A fixed scalar member with an expected OpenAPI <c>type</c>/<c>format</c>.</summary>
    Scalar,

    /// <summary>The array of the row type parameter (<c>TRow[]</c>) — the row-bearing member.</summary>
    RowArray,

    /// <summary>A nested object matching another <see cref="StructuralTemplate"/> by name.</summary>
    NestedTemplate,
}

/// <summary>
/// A single expected member within a <see cref="StructuralTemplate"/>.
/// </summary>
/// <param name="Name">The verbatim, case-sensitive member name.</param>
/// <param name="Kind">The expected shape of the member.</param>
/// <param name="Required">Whether the template requires the member to be present.</param>
/// <param name="ExpectedType">The expected OpenAPI <c>type</c> for a <see cref="EnvelopeTemplateMemberKind.Scalar"/> member.</param>
/// <param name="ExpectedFormat">The expected OpenAPI <c>format</c> for a <see cref="EnvelopeTemplateMemberKind.Scalar"/> member.</param>
/// <param name="NestedTemplateName">The referenced template name for a <see cref="EnvelopeTemplateMemberKind.NestedTemplate"/> member.</param>
public sealed record EnvelopeTemplateMember(
    string Name,
    EnvelopeTemplateMemberKind Kind,
    bool Required,
    string? ExpectedType = null,
    string? ExpectedFormat = null,
    string? NestedTemplateName = null);
