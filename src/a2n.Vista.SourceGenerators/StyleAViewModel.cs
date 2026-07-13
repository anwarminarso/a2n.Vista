// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Equatable incremental data model for the Style A (anonymous) coverage pipeline (D129,
// style-a-coverage).
//
// WHY THIS EXISTS (incremental caching, R7.2 / Phase 1-2-3-4-5 caching contract):
//   The StyleAShapeGenerator is a fifth IIncrementalGenerator added to a2n.Vista.SourceGenerators. Unlike
//   the prior phases (which key off a View<...> class declaration), it recognizes
//   ViewTemplate<TDbContext>.AddView<TRow>(...) INVOCATION call sites. Its semantic transform must produce
//   a FULLY EQUATABLE value model so Roslyn caches unchanged call sites and an unrelated edit does NOT
//   regenerate every view's artifacts. This record carries only strings/bools, the equatable
//   DtoTypeModel/DtoMemberModel shapes reused verbatim from the per-view JsonTypeInfo phase (D125), an
//   equatable LocationInfo SURROGATE (not the non-value-equal Microsoft.CodeAnalysis.Location), and the
//   order-sensitive sequences wrapped in EquatableArray<T> so structural equality holds.
//
//   Following the Phase 1/2/3/4/5 convention (see ViewModel in ViewAccessorGenerator.cs, WriteMapperModel,
//   ViewInvokerModel, and ViewJsonContextModel), the record uses get-only auto properties set through the
//   constructor rather than positional/`init` members: this avoids the
//   System.Runtime.CompilerServices.IsExternalInit shim netstandard2.0 would otherwise need. The project
//   builds with <Nullable>disable</Nullable>, so members that are conceptually nullable (TemplateNamespace,
//   ViewName, RowTypeFqn, CrudTypeFqn, CrudDto, Location) are declared as plain reference types and their
//   nullability is documented in XML comments only.
//
//   The DtoTypeModel / DtoMemberModel shapes (TypeFqn, construction kind, members, member JSON property
//   name, shape kind) are REUSED VERBATIM from ViewJsonContextModel.cs (D125) — not duplicated — so the
//   Emittable_Shape analysis and the emitter can be shared with the JsonTypeInfo phase.

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// Fully equatable description of a Style A <c>AddView&lt;TRow&gt;(...)</c> call site discovered by the
    /// <c>StyleAShapeGenerator</c> (D129). Equality is value-based and covers every declared member,
    /// including the (order-sensitive) <see cref="ReadDtos"/> and <see cref="NonEmittableMembers"/>
    /// sequences and the value-equal <see cref="CrudDto"/> record, so the incremental pipeline can reuse
    /// cached output for an unchanged call site (the Phase 1/2/3/4/5 caching contract, R7.2). Reconstruct a
    /// reportable location from <see cref="Location"/> via <see cref="LocationInfo.ToLocation"/> at
    /// diagnostic-report time.
    /// </summary>
    /// <remarks>
    /// The coverage classification the emitters and diagnostics act on (design "Coverage classification"):
    /// <list type="bullet">
    /// <item>constant name + named <c>TRow</c> (+ optional named <c>TCrud</c>) → accessors + read
    /// <c>JsonTypeInfo</c> (+ <c>TCrud</c> <c>JsonTypeInfo</c>) + VISTA0060.</item>
    /// <item>constant name + anonymous <c>TRow</c> + named <c>TCrud</c> → <c>TCrud</c> <c>JsonTypeInfo</c>
    /// only + VISTA0060 (write) + VISTA0061 (read stays RUC by design, D96).</item>
    /// <item>constant name + anonymous <c>TRow</c>, read-only → nothing generated + VISTA0061.</item>
    /// <item>a non-emittable DTO member → that DTO's <c>JsonTypeInfo</c> is skipped + VISTA0063 (a named
    /// <c>TRow</c> still gets its accessor map).</item>
    /// <item>non-constant name → nothing generated + VISTA0062.</item>
    /// </list>
    /// </remarks>
    internal sealed record StyleAViewModel
    {
        public StyleAViewModel(
            string templateNamespace,
            string templateClassName,
            string viewName,
            bool hasConstantName,
            string rowTypeFqn,
            bool hasNamedRowType,
            string crudTypeFqn,
            bool isWritable,
            EquatableArray<DtoTypeModel> readDtos,
            DtoTypeModel crudDto,
            bool readDtosEmittable,
            bool crudDtoEmittable,
            EquatableArray<string> nonEmittableMembers,
            EquatableArray<AuxTypeModel> auxTypes,
            LocationInfo location)
        {
            TemplateNamespace = templateNamespace;
            TemplateClassName = templateClassName;
            ViewName = viewName;
            HasConstantName = hasConstantName;
            RowTypeFqn = rowTypeFqn;
            HasNamedRowType = hasNamedRowType;
            CrudTypeFqn = crudTypeFqn;
            IsWritable = isWritable;
            ReadDtos = readDtos;
            CrudDto = crudDto;
            ReadDtosEmittable = readDtosEmittable;
            CrudDtoEmittable = crudDtoEmittable;
            NonEmittableMembers = nonEmittableMembers;
            AuxTypes = auxTypes;
            Location = location;
        }

        /// <summary>
        /// Declaring namespace of the enclosing <c>ViewTemplate&lt;TDbContext&gt;</c> subclass, or
        /// <c>null</c> for the global namespace.
        /// </summary>
        public string TemplateNamespace { get; }

        /// <summary>The enclosing template class name (without namespace).</summary>
        public string TemplateClassName { get; }

        /// <summary>
        /// The constant-folded <c>AddView</c> <c>name</c> argument — the view-name key generated artifacts
        /// are registered under — or <c>null</c> when the name is not a compile-time constant (see
        /// <see cref="HasConstantName"/>).
        /// </summary>
        public string ViewName { get; }

        /// <summary>
        /// Whether the <c>AddView</c> <c>name</c> argument resolved to a compile-time constant string.
        /// <c>false</c> → the view cannot be keyed statically, so no artifact is emitted and the call site
        /// is reported as VISTA0062, staying on the reflection path (R1.2, R8.3).
        /// </summary>
        public bool HasConstantName { get; }

        /// <summary>
        /// Fully-qualified (<c>global::</c>-prefixed) name of the read row type <c>TRow</c>, or
        /// <c>null</c> when <c>TRow</c> is anonymous or <c>object</c> (unnameable in generated source).
        /// </summary>
        public string RowTypeFqn { get; }

        /// <summary>
        /// Whether <c>TRow</c> is a named (non-anonymous, non-<c>object</c>) type. <c>false</c> → no
        /// read-side artifact (export accessors, read-DTO <c>JsonTypeInfo</c>) is generated and the read
        /// serialization stays <c>[RequiresUnreferencedCode]</c> by design (D96), reported as VISTA0061
        /// (R1.4, R8.2).
        /// </summary>
        public bool HasNamedRowType { get; }

        /// <summary>
        /// Fully-qualified (<c>global::</c>-prefixed) name of the write model <c>TCrud</c> supplied to a
        /// chained <c>.WithCrud&lt;TCrud, TEntity&gt;()</c>, or <c>null</c> for a read-only view.
        /// <c>TCrud</c> is always a named type (the authoring surface forbids an anonymous write model,
        /// D38), so it is nameable and generatable even when <see cref="HasNamedRowType"/> is <c>false</c>.
        /// </summary>
        public string CrudTypeFqn { get; }

        /// <summary>
        /// Whether the <c>AddView</c> call site is continued by <c>.WithCrud&lt;TCrud, TEntity&gt;()</c>
        /// (i.e. the view is writable). Drives whether the write-model <c>TCrud</c> <c>JsonTypeInfo</c> is
        /// emitted, independently of the read <c>TRow</c> being named or anonymous (R1.5, R4.2).
        /// </summary>
        public bool IsWritable { get; }

        /// <summary>
        /// The read Serializable_DTO_Set for a named-<c>TRow</c> view — <c>TRow</c>,
        /// <c>ViewListResult&lt;TRow&gt;</c>, and <c>PagedResult&lt;TRow&gt;</c> — reusing the D125
        /// <see cref="DtoTypeModel"/> shape verbatim. Empty when <see cref="HasNamedRowType"/> is
        /// <c>false</c> (an anonymous row has no nameable read DTOs). Wrapped in
        /// <see cref="EquatableArray{T}"/> so the (order-sensitive) sequence participates in the record's
        /// value equality.
        /// </summary>
        public EquatableArray<DtoTypeModel> ReadDtos { get; }

        /// <summary>
        /// The write-model DTO (<c>TCrud</c>) for a writable view, reusing the D125
        /// <see cref="DtoTypeModel"/> shape verbatim, or <c>null</c> for a read-only view. A single
        /// value-equal record (not an array) because a view has at most one <c>TCrud</c>.
        /// </summary>
        public DtoTypeModel CrudDto { get; }

        /// <summary>
        /// Whether every member of every DTO in <see cref="ReadDtos"/> is an Emittable_Shape. <c>false</c>
        /// → no read-DTO <c>JsonTypeInfo</c> is emitted and the offending members are reported as VISTA0063,
        /// preferring parity with the oracle over best-effort coverage (R1.7, R8.4). A named-<c>TRow</c>
        /// view still receives its export accessor map regardless of this flag.
        /// </summary>
        public bool ReadDtosEmittable { get; }

        /// <summary>
        /// Whether every member of <see cref="CrudDto"/> is an Emittable_Shape. <c>false</c> → no
        /// write-model <c>JsonTypeInfo</c> is emitted and the offending members are reported as VISTA0063
        /// (R1.7, R8.4). Always <c>false</c> for a read-only view (there is no <c>TCrud</c>).
        /// </summary>
        public bool CrudDtoEmittable { get; }

        /// <summary>
        /// The <c>Type.Member</c> descriptions of members whose shape cannot be emitted reflection-free,
        /// used to compose the VISTA0063 message. Wrapped in <see cref="EquatableArray{T}"/> so the
        /// sequence participates in value equality. Empty when both <see cref="ReadDtosEmittable"/> and
        /// <see cref="CrudDtoEmittable"/> hold for their applicable DTOs.
        /// </summary>
        public EquatableArray<string> NonEmittableMembers { get; }

        /// <summary>
        /// The auxiliary (non-object) types the generated per-view context must ALSO provide a
        /// <c>JsonTypeInfo</c> for — nullable value types, collections, and scalar/enum leaves reachable
        /// from the EMITTED DTO sides (the read set when <see cref="ReadDtosEmittable"/>, and/or
        /// <c>TCrud</c> when <see cref="CrudDtoEmittable"/>) — so the covered view's DTOs (de)serialize
        /// with NO reflection fallback in the chain (R2.1, R8.1). Collected in <c>StyleAShapeGenerator</c>
        /// for exactly the emitted sides (independent gating, R4.2): a writable view with an anonymous read
        /// row contributes only <c>TCrud</c>'s aux, never the read side's. Reuses the D125
        /// <see cref="AuxTypeModel"/> shape verbatim and is wrapped in <see cref="EquatableArray{T}"/> so
        /// the (fixed, first-occurrence order) sequence participates in the record's value equality and the
        /// emitted output stays deterministic (R7.2, R7.4). Consumed by <see cref="JsonContextEmitter"/> at
        /// emission time (task 5.2).
        /// </summary>
        public EquatableArray<AuxTypeModel> AuxTypes { get; }

        /// <summary>
        /// Equatable surrogate for the <c>AddView</c> call site's source location, used to report
        /// diagnostics. A <see cref="LocationInfo"/> (not a raw <see cref="Microsoft.CodeAnalysis.Location"/>)
        /// so the model stays value-equal and incremental caching is preserved.
        /// </summary>
        public LocationInfo Location { get; }
    }
}
