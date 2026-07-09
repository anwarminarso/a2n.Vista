// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Equatable incremental data models for the write-mapper pipeline (D121, source-generator-write-mapper).
//
// WHY THESE EXIST (incremental caching, R1.3 / Phase 1-2 caching contract):
//   The WriteMapperGenerator is a second IIncrementalGenerator added to a2n.Vista.SourceGenerators. Its
//   semantic transform must produce a FULLY EQUATABLE value model so Roslyn caches unchanged views and an
//   unrelated edit does NOT regenerate every write mapper. These records carry only strings/bools, an
//   equatable LocationInfo SURROGATE (not the non-value-equal Microsoft.CodeAnalysis.Location), and
//   sequences wrapped in EquatableArray<T> so order-sensitive structural equality holds.
//
//   Following the Phase 1/2 convention (see ViewModel in ViewAccessorGenerator.cs), each record uses
//   get-only auto properties set through the constructor rather than positional/`init` members: this
//   avoids the System.Runtime.CompilerServices.IsExternalInit shim netstandard2.0 would otherwise need.

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// Fully equatable description of a candidate typed Style B writable view discovered by the
    /// <c>WriteMapperGenerator</c>. Equality is value-based and covers every declared member, including
    /// the (order-sensitive) <see cref="Mappings"/> and <see cref="DeclaredKeyMembers"/> sequences, so
    /// the incremental pipeline can reuse cached output for an unchanged view (the Phase 1/2 caching
    /// contract). Reconstruct a reportable location from <see cref="Location"/> via
    /// <see cref="LocationInfo.ToLocation"/> at diagnostic-report time.
    /// </summary>
    internal sealed record WriteMapperModel
    {
        public WriteMapperModel(
            string @namespace,
            string className,
            string viewFqn,
            string crudTypeFqn,
            string entityTypeFqn,
            bool isPartial,
            bool isAbstract,
            bool hasNamedCrudType,
            bool hasPublicParameterlessCtor,
            bool hasCrudFacet,
            bool analyzable,
            EquatableArray<WriteMappingModel> mappings,
            string concurrencyTokenMember,
            EquatableArray<string> declaredKeyMembers,
            string unanalyzableExpression,
            LocationInfo location)
        {
            Namespace = @namespace;
            ClassName = className;
            ViewFqn = viewFqn;
            CrudTypeFqn = crudTypeFqn;
            EntityTypeFqn = entityTypeFqn;
            IsPartial = isPartial;
            IsAbstract = isAbstract;
            HasNamedCrudType = hasNamedCrudType;
            HasPublicParameterlessCtor = hasPublicParameterlessCtor;
            HasCrudFacet = hasCrudFacet;
            Analyzable = analyzable;
            Mappings = mappings;
            ConcurrencyTokenMember = concurrencyTokenMember;
            DeclaredKeyMembers = declaredKeyMembers;
            UnanalyzableExpression = unanalyzableExpression;
            Location = location;
        }

        /// <summary>Declaring namespace, or <c>null</c> for the global namespace.</summary>
        public string Namespace { get; }

        /// <summary>The view class name (without namespace).</summary>
        public string ClassName { get; }

        /// <summary>Fully-qualified (<c>global::</c>-prefixed) name of the view type.</summary>
        public string ViewFqn { get; }

        /// <summary>Fully-qualified (<c>global::</c>-prefixed) name of <c>TCrud</c> (the write model).</summary>
        public string CrudTypeFqn { get; }

        /// <summary>Fully-qualified (<c>global::</c>-prefixed) name of <c>TEntity</c> (the mapped entity).</summary>
        public string EntityTypeFqn { get; }

        /// <summary>Whether the view is declared <c>partial</c>.</summary>
        public bool IsPartial { get; }

        /// <summary>Whether the view is declared <c>abstract</c> (abstract views are not candidates).</summary>
        public bool IsAbstract { get; }

        /// <summary>
        /// Whether <c>TCrud</c> is a named type. <c>false</c> when <c>TCrud</c> is <c>object</c>, an
        /// anonymous type, or otherwise not a named type — no generated mapper is emitted (R1.4).
        /// </summary>
        public bool HasNamedCrudType { get; }

        /// <summary>
        /// Whether the view has a public parameterless constructor. When <c>false</c>, the generated
        /// <c>[ModuleInitializer]</c> cannot instantiate the view to read its runtime <c>Name</c>, so
        /// neither the mapper nor the initializer is emitted (R6.5).
        /// </summary>
        public bool HasPublicParameterlessCtor { get; }

        /// <summary>Whether the view declares a CRUD facet (a <c>CrudOn</c>/<c>MapWritable</c> chain).</summary>
        public bool HasCrudFacet { get; }

        /// <summary>
        /// Whether the view's <c>MapWritable</c> chain is statically analyzable. <c>false</c> when any
        /// mapping selector is not a simple member selector after conversion unwrapping, or the view has
        /// no named <c>TCrud</c>; drives the VISTA0033 reflection fallback (R1.5, R2.4, R8).
        /// </summary>
        public bool Analyzable { get; }

        /// <summary>
        /// The extracted <c>(CrudMember, EntityMember)</c> pairs in textual declaration order. Wrapped in
        /// <see cref="EquatableArray{T}"/> so the (order-sensitive) sequence participates in the record's
        /// value equality (R2.1, R2.2).
        /// </summary>
        public EquatableArray<WriteMappingModel> Mappings { get; }

        /// <summary>
        /// The entity member named by the CRUD facet's concurrency token (from <c>WithConcurrencyToken</c>),
        /// or <c>null</c> when none is declared. A token target is skipped by the defense-in-depth rules
        /// (R5.2).
        /// </summary>
        public string ConcurrencyTokenMember { get; }

        /// <summary>
        /// The statically declared key members (from <c>.Key(...)</c>/<c>.PrimaryKey()</c>). Wrapped in
        /// <see cref="EquatableArray{T}"/> so the sequence participates in value equality. A key target is
        /// skipped by the defense-in-depth rules (R5.1).
        /// </summary>
        public EquatableArray<string> DeclaredKeyMembers { get; }

        /// <summary>
        /// When <see cref="Analyzable"/> is <c>false</c> because a <c>MapWritable</c> mapping is not a
        /// simple member selector (or the two-selector overload was not used), the source text of the
        /// first (declaration-ordered) offending expression — used to name it in the VISTA0033 warning
        /// (R8.2). <c>null</c> when there is no such expression (e.g. the view is unanalyzable only
        /// because it has no named <c>TCrud</c>, which is skipped silently rather than warned). A plain
        /// string, so the model stays value-equal.
        /// </summary>
        public string UnanalyzableExpression { get; }

        /// <summary>
        /// Equatable surrogate for the view class identifier's source location, used to report
        /// diagnostics. A <see cref="LocationInfo"/> (not a raw <see cref="Microsoft.CodeAnalysis.Location"/>)
        /// so the model stays value-equal and incremental caching is preserved.
        /// </summary>
        public LocationInfo Location { get; }
    }

    /// <summary>
    /// A single captured <c>MapWritable</c> mapping: the source member on <c>TCrud</c>, the target member
    /// on <c>TEntity</c>, and whether the target is a <c>Scalar_Member</c> (a value type with
    /// <c>Nullable&lt;T&gt;</c> unwrapped, <c>string</c>, or <c>byte[]</c>). A record so it is value-equal
    /// and implements <see cref="System.IEquatable{T}"/>, satisfying the <see cref="EquatableArray{T}"/>
    /// element constraint.
    /// </summary>
    internal sealed record WriteMappingModel
    {
        public WriteMappingModel(
            string crudMember,
            string entityMember,
            bool targetIsScalar,
            LocationInfo location)
        {
            CrudMember = crudMember;
            EntityMember = entityMember;
            TargetIsScalar = targetIsScalar;
            Location = location;
        }

        /// <summary>The source member name on <c>TCrud</c> (the <c>From</c> selector).</summary>
        public string CrudMember { get; }

        /// <summary>The target member name on <c>TEntity</c> (the <c>To</c> selector).</summary>
        public string EntityMember { get; }

        /// <summary>
        /// Whether the target is a <c>Scalar_Member</c>: a value type (after unwrapping
        /// <c>Nullable&lt;T&gt;</c>), <c>string</c>, or <c>byte[]</c>. A non-scalar (navigation) target is
        /// skipped by the defense-in-depth rules and reported as VISTA0031 (R5.3).
        /// </summary>
        public bool TargetIsScalar { get; }

        /// <summary>
        /// Equatable surrogate for the mapping's source location, used to report per-mapping diagnostics
        /// (VISTA0031/VISTA0032/VISTA0033). A <see cref="LocationInfo"/> so the model stays value-equal.
        /// </summary>
        public LocationInfo Location { get; }
    }
}
