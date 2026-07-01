using System.Linq.Expressions;
using a2n.Vista.Metadata;

namespace a2n.Vista.Authoring;

/// <summary>
/// Default <see cref="IViewBuilder{TQuery, TCrud}"/> implementation for the class-per-view ("Gaya B")
/// write-capable authoring path. It reuses all read-side behaviour from <see cref="ViewBuilder{TQuery}"/>
/// and adds the typed write facet via <see cref="CrudOn{TEntity}"/>.
/// Authoritative shape: docs/spec/01-view.md §5.2.
/// </summary>
/// <typeparam name="TQuery">The projected (read) row type sent to clients.</typeparam>
/// <typeparam name="TCrud">The typed write contract received from clients.</typeparam>
/// <remarks>
/// A write-capable view must declare exactly the write facet it needs: <see cref="CrudOn{TEntity}"/> is
/// mandatory and the resulting facet must whitelist at least one field (Requirement R3.2). The view also
/// requires a primary key so writes can resolve the target row (Requirement R4.4). Both invariants are
/// enforced when metadata is built. In this release a single write facet per view is supported; a later
/// <see cref="CrudOn{TEntity}"/> call replaces the previous one.
/// </remarks>
internal sealed class ViewBuilder<TQuery, TCrud> : ViewBuilder<TQuery>, IViewBuilder<TQuery, TCrud>
    where TQuery : class
    where TCrud : class
{
    private CrudFacetState? _crudState;

    /// <inheritdoc />
    public ICrudBuilder<TQuery, TCrud, TEntity> CrudOn<TEntity>(
        Expression<Func<TEntity, TQuery>>? projectionForRead = null)
        where TEntity : class
    {
        var state = new CrudFacetState(typeof(TCrud), typeof(TEntity));
        _crudState = state;
        return new CrudBuilder<TQuery, TCrud, TEntity>(state);
    }

    private protected override Type? GetCrudType() => typeof(TCrud);

    private protected override Type? GetCrudEntityType() => _crudState?.EntityType;

    private protected override bool IsReadOnlyView() => false;

    /// <summary>
    /// Exposes the captured Gaya B write facet as a <see cref="CrudFacetDefinition"/>, so registration
    /// can feed it into the same write-facet registry the Gaya A path uses. Returns <see langword="null"/>
    /// when no write facet was declared. Only valid to call after <see cref="ValidateWriteFacet"/>
    /// has passed (which guarantees at least one <c>MapWritable</c> mapping).
    /// </summary>
    private protected override CrudFacetDefinition? GetCrudFacetDefinition() => _crudState?.Build();

    private protected override void ValidateWriteFacet(
        string viewName,
        bool hasPrimaryKey,
        IReadOnlyList<string> keyFields)
    {
        // These startup guards are the interim safety net for the M9 write-DSL analyzer diagnostics
        // (VISTA0030 zero-mapping, VISTA0031 non-scalar/navigation target, VISTA0032 key/token target).
        // Until the source generator reports them at build time, metadata build fails fast at startup so
        // a mass-assignment-unsafe or unresolvable write facet can never reach the request pipeline
        // (Requirements R4.4, R4.6, R5.4).
        if (_crudState is null)
        {
            throw new InvalidOperationException(
                $"View '{viewName}' is a write-capable view (View<{typeof(TQuery).Name}, " +
                $"{typeof(TCrud).Name}>) but never declared a write facet; call CrudOn<TEntity>(...) " +
                "in Configure.");
        }

        // VISTA0030 (interim): the write whitelist must not be empty; write is default-deny (R3.2).
        if (_crudState.WritableFields.Count == 0)
        {
            throw new InvalidOperationException(
                $"The write facet of view '{viewName}' must whitelist at least one field; call " +
                "MapWritable(...) at least once (R3.2). Write is default-deny.");
        }

        if (!hasPrimaryKey)
        {
            throw new InvalidOperationException(
                $"View '{viewName}' has a write facet and therefore requires a primary key; mark one " +
                "projected field with .PrimaryKey() (R4.4).");
        }

        // The members a MapWritable target may never bind to, regardless of the authored whitelist:
        // any resolved key field (row identity is taken from the request, never the body — R5.4) and the
        // concurrency-token member (D30). Mirrors the runtime defense-in-depth in ReflectionWriteMapper.
        var keyFieldSet = new HashSet<string>(keyFields, StringComparer.Ordinal);
        var concurrencyMember = GetConcurrencyMemberName(_crudState.ConcurrencyToken);

        foreach (var mapping in _crudState.WritableFields)
        {
            // VISTA0031 (interim): a MapWritable target must be a scalar (assignable) member, never a
            // navigation / non-scalar reference type (R4.6). Bulk-copying a navigation would let a client
            // reshape related graphs the whitelist never intended to expose.
            if (!IsScalar(mapping.To.ReturnType))
            {
                throw new InvalidOperationException(
                    $"The write facet of view '{viewName}' maps '{mapping.CrudMember}' to entity member " +
                    $"'{mapping.EntityMember}', which is a navigation / non-scalar member. MapWritable " +
                    "targets must be scalar members (R4.6).");
            }

            // VISTA0032 (interim): a MapWritable target must not be a key field or the concurrency token
            // (R5.4). Row identity comes from the request key and the token is compared, never assigned;
            // whitelisting either would reopen mass assignment on protected members.
            if (keyFieldSet.Contains(mapping.EntityMember))
            {
                throw new InvalidOperationException(
                    $"The write facet of view '{viewName}' maps '{mapping.CrudMember}' to key field " +
                    $"'{mapping.EntityMember}'. A key field is never client-assignable; row identity comes " +
                    "from the request key, not the body (R5.4).");
            }

            if (concurrencyMember is not null
                && string.Equals(mapping.EntityMember, concurrencyMember, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The write facet of view '{viewName}' maps '{mapping.CrudMember}' to the concurrency " +
                    $"token '{mapping.EntityMember}'. The token is compared for optimistic concurrency, " +
                    "never client-assigned (R5.4).");
            }
        }
    }

    /// <summary>
    /// Extracts the concurrency-token member name from the facet's token selector, or
    /// <see langword="null"/> when the view declares none. Unwraps the compiler-inserted
    /// <see cref="ExpressionType.Convert"/>, mirroring the EF-layer <c>ReflectionWriteMapper</c> so the
    /// startup guard and the runtime safety net agree on which member is the token.
    /// </summary>
    private static string? GetConcurrencyMemberName(LambdaExpression? tokenSelector)
    {
        if (tokenSelector is null)
        {
            return null;
        }

        var body = tokenSelector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        return body is MemberExpression member ? member.Member.Name : null;
    }

    /// <summary>
    /// Determines whether <paramref name="type"/> is a scalar (assignable) member type rather than a
    /// navigation. Nullable value types are unwrapped; <see cref="string"/> and <c>byte[]</c> count as
    /// scalar, every other reference type is treated as a navigation. Mirrors the EF-layer
    /// <c>ReflectionWriteMapper</c> so the startup guard rejects exactly the targets the runtime mapper
    /// would otherwise skip (R4.6).
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
}
