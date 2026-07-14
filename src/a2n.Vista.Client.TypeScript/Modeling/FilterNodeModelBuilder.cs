using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;

namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// The presence-discriminated <c>FilterNode</c> modeling step (task 7.3; design "Design note — <c>FilterNode</c>
/// is discriminated by member presence (no <c>discriminator</c>)"). M18 emits <c>FilterNode</c> as a
/// <b>bare</b> <c>oneOf</c> of the four variants (<c>FilterLeaf</c>/<c>FilterAnd</c>/<c>FilterOr</c>/
/// <c>FilterNot</c>) and deliberately omits an OpenAPI <c>discriminator</c>, because the server converter
/// branches <b>by member presence</b> (<c>and</c>/<c>or</c>/<c>not</c>, or <c>field</c>+<c>op</c> for a leaf),
/// not by a shared discriminant property. This builder binds that family from the resolved document into a
/// pure intermediate <see cref="FilterNodeModel"/> the emitter (task 9.3 <c>FilterNodeEmitter</c>) and the
/// operation graph (task 7.5) consume.
/// </summary>
/// <remarks>
/// <para>
/// <b>Required family (Requirement 2.7 intent).</b> Filtering is part of the read surface —
/// <c>VistaListRequestBody.filter</c>/<c>scope</c> reference <c>FilterNode</c> — so the whole family is
/// treated as required, mirroring the fixed-envelope binding approach (<see cref="EnvelopeCatalog"/>). A
/// missing <c>FilterNode</c> union or any missing variant it references is a fatal
/// <see cref="GenerationError.MissingSchema"/>, returned rather than thrown so the buffered pipeline routes
/// it through the single abort path.
/// </para>
/// <para>
/// <b>Faithful to the wire.</b> The union member type names and their order come from the document's
/// <c>oneOf</c> (document order); the <c>FilterOperator</c> literal union is extracted verbatim from the
/// leaf variant's <c>op</c> string-enum, in document order (Requirement 3.2). There is no
/// <c>discriminator</c> to read, so the model records only the structural facts the emitter needs to emit a
/// presence-discriminated union whose branches narrow on the same required members the server uses
/// (Requirement 2.2 intent). The recursive <c>FilterAnd</c>/<c>FilterOr</c> <c>FilterNode[]</c> and
/// <c>FilterNot</c> <c>FilterNode</c> edges are preserved as by-name references
/// (<see cref="TsType.Named(string)"/>), so the cycle stays a finite by-name edge and supports arbitrary
/// nesting depth (Requirement 2.3).
/// </para>
/// <para>
/// <b>Pure.</b> The builder performs no I/O and mutates nothing except the supplied
/// <see cref="NoticeCollector"/>, to which it records a non-fatal permissive-member notice for any variant
/// member that carries neither a type nor a reference (for example the leaf's permissive <c>value</c>),
/// matching how <see cref="TypeMapper"/> treats a permissive member. Member declarations are stored
/// pre-sorted by ordinal, case-sensitive name so the model never perturbs byte-for-byte determinism
/// (Requirement 9.2); the union member order and the operator literal order stay in document order as the
/// task requires.
/// </para>
/// </remarks>
public sealed class FilterNodeModelBuilder
{
    /// <summary>The verbatim schema name of the presence-discriminated union component.</summary>
    public const string FilterNodeName = "FilterNode";

    /// <summary>The logical name of the derived operator literal union (not a document component).</summary>
    public const string FilterOperatorName = "FilterOperator";

    /// <summary>The leaf variant's operator property name the operator literals are extracted from.</summary>
    public const string OperatorPropertyName = "op";

    /// <summary>
    /// Binds the <c>FilterNode</c> family from the resolved document into a <see cref="FilterNodeModel"/>,
    /// or returns the first fatal <see cref="GenerationError.MissingSchema"/> for a missing union or variant
    /// (Requirement 2.7 intent). Never throws for an absent or malformed family.
    /// </summary>
    /// <param name="document">The resolved document whose name-keyed schema graph is bound against.</param>
    /// <param name="notices">The collector that receives any non-fatal permissive-member notice.</param>
    /// <returns>
    /// <see cref="Result{T, E}.Ok"/> carrying the <see cref="FilterNodeModel"/> when the whole family is
    /// present; otherwise <see cref="Result{T, E}.Err"/> carrying the first
    /// <see cref="GenerationError.MissingSchema"/>.
    /// </returns>
    public Result<FilterNodeModel, GenerationError> Build(ResolvedDocument document, NoticeCollector notices)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(notices);

        // 1. The union component itself must be present and be a oneOf. A FilterNode without its union
        //    definition is, for the read/filter surface, an absent FilterNode contract (Requirement 2.7).
        if (!document.Schemas.TryGetValue(FilterNodeName, out var filterNode) ||
            filterNode.OneOf is not { Count: > 0 } variants)
        {
            return Result<FilterNodeModel, GenerationError>.Err(new GenerationError.MissingSchema(FilterNodeName));
        }

        // 2. The union member type names, in document order (the same order M18 lists the oneOf). Each
        //    member must be a local component $ref; a non-ref (inline) member has no nameable variant to
        //    key the presence-discriminated union on, so a malformed union aborts naming FilterNode.
        var memberTypeNames = new List<string>(variants.Count);
        foreach (var variantRef in variants)
        {
            if (string.IsNullOrEmpty(variantRef.Ref) ||
                !ResolvedDocument.TryGetComponentName(variantRef.Ref, ResolvedDocument.SchemaRefPrefix, out var name))
            {
                return Result<FilterNodeModel, GenerationError>.Err(
                    new GenerationError.MissingSchema(FilterNodeName));
            }

            memberTypeNames.Add(name);
        }

        // 3. Build each variant's declaration from its own component schema, capturing the operator literals
        //    from the leaf's `op` string-enum along the way.
        var members = new List<FilterVariant>(memberTypeNames.Count);
        IReadOnlyList<string>? operatorLiterals = null;

        foreach (var memberName in memberTypeNames)
        {
            if (!document.Schemas.TryGetValue(memberName, out var variantSchema))
            {
                // A variant the union references is missing from components.schemas (Requirement 2.7).
                return Result<FilterNodeModel, GenerationError>.Err(
                    new GenerationError.MissingSchema(memberName));
            }

            var properties = new List<TsProperty>();
            if (variantSchema.Properties is { } schemaProperties)
            {
                foreach (var (propertyName, propertySchema) in schemaProperties)
                {
                    var required = variantSchema.Required.Contains(propertyName, StringComparer.Ordinal);

                    TsType propertyType;
                    if (IsStringEnum(propertySchema))
                    {
                        // The leaf's operator property: reference the single named FilterOperator literal
                        // union rather than inlining the enum, and capture its literals in document order.
                        // Prefer a property literally named `op`; otherwise the first string-enum wins.
                        if (operatorLiterals is null ||
                            string.Equals(propertyName, OperatorPropertyName, StringComparison.Ordinal))
                        {
                            operatorLiterals = propertySchema.Enum!;
                        }

                        propertyType = TsType.Named(FilterOperatorName);
                    }
                    else
                    {
                        propertyType = MapVariantMember(propertySchema, memberName, propertyName, notices);
                    }

                    properties.Add(new TsProperty(propertyName, propertyType, Optional: !required));
                }
            }

            // Members are stored pre-sorted by ordinal name so the model layer never depends on the
            // document's property enumeration order (Requirement 9.2).
            var orderedProperties = DeterministicOrder.ByName(properties, property => property.Name);
            members.Add(new FilterVariant(memberName, orderedProperties));
        }

        // 4. The operator literal union is essential to the filter surface. If no variant carried an
        //    operator enum, the family is degraded beyond use; abort naming the derived operator type.
        if (operatorLiterals is not { Count: > 0 })
        {
            return Result<FilterNodeModel, GenerationError>.Err(
                new GenerationError.MissingSchema(FilterOperatorName));
        }

        var model = new FilterNodeModel(
            FilterNodeName,
            memberTypeNames,
            members,
            FilterOperatorName,
            operatorLiterals,
            TsType.LiteralUnion(operatorLiterals),
            TsType.Named(FilterNodeName));

        return Result<FilterNodeModel, GenerationError>.Ok(model);
    }

    // Maps a non-operator variant member to its TypeScript type. Handles the by-name recursion edges
    // (`FilterNot.not` → FilterNode, `FilterAnd`/`FilterOr` items → FilterNode[]), recognized scalars
    // (`FilterLeaf.field` → string), and the permissive leaf `value` (typeless, nullable → `unknown | null`).
    private TsType MapVariantMember(
        OpenApiSchema schema,
        string variantContext,
        string propertyContext,
        NoticeCollector notices)
    {
        // A $ref is a by-name reference. For the recursive `not` edge this yields Named("FilterNode"),
        // keeping the cycle a finite by-name edge (Requirement 2.3).
        if (!string.IsNullOrEmpty(schema.Ref))
        {
            return TsType.Named(ExtractRefName(schema.Ref));
        }

        // An array → T[] over the mapped element type; the `and`/`or` items reference FilterNode, so this
        // produces FilterNode[] (Requirement 2.3). A malformed array (no item schema) degrades to unknown[].
        if (IsType(schema, "array"))
        {
            var element = schema.Items is null
                ? TsType.Unknown
                : MapVariantMember(schema.Items, variantContext, propertyContext, notices);
            return TsType.ArrayOf(element);
        }

        // Recognized scalars (kept minimal — the family only uses `string`, but the others keep the mapper
        // faithful should a variant carry one).
        if (IsType(schema, "string"))
        {
            return TsType.String;
        }

        if (IsType(schema, "integer") || IsType(schema, "number"))
        {
            return TsType.Number;
        }

        if (IsType(schema, "boolean"))
        {
            return TsType.Boolean;
        }

        // Permissive / typeless member (the leaf's `value`): it carries neither a type nor a reference, so
        // it degrades to `unknown`, matching how TypeMapper treats a permissive member. A nullable such
        // member is emitted as `unknown | null` to stay faithful to the design's declared FilterLeaf shape.
        notices.AddPermissiveObjectMember(variantContext, propertyContext);
        return schema.Nullable ? new TsNullable(TsType.Unknown) : TsType.Unknown;
    }

    // Resolves a local component $ref (e.g. "#/components/schemas/FilterNode") to its bare name.
    private static string ExtractRefName(string reference) =>
        ResolvedDocument.TryGetComponentName(reference, ResolvedDocument.SchemaRefPrefix, out var name)
            ? name
            : reference;

    private static bool IsType(OpenApiSchema schema, string type) =>
        string.Equals(schema.Type, type, StringComparison.Ordinal);

    private static bool IsStringEnum(OpenApiSchema schema) =>
        IsType(schema, "string") && schema.Enum is { Count: > 0 };
}

/// <summary>
/// The pure intermediate model of the presence-discriminated <c>FilterNode</c> family (task 7.3). Captures
/// exactly what the emitter (task 9.3 <c>FilterNodeEmitter</c>) and the operation graph (task 7.5) need: the
/// union member type names, each variant's member declarations, and the <c>FilterOperator</c> literal union.
/// It holds no <c>discriminator</c>, because the document has none — the union is discriminated by member
/// presence (design note).
/// </summary>
/// <param name="UnionName">The union type name (<c>FilterNode</c>), used verbatim.</param>
/// <param name="MemberTypeNames">
/// The union member type names in <b>document order</b> (from the document's <c>oneOf</c>): the emitter
/// renders <c>type FilterNode = FilterLeaf | FilterAnd | FilterOr | FilterNot;</c> from these.
/// </param>
/// <param name="Members">
/// The variant declarations, one per <see cref="MemberTypeNames"/> entry in the same document order; each
/// carries its member properties pre-sorted by ordinal name (Requirement 9.2).
/// </param>
/// <param name="OperatorUnionName">The operator literal union type name (<c>FilterOperator</c>).</param>
/// <param name="OperatorLiterals">
/// The operator literal values in <b>document order</b>, extracted verbatim from the leaf variant's <c>op</c>
/// string-enum (Requirement 3.2).
/// </param>
/// <param name="OperatorUnion">
/// The operator literal union as a <see cref="TsType"/> (<c>TsLiteralUnion</c> over <see cref="OperatorLiterals"/>).
/// </param>
/// <param name="UnionReference">
/// A convenience by-name reference to the union (<c>TsType.Named("FilterNode")</c>) — the same recursive
/// edge the variants use, exposed for the operation graph binding <c>VistaListRequestBody.filter</c>.
/// </param>
public sealed record FilterNodeModel(
    string UnionName,
    IReadOnlyList<string> MemberTypeNames,
    IReadOnlyList<FilterVariant> Members,
    string OperatorUnionName,
    IReadOnlyList<string> OperatorLiterals,
    TsType OperatorUnion,
    TsType UnionReference);

/// <summary>
/// One member of the presence-discriminated <c>FilterNode</c> union — a variant interface such as
/// <c>FilterLeaf</c>, <c>FilterAnd</c>, <c>FilterOr</c>, or <c>FilterNot</c>. The variant's required members
/// are the presence key the union narrows on (design note): a value narrows to exactly one member by which
/// key is present (Requirement 2.2 intent).
/// </summary>
/// <param name="Name">The variant interface name, used verbatim (e.g. <c>FilterLeaf</c>).</param>
/// <param name="Properties">
/// The variant's member declarations, pre-sorted by ordinal, case-sensitive name (Requirement 9.2). The
/// recursive edges are by-name references: <c>FilterAnd.and</c>/<c>FilterOr.or</c> are <c>FilterNode[]</c>
/// and <c>FilterNot.not</c> is <c>FilterNode</c> (Requirement 2.3); <c>FilterLeaf.op</c> references the
/// named <c>FilterOperator</c> literal union.
/// </param>
public sealed record FilterVariant(string Name, IReadOnlyList<TsProperty> Properties);
