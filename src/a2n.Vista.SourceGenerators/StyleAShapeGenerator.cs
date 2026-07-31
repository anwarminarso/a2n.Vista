// Licensed to the a2n.Vista project. Published artifact — English only.
//
// M9 Source Generator (Pillar 3) — the final planned phase: Style A (anonymous) coverage (D129/D130,
// style-a-coverage).
//
// This is the FIFTH IIncrementalGenerator in the a2n.Vista.SourceGenerators project (netstandard2.0),
// independent of the Phase 1/2 ViewAccessorGenerator, the Phase 3 WriteMapperGenerator, the Phase 4
// ViewInvokerGenerator, and the Phase 5 ViewJsonContextGenerator. It is the FIRST generator to key off an
// INVOCATION rather than a class declaration: the prior phases recognize typed Style B views (classes
// deriving a2n.Vista.Authoring.View<...>), while Style A views are not classes at all — they are
// a2n.Vista.Authoring.ViewTemplate<TDbContext>.AddView<TRow>(name, projection) CALL SITES inside a
// template's Configure override (the DynData-style "central template" authoring experience). This
// generator recognizes those call sites and — in later tasks — emits, for the nameable subset, the same
// shape-driven artifacts the prior phases emit for Style B (export accessors + per-view JsonTypeInfo),
// registered into the EXISTING Core stores (ViewAccessorRegistry, GeneratedJsonContextStore). Recognition
// is by fully-qualified name only; the generator references NO a2n.Vista project (D48, R1.6, R7.1).
//
// THE WALL THAT DEFINES THE SCOPE (design "The wall that defines the scope"):
//   A C# anonymous type has no source-writable name, so the generator cannot emit an export accessor, a
//   member-access expression, or a JsonTypeInfo for an anonymous row type. Style A projections are
//   typically anonymous, so an anonymous read TRow stays permanently [RequiresUnreferencedCode] by design
//   (D96/D130). This generator covers only the NAMEABLE subset: a named read TRow (read-side artifacts) and
//   the always-named TCrud of a writable view (write-side artifact), keyed by a constant AddView name.
//
// SCOPE OF THIS FILE AS OF TASK 2.2 (tasks.md §2.2, requirements R1.1, R1.5, R1.6, R7.1):
//   * Stand up the [Generator] IIncrementalGenerator.
//   * Fast INVOCATION SYNTAX PREDICATE (no semantics) — an InvocationExpressionSyntax whose invoked member
//     is named `AddView`. This is the architectural novelty: every prior phase used a ClassDeclarationSyntax
//     predicate; this one filters invocation expressions. The predicate is a cheap textual name match; the
//     semantic transform does the precise FQN-based filtering.
//   * SEMANTIC TRANSFORM — resolve the invoked symbol and keep it ONLY when it is a genuine Style A
//     AddView call site: the method resolves to a2n.Vista.Authoring.IViewTemplateBuilder<TDbContext>.
//     AddView<TRow> (by fully-qualified name) AND the enclosing type derives
//     a2n.Vista.Authoring.ViewTemplate<TDbContext> (by fully-qualified name). Non-candidates are dropped by
//     returning `null` (R1.1). For a genuine call site the transform builds the FULLY EQUATABLE
//     StyleAViewModel and wires up the TEMPLATE/VIEW IDENTIFICATION and the captured types:
//       - TemplateNamespace / TemplateClassName — the enclosing ViewTemplate<TDbContext> subclass (R1.1).
//       - ViewName / HasConstantName — the AddView `name` argument constant-folded to its compile-time
//         string value; a non-constant name clears HasConstantName (the call site cannot be keyed
//         statically — the later VISTA0062 case, R1.2).
//       - RowTypeFqn / HasNamedRowType — the AddView type argument TRow. HasNamedRowType is false when TRow
//         is anonymous or `object` (unnameable in generated source → the later VISTA0061 case, R1.4);
//         RowTypeFqn is captured global::-qualified only when named.
//       - CrudTypeFqn / IsWritable — captured by WALKING A CHAINED `.WithCrud<TCrud, TEntity>()` on the
//         AddView result (the only door to Style A writes). TCrud is always a named type (the authoring
//         surface forbids an anonymous write model, D38), so it is nameable and captured global::-qualified
//         even when the read TRow is anonymous (R1.5, R4.2).
//     All type names are captured global::-qualified. The equatable Location is a LocationInfo surrogate
//     (not the non-value-equal Microsoft.CodeAnalysis.Location) taken at the AddView call site so incremental
//     caching holds (R7.2). The equatable-model stage is tagged TrackingNames.StyleAViewModel for the
//     cache-reuse assertions (R7.2).
//
// SCOPE HARDENED BY TASK 2.3 (tasks.md §2.3, requirements R1.2, R1.3, R1.4, R1.5):
//   Task 2.2 wrote a first cut of the name/row/crud classification; task 2.3 REVIEWS and HARDENS it to fully
//   satisfy R1.2-R1.5 across edge cases (no rewrite — the correct 2.2 logic is kept):
//     * CONSTANT NAME (R1.2): TryGetConstantViewName now (a) locates the `name` argument by its `name:`
//       label when present, so a REORDERED call (AddView(query: ..., name: "orders")) is still recognized,
//       else the first positional argument; and (b) requires a NON-EMPTY constant string. GetConstantValue
//       already folds a string literal, a `const`, and nameof(...) to a constant while leaving an
//       interpolated-with-holes / non-const local / method call / non-const concatenation non-constant; a
//       `null` constant (const string X = null;) fails the `is string` test and an empty string is rejected
//       as an unusable registry key — both fall to the non-constant path (VISTA0062).
//     * READ TRow (R1.3/R1.4): confirmed already-correct — IsNamedContractType rejects anonymous types,
//       `object`, error types, type parameters, and `dynamic`; RowTypeFqn uses
//       SymbolDisplayFormat.FullyQualifiedFormat, which is global::-qualified, keeps generic type arguments
//       (a named generic DTO renders global::Ns.Dto<global::Ns.Arg>), and omits nullable reference
//       annotations — exactly the source-writable form the emitters (5.x) need.
//     * WRITE TCrud (R1.5): confirmed the WithCrud walk handles intervening read-facet calls
//       (Field/MaxPageSize/Key/...) between AddView and WithCrud; IsWritable reflects WithCrud presence and a
//       read-only view stays IsWritable == false / CrudTypeFqn == null (R4.4). Hardened so CrudTypeFqn is
//       captured only when TCrud genuinely resolves to a named contract type (D38 guarantees this for real
//       code) — a pathological WithCrud<object, TEntity>() or an unresolved TCrud yields a null FQN, so no
//       non-writable name leaks to the emitters; emittability stays gated by the task 2.4 shape analysis.
//
// SCOPE ADDED BY TASK 3.2 (tasks.md §3.2, requirements R8.1, R8.2, R8.3, R8.4, R8.5):
//   * DIAGNOSTIC REPORTING is wired into the source-output stage (Emit). It reads only the recognition /
//     coverage facet of the equatable StyleAViewModel and is non-blocking (Info/Warning, never Error, R8.5):
//       - VISTA0062 (Info) — HARD GATE. A non-constant AddView name (HasConstantName == false) cannot key
//         ANY artifact statically (neither read nor write), so the whole call site stays on the reflection
//         path. Reported against the enclosing template (the view has no usable name) and RETURNS — no other
//         Style A diagnostic applies (R1.2, R8.3).
//       - VISTA0061 (Info) — ADDITIVE. An anonymous/object read TRow (HasNamedRowType == false) keeps the
//         READ side (accessors + read-DTO JsonTypeInfo) on the reflection path permanently by design
//         (D96/D130); it does NOT stop the write side, so it falls through to VISTA0060 (R1.4, R8.2).
//       - VISTA0063 (Warning) — ADDITIVE. A covered candidate DTO (a named TRow read DTO and/or a TCrud)
//         with a non-emittable member records that member into NonEmittableMembers; one warning names the
//         offending type/member(s). No JsonTypeInfo is emitted for that DTO (a named TRow view still gets its
//         accessor map). Guarded on a recorded entry — matching the sibling ViewJsonContextGenerator's
//         VISTA0051 guard — so nothing fires until the Emittable_Shape analysis (task 2.4) populates the
//         field (R1.7, R8.4).
//       - VISTA0060 (Info) — ADDITIVE. When the covered artifact set is non-empty, one report names the
//         EXACT set: "export accessors" (a named TRow — accessors need only member access, not
//         emittability), "read-DTO JsonTypeInfo" (a named TRow whose read DTOs are all emittable), and/or
//         "TCrud JsonTypeInfo" (a writable view whose TCrud is emittable). Anything not listed stays on the
//         reflection path by design (R8.1).
//     Unlike the sibling generators' mutually-exclusive covered/uncovered reports, a single Style A view can
//     legitimately trigger MORE THAN ONE diagnostic (the D96 asymmetry): an anonymous-read writable view
//     reports BOTH VISTA0061 (read stays RUC) AND VISTA0060 (TCrud write side covered). The reports are
//     therefore ADDITIVE (not early-return) after the VISTA0062 gate. The reportable Location is
//     reconstructed from the equatable LocationInfo via ToLocation(), mirroring how ViewInvokerGenerator /
//     ViewJsonContextGenerator wired their VISTA00xx reporting from a model in RegisterSourceOutput.
//
//     NOTE — SHAPE ANALYSIS LANDED (task 2.4): ReadDtosEmittable/CrudDtoEmittable and NonEmittableMembers
//     are now populated by the shared EmittableShapeAnalyzer in Transform, so the emittability-gated
//     artifacts fire for real: a named TRow with emittable read DTOs adds "read-DTO JsonTypeInfo" to
//     VISTA0060, a writable view with an emittable TCrud adds "TCrud JsonTypeInfo", and a DTO with a
//     non-emittable member fires VISTA0063 (with that DTO's JsonTypeInfo artifact withheld). "export
//     accessors" still fires for any named TRow independently of emittability. This stage was written to
//     read those fields, so task 2.4 landing required NO change to it.
//
// SCOPE ADDED BY TASK 2.4 (tasks.md §2.4, requirements R1.7, R3.4):
//   * The EMITTABLE_SHAPE DTO ANALYSIS now runs in the semantic transform, populating the DTO facet of the
//     equatable StyleAViewModel by delegating to the SHARED EmittableShapeAnalyzer — the SAME rules the D125
//     per-view JsonTypeInfo phase uses. The analysis was extracted from ViewJsonContextGenerator into that
//     helper so NEITHER phase forks a divergent copy of the emittable-shape rules; byte-for-byte parity with
//     the reflection oracle depends on both phases classifying DTOs identically. The facet:
//       - ReadDtos — for a NAMED TRow, the read Serializable_DTO_Set { TRow, ViewListResult<TRow>,
//         PagedResult<TRow> } modeled as DtoTypeModel/DtoMemberModel (each member's JSON name resolved per
//         the seam naming policy for parity; object-construction kind detected per DTO, R3.4). Empty for an
//         anonymous/object row (no nameable read DTOs).
//       - CrudDto — for a writable view with a named TCrud (always named, D38), TCrud modeled the same way,
//         INDEPENDENTLY of the read TRow being named or anonymous (the D96 asymmetry, R4.2).
//       - ReadDtosEmittable / CrudDtoEmittable — each true only when EVERY member of its DTO(s) is an
//         Emittable_Shape; the SAFE DEFAULT is "not emittable" for anything the analyzer cannot fully resolve
//         (a bespoke/polymorphic converter, an unresolved generic, or nesting beyond a single POCO level),
//         preferring parity over coverage (R1.7). Read and write emittability are gated SEPARATELY.
//       - NonEmittableMembers — a "Type.Member (memberTypeFqn)" description per offending TRow/TCrud member,
//         which the task-3.2 VISTA0063 report joins verbatim.
//     With this landed, the diagnostic stage's emittability-gated artifacts (the "read-DTO JsonTypeInfo" /
//     "TCrud JsonTypeInfo" entries in VISTA0060, and VISTA0063 for a non-emittable member) now fire from the
//     populated model with NO change to Emit (see the note in Emit below).
//
// SCOPE ADDED BY TASK 5.1 (tasks.md §5.1, requirements R2.1, R2.2, R2.4, R5.1, R7.3, R7.4):
//   * The EXPORT ACCESSOR MAP emitter is wired into the source-output stage (Emit -> EmitAccessorMap). For a
//     COVERED view with a named TRow and a constant name it emits, into its own generated file, a
//     `file static` accessor map — a Dictionary<string, Func<object, object?>> with one entry per public
//     readable TRow member (cast + member read: `static row => ((global::Ns.NamedRow)row).Member`, never
//     reflection) — plus exactly one [ModuleInitializer] that registers the map into the EXISTING
//     a2n.Vista.Metadata.ViewAccessorRegistry (D117), so the export (CSV/XLSX) value path for the view stops
//     using reflection. This is the IDENTICAL file shape to the Phase 1 (D117) ViewAccessorGenerator emitter,
//     with ONE difference (design "Keying — the difference from Phases 1/5"): a Style A view is an AddView
//     CALL SITE, not a class, so there is nothing to instantiate — the [ModuleInitializer] keys the
//     registration by the CONSTANT view-name LITERAL lifted from AddView (`Register("customers", Map)`),
//     never `new View().Name`. Member names come from ReadDtos[0].Members (TRow's DtoTypeModel, appended
//     first by the shared analyzer) and the cast target from RowTypeFqn. Emitted for ANY named TRow
//     INDEPENDENTLY of JSON emittability (accessors are cast + member read only, matching the "export
//     accessors" artifact VISTA0060 already lists for any named TRow), so gated on HasConstantName &&
//     HasNamedRowType — NOT on ReadDtosEmittable. No PropertyInfo, no Activator, no RUC; fixed "\n" line
//     endings for byte-determinism (R7.4); English identifiers/comments.
//
// SCOPE ADDED BY TASK 5.2 (tasks.md §5.2, requirements R3.1-R3.5, R4.1, R4.2, R4.4, R4.5, R5.2, R5.4,
// R7.3, R7.4, R7.5):
//   * The PER-VIEW IJsonTypeInfoResolver emitter is wired into the source-output stage (Emit ->
//     EmitJsonContext), directly after EmitAccessorMap. For a COVERED view with a constant name it emits a
//     `file sealed` class implementing System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver whose
//     GetTypeInfo returns JsonMetadataServices-built JsonTypeInfo for the read set { TRow,
//     ViewListResult<TRow>, PagedResult<TRow> } WHEN TRow is named + emittable AND for TCrud WHEN writable +
//     emittable — plus a [ModuleInitializer] registering it into the EXISTING GeneratedJsonContextStore
//     (D125) keyed by the CONSTANT view name. This REUSES the SHARED JsonContextEmitter (extracted verbatim
//     from ViewJsonContextGenerator so the D125 phase and this one emit byte-for-byte identical contexts —
//     parity by construction, mirroring how task 2.4 extracted the shape ANALYSIS into
//     EmittableShapeAnalyzer). THE ONE DIFFERENCE from D125 (design "Keying — the difference from Phases
//     1/5"): the [ModuleInitializer] key is the CONSTANT AddView name LITERAL (`Register("customers", ...)`),
//     not `new View().Name` — a Style A view is a call site, not a class, so there is nothing to
//     instantiate; the shared emitter parameterizes the registration-key expression for exactly this reason.
//   * INDEPENDENT GATING (the D96 asymmetry, R4.2): the read set and TCrud are gated SEPARATELY, so a
//     writable view with an anonymous read row emits a context with ONLY TCrud (its read row is unnameable,
//     VISTA0061); a read-only named view emits ONLY the read set; a named writable view emits both. The
//     context (and its [ModuleInitializer]) is emitted only when AT LEAST ONE side is emittable; a view with
//     neither side emittable has no generated context and stays on the reflection fallback (an anonymous
//     read-only view, or a named view whose only DTO is non-emittable).
//   * AUX TYPES SURFACED (task 5.2): StyleAViewModel now carries an AuxTypes field (EquatableArray<
//     AuxTypeModel>, the D125 shape reused verbatim). Transform collects the auxiliary (nullable/collection/
//     leaf/enum) JsonTypeInfo arms PER SIDE and combines only the EMITTED sides' aux (so the emitted
//     context's no-reflection-fallback chain resolves every member type, and the aux set corresponds
//     exactly to the DTO sides actually emitted — no read-side aux when only TCrud is emitted). The model
//     stays fully value-equatable (R7.2).

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// Incremental generator that discovers Style A (central-template) views — the
    /// <c>a2n.Vista.Authoring.ViewTemplate&lt;TDbContext&gt;.AddView&lt;TRow&gt;(name, projection)</c>
    /// invocation call sites — and, in later tasks, emits reflection-free export accessors and per-view
    /// <c>JsonTypeInfo</c> for the nameable subset, registered via module initializers into
    /// <c>a2n.Vista.Core</c>'s existing <c>ViewAccessorRegistry</c> and <c>GeneratedJsonContextStore</c>
    /// (D129). Unlike the prior phases it keys off an <see cref="InvocationExpressionSyntax"/> rather than a
    /// class declaration. It recognizes Vista authoring types by fully-qualified name only and references no
    /// other a2n.Vista project (D48, R1.6, R7.1).
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class StyleAShapeGenerator : IIncrementalGenerator
    {
        // The recognized Style A authoring method/type names. AddView<TRow> and WithCrud<TCrud, TEntity> are
        // matched by method name (fast) + declaring interface (FQN), and the enclosing type is matched by
        // walking its base types to ViewTemplate<TDbContext>. Roslyn encodes generic arity in the metadata
        // name (`1 / `2). Recognition is by metadata name + namespace only — the generator references no
        // a2n.Vista assembly (R1.6, R7.1).
        private const string AddViewMethodName = "AddView";
        private const string WithCrudMethodName = "WithCrud";
        private const string AuthoringNamespace = "a2n.Vista.Authoring";
        private const string ViewTemplateBuilderMetadataName = "IViewTemplateBuilder`1";
        private const string ReadViewBuilderMetadataName = "IReadViewBuilder`1";
        private const string ViewTemplateMetadataName = "ViewTemplate`1";

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // CreateSyntaxProvider pipeline: cheap syntactic filter first (an AddView invocation), semantic
            // resolution second. The transform yields a fully equatable StyleAViewModel (or null to drop
            // non-candidates), so Roslyn's incremental cache can skip re-emitting call sites whose model is
            // unchanged (R7.2, mirroring Phases 1/2/3/4/5).
            var candidates = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateInvocation(node),
                    transform: static (ctx, ct) => Transform(ctx, ct))
                .Where(static model => model is not null)
                // Tag the equatable-model stage so the incremental host records its per-step cache outcome.
                // This is observability only — it does not change emission — and lets the generator tests
                // assert cache reuse (IncrementalStepRunReason.Cached/Unchanged), proving the equatable value
                // model (R7.2, mirroring TrackingNames.ViewModel / WriteMapperModel / ViewInvokerModel /
                // ViewJsonContextModel). See TrackingNames.
                .WithTrackingName(TrackingNames.StyleAViewModel);

            // Source-output stage. Task 3.2 wired the non-blocking VISTA0060–VISTA0063 diagnostics into this
            // stage (see Emit) and task 5.1 wired the export accessor map emission for a covered named-TRow
            // view (Emit -> EmitAccessorMap); task 5.2 will additionally emit the per-view
            // IJsonTypeInfoResolver (read DTOs and/or TCrud) for covered views. Mirrors how the sibling
            // ViewInvokerGenerator / ViewJsonContextGenerator landed their diagnostics ahead of / alongside
            // their emitters.
            context.RegisterSourceOutput(candidates, static (spc, model) => Emit(spc, model));
        }

        /// <summary>
        /// Fast invocation syntax predicate (no semantics): an <see cref="InvocationExpressionSyntax"/> whose
        /// invoked member is named <c>AddView</c>. Cheap enough to run on every changed node; the semantic
        /// transform does the precise FQN-based filtering (the method resolves to
        /// <c>IViewTemplateBuilder&lt;TDbContext&gt;.AddView&lt;TRow&gt;</c> and the enclosing type derives
        /// <c>ViewTemplate&lt;TDbContext&gt;</c>). This is the architectural novelty versus Phases 1–5, which
        /// filter class declarations.
        /// </summary>
        private static bool IsCandidateInvocation(SyntaxNode node)
            => node is InvocationExpressionSyntax invocation
               && GetInvokedName(invocation) == AddViewMethodName;

        /// <summary>
        /// Semantic transform (task 2.2): resolve the invoked symbol and keep it only when it is a genuine
        /// Style A <c>AddView</c> call site — the method resolves (by fully-qualified name) to
        /// <c>a2n.Vista.Authoring.IViewTemplateBuilder&lt;TDbContext&gt;.AddView&lt;TRow&gt;</c> and the
        /// enclosing type derives <c>a2n.Vista.Authoring.ViewTemplate&lt;TDbContext&gt;</c>. Returns a fully
        /// equatable <see cref="StyleAViewModel"/> carrying the template/view identification, the captured
        /// <c>TRow</c>/<c>TCrud</c> types, and — via the shared <see cref="EmittableShapeAnalyzer"/> (task
        /// 2.4) — the analyzed read/write DTO facet (<c>ReadDtos</c>, <c>CrudDto</c>, the <c>*Emittable</c>
        /// flags, and <c>NonEmittableMembers</c>), or <c>null</c> to drop the invocation (R1.1). The accessor
        /// and per-view <c>IJsonTypeInfoResolver</c> emission (tasks 5.1/5.2) is deferred.
        /// </summary>
        private static StyleAViewModel Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var invocation = (InvocationExpressionSyntax)ctx.Node;

            // Resolve the invoked symbol to the AddView<TRow> method and verify it is the Vista authoring
            // method by FQN (R1.1, R1.6). A non-AddView invocation that merely shares the name is dropped.
            if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol addViewMethod
                || !IsAddViewMethod(addViewMethod))
            {
                return null;
            }

            // The enclosing type must derive a2n.Vista.Authoring.ViewTemplate<TDbContext> (by FQN): Style A
            // AddView calls live inside a template's Configure override (R1.1). A call in any other type is
            // not a Style A call site.
            var enclosingTypeDecl = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (enclosingTypeDecl is null
                || ctx.SemanticModel.GetDeclaredSymbol(enclosingTypeDecl, ct) is not INamedTypeSymbol templateSymbol
                || !DerivesFromViewTemplate(templateSymbol))
            {
                return null;
            }

            // Capture the read row type TRow (the AddView type argument). HasNamedRowType is false when TRow
            // is anonymous or `object` — unnameable in generated source, so the read side stays RUC by design
            // (R1.4); RowTypeFqn is captured global::-qualified only when named.
            var rowType = addViewMethod.TypeArguments.Length == 1 ? addViewMethod.TypeArguments[0] : null;
            var hasNamedRowType = IsNamedContractType(rowType);
            var rowTypeFqn = hasNamedRowType
                ? rowType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;

            // Walk a chained .WithCrud<TCrud, TEntity>() on the AddView result to capture the write model
            // TCrud (the only door to Style A writes, R1.5). A read-only view (no WithCrud) leaves
            // IsWritable == false and CrudTypeFqn == null (R4.4). TCrud is always a named type (D38 forbids
            // an anonymous write model), so it is nameable and captured global::-qualified even when the read
            // TRow is anonymous (R1.5, R4.2). The FQN is captured only when TCrud genuinely resolves to a
            // named contract type: a pathological WithCrud<object, TEntity>() or an unresolved (error) TCrud
            // yields a null FQN so no non-writable name can leak to the emitters (5.x); its emittability is
            // gated separately by the shape analysis (task 2.4).
            var crudType = FindChainedCrudType(invocation, ctx.SemanticModel, ct);
            var isWritable = crudType is not null;
            var crudTypeFqn = IsNamedContractType(crudType)
                ? crudType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;

            // View identification: constant-fold the AddView `name` argument to its compile-time string. A
            // non-constant name clears HasConstantName so the call site cannot be keyed statically (R1.2).
            var hasConstantName = TryGetConstantViewName(invocation, ctx.SemanticModel, ct, out var viewName);

            // Template identification: the enclosing ViewTemplate<TDbContext> subclass (null namespace for
            // the global namespace, matching the sibling generators).
            var templateNamespace = templateSymbol.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? ns.ToDisplayString()
                : null;

            // -------------------------------------------------------------------------------------
            // Emittable_Shape DTO analysis (task 2.4, R1.7/R3.4).
            // -------------------------------------------------------------------------------------
            // Run the SHARED EmittableShapeAnalyzer — the SAME member-classification, JSON-property-name, and
            // object-construction-kind rules the D125 per-view JsonTypeInfo phase uses — over Style A's
            // nameable DTOs, so the two phases stay byte-for-byte compatible with the reflection oracle
            // (parity is the master guarantee and it depends on IDENTICAL rules; the shared helper is the
            // single home for those rules). The read and write sides are classified INDEPENDENTLY — unlike
            // D125's single per-view context which gates on both — so a writable view can cover its write
            // TCrud while its read TRow stays on the reflection path (the D96 asymmetry, R4.2).
            var compilation = ctx.SemanticModel.Compilation;
            var readDtoModels = new List<DtoTypeModel>();
            var nonEmittable = new List<string>();

            // The auxiliary (nullable/collection/leaf/enum) JsonTypeInfo arms the covered DTOs reach are
            // collected by the shared analyzer for the task 5.2 emitter's no-reflection-fallback chain. They
            // are gathered PER SIDE (read vs write) into separate sinks so only the EMITTED sides contribute
            // to the model's AuxTypes. Independent gating (R4.2) means a writable view with an anonymous read
            // row emits a context with ONLY TCrud (and only TCrud's aux), while a read-only named-TRow view
            // emits only the read set (and only its aux); combining AFTER emittability is decided keeps the
            // aux set exactly matched to the DTO sides the emitter (task 5.2) actually writes.
            var readAuxTypes = new List<AuxTypeModel>();

            // Read side (only for a NAMED TRow, R1.3/R1.4): TRow + ViewListResult<TRow> + PagedResult<TRow>.
            // An anonymous/object row is unnameable in generated source, so it has no nameable read DTOs and
            // its read serialization stays RUC by design (D96/D130); the read set stays empty. ReadDtosEmittable
            // follows TRow's members (the two envelopes are known shapes over TRow, so their emittability
            // follows it).
            var readDtosEmittable = false;
            if (hasNamedRowType && rowType is INamedTypeSymbol rowNamed)
            {
                var readAuxSeen = new HashSet<string>(StringComparer.Ordinal);
                readDtosEmittable = EmittableShapeAnalyzer.BuildReadDtoSet(
                    compilation, rowNamed, readDtoModels, nonEmittable, readAuxTypes, readAuxSeen);
            }

            // Write side (only for a writable view with a NAMED TCrud, R1.5/R4.2): TCrud — independent of the
            // read TRow being named or anonymous. TCrud is always a named type (D38 forbids an anonymous write
            // model), so a writable view's write model is nameable/generatable even when its read row is
            // anonymous. crudTypeFqn is non-null exactly when TCrud is a named contract type (see above), so
            // it gates the analysis alongside the cast. Its aux goes to a SEPARATE sink so it can be included
            // independently of the read side (R4.2).
            DtoTypeModel crudDto = null;
            var crudDtoEmittable = false;
            var crudAuxTypes = new List<AuxTypeModel>();
            if (isWritable && crudTypeFqn is not null && crudType is INamedTypeSymbol crudNamed)
            {
                var crudDtoModels = new List<DtoTypeModel>();
                var crudAuxSeen = new HashSet<string>(StringComparer.Ordinal);
                crudDtoEmittable = EmittableShapeAnalyzer.BuildDtoModel(
                    crudNamed, nonEmittable, crudDtoModels, crudAuxTypes, crudAuxSeen);

                // BuildDtoModel appends exactly one DTO model (TCrud) to the list it is given.
                crudDto = crudDtoModels.Count > 0 ? crudDtoModels[0] : null;
            }

            // Combine the auxiliary arms for exactly the EMITTED sides (independent gating, R4.2): the read
            // side's aux only when the read DTOs are emitted (a named + emittable TRow), the write side's aux
            // only when TCrud is emitted (a writable + emittable TCrud). A side that is not emitted
            // contributes no aux, so the model's AuxTypes always corresponds to the DTO sides the emitter
            // (task 5.2) writes. Deduplicated across the two sides by FQN, preserving read-first,
            // first-occurrence order for deterministic output (R7.4). Each side's own aux was already
            // collected only for its emittable members, so a non-emittable side contributes nothing anyway;
            // gating on the *Emittable flags makes that explicit and future-proof.
            var auxList = new List<AuxTypeModel>();
            var auxSeen = new HashSet<string>(StringComparer.Ordinal);
            if (hasNamedRowType && readDtosEmittable)
            {
                foreach (var aux in readAuxTypes)
                {
                    if (auxSeen.Add(aux.TypeFqn))
                    {
                        auxList.Add(aux);
                    }
                }
            }

            if (isWritable && crudDtoEmittable)
            {
                foreach (var aux in crudAuxTypes)
                {
                    if (auxSeen.Add(aux.TypeFqn))
                    {
                        auxList.Add(aux);
                    }
                }
            }

            var readDtos = new EquatableArray<DtoTypeModel>(readDtoModels.ToArray());
            var nonEmittableMembers = new EquatableArray<string>(nonEmittable.ToArray());
            var auxTypes = new EquatableArray<AuxTypeModel>(auxList.ToArray());

            return new StyleAViewModel(
                templateNamespace: templateNamespace,
                templateClassName: templateSymbol.Name,
                viewName: viewName,
                hasConstantName: hasConstantName,
                rowTypeFqn: rowTypeFqn,
                hasNamedRowType: hasNamedRowType,
                crudTypeFqn: crudTypeFqn,
                isWritable: isWritable,
                readDtos: readDtos,
                crudDto: crudDto,
                readDtosEmittable: readDtosEmittable,
                crudDtoEmittable: crudDtoEmittable,
                nonEmittableMembers: nonEmittableMembers,
                auxTypes: auxTypes,
                location: LocationInfo.From(GetInvokedNameNode(invocation) ?? (SyntaxNode)invocation));
        }

        /// <summary>
        /// Source-output stage (task 3.2). Reports the non-blocking Style A coverage diagnostics
        /// (<c>VISTA0060</c>–<c>VISTA0063</c>) by reading the recognition / coverage facet of the equatable
        /// <see cref="StyleAViewModel"/>. Every diagnostic in this family is Info or Warning — never Error —
        /// so this stage never breaks the build: an uncovered Style A view is a valid, working view served by
        /// the reflection fallback; only the AOT-clean auto-generation is missed (R8.5).
        /// <para>
        /// Unlike the sibling generators' mutually-exclusive covered/uncovered reports, a single Style A view
        /// can legitimately trigger MORE THAN ONE diagnostic (the D96 asymmetry): an anonymous-read writable
        /// view reports both <c>VISTA0061</c> (its read side stays RUC) AND <c>VISTA0060</c> (its
        /// <c>TCrud</c> write side is covered). The reports are therefore ADDITIVE rather than early-return,
        /// after the single hard gate below.
        /// </para>
        /// <list type="bullet">
        ///   <item>
        ///     <b><c>VISTA0062</c> (Info) — hard gate.</b> A non-constant <c>AddView</c> name
        ///     (<see cref="StyleAViewModel.HasConstantName"/> is <c>false</c>) means no artifact can be keyed
        ///     statically (neither read nor write), so the whole call site stays on the reflection path.
        ///     Reported against the enclosing template (the view has no usable name) and returns — no other
        ///     Style A diagnostic applies (R1.2, R8.3).
        ///   </item>
        ///   <item>
        ///     <b><c>VISTA0061</c> (Info) — additive.</b> An anonymous/<c>object</c> read <c>TRow</c>
        ///     (<see cref="StyleAViewModel.HasNamedRowType"/> is <c>false</c>) keeps the READ side (accessors
        ///     + read-DTO <c>JsonTypeInfo</c>) on the reflection path permanently by design (D96/D130) because
        ///     an anonymous type cannot be named in generated source. It does NOT stop the write side — a
        ///     writable view's <c>TCrud</c> is always named (D38) and may still be covered — so it falls
        ///     through to <c>VISTA0060</c> (R1.4, R8.2).
        ///   </item>
        ///   <item>
        ///     <b><c>VISTA0063</c> (Warning) — additive.</b> A covered candidate DTO (a named <c>TRow</c>
        ///     read DTO and/or a <c>TCrud</c>) with a member whose shape cannot be emitted reflection-free
        ///     records that member into <see cref="StyleAViewModel.NonEmittableMembers"/>; one warning names
        ///     the offending type/member(s). No <c>JsonTypeInfo</c> is emitted for that DTO (a named
        ///     <c>TRow</c> view still gets its accessor map). Guarded on a recorded entry — matching the
        ///     sibling <c>ViewJsonContextGenerator</c>'s <c>VISTA0051</c> — so nothing fires until the
        ///     Emittable_Shape analysis (task 2.4) populates the field (R1.7, R8.4).
        ///   </item>
        ///   <item>
        ///     <b><c>VISTA0060</c> (Info) — additive.</b> When the covered artifact set is non-empty, one
        ///     report names the EXACT set: <c>export accessors</c> (a named <c>TRow</c> — accessors need only
        ///     member access, not emittability), <c>read-DTO JsonTypeInfo</c> (a named <c>TRow</c> whose read
        ///     DTOs are all emittable), and/or <c>TCrud JsonTypeInfo</c> (a writable view whose <c>TCrud</c>
        ///     is emittable). Anything not listed stays on the reflection path by design. A view with an
        ///     EMPTY set (an anonymous read-only view, or one whose only nameable DTO is non-emittable) is
        ///     not "covered" and gets no <c>VISTA0060</c> (R8.1).
        ///   </item>
        /// </list>
        /// The reportable <see cref="Location"/> is reconstructed from the equatable
        /// <see cref="LocationInfo"/> via <see cref="LocationInfo.ToLocation"/>. After reporting, this stage
        /// emits the reflection-free export accessor map for a covered named-<c>TRow</c> view (task 5.1,
        /// <see cref="EmitAccessorMap"/>) into the existing <c>ViewAccessorRegistry</c> keyed by the constant
        /// view name; the per-view <c>IJsonTypeInfoResolver</c> emitter (task 5.2) slots in directly after
        /// that call.
        /// </summary>
        private static void Emit(SourceProductionContext context, StyleAViewModel model)
        {
            // Reconstruct a reportable Location from the equatable surrogate captured at the AddView call
            // site (mirroring the sibling generators). Location.None is the defensive fallback.
            var location = model.Location?.ToLocation() ?? Location.None;

            // Hard gate — VISTA0062 (Info). A non-constant AddView name cannot key ANY artifact statically
            // (the runtime name is unknowable at compile time, so a wrong key would silently miss), so the
            // whole call site stays on the reflection path. Reported against the enclosing template since the
            // view has no usable name to identify it, then return — no other Style A diagnostic applies
            // (R1.2, R8.3).
            if (!model.HasConstantName)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.StyleANonConstantViewName,
                    location,
                    model.TemplateClassName));
                return;
            }

            // Additive — VISTA0061 (Info). An anonymous/object read TRow keeps the READ side (accessors +
            // read-DTO JsonTypeInfo) on the reflection path permanently by design (D96/D130): an anonymous
            // type has no source-writable name. This does NOT stop the write side — a writable view's TCrud
            // is always named (D38) — so we fall through to compose VISTA0060 below (R1.4, R8.2).
            if (!model.HasNamedRowType)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.StyleAAnonymousRowStaysReflection,
                    location,
                    model.ViewName));
            }

            // Additive — VISTA0063 (Warning). A covered candidate DTO (named TRow read DTOs and/or TCrud)
            // has a member whose shape cannot be emitted reflection-free; name the offending type/member(s).
            // No JsonTypeInfo is emitted for that DTO; a named-TRow view still gets its accessor map. Guarded
            // on a recorded entry so nothing fires until the Emittable_Shape analysis (task 2.4) populates
            // NonEmittableMembers — matching the sibling ViewJsonContextGenerator's VISTA0051 guard. The
            // NonEmittableMembers entries are already formatted "Type.Member (typeFqn)", so a single joined
            // report unambiguously names every offender across both the read and write DTOs (R1.7, R8.4).
            if (model.NonEmittableMembers.Count > 0)
            {
                var offendingMembers = string.Join(", ", model.NonEmittableMembers);
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.StyleADtoMemberNotEmittable,
                    location,
                    model.ViewName,
                    offendingMembers));
            }

            // Compose the EXACT covered artifact set in a fixed order (deterministic message, R7.4) and
            // report VISTA0060 (Info) when it is non-empty (R8.1):
            //   * export accessors      — a named TRow. Accessors are compile-time member access (cast +
            //                             property read), so they need only a nameable row, NOT DTO
            //                             emittability; this fires independently of the *Emittable flags.
            //   * read-DTO JsonTypeInfo — a named TRow whose read DTOs are all emittable.
            //   * TCrud JsonTypeInfo    — a writable view whose TCrud is emittable (independent of the read
            //                             TRow being named or anonymous, R4.2).
            // Anything not listed stays on the reflection path by design. A view with an EMPTY set (an
            // anonymous read-only view, or one whose only nameable DTO is non-emittable) is not "covered" and
            // gets no VISTA0060 — only the VISTA0061/VISTA0063 boundary notes above.
            var artifacts = new List<string>();
            if (model.HasNamedRowType)
            {
                artifacts.Add("export accessors");
            }

            if (model.HasNamedRowType && model.ReadDtosEmittable)
            {
                artifacts.Add("read-DTO JsonTypeInfo");
            }

            if (model.IsWritable && model.CrudDtoEmittable)
            {
                artifacts.Add("TCrud JsonTypeInfo");
            }

            if (artifacts.Count > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.StyleAViewCovered,
                    location,
                    model.ViewName,
                    string.Join(", ", artifacts)));
            }

            // -------------------------------------------------------------------------------------
            // Task 5.1 — export accessor map emission (R2.1, R2.2, R2.4, R5.1, R7.3, R7.4).
            // -------------------------------------------------------------------------------------
            // Emit the reflection-free export accessor map for a covered NAMED-TRow view and register it,
            // via a [ModuleInitializer], into the EXISTING ViewAccessorRegistry (D117) keyed by the CONSTANT
            // view name (HasConstantName is guaranteed true here — the VISTA0062 gate above returned
            // otherwise). It is emitted for ANY named TRow INDEPENDENTLY of JSON emittability — accessors are
            // just a cast + member read, so they are gated on HasNamedRowType, NOT ReadDtosEmittable (the
            // same reason "export accessors" is listed in VISTA0060 for any named TRow above). An
            // anonymous/object read row is unnameable in generated source, so nothing is emitted and its read
            // path stays RUC by design (D96/D130, VISTA0061). The per-view IJsonTypeInfoResolver emitter
            // (task 5.2) slots in directly after this call, consuming the same covered model.
            EmitAccessorMap(context, model);

            // -------------------------------------------------------------------------------------
            // Task 5.2 — per-view IJsonTypeInfoResolver emission (R3.*, R4.*, R5.2, R5.4, R7.3-R7.5).
            // -------------------------------------------------------------------------------------
            // Emit the reflection-free per-view context (read DTOs and/or TCrud) and register it, via a
            // [ModuleInitializer], into the EXISTING GeneratedJsonContextStore (D125) keyed by the CONSTANT
            // view name (HasConstantName is guaranteed true here — the VISTA0062 gate above returned
            // otherwise). It REUSES the SHARED JsonContextEmitter so the emitted context is byte-for-byte the
            // same shape ViewJsonContextGenerator (D125) emits for Style B (parity by construction — the only
            // per-phase difference is the registration key: the constant AddView name literal here, vs
            // `new <View>().Name` for Style B). The read and write sides are gated INDEPENDENTLY (R4.2), so a
            // writable anonymous-read view emits a context with ONLY its TCrud; when neither side is
            // emittable, nothing is emitted and the view stays on the reflection fallback.
            EmitJsonContext(context, model);
        }

        // =============================================================================================
        // TASK 5.1 — export accessor map emitter (reused Phase 1 / D117 file shape; R2.1, R2.2, R2.4,
        // R5.1, R7.3, R7.4).
        //
        // Emits, for a COVERED view with a named TRow and a constant name, a `file static` accessor map
        // (fieldName -> Func<object, object?>, each a cast + member read — never reflection) plus one
        // [ModuleInitializer] registering it into the EXISTING a2n.Vista.Metadata.ViewAccessorRegistry
        // (D117). This is byte-shape-identical to ViewAccessorGenerator.BuildAccessorSource, with the single
        // D129 difference (design "Keying — the difference from Phases 1/5"): a Style A view is an AddView
        // CALL SITE, not a class, so the registration is keyed by the CONSTANT view-name LITERAL lifted from
        // AddView — never `new View().Name` (there is nothing to instantiate). Member names come from
        // ReadDtos[0].Members (TRow's DtoTypeModel, appended first by the shared analyzer) and the cast
        // target from RowTypeFqn. No PropertyInfo / Activator / RUC; fixed "\n" line endings for
        // byte-determinism (R7.4); English identifiers/comments only.
        // =============================================================================================

        /// <summary>
        /// Emits the export accessor map for a covered named-<c>TRow</c> Style A view (task 5.1). Gated on a
        /// named read row (<see cref="StyleAViewModel.HasNamedRowType"/>) — <see cref="Emit"/> only reaches
        /// this after the <c>VISTA0062</c> gate, so <see cref="StyleAViewModel.HasConstantName"/> is
        /// guaranteed and the constant literal can key the registration. Accessors are a cast + member read,
        /// so they are emitted independently of JSON emittability (R2.1); an anonymous/<c>object</c> read row
        /// is unnameable in generated source, so nothing is emitted and its read path stays
        /// <c>[RequiresUnreferencedCode]</c> by design (D96/D130). Defensively confirms
        /// <c>ReadDtos[0]</c> is <c>TRow</c> (the shared analyzer appends it first) before reading its members.
        /// </summary>
        private static void EmitAccessorMap(SourceProductionContext context, StyleAViewModel model)
        {
            // A named read row is required: accessors cast to RowTypeFqn and read members off it. An
            // anonymous/object row has no source-writable name (VISTA0061 already reported it), so skip.
            if (!model.HasNamedRowType || model.RowTypeFqn is null)
            {
                return;
            }

            // Defensive: EmittableShapeAnalyzer.BuildReadDtoSet appends TRow's DtoTypeModel FIRST, so
            // ReadDtos[0] is TRow. Confirm by matching its TypeFqn to RowTypeFqn before reading its members;
            // if the model shape ever differs, skip emission rather than emit a map over the wrong type.
            if (model.ReadDtos.Count == 0
                || !string.Equals(model.ReadDtos[0].TypeFqn, model.RowTypeFqn, StringComparison.Ordinal))
            {
                return;
            }

            var source = BuildAccessorSource(model);
            context.AddSource(BuildAccessorHintName(model), SourceText.From(source, Encoding.UTF8));
        }

        /// <summary>
        /// Builds the per-view generated source: a <c>file static</c> class exposing a
        /// <c>Dictionary&lt;string, Func&lt;object, object?&gt;&gt;</c> accessor map keyed by member name,
        /// each accessor a cast to the fully-qualified <c>TRow</c> type followed by a member read — never
        /// reflection (R2.1). The same class carries a <c>[ModuleInitializer]</c> that registers the map into
        /// <c>ViewAccessorRegistry</c> keyed by the <b>constant view name</b> (the D129 difference from
        /// D117's <c>new View().Name</c>, R2.2). Member order follows <c>ReadDtos[0].Members</c> (declaration
        /// order) and line endings are fixed <c>"\n"</c> so the output is byte-for-byte deterministic (R7.4).
        /// A view whose <c>TRow</c> has no public readable members yields an empty map (kept for consistency,
        /// matching D117).
        /// </summary>
        private static string BuildAccessorSource(StyleAViewModel model)
        {
            // Fixed "\n" line endings (not Environment.NewLine) so generated text is byte-identical across
            // platforms, keeping the determinism property (task 5.4) stable (R7.4).
            const string nl = "\n";
            var accessorClassName = BuildAccessorClassName(model);
            var members = model.ReadDtos[0].Members;

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>").Append(nl);
            sb.Append("#nullable enable").Append(nl);
            sb.Append(nl);

            // A file-local class: the `file` modifier scopes the type to this generated file, so two views
            // (in one template or across templates) never collide at the type level (C# 11+; consumer TFMs
            // net8/9/10 support it — R7.3). No namespace is emitted; the accessor class is an internal
            // implementation detail referenced only by its own module initializer.
            sb.Append("file static class ").Append(accessorClassName).Append(nl);
            sb.Append("{").Append(nl);
            sb.Append("    public static readonly global::System.Collections.Generic.Dictionary<string, global::System.Func<object, object?>> Map = new()").Append(nl);
            sb.Append("    {").Append(nl);

            foreach (var member in members)
            {
                // ["Member"] = static row => ((global::Ns.NamedRow)row).Member,
                sb.Append("        [").Append(Literal(member.MemberName)).Append("] = static row => ((")
                  .Append(model.RowTypeFqn).Append(")row).").Append(member.MemberName).Append(',').Append(nl);
            }

            sb.Append("    };").Append(nl);
            sb.Append(nl);

            // [ModuleInitializer] registration (R2.2). THE D129 DIFFERENCE FROM D117: a Style A view is an
            // AddView CALL SITE, not a class, so there is nothing to instantiate — the initializer registers
            // the map keyed by the CONSTANT view-name LITERAL lifted from AddView (`Register("customers",
            // Map)`), never `new View().Name`. ViewAccessorRegistry.Register is first-wins idempotent, so a
            // duplicate name keeps the first registration. The method is `internal static void` and
            // parameterless so it satisfies the ModuleInitializer signature contract; all emitted constructs
            // (file-local type, [ModuleInitializer], target-typed `new()`) are available on the lowest
            // consumer TFM, net8.0 (R7.3).
            sb.Append("    [global::System.Runtime.CompilerServices.ModuleInitializer]").Append(nl);
            sb.Append("    internal static void Register()").Append(nl);
            sb.Append("        => global::a2n.Vista.Metadata.ViewAccessorRegistry.Register(").Append(nl);
            sb.Append("               ").Append(Literal(model.ViewName)).Append(", Map);").Append(nl);
            sb.Append("}").Append(nl);

            return sb.ToString();
        }

        /// <summary>
        /// The <c>file static</c> accessor class name: <c>&lt;Template&gt;_&lt;View&gt;_VistaAccessors</c>,
        /// with the view name sanitized to a valid identifier fragment. Because the class is
        /// <c>file</c>-scoped it need only be a valid identifier (its uniqueness is per-generated-file); the
        /// template class + view name keep it readable and stable (R7.4).
        /// </summary>
        private static string BuildAccessorClassName(StyleAViewModel model)
            => model.TemplateClassName + "_" + SanitizeIdentifierPart(model.ViewName) + "_VistaAccessors";

        /// <summary>
        /// Builds a unique <c>AddSource</c> hint name for the view's accessor map. The template namespace and
        /// class are folded in (dots replaced with underscores, mirroring the Phase 1 <c>BuildHintName</c>
        /// convention) and the view name is appended so two views in one template never collide (R7.4). Kept
        /// distinct from the task-5.2 per-view context hint so the two generated files never collide.
        /// </summary>
        private static string BuildAccessorHintName(StyleAViewModel model)
        {
            var prefix = string.IsNullOrEmpty(model.TemplateNamespace)
                ? string.Empty
                : model.TemplateNamespace.Replace('.', '_') + "_";

            return prefix + model.TemplateClassName + "_" + SanitizeIdentifierPart(model.ViewName) + "_VistaAccessors.g.cs";
        }

        // =============================================================================================
        // TASK 5.2 — per-view IJsonTypeInfoResolver emitter (reused D125 file shape via the SHARED
        // JsonContextEmitter; R3.1-R3.5, R4.1, R4.2, R4.4, R4.5, R5.2, R5.4, R7.3, R7.4, R7.5).
        //
        // Emits, for a COVERED view with a constant name, a `file sealed` IJsonTypeInfoResolver built by
        // hand via System.Text.Json.Serialization.Metadata.JsonMetadataServices — the SAME artifact
        // ViewJsonContextGenerator (D125) emits for Style B, produced by the SHARED JsonContextEmitter so
        // the two phases stay byte-for-byte identical (parity by construction). The GetTypeInfo dispatch
        // covers the read set { TRow, ViewListResult<TRow>, PagedResult<TRow> } WHEN TRow is named +
        // emittable AND TCrud WHEN writable + emittable — gated INDEPENDENTLY (R4.2), so a writable view
        // with an anonymous read row emits a context with ONLY TCrud. A [ModuleInitializer] registers the
        // context into the EXISTING GeneratedJsonContextStore (D125) keyed by the CONSTANT view-name LITERAL
        // (the D129 difference from D125's `new <View>().Name` — a Style A view is an AddView call site, not
        // a class, so there is nothing to instantiate). No Activator / PropertyInfo / Expression.Compile /
        // MakeGenericMethod / [JsonSerializable]; net8.0 features + shared-framework STJ only (no NuGet
        // package); emitted into the template's assembly with no ASP.NET Core dependency; deterministic
        // byte-for-byte output; English identifiers/comments only.
        // =============================================================================================

        /// <summary>
        /// Emits the per-view <c>IJsonTypeInfoResolver</c> for a covered Style A view (task 5.2) by building
        /// the flat Serializable_DTO_Set from the EMITTED sides only and delegating to the shared
        /// <see cref="JsonContextEmitter.BuildContextSource"/>. The read set (<c>TRow</c>,
        /// <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>) is included iff
        /// <see cref="StyleAViewModel.HasNamedRowType"/> and <see cref="StyleAViewModel.ReadDtosEmittable"/>;
        /// <c>TCrud</c> is included iff <see cref="StyleAViewModel.IsWritable"/> and
        /// <see cref="StyleAViewModel.CrudDtoEmittable"/> — the two sides gated INDEPENDENTLY (the D96
        /// asymmetry, R4.2). When neither side is emittable the flat set is empty and NOTHING is emitted: the
        /// view has no generated context and stays on the reflection fallback (e.g. an anonymous read-only
        /// view, or a named view whose only DTO is non-emittable). The registration is keyed by the CONSTANT
        /// view-name literal (guaranteed present — <see cref="Emit"/> only reaches here past the
        /// <c>VISTA0062</c> gate). The model's <see cref="StyleAViewModel.AuxTypes"/> was already gated to the
        /// emitted sides in <c>Transform</c>, so it matches the flat DTO set exactly.
        /// </summary>
        private static void EmitJsonContext(SourceProductionContext context, StyleAViewModel model)
        {
            // Build the flat Serializable_DTO_Set from the EMITTED sides only, in the fixed order the shared
            // emitter and the D125 phase use (read DTOs first — TRow, then the two envelopes — then TCrud).
            // ReadDtos already holds { TRow, ViewListResult<TRow>, PagedResult<TRow> } in that order (the
            // shared analyzer appends TRow first); it is added ONLY when the read side is emitted (R4.2).
            var dtos = new List<DtoTypeModel>();

            if (model.HasNamedRowType && model.ReadDtosEmittable)
            {
                foreach (var dto in model.ReadDtos)
                {
                    dtos.Add(dto);
                }
            }

            // TCrud is added ONLY when the write side is emitted, INDEPENDENTLY of the read side (R4.2): a
            // writable view with an anonymous read row (no read DTOs) still emits a context with just TCrud.
            if (model.IsWritable && model.CrudDtoEmittable && model.CrudDto is not null)
            {
                dtos.Add(model.CrudDto);
            }

            // Neither side emittable → no generated context (the view stays on the reflection fallback). This
            // is the "a view with neither side emittable has no generated context" case (design "Coverage
            // classification"): an anonymous read-only view, or a named view whose only DTO is non-emittable.
            if (dtos.Count == 0)
            {
                return;
            }

            // Reuse the shared emitter. The registration key is the CONSTANT view-name LITERAL (the D129
            // difference from D125's `new <View>().Name`); model.AuxTypes was gated to the emitted sides in
            // Transform, so it corresponds exactly to `dtos`.
            var source = JsonContextEmitter.BuildContextSource(
                BuildContextClassName(model),
                dtos,
                model.AuxTypes,
                Literal(model.ViewName));

            context.AddSource(BuildContextHintName(model), SourceText.From(source, Encoding.UTF8));
        }

        /// <summary>
        /// The <c>file sealed</c> per-view context class name:
        /// <c>&lt;Template&gt;_&lt;View&gt;_VistaJsonContext</c>, with the view name sanitized to a valid
        /// identifier fragment. Because the class is <c>file</c>-scoped it need only be a valid identifier
        /// (its uniqueness is per-generated-file); the template class + view name keep it readable and stable
        /// (R7.4). Kept parallel to the accessor class name (<c>_VistaAccessors</c>) so the two generated
        /// types never collide.
        /// </summary>
        private static string BuildContextClassName(StyleAViewModel model)
            => model.TemplateClassName + "_" + SanitizeIdentifierPart(model.ViewName) + "_VistaJsonContext";

        /// <summary>
        /// Builds a unique <c>AddSource</c> hint name for the view's per-view context. The template namespace
        /// and class are folded in (dots replaced with underscores, mirroring the Phase 1/5 convention) and
        /// the view name is appended so two views in one template never collide (R7.4). Kept DISTINCT from
        /// the task-5.1 accessor hint (<c>_VistaAccessors.g.cs</c>) so the two generated files never collide.
        /// </summary>
        private static string BuildContextHintName(StyleAViewModel model)
        {
            var prefix = string.IsNullOrEmpty(model.TemplateNamespace)
                ? string.Empty
                : model.TemplateNamespace.Replace('.', '_') + "_";

            return prefix + model.TemplateClassName + "_" + SanitizeIdentifierPart(model.ViewName) + "_VistaJsonContext.g.cs";
        }

        /// <summary>
        /// Sanitizes an arbitrary view-name string into a valid C# identifier fragment / hint-name token:
        /// ASCII letters and digits pass through, every other character becomes <c>_</c>. Deterministic and
        /// culture-invariant (R7.4). Never empty for a covered view (the constant-name gate guarantees a
        /// non-empty <c>ViewName</c>), but an all-non-alphanumeric name still yields a usable <c>_</c>-run.
        /// </summary>
        private static string SanitizeIdentifierPart(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "_";
            }

            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                var isAscii = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9');
                sb.Append(isAscii ? ch : '_');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Renders a C# string literal for <paramref name="value"/> with backslashes and double quotes
        /// escaped, so an arbitrary constant view name / member name emits safely into the generated source
        /// Delegates to the assembly-wide <see cref="SourceLiterals.Literal"/> so every emitter writes string
        /// literals one way (audit finding <c>DEAD-09</c>).
        /// </summary>
        private static string Literal(string value) => SourceLiterals.Literal(value);

        /// <summary>
        /// The invoked member's simple name for a fluent-call invocation: the <c>.Name</c> of a member access
        /// (<c>views.AddView(...)</c>), a conditional member binding (<c>views?.AddView(...)</c>), or a bare
        /// simple name (<c>AddView(...)</c>), or <c>null</c> when the invoked expression is none of those.
        /// Handles both the inferred form (<c>AddView(...)</c>, an <see cref="IdentifierNameSyntax"/> since
        /// <c>TRow</c> is inferred from the projection) and the explicit form (<c>AddView&lt;TRow&gt;(...)</c>,
        /// a <see cref="GenericNameSyntax"/>) because both derive <see cref="SimpleNameSyntax"/>.
        /// </summary>
        private static string GetInvokedName(InvocationExpressionSyntax invocation)
            => GetInvokedNameNode(invocation)?.Identifier.ValueText;

        /// <summary>
        /// The <see cref="SimpleNameSyntax"/> naming the invoked member of a fluent call (see
        /// <see cref="GetInvokedName"/>), used both for the fast predicate and to anchor the diagnostic
        /// location at the <c>AddView</c> name token. Returns <c>null</c> when the invoked expression is not a
        /// member access / member binding / simple name.
        /// </summary>
        private static SimpleNameSyntax GetInvokedNameNode(InvocationExpressionSyntax invocation)
        {
            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    return memberAccess.Name;
                case MemberBindingExpressionSyntax memberBinding:
                    return memberBinding.Name;
                case SimpleNameSyntax simpleName:
                    return simpleName;
                default:
                    return null;
            }
        }

        /// <summary>
        /// True when <paramref name="method"/> is <c>a2n.Vista.Authoring.IViewTemplateBuilder&lt;TDbContext&gt;
        /// .AddView&lt;TRow&gt;</c>: named <c>AddView</c>, one type argument (<c>TRow</c>), declared on the
        /// <c>IViewTemplateBuilder&lt;&gt;</c> interface (recognized by metadata name + namespace, FQN-only,
        /// R1.6/R7.1).
        /// </summary>
        private static bool IsAddViewMethod(IMethodSymbol method)
            => method.Name == AddViewMethodName
               && method.TypeArguments.Length == 1
               && IsRecognizedAuthoringType(
                   method.ContainingType?.OriginalDefinition,
                   ViewTemplateBuilderMetadataName);

        /// <summary>
        /// True when <paramref name="method"/> is <c>a2n.Vista.Authoring.IReadViewBuilder&lt;TRow&gt;
        /// .WithCrud&lt;TCrud, TEntity&gt;</c>: named <c>WithCrud</c>, two type arguments
        /// (<c>TCrud</c>/<c>TEntity</c>), declared on the <c>IReadViewBuilder&lt;&gt;</c> interface
        /// (recognized by metadata name + namespace, FQN-only, R1.6/R7.1).
        /// </summary>
        private static bool IsWithCrudMethod(IMethodSymbol method)
            => method.Name == WithCrudMethodName
               && method.TypeArguments.Length == 2
               && IsRecognizedAuthoringType(
                   method.ContainingType?.OriginalDefinition,
                   ReadViewBuilderMetadataName);

        /// <summary>
        /// Walks the fluent chain built on the <c>AddView</c> result and returns the <c>TCrud</c> type of a
        /// chained <c>.WithCrud&lt;TCrud, TEntity&gt;()</c> (the only door to Style A writes, R1.5), or
        /// <c>null</c> when the view is read-only. <c>AddView</c> may be followed by any number of read-facet
        /// calls (<c>Field</c>, <c>MaxPageSize</c>, <c>Key</c>, …) before <c>WithCrud</c>, so this ascends each
        /// <c>receiver.Method(...)</c> link, checking each invoked method by name first and confirming
        /// <c>WithCrud</c> semantically (FQN) before capturing its first type argument (<c>TCrud</c>).
        /// </summary>
        private static ITypeSymbol FindChainedCrudType(
            InvocationExpressionSyntax addViewInvocation,
            SemanticModel semanticModel,
            CancellationToken ct)
        {
            ExpressionSyntax receiver = addViewInvocation;

            // Ascend the fluent chain: receiver.<Method>(...) where `receiver` is the current call's result.
            while (receiver.Parent is MemberAccessExpressionSyntax memberAccess
                   && ReferenceEquals(memberAccess.Expression, receiver)
                   && memberAccess.Parent is InvocationExpressionSyntax chainedInvocation)
            {
                if (memberAccess.Name.Identifier.ValueText == WithCrudMethodName
                    && semanticModel.GetSymbolInfo(chainedInvocation, ct).Symbol is IMethodSymbol withCrudMethod
                    && IsWithCrudMethod(withCrudMethod))
                {
                    // WithCrud<TCrud, TEntity>(): TCrud is the first type argument (D38: always named).
                    return withCrudMethod.TypeArguments[0];
                }

                receiver = chainedInvocation;
            }

            return null;
        }

        /// <summary>
        /// Constant-folds the <c>AddView(name, projection)</c> <c>name</c> argument to its compile-time
        /// string value. Returns <see langword="true"/> and sets <paramref name="viewName"/> only when the
        /// <c>name</c> argument resolves to a NON-EMPTY compile-time constant string — a string literal, a
        /// <c>const</c> field/local, or <c>nameof(...)</c> all fold; an interpolated string with holes, a
        /// non-const local/property, a method call, or a concatenation with a non-const operand do not.
        /// Returns <see langword="false"/> with a <c>null</c> name for a non-constant, <c>null</c>, or empty
        /// name — the call site cannot be keyed statically and stays on the reflection path (R1.2, the
        /// <c>VISTA0062</c> case).
        /// </summary>
        private static bool TryGetConstantViewName(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            CancellationToken ct,
            out string viewName)
        {
            viewName = null;

            var argumentList = invocation.ArgumentList;
            if (argumentList is null || argumentList.Arguments.Count == 0)
            {
                return false;
            }

            // Locate the `name` argument: an explicit `name:` argument wins (so a reordered call such as
            // AddView(query: ..., name: "orders") is still recognized), otherwise the first positional
            // argument binds to `name` (the first AddView<TRow>(string name, ...) parameter, R1.2).
            var nameArgument = FindNameArgument(argumentList.Arguments);
            if (nameArgument is null)
            {
                return false;
            }

            // GetConstantValue folds every compile-time constant string form to its value: a string literal,
            // a `const` field/local, and `nameof(...)` all fold; an interpolated string with holes, a
            // non-const local/property, a method call, or a concatenation with a non-const operand do NOT
            // (HasValue == false → non-constant). A `null` constant (const string X = null;) is HasValue with
            // a null Value, which fails the `is string` test; an empty string is a constant but is not a
            // usable registry key. Both null and empty are therefore treated as non-constant so the view
            // stays on the reflection path rather than being keyed under an unusable name (R1.2).
            var constant = semanticModel.GetConstantValue(nameArgument.Expression, ct);
            if (constant.HasValue && constant.Value is string name && !string.IsNullOrEmpty(name))
            {
                viewName = name;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Locates the <c>AddView(name, projection)</c> <c>name</c> argument: an explicit <c>name:</c> named
        /// argument (so a reordered call such as <c>AddView(query: ..., name: "orders")</c> is still
        /// recognized), otherwise the first positional argument — <c>name</c> is the first parameter of
        /// <c>AddView&lt;TRow&gt;(string name, ...)</c>. Returns <c>null</c> when the first argument is a
        /// different named argument (no positional <c>name</c>), leaving the call site classified as having
        /// no constant name (R1.2).
        /// </summary>
        private static ArgumentSyntax FindNameArgument(SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            foreach (var argument in arguments)
            {
                if (argument.NameColon?.Name.Identifier.ValueText == "name")
                {
                    return argument;
                }
            }

            var first = arguments[0];
            return first.NameColon is null ? first : null;
        }

        /// <summary>
        /// Walks the base-type chain and returns <see langword="true"/> when <paramref name="symbol"/> derives
        /// from <c>a2n.Vista.Authoring.ViewTemplate&lt;TDbContext&gt;</c> (recognized by metadata name +
        /// namespace, FQN-only, R1.6/R7.1). Style A <c>AddView</c> calls live in a <c>ViewTemplate</c>
        /// subclass's <c>Configure</c> override.
        /// </summary>
        private static bool DerivesFromViewTemplate(INamedTypeSymbol symbol)
        {
            for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            {
                if (IsRecognizedAuthoringType(current.OriginalDefinition, ViewTemplateMetadataName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Matches an unbound Vista authoring type definition by <paramref name="metadataName"/> (arity-encoded,
        /// e.g. <c>ViewTemplate`1</c>) and the <c>a2n.Vista.Authoring</c> containing namespace. FQN-only
        /// recognition, since the generator references no a2n.Vista assembly (R1.6, R7.1).
        /// </summary>
        private static bool IsRecognizedAuthoringType(ITypeSymbol definition, string metadataName)
        {
            if (definition is not INamedTypeSymbol named || named.MetadataName != metadataName)
            {
                return false;
            }

            var ns = named.ContainingNamespace;
            return ns is { IsGlobalNamespace: false } && ns.ToDisplayString() == AuthoringNamespace;
        }

        /// <summary>
        /// True when <paramref name="type"/> is a genuine named contract type: a named type that is not
        /// <c>object</c>, not anonymous, and not an error/type-parameter symbol. When it is not (an
        /// <c>object</c>/anonymous read <c>TRow</c>), the read side is uncovered — unnameable in generated
        /// source, so it stays on the reflection path by design (R1.4, the later <c>VISTA0061</c> case).
        /// </summary>
        private static bool IsNamedContractType(ITypeSymbol type)
            => type is INamedTypeSymbol named
               && !named.IsAnonymousType
               && named.SpecialType != SpecialType.System_Object
               && named.TypeKind != TypeKind.Error;
    }
}
