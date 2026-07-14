using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;

namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// The per-view DTO modeling step (task 7.4; design §A.5 step 3 "DTO modeling"). Turns a named object
/// component (a view's <c>TRow</c> read model or, for a writable view, its <c>TCrud</c> write model) into a
/// <see cref="TsTypeDecl"/> by mapping each of its schema properties through <see cref="TypeMapper"/>
/// (Requirements 3.1–3.7), and exposes the by-name references (<c>RowType</c>/<c>CrudType</c>) the
/// operation-graph step (task 7.5) uses to bind those DTOs to each view.
/// </summary>
/// <remarks>
/// <para>
/// <b>Verbatim, wire-faithful mapping (Requirement 3).</b> Each property is mapped by
/// <see cref="TypeMapper.MapProperty"/>, so property names are used exactly and case-sensitively
/// (Requirement 3.1), string enums become literal unions in document order (Requirement 3.2), nullable
/// members include <c>null</c> (Requirement 3.3), members absent from the schema's <c>required</c> list are
/// optional (Requirement 3.4), recognized scalars map per the scalar table (Requirement 3.5), and a
/// permissive/unconstrained member or an unrecognized scalar degrades to <c>unknown</c> with a non-fatal
/// notice (Requirements 3.6, 3.7). A property that is itself a <c>$ref</c> maps to a by-name reference; an
/// inline structured object degrades to <c>unknown</c>, matching the scalar mapper's scope.
/// </para>
/// <para>
/// <b>Deterministic member order (Requirement 9.2).</b> The mapped members are stored pre-sorted by
/// ordinal, case-sensitive name via <see cref="DeterministicOrder"/>, so the declaration never depends on
/// the document's property enumeration order.
/// </para>
/// <para>
/// <b>Pure.</b> The builder performs no I/O and mutates nothing except the supplied
/// <see cref="NoticeCollector"/>. Looking up a component that is absent from <c>components.schemas</c> is a
/// fatal <see cref="GenerationError.MissingSchema"/> (Requirement 2.7), returned rather than thrown so the
/// buffered pipeline routes it through the single abort path. Building a decl directly from a schema never
/// fails: an unmappable member degrades, it is never omitted and never fatal (Requirement 3.6/3.7).
/// </para>
/// </remarks>
public sealed class DtoModelBuilder
{
    private readonly TypeMapper _typeMapper;

    /// <summary>Creates a builder that maps properties through a fresh <see cref="TypeMapper"/>.</summary>
    public DtoModelBuilder()
        : this(new TypeMapper())
    {
    }

    /// <summary>Creates a builder that maps properties through the supplied <paramref name="typeMapper"/>.</summary>
    /// <param name="typeMapper">The scalar/type mapper used to map each DTO property.</param>
    public DtoModelBuilder(TypeMapper typeMapper)
    {
        ArgumentNullException.ThrowIfNull(typeMapper);
        _typeMapper = typeMapper;
    }

    /// <summary>
    /// Creates a by-name reference to a DTO component (e.g. <c>CustomerRow</c>), suitable for a view's
    /// <c>RowType</c>/<c>CrudType</c> binding. The name is used verbatim (Requirement 2.5).
    /// </summary>
    /// <param name="componentName">The DTO component name, used verbatim.</param>
    public static TsType Reference(string componentName)
    {
        ArgumentException.ThrowIfNullOrEmpty(componentName);
        return TsType.Named(componentName);
    }

    /// <summary>
    /// Builds the per-view DTO binding: a verbatim by-name <c>RowType</c> reference and, when the view is
    /// writable, a <c>CrudType</c> reference. This is the clean API the operation-graph step (task 7.5)
    /// calls once it has discovered which component is the view's row model and which (if any) is its
    /// write model; the actual discovery of those component names lives in task 7.5.
    /// </summary>
    /// <param name="viewName">The view name the binding is for, used verbatim.</param>
    /// <param name="rowComponentName">The row DTO component name (the view's <c>TRow</c>).</param>
    /// <param name="crudComponentName">
    /// The write-model DTO component name (the view's <c>TCrud</c>), or <c>null</c> for a read-only view.
    /// </param>
    public static ViewDtoBinding BindView(string viewName, string rowComponentName, string? crudComponentName)
    {
        ArgumentException.ThrowIfNullOrEmpty(viewName);
        ArgumentException.ThrowIfNullOrEmpty(rowComponentName);

        var crudType = string.IsNullOrEmpty(crudComponentName) ? null : Reference(crudComponentName);
        return new ViewDtoBinding(viewName, Reference(rowComponentName), crudType);
    }

    /// <summary>
    /// Builds the <see cref="TsTypeDecl"/> for a named DTO component in the resolved document, looking the
    /// schema up by name and mapping its properties. A component absent from <c>components.schemas</c> is a
    /// fatal <see cref="GenerationError.MissingSchema"/> (Requirement 2.7).
    /// </summary>
    /// <param name="componentName">The DTO component name to look up and model.</param>
    /// <param name="document">The resolved document whose schema graph is bound against.</param>
    /// <param name="notices">The collector that receives any non-fatal degradation notice.</param>
    /// <returns>
    /// <see cref="Result{T, E}.Ok"/> carrying the DTO declaration when the component is present; otherwise
    /// <see cref="Result{T, E}.Err"/> carrying the <see cref="GenerationError.MissingSchema"/>.
    /// </returns>
    public Result<TsTypeDecl, GenerationError> BuildDecl(
        string componentName,
        ResolvedDocument document,
        NoticeCollector notices)
    {
        ArgumentException.ThrowIfNullOrEmpty(componentName);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(notices);

        if (!document.Schemas.TryGetValue(componentName, out var schema))
        {
            return Result<TsTypeDecl, GenerationError>.Err(new GenerationError.MissingSchema(componentName));
        }

        return Result<TsTypeDecl, GenerationError>.Ok(BuildDecl(componentName, schema, notices));
    }

    /// <summary>
    /// Builds the <see cref="TsTypeDecl"/> for a set of named DTO components, aborting on the first component
    /// absent from <c>components.schemas</c> (Requirement 2.7). The resulting declarations are returned in
    /// deterministic ordinal order by component name (Requirement 9.2), independent of the order supplied.
    /// </summary>
    /// <param name="componentNames">The DTO component names to look up and model.</param>
    /// <param name="document">The resolved document whose schema graph is bound against.</param>
    /// <param name="notices">The collector that receives any non-fatal degradation notice.</param>
    /// <returns>
    /// <see cref="Result{T, E}.Ok"/> carrying the ordered declarations when every component is present;
    /// otherwise <see cref="Result{T, E}.Err"/> carrying the first
    /// <see cref="GenerationError.MissingSchema"/>.
    /// </returns>
    public Result<IReadOnlyList<TsTypeDecl>, GenerationError> BuildDecls(
        IEnumerable<string> componentNames,
        ResolvedDocument document,
        NoticeCollector notices)
    {
        ArgumentNullException.ThrowIfNull(componentNames);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(notices);

        // Order the names deterministically first so both the "first missing" report and the resulting
        // declaration order are stable across runs and operating systems (Requirement 9.2).
        var decls = new List<TsTypeDecl>();
        foreach (var componentName in DeterministicOrder.OrderNames(componentNames))
        {
            var result = BuildDecl(componentName, document, notices);
            if (!result.IsOk)
            {
                return Result<IReadOnlyList<TsTypeDecl>, GenerationError>.Err(result.Error);
            }

            decls.Add(result.Value);
        }

        return Result<IReadOnlyList<TsTypeDecl>, GenerationError>.Ok(decls);
    }

    /// <summary>
    /// Builds the <see cref="TsTypeDecl"/> for a DTO directly from its schema. Never fails: an unmappable
    /// member degrades to <c>unknown</c> with a notice rather than being omitted or aborting generation
    /// (Requirements 3.6, 3.7). Members are mapped through <see cref="TypeMapper.MapProperty"/> and stored
    /// pre-sorted by ordinal, case-sensitive name (Requirement 9.2). A schema with no properties yields an
    /// empty-member declaration.
    /// </summary>
    /// <param name="declarationName">The declared interface name, used verbatim.</param>
    /// <param name="schema">The DTO object schema whose properties are mapped.</param>
    /// <param name="notices">The collector that receives any non-fatal degradation notice.</param>
    /// <returns>The DTO declaration.</returns>
    public TsTypeDecl BuildDecl(string declarationName, OpenApiSchema schema, NoticeCollector notices)
    {
        ArgumentException.ThrowIfNullOrEmpty(declarationName);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(notices);

        var members = new List<TsProperty>();

        if (schema.Properties is { } properties)
        {
            foreach (var (propertyName, propertySchema) in properties)
            {
                var required = schema.Required.Contains(propertyName, StringComparer.Ordinal);

                // The declaration name doubles as the notice context, so a degraded member is identified by
                // the DTO it belongs to (there is no separate "view" label at DTO-modeling time).
                var member = _typeMapper.MapProperty(
                    propertyName,
                    propertySchema,
                    required,
                    declarationName,
                    notices);

                members.Add(member);
            }
        }

        // Store members in the deterministic by-name order so the declaration never depends on the
        // document's property enumeration order (Requirement 9.2).
        var orderedMembers = DeterministicOrder.ByName(members, member => member.Name);
        return new TsTypeDecl(declarationName, orderedMembers);
    }
}

/// <summary>
/// The per-view DTO binding produced by <see cref="DtoModelBuilder.BindView"/>: a view's verbatim by-name
/// <c>RowType</c> reference and, when the view is writable, its <c>CrudType</c> reference. Consumed by the
/// operation-graph step (task 7.5) to populate each <c>ViewModel</c>'s row/crud types.
/// </summary>
/// <param name="ViewName">The view name the binding is for.</param>
/// <param name="RowType">The by-name reference to the view's <c>TRow</c> DTO (Requirement 2.5).</param>
/// <param name="CrudType">
/// The by-name reference to the view's <c>TCrud</c> write-model DTO, or <c>null</c> for a read-only view.
/// </param>
public sealed record ViewDtoBinding(string ViewName, TsType RowType, TsType? CrudType);
