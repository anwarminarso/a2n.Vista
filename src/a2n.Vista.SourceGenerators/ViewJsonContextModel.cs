// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Equatable incremental data models for the per-view JsonTypeInfo pipeline (D125,
// source-generator-json-typeinfo).
//
// WHY THESE EXIST (incremental caching, R7.2 / Phase 1-2-3-4 caching contract):
//   The ViewJsonContextGenerator is a fourth IIncrementalGenerator added to a2n.Vista.SourceGenerators.
//   Its semantic transform must produce a FULLY EQUATABLE value model so Roslyn caches unchanged views
//   and an unrelated edit does NOT regenerate every view's JsonTypeInfo context. These records carry only
//   strings/bools/enums, an equatable LocationInfo SURROGATE (not the non-value-equal
//   Microsoft.CodeAnalysis.Location), and sequences wrapped in EquatableArray<T> so order-sensitive
//   structural equality holds.
//
//   Following the Phase 1/2/3/4 convention (see ViewModel in ViewAccessorGenerator.cs and
//   WriteMapperModel), each record uses get-only auto properties set through the constructor rather than
//   positional/`init` members: this avoids the System.Runtime.CompilerServices.IsExternalInit shim
//   netstandard2.0 would otherwise need.

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// How the generated <c>JsonTypeInfo</c> must construct a DTO instance during deserialization. Drives
    /// the emitted <c>ObjectCreator</c> vs parameterized/<c>init</c> constructor path so records, init-only,
    /// and required members round-trip (R2.5).
    /// </summary>
    internal enum ObjectConstructionKind
    {
        /// <summary>The DTO has a usable public parameterless constructor — emit a simple <c>ObjectCreator</c>.</summary>
        Parameterless = 0,

        /// <summary>
        /// The DTO is a record or uses init-only/required members — emit the parameterized/<c>init</c>
        /// constructor path so deserialization can populate those members (R2.5).
        /// </summary>
        Parameterized = 1,
    }

    /// <summary>
    /// The classification of a DTO member's type against the Emittable_Shape set. Any member classified
    /// <see cref="NonEmittable"/> makes the whole view not covered (VISTA0051 + reflection fallback),
    /// preferring parity with the oracle over best-effort coverage (R1.4, R1.5).
    /// </summary>
    internal enum MemberShapeKind
    {
        /// <summary>A BCL scalar (<c>int</c>, <c>long</c>, <c>double</c>, <c>decimal</c>, <c>bool</c>, <c>Guid</c>, <c>DateTime</c>, …).</summary>
        Scalar = 0,

        /// <summary><c>string</c>.</summary>
        String = 1,

        /// <summary>An enum (serialized via the seam's <c>JsonStringEnumConverter</c> for parity).</summary>
        Enum = 2,

        /// <summary>A nullable value type (<c>T?</c>) whose underlying type is itself emittable.</summary>
        Nullable = 3,

        /// <summary>A collection (<c>List&lt;T&gt;</c>/<c>T[]</c>/<c>IReadOnlyList&lt;T&gt;</c>) of an emittable element.</summary>
        Collection = 4,

        /// <summary>A nested POCO whose members are all emittable.</summary>
        Nested = 5,

        /// <summary>
        /// A member whose shape the generator cannot emit reflection-free (bespoke/custom converter,
        /// unsupported polymorphic shape, or unresolved generic) — makes the view not covered (R1.5).
        /// </summary>
        NonEmittable = 6,
    }

    /// <summary>
    /// The kind of auxiliary (non-object) <c>JsonTypeInfo</c> the generated resolver must ALSO provide so
    /// that a covered view's DTOs serialize/deserialize with NO reflection fallback in the chain. When the
    /// reflection resolver is removed (the AOT probe scenario, R8.1), System.Text.Json resolves a
    /// property's <c>JsonTypeInfo</c> for these "complex" member shapes from the resolver chain — unlike
    /// plain scalars/strings/enums which it can build from its built-in converters. The generator therefore
    /// emits a dispatch arm + factory for each so the whole Serializable_DTO_Set is self-contained (R2.1,
    /// design "Emittable_Shape" collection-info helpers).
    /// </summary>
    internal enum AuxTypeKind
    {
        /// <summary>A nullable value type (<c>T?</c>) — built via <c>CreateValueInfo</c> + <c>GetNullableConverter</c>.</summary>
        Nullable = 0,

        /// <summary>A collection — built via the matching <c>JsonMetadataServices</c> collection-info helper.</summary>
        Collection = 1,

        /// <summary>
        /// A BCL scalar or <c>string</c> leaf — built via <c>CreateValueInfo</c> + the matching built-in
        /// <c>JsonMetadataServices</c> converter. System.Text.Json can build a simple-type property
        /// converter inline, but a scalar reached as a nullable's underlying type (via
        /// <c>GetNullableConverter</c>) or as a collection element is resolved from the resolver chain, so
        /// the whole reachable leaf set gets an explicit arm — matching the built-in source generator's
        /// completeness — so the covered DTOs (de)serialize with NO reflection fallback (R2.1, R8.1).
        /// </summary>
        Scalar = 2,

        /// <summary>
        /// An enum leaf — built via <c>CreateValueInfo</c> + the converter the seam's <c>options</c> resolve
        /// for the enum (so it rides the seam's registered <c>JsonStringEnumConverter</c> for parity, R2.3,
        /// R6.4). System.Text.Json requires the enum's <c>JsonTypeInfo</c> from the resolver chain when the
        /// reflection fallback is removed, so a covered DTO with an enum member needs this arm (R2.1, R8.1).
        /// </summary>
        Enum = 3,
    }

    /// <summary>
    /// The concrete collection shape of an <see cref="AuxTypeKind.Collection"/> auxiliary type, selecting
    /// the exact <c>JsonMetadataServices</c> collection-info helper the emitter uses (mirroring the built-in
    /// System.Text.Json source generator's helper choice per shape).
    /// </summary>
    internal enum CollectionShapeKind
    {
        /// <summary><c>List&lt;T&gt;</c> → <c>CreateListInfo&lt;List&lt;T&gt;, T&gt;</c> (with an <c>ObjectCreator</c>).</summary>
        List = 0,

        /// <summary><c>T[]</c> → <c>CreateArrayInfo&lt;T&gt;</c>.</summary>
        Array = 1,

        /// <summary><c>IReadOnlyList&lt;T&gt;</c> → <c>CreateIEnumerableInfo&lt;IReadOnlyList&lt;T&gt;, T&gt;</c>.</summary>
        IReadOnlyList = 2,

        /// <summary><c>IReadOnlyCollection&lt;T&gt;</c> → <c>CreateIEnumerableInfo&lt;…, T&gt;</c>.</summary>
        IReadOnlyCollection = 3,

        /// <summary><c>IEnumerable&lt;T&gt;</c> → <c>CreateIEnumerableInfo&lt;…, T&gt;</c>.</summary>
        IEnumerable = 4,

        /// <summary><c>IList&lt;T&gt;</c> → <c>CreateIListInfo&lt;IList&lt;T&gt;, T&gt;</c> (with a <c>List&lt;T&gt;</c> <c>ObjectCreator</c>).</summary>
        IList = 5,

        /// <summary><c>ICollection&lt;T&gt;</c> → <c>CreateICollectionInfo&lt;ICollection&lt;T&gt;, T&gt;</c> (with a <c>List&lt;T&gt;</c> <c>ObjectCreator</c>).</summary>
        ICollection = 6,
    }

    /// <summary>
    /// Fully equatable description of an auxiliary (non-object) type the generated resolver must provide a
    /// <c>JsonTypeInfo</c> for — a nullable value type or a collection reachable from the Serializable_DTO_Set
    /// (for example the envelope's <c>Items</c> member <c>IReadOnlyList&lt;TRow&gt;</c>, or a <c>List&lt;string&gt;</c>
    /// DTO member). Without these arms the no-reflection-fallback chain throws
    /// <c>NotSupportedException</c> for the "complex" member shape (R2.1, R8.1). A record so it is value-equal
    /// and satisfies the <see cref="EquatableArray{T}"/> element constraint.
    /// </summary>
    internal sealed record AuxTypeModel
    {
        public AuxTypeModel(
            string typeFqn,
            AuxTypeKind kind,
            string elementOrUnderlyingFqn,
            CollectionShapeKind collectionShape)
        {
            TypeFqn = typeFqn;
            Kind = kind;
            ElementOrUnderlyingFqn = elementOrUnderlyingFqn;
            CollectionShape = collectionShape;
        }

        /// <summary>Fully-qualified (<c>global::</c>-prefixed) name of the auxiliary type (e.g. <c>decimal?</c>, <c>List&lt;string&gt;</c>).</summary>
        public string TypeFqn { get; }

        /// <summary>Whether this auxiliary type is a nullable value type or a collection.</summary>
        public AuxTypeKind Kind { get; }

        /// <summary>
        /// For a <see cref="AuxTypeKind.Collection"/>, the element type FQN; for a
        /// <see cref="AuxTypeKind.Nullable"/>, the underlying (non-nullable) value type FQN.
        /// </summary>
        public string ElementOrUnderlyingFqn { get; }

        /// <summary>The concrete collection shape (only meaningful when <see cref="Kind"/> is <see cref="AuxTypeKind.Collection"/>).</summary>
        public CollectionShapeKind CollectionShape { get; }
    }

    /// <summary>
    /// Fully equatable description of a candidate typed Style B view discovered by the
    /// <c>ViewJsonContextGenerator</c>. Equality is value-based and covers every declared member,
    /// including the (order-sensitive) <see cref="Dtos"/> and <see cref="NonEmittableMembers"/> sequences,
    /// so the incremental pipeline can reuse cached output for an unchanged view (the Phase 1/2/3/4 caching
    /// contract, R7.2). Reconstruct a reportable location from <see cref="Location"/> via
    /// <see cref="LocationInfo.ToLocation"/> at diagnostic-report time.
    /// </summary>
    internal sealed record ViewJsonContextModel
    {
        public ViewJsonContextModel(
            string @namespace,
            string className,
            string viewFqn,
            string rowTypeFqn,
            string crudTypeFqn,
            bool isWritable,
            bool isPartial,
            bool isAbstract,
            bool hasNamedRowType,
            bool hasNamedCrudType,
            bool hasPublicParameterlessCtor,
            EquatableArray<DtoTypeModel> dtos,
            bool allShapesEmittable,
            EquatableArray<string> nonEmittableMembers,
            EquatableArray<AuxTypeModel> auxTypes,
            LocationInfo location)
        {
            Namespace = @namespace;
            ClassName = className;
            ViewFqn = viewFqn;
            RowTypeFqn = rowTypeFqn;
            CrudTypeFqn = crudTypeFqn;
            IsWritable = isWritable;
            IsPartial = isPartial;
            IsAbstract = isAbstract;
            HasNamedRowType = hasNamedRowType;
            HasNamedCrudType = hasNamedCrudType;
            HasPublicParameterlessCtor = hasPublicParameterlessCtor;
            Dtos = dtos;
            AllShapesEmittable = allShapesEmittable;
            NonEmittableMembers = nonEmittableMembers;
            AuxTypes = auxTypes;
            Location = location;
        }

        /// <summary>Declaring namespace, or <c>null</c> for the global namespace.</summary>
        public string Namespace { get; }

        /// <summary>The view class name (without namespace).</summary>
        public string ClassName { get; }

        /// <summary>Fully-qualified (<c>global::</c>-prefixed) name of the view type.</summary>
        public string ViewFqn { get; }

        /// <summary>Fully-qualified (<c>global::</c>-prefixed) name of <c>TRow</c> (the projected read type).</summary>
        public string RowTypeFqn { get; }

        /// <summary>
        /// Fully-qualified (<c>global::</c>-prefixed) name of <c>TCrud</c> (the write model), or
        /// <c>null</c> for a read-only view (or a writable view whose <c>TCrud</c> is <c>object</c>/anonymous).
        /// </summary>
        public string CrudTypeFqn { get; }

        /// <summary>Whether the view derives <c>View&lt;TQuery, TCrud&gt;</c> (arity-2, writable).</summary>
        public bool IsWritable { get; }

        /// <summary>Whether the view is declared <c>partial</c>.</summary>
        public bool IsPartial { get; }

        /// <summary>Whether the view is declared <c>abstract</c> (abstract views are not candidates).</summary>
        public bool IsAbstract { get; }

        /// <summary>
        /// Whether <c>TQuery</c> is a named type. <c>false</c> when <c>TQuery</c> is <c>object</c> or an
        /// anonymous type — the view is not a serialization candidate (R1.1, R1.3).
        /// </summary>
        public bool HasNamedRowType { get; }

        /// <summary>
        /// Whether a writable view's <c>TCrud</c> is a named type. <c>false</c> when <c>TCrud</c> is
        /// <c>object</c>/anonymous — <c>JsonTypeInfo</c> is generated for the read DTOs only (R1.2).
        /// </summary>
        public bool HasNamedCrudType { get; }

        /// <summary>
        /// Whether the view has a public parameterless constructor. When <c>false</c>, the generated
        /// <c>[ModuleInitializer]</c> cannot instantiate the view to read its runtime <c>Name</c>, so no
        /// context/initializer is emitted and the view stays on the reflection fallback (R1.7, R4.5).
        /// </summary>
        public bool HasPublicParameterlessCtor { get; }

        /// <summary>
        /// The Serializable_DTO_Set for this view — <c>TRow</c>, <c>ViewListResult&lt;TRow&gt;</c>,
        /// <c>PagedResult&lt;TRow&gt;</c>, and (for a writable view with a named <c>TCrud</c>)
        /// <c>TCrud</c>. Wrapped in <see cref="EquatableArray{T}"/> so the (order-sensitive) sequence
        /// participates in the record's value equality.
        /// </summary>
        public EquatableArray<DtoTypeModel> Dtos { get; }

        /// <summary>
        /// Whether every member of every DTO in <see cref="Dtos"/> is an Emittable_Shape. <c>false</c>
        /// classifies the view as not covered → VISTA0051, no emission, reflection fallback (R1.5, R9.2).
        /// </summary>
        public bool AllShapesEmittable { get; }

        /// <summary>
        /// The <c>Type.Member</c> descriptions of members whose shape cannot be emitted reflection-free,
        /// used to compose the VISTA0051 message. Wrapped in <see cref="EquatableArray{T}"/> so the
        /// sequence participates in value equality. Empty when <see cref="AllShapesEmittable"/> is <c>true</c>.
        /// </summary>
        public EquatableArray<string> NonEmittableMembers { get; }

        /// <summary>
        /// The auxiliary (non-object) types the generated resolver must ALSO provide a <c>JsonTypeInfo</c>
        /// for — nullable value types and collections reachable from <see cref="Dtos"/> (notably the
        /// envelope's <c>Items</c> collection <c>IReadOnlyList&lt;TRow&gt;</c> and any collection DTO member)
        /// — so the covered view's DTOs (de)serialize with NO reflection fallback in the chain (R2.1, R8.1).
        /// Collected in a fixed, first-occurrence order and wrapped in <see cref="EquatableArray{T}"/> so the
        /// sequence participates in value equality and the emitted output stays deterministic (R7.4).
        /// </summary>
        public EquatableArray<AuxTypeModel> AuxTypes { get; }

        /// <summary>
        /// Equatable surrogate for the view class identifier's source location, used to report
        /// diagnostics. A <see cref="LocationInfo"/> (not a raw <see cref="Microsoft.CodeAnalysis.Location"/>)
        /// so the model stays value-equal and incremental caching is preserved.
        /// </summary>
        public LocationInfo Location { get; }
    }

    /// <summary>
    /// Fully equatable description of a single DTO in a view's Serializable_DTO_Set: its
    /// <c>global::</c>-qualified type name, the construction kind that drives the emitted deserialization
    /// path (R2.5), and its serializable members in declaration order. A record so it is value-equal and
    /// implements <see cref="System.IEquatable{T}"/>, satisfying the <see cref="EquatableArray{T}"/>
    /// element constraint.
    /// </summary>
    internal sealed record DtoTypeModel
    {
        public DtoTypeModel(
            string typeFqn,
            ObjectConstructionKind construction,
            EquatableArray<DtoMemberModel> members)
        {
            TypeFqn = typeFqn;
            Construction = construction;
            Members = members;
        }

        /// <summary>Fully-qualified (<c>global::</c>-prefixed) name of the DTO type.</summary>
        public string TypeFqn { get; }

        /// <summary>
        /// How the emitted <c>JsonTypeInfo</c> must construct this DTO during deserialization
        /// (parameterless <c>ObjectCreator</c> vs the parameterized/<c>init</c> constructor path for
        /// records/init-only/required members, R2.5).
        /// </summary>
        public ObjectConstructionKind Construction { get; }

        /// <summary>
        /// The DTO's serializable members in declaration order. Wrapped in <see cref="EquatableArray{T}"/>
        /// so the (order-sensitive) sequence participates in value equality (property ordering is
        /// wire-visible, R6.4).
        /// </summary>
        public EquatableArray<DtoMemberModel> Members { get; }
    }

    /// <summary>
    /// Fully equatable description of a single serializable DTO member: its CLR member name, its
    /// <c>global::</c>-qualified member type, the JSON property name resolved per the seam's naming policy
    /// (for parity, R2.3/R6.4), whether it is read-only (init-only/required handling), and its shape
    /// classification. A record so it is value-equal and implements <see cref="System.IEquatable{T}"/>,
    /// satisfying the <see cref="EquatableArray{T}"/> element constraint.
    /// </summary>
    internal sealed record DtoMemberModel
    {
        public DtoMemberModel(
            string memberName,
            string memberTypeFqn,
            string jsonPropertyName,
            bool isReadOnly,
            MemberShapeKind shapeKind)
        {
            MemberName = memberName;
            MemberTypeFqn = memberTypeFqn;
            JsonPropertyName = jsonPropertyName;
            IsReadOnly = isReadOnly;
            ShapeKind = shapeKind;
        }

        /// <summary>The CLR member (property) name on the DTO.</summary>
        public string MemberName { get; }

        /// <summary>Fully-qualified (<c>global::</c>-prefixed) name of the member's type.</summary>
        public string MemberTypeFqn { get; }

        /// <summary>
        /// The JSON property name resolved per the seam's <c>JsonSerializerOptions</c> naming policy, so
        /// the generated context emits the same wire name as the Behavioral_Oracle (parity, R2.3/R6.4).
        /// </summary>
        public string JsonPropertyName { get; }

        /// <summary>
        /// Whether the member is read-only from the serializer's perspective (init-only or has no public
        /// setter), which forces construction through the parameterized/<c>init</c> path (R2.5).
        /// </summary>
        public bool IsReadOnly { get; }

        /// <summary>The member's shape classification against the Emittable_Shape set (R1.4, R1.5).</summary>
        public MemberShapeKind ShapeKind { get; }
    }
}
