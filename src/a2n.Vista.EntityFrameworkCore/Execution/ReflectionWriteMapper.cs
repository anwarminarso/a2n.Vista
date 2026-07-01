// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using a2n.Vista.Authoring;
using a2n.Vista.Metadata;
using a2n.Vista.Write;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// The reflection-based, throwaway fallback that builds a <see cref="WriteMapper"/> for a view from the
/// captured <see cref="WritableFieldMapping"/> lambdas delivered through the Core
/// <see cref="IWriteFacetRegistry"/> (Decision Log D119). It is the interchangeable counterpart of the
/// future source-generated mapper held in <see cref="GeneratedWriteMapperStore"/>: the executor resolves
/// exactly one <see cref="WriteMapper"/> per write through the fixed-signature seam and never branches on
/// which implementation produced it (Requirements R13.1, R13.4).
/// </summary>
/// <remarks>
/// <para>
/// For each view this type compiles, once and caches, an <see cref="Action{T1, T2}"/> that performs the
/// whitelisted assignment <c>entity.&lt;EntityMember&gt; = model.&lt;CrudMember&gt;</c> for every
/// <see cref="WritableFieldMapping"/>, rebinding the authored <see cref="WritableFieldMapping.From"/> and
/// <see cref="WritableFieldMapping.To"/> selectors onto the boxed <see cref="object"/> seam parameters.
/// The compiled delegate is the <em>only</em> channel through which client values reach the entity, so a
/// member absent from the whitelist is never written and never raises an error (Requirements R4.1, R4.2).
/// </para>
/// <para>
/// <b>Defense in depth (Requirements R5.1, R5.3).</b> On top of the build-time authoring guards
/// (VISTA0031/0032 / the interim startup fail-fast), the compiler additionally skips any mapping whose
/// target is one of the view's <see cref="ViewMetadata.KeyFields"/> or the facet's concurrency-token
/// member, and skips any non-scalar (navigation) target. A skipped mapping leaves the corresponding
/// entity member byte-identical to its pre-write value; no error is raised (Property 1).
/// </para>
/// <para>
/// <b>AOT hygiene (Requirement R13.5).</b> Compiling the captured lambdas at runtime is not
/// trim/AOT-safe, so every entry point that reaches the compilation carries
/// <see cref="RequiresUnreferencedCodeAttribute"/>. The annotation is confined to this type; the
/// generated write path (<see cref="GeneratedWriteMapperStore"/>) stays warning-free.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(
    "The reflection write mapper compiles the captured MapWritable selectors at runtime; use the " +
    "source-generated write mapper (GeneratedWriteMapperStore) for the AOT-clean path.")]
public sealed class ReflectionWriteMapper
{
    private readonly IWriteFacetRegistry _facetRegistry;
    private readonly ConcurrentDictionary<string, WriteMapper> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new <see cref="ReflectionWriteMapper"/> over the Core write-facet registry that
    /// delivers the captured <see cref="CrudFacetDefinition"/> for each writable view.
    /// </summary>
    /// <param name="facetRegistry">The per-view write-facet lookup populated at registration time.</param>
    /// <exception cref="ArgumentNullException"><paramref name="facetRegistry"/> is <see langword="null"/>.</exception>
    public ReflectionWriteMapper(IWriteFacetRegistry facetRegistry)
    {
        ArgumentNullException.ThrowIfNull(facetRegistry);
        _facetRegistry = facetRegistry;
    }

    /// <summary>
    /// Returns the compiled, cached <see cref="WriteMapper"/> for <paramref name="view"/>, building it on
    /// first use from the view's captured write facet. The compiled delegate assigns only the whitelisted
    /// scalar members that are neither a key field nor the concurrency token (defense in depth).
    /// </summary>
    /// <param name="view">The writable view whose write mapper is requested.</param>
    /// <returns>The whitelisted <see cref="WriteMapper"/> for the view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No write facet is registered for the view (for example a read-only view). Callers that need the
    /// indistinguishable not-found / no-plan behavior should check the registry before resolving a mapper.
    /// </exception>
    public WriteMapper GetOrCreate(ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return _cache.GetOrAdd(view.Name, _ => Build(view));
    }

    private WriteMapper Build(ViewMetadata view)
    {
        if (!_facetRegistry.TryGet(view.Name, out var facet))
        {
            throw new InvalidOperationException(
                $"No write facet is registered for view '{view.Name}', so a reflection write mapper cannot " +
                "be built. A writable view must publish a captured CRUD facet through the write-facet registry.");
        }

        // Members the client must never assign, regardless of the authored whitelist (Requirements R5.1,
        // R5.3). The build-time guards already reject such mappings; this is the runtime safety net.
        var keyFields = new HashSet<string>(view.KeyFields, StringComparer.Ordinal);
        var concurrencyMember = GetConcurrencyMemberName(facet);

        // The boxed seam parameters: (object model, object entity).
        var modelParam = Expression.Parameter(typeof(object), "model");
        var entityParam = Expression.Parameter(typeof(object), "entity");

        // Down-cast once to the strongly-typed contract/entity; every assignment shares these locals.
        var typedModel = Expression.Convert(modelParam, facet.CrudType);
        var typedEntity = Expression.Convert(entityParam, facet.EntityType);

        var assignments = new List<Expression>();
        foreach (var mapping in facet.WritableFields)
        {
            // Defense in depth: never assign a key field or the concurrency token, and never assign a
            // navigation (non-scalar) member. Skipped mappings leave the entity member untouched.
            if (keyFields.Contains(mapping.EntityMember)
                || string.Equals(mapping.EntityMember, concurrencyMember, StringComparison.Ordinal)
                || !IsScalar(mapping.To.ReturnType))
            {
                continue;
            }

            // value  = ((TCrud)model).<CrudMember>
            var value = Rebind(mapping.From, typedModel);
            // target = ((TEntity)entity).<EntityMember>   (an assignable member access)
            var target = Rebind(mapping.To, typedEntity);

            assignments.Add(Expression.Assign(target, value));
        }

        // A whitelist that skips down to nothing yields a conforming no-op mapper rather than throwing:
        // the write is a valid "no net change" (Requirement R2.2) and raises no error.
        Expression body = assignments.Count == 0 ? Expression.Empty() : Expression.Block(assignments);

        var lambda = Expression.Lambda<Action<object, object>>(body, modelParam, entityParam);
        var compiled = lambda.Compile();
        return new WriteMapper(compiled);
    }

    /// <summary>
    /// Rebinds a single-parameter selector's body onto <paramref name="replacement"/> so the captured
    /// <c>x =&gt; x.Member</c> lambda becomes a member access over the down-cast seam local, preserving
    /// the original member (and therefore its assignability for the <see cref="WritableFieldMapping.To"/>
    /// selector).
    /// </summary>
    private static Expression Rebind(LambdaExpression selector, Expression replacement)
    {
        var parameter = selector.Parameters[0];
        var body = new ParameterReplacer(parameter, replacement).Visit(selector.Body);
        return body;
    }

    /// <summary>
    /// Extracts the concurrency-token member name from the facet's token selector, or
    /// <see langword="null"/> when the view declares no token. Mirrors the authoring-side member-name
    /// extraction (unwrapping the compiler-inserted <see cref="ExpressionType.Convert"/>).
    /// </summary>
    private static string? GetConcurrencyMemberName(CrudFacetDefinition facet)
    {
        if (facet.ConcurrencyToken is not { } selector)
        {
            return null;
        }

        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        return body is MemberExpression member ? member.Member.Name : null;
    }

    /// <summary>
    /// Determines whether <paramref name="type"/> is a scalar (assignable) member type rather than a
    /// navigation. Nullable value types are unwrapped; <see cref="string"/> and <c>byte[]</c> count as
    /// scalar, every other reference type is treated as a navigation and skipped (Requirement R4.5).
    /// </summary>
    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string) || underlying == typeof(byte[]))
        {
            return true;
        }

        // Primitives, enums, and any struct (decimal, DateTime, DateTimeOffset, DateOnly, TimeOnly,
        // TimeSpan, Guid, ...) are scalar; a non-string/non-byte[] reference type is a navigation.
        return underlying.IsValueType;
    }

    /// <summary>
    /// Replaces every occurrence of a single lambda parameter with a supplied expression, used to rebind
    /// the captured selectors onto the down-cast seam locals.
    /// </summary>
    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == parameter ? replacement : base.VisitParameter(node);
    }
}
