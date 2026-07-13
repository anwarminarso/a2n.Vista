// Licensed to the a2n.Vista project. Published artifact — English only.
//
// M9 Source Generator (Pillar 3) — the per-view JsonTypeInfo provider (D125,
// source-generator-json-typeinfo).
//
// This is the FOURTH IIncrementalGenerator in the a2n.Vista.SourceGenerators project (netstandard2.0),
// independent of the Phase 1/2 ViewAccessorGenerator, the Phase 3 WriteMapperGenerator, and the Phase 4
// ViewInvokerGenerator. It targets typed "Style B" views (classes deriving a2n.Vista.Authoring.View<TQuery>
// or View<TQuery, TCrud>) and emits, per covered view, a reflection-free
// System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver (built via JsonMetadataServices, NOT the
// [JsonSerializable] attribute route) that provides the JsonTypeInfo for the view's TRow,
// ViewListResult<TRow>, PagedResult<TRow>, and — when writable — TCrud, plus a [ModuleInitializer] that
// registers it into a2n.Vista.Core's GeneratedJsonContextStore keyed by the view's runtime Name (D125).
// Recognition is by fully-qualified name only; the generator references NO a2n.Vista project (D48, R1.6,
// R7.1).
//
// SCOPE OF THIS FILE AS OF TASK 2.2 (tasks.md §2.2, requirements R1.1, R1.2, R1.3, R1.6, R7.1):
//   * Stand up the [Generator] IIncrementalGenerator.
//   * Fast SYNTAX PREDICATE — a ClassDeclarationSyntax that has a non-empty base list (no semantics),
//     mirroring Phases 1/2/3/4.
//   * SEMANTIC TRANSFORM — resolve the declared symbol and keep ONLY genuine serialization candidates: a
//     non-abstract, `partial` class that walks its base types (by fully-qualified metadata name) to
//     a2n.Vista.Authoring.View<TQuery> (arity-1, read-only) or View<TQuery, TCrud> (arity-2, writable).
//     Non-candidates are dropped by returning `null` (R1.1, R1.3). The transform builds the FULLY
//     EQUATABLE ViewJsonContextModel and populates the RECOGNITION / COVERAGE facet (R1.1, R1.2, R1.3):
//       - IsWritable        — arity-2 (the view carries a typed TCrud) vs arity-1 (read-only).
//       - HasNamedRowType   — false when TQuery is object/anonymous/not a named type (the view is not a
//                             serialization candidate; it stays on the developer App_Json_Context /
//                             reflection fallback, R1.1/R1.3).
//       - HasNamedCrudType  — false for a read-only view, and for a writable view whose TCrud is
//                             object/anonymous; true only for a writable view with a named TCrud. When
//                             false on a writable view, JsonTypeInfo is generated for the read DTOs only
//                             (R1.2).
//       - HasPublicParameterlessCtor — whether the generated [ModuleInitializer] can instantiate the view
//                             to read its runtime Name (R1.7; the emitter, task 5.1, skips a view without
//                             one, mirroring Phases 1/2/3/4).
//     All type names are captured `global::`-qualified. The equatable Location is a LocationInfo surrogate
//     (not the non-value-equal Microsoft.CodeAnalysis.Location) so incremental caching holds (R7.2).
//
// SCOPE ADDED BY TASK 2.3 (tasks.md §2.3, requirements R1.4, R1.5, R2.5):
//   * The EMITTABLE_SHAPE ANALYSIS now runs in the semantic transform for a genuine serialization candidate
//     (named TQuery), populating the DTO facet of the equatable ViewJsonContextModel:
//       - Dtos — the Serializable_DTO_Set modeled as DtoTypeModel/DtoMemberModel: TRow, ViewListResult<TRow>,
//         PagedResult<TRow>, and (writable + named) TCrud. TRow/TCrud are walked member-by-member; the two
//         Vista read envelopes are resolved from the compilation as KNOWN shapes over TRow and modeled for
//         the task 5.1 emitter (their emittability follows TRow's, so they are not gated separately).
//       - Per member: the JSON property name is resolved for PARITY with the reflection oracle
//         ([JsonPropertyName] literal, else the seam's JsonSerializerDefaults.Web camel-case policy);
//         [JsonIgnore(Always)] members are dropped; the member type is classified against the
//         Emittable_Shape set (BCL scalars, string, nullable value types, enums, byte[], collections of an
//         emittable element, the Vista envelopes, single-level nested emittable POCOs). The DTO's
//         object-construction kind (Parameterless vs Parameterized/init/required) is detected for R2.5.
//       - AllShapesEmittable — true only when every GATING DTO (TRow, and TCrud when named) is fully
//         emittable; any member the analyzer cannot fully resolve is classified NonEmittable, clearing the
//         flag (the SAFE DEFAULT — parity over coverage, R1.5).
//       - NonEmittableMembers — a "Type.Member (memberTypeFqn)" description per offending TRow/TCrud member,
//         composed into the VISTA0051 message by the diagnostic stage below.
//     A non-candidate (anonymous/object TQuery) keeps the placeholder facet (empty Dtos, AllShapesEmittable
//     == false) and stays on the reflection fallback (R1.3).
//
// SCOPE ADDED BY TASK 3.2 (tasks.md §3.2, requirements R9.1, R9.2, R9.4):
//   * DIAGNOSTIC REPORTING is wired into the source-output stage (Emit). It reads the coverage/emittability
//     fields of the equatable ViewJsonContextModel and is non-blocking (Info/Warning, never Error, R9.4):
//       - VISTA0050 (Info) — one per COVERED view (a serialization candidate, HasNamedRowType == true, with
//         all DTO shapes emittable, AllShapesEmittable == true, AND a public parameterless ctor,
//         HasPublicParameterlessCtor == true, so a [ModuleInitializer] can register its context). Composes
//         the exact Serializable_DTO_Set — { TRow, ViewListResult<TRow>, PagedResult<TRow> } plus TCrud iff
//         writable with a named TCrud — as a comma-joined list of global::-qualified names into the message
//         {1} placeholder, so the developer knows the App_Json_Context entry for that view is optional
//         (R9.1). The build succeeds.
//       - VISTA0051 (Warning) — one per serialization candidate (HasNamedRowType == true) that has a
//         non-emittable DTO member (AllShapesEmittable == false with recorded NonEmittableMembers), naming
//         the offending type/member(s) in the message {1} placeholder; no context is emitted and the view
//         falls back to the developer App_Json_Context / reflection resolver (R9.2). The build succeeds.
//     A class whose TQuery is anonymous/object (HasNamedRowType == false) is NOT a serialization candidate
//     (R1.3): it receives no diagnostic and stays on the reflection fallback. The reportable Location is
//     reconstructed from the equatable LocationInfo via ToLocation(). The per-view IJsonTypeInfoResolver
//     emitter + its [ModuleInitializer] remain deferred to task 5.1 — this stage reports diagnostics only
//     and emits no source.
//
//   With the task 2.3 shape analysis in place, the diagnostic stage reads the populated model: a covered
//   candidate (AllShapesEmittable == true, with a public parameterless ctor) fires VISTA0050, and a
//   candidate with a non-emittable DTO member (AllShapesEmittable == false with recorded
//   NonEmittableMembers) fires VISTA0051 — mirroring how the ViewInvokerGenerator wired VISTA0040/0041 to
//   read its model. The per-view IJsonTypeInfoResolver emitter (task 5.1) consumes the same Dtos facet.
//
// CONTEXT EMISSION EXTRACTED TO THE SHARED JsonContextEmitter (D129 Style A coverage, task 5.2):
//   The per-view IJsonTypeInfoResolver emission code (the `file sealed` resolver + its GetTypeInfo dispatch,
//   the JsonMetadataServices CreateObjectInfo/CreatePropertyInfo/collection factories, and the
//   [ModuleInitializer] registration) was originally authored here (task 5.1). It is now EXTRACTED VERBATIM
//   into the shared JsonContextEmitter so the Style A coverage phase (D129, StyleAShapeGenerator) emits the
//   IDENTICAL context — byte-for-byte serialization parity with the reflection oracle depends on both phases
//   emitting the same code, exactly as the Emittable_Shape ANALYSIS was extracted into EmittableShapeAnalyzer
//   (task 2.4). This generator's BuildContextSource is now a thin wrapper that calls the shared emitter,
//   passing `new <View>().Name` as the [ModuleInitializer] registration key (the ONE per-phase difference:
//   Style A passes the constant AddView name literal instead — design "Keying — the difference from
//   Phases 1/5"). The extraction preserves D125's emitted output byte-for-byte (the shared method's default
//   inputs reproduce the exact former behavior); the D125 byte-identical/determinism tests are the guard.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// Incremental generator that discovers typed Style B views (non-abstract <c>partial</c> classes
    /// deriving from <c>a2n.Vista.Authoring.View&lt;TQuery&gt;</c> or <c>View&lt;TQuery, TCrud&gt;</c>)
    /// and emits a reflection-free per-view
    /// <c>System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver</c> (built via
    /// <c>JsonMetadataServices</c>, via the shared <see cref="JsonContextEmitter"/>) registered via a module
    /// initializer into <c>a2n.Vista.Core</c>'s <c>GeneratedJsonContextStore</c> (D125). It recognizes Vista
    /// types by fully-qualified name only and references no other a2n.Vista project (D48, R1.6, R7.1).
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class ViewJsonContextGenerator : IIncrementalGenerator
    {
        // Metadata names of the two recognized base types. Roslyn encodes arity in the metadata name
        // (View`1 / View`2). We pair these with the containing namespace below. Arity-1 is a read-only
        // serialization candidate; arity-2 additionally carries a typed TCrud (writable). Recognition is by
        // metadata name + namespace only — the generator references no a2n.Vista assembly (R1.6, R7.1).
        private const string ViewSingleMetadataName = "View`1";
        private const string ViewCrudMetadataName = "View`2";
        private const string ViewNamespace = "a2n.Vista.Authoring";

        // Fully-qualified, global::-prefixed open generic names of the Vista read envelopes in the
        // Serializable_DTO_Set. Composed (with the view's TRow) into the VISTA0050 message so it names the
        // exact { TRow, ViewListResult<TRow>, PagedResult<TRow> } (+ TCrud) set now served by the generated
        // context (R9.1). These are string constants — FQN-only recognition, no symbol references (R7.1) —
        // and deliberately mirror the ViewInvokerGenerator's VISTA0041 composition for parity.
        private const string ViewListResultOpenFqn = "global::a2n.Vista.Ports.ViewListResult<";
        private const string PagedResultOpenFqn = "global::a2n.Vista.Results.PagedResult<";

        // NOTE: the Emittable_Shape member-classification and JSON-property-name rules (and the metadata-name
        // / attribute-FQN constants they use) live in the SHARED EmittableShapeAnalyzer, and the per-view
        // IJsonTypeInfoResolver EMISSION lives in the SHARED JsonContextEmitter, so the Style A coverage
        // phase (D129) classifies DTOs AND emits contexts identically — byte-for-byte serialization parity
        // with the reflection oracle depends on both phases applying the exact same rules and emitting the
        // exact same code. This generator's Transform calls EmittableShapeAnalyzer.BuildReadDtoSet /
        // BuildDtoModel and its Emit calls JsonContextEmitter.BuildContextSource; only the emitter-specific
        // constants (the global::-prefixed envelope FQNs above) stay local here.

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // CreateSyntaxProvider pipeline: cheap syntactic filter first, semantic resolution second.
            // The transform yields a fully equatable ViewJsonContextModel (or null to drop non-candidates),
            // so Roslyn's incremental cache can skip re-emitting views whose model is unchanged (R7.2,
            // mirroring Phases 1/2/3/4).
            var candidates = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateClass(node),
                    transform: static (ctx, ct) => Transform(ctx, ct))
                .Where(static model => model is not null)
                // Tag the equatable-model stage so the incremental host records its per-step cache outcome.
                // This is observability only — it does not change emission — and lets the generator tests
                // assert cache reuse (IncrementalStepRunReason.Cached/Unchanged), proving the equatable
                // value model (R7.2, mirroring TrackingNames.ViewModel / WriteMapperModel /
                // ViewInvokerModel). See TrackingNames.
                .WithTrackingName(TrackingNames.ViewJsonContextModel);

            // Source-output stage. Task 3.2 wires the VISTA0050/VISTA0051 diagnostics and task 5.1 emits the
            // per-view IJsonTypeInfoResolver + its [ModuleInitializer] (via the shared JsonContextEmitter).
            context.RegisterSourceOutput(candidates, static (spc, model) => Emit(spc, model));
        }

        /// <summary>
        /// Fast syntax predicate (no semantics): a class declaration that has a non-empty base list. Cheap
        /// enough to run on every changed node; the semantic transform does the precise FQN-based filtering
        /// (non-abstract, partial, derives <c>View&lt;TQuery&gt;</c>/<c>View&lt;TQuery, TCrud&gt;</c>).
        /// Mirrors the Phase 1/2/3/4 predicate.
        /// </summary>
        private static bool IsCandidateClass(SyntaxNode node)
            => node is ClassDeclarationSyntax classDecl
               && classDecl.BaseList is not null
               && classDecl.BaseList.Types.Count > 0;

        /// <summary>
        /// Semantic transform (task 2.2): resolve the declared symbol and keep it only when it is a genuine
        /// serialization candidate — a non-abstract, <c>partial</c> class deriving (by fully-qualified
        /// metadata name) from <c>a2n.Vista.Authoring.View&lt;TQuery&gt;</c> or <c>View&lt;TQuery,
        /// TCrud&gt;</c>. Returns a fully equatable <see cref="ViewJsonContextModel"/> carrying the type
        /// fields and coverage flags, or <c>null</c> to drop the class (R1.1, R1.3). The Emittable_Shape
        /// analysis (task 2.3) runs via the shared <see cref="EmittableShapeAnalyzer"/>.
        /// </summary>
        private static ViewJsonContextModel Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var classDecl = (ClassDeclarationSyntax)ctx.Node;

            if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol symbol)
            {
                return null;
            }

            // R1.3: candidates are classes only, and abstract views are never serialization candidates.
            if (symbol.TypeKind != TypeKind.Class || symbol.IsAbstract)
            {
                return null;
            }

            // R1.3 (silent): a non-partial view is dropped here. The VISTA0001 "must be partial" diagnostic
            // is owned by the Phase 1 ViewAccessorGenerator, so this generator does not re-report it — it
            // simply produces no per-view JsonTypeInfo context (mirroring the write-mapper / invoker
            // generators' drop rule).
            var isPartial = classDecl.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword));
            if (!isPartial)
            {
                return null;
            }

            // Walk the base-type chain to the recognized View<TQuery> or View<TQuery, TCrud> definition.
            // Recognition is by metadata name (encodes arity) + namespace, since the generator references
            // no a2n.Vista assembly (R1.6, R7.1). A class that derives neither is not a candidate.
            var viewBase = FindViewBase(symbol);
            if (viewBase is null)
            {
                return null;
            }

            // Arity-2 (View<TQuery, TCrud>) is writable; arity-1 (View<TQuery>) is read-only. The write
            // facet requires a typed TCrud (R1.2).
            var isWritable = viewBase.OriginalDefinition.MetadataName == ViewCrudMetadataName;

            // TQuery is the first type argument; TCrud (writable only) is the second.
            var rowType = viewBase.TypeArguments.Length > 0 ? viewBase.TypeArguments[0] : null;
            var crudType = isWritable && viewBase.TypeArguments.Length > 1 ? viewBase.TypeArguments[1] : null;

            // Coverage flags (R1.1, R1.2, R1.3). HasNamedRowType is false when TQuery is object/anonymous/
            // not a named type — the view is not a serialization candidate (it stays on the developer
            // App_Json_Context / reflection fallback). HasNamedCrudType is false for a read-only view and
            // for a writable view whose TCrud is object/anonymous (read-DTO coverage only).
            var hasNamedRowType = IsNamedContractType(rowType);
            var hasNamedCrudType = isWritable && IsNamedContractType(crudType);

            // Whether the view can be instantiated by the generated [ModuleInitializer] (task 5.1) to read
            // its runtime Name. InstanceConstructors includes the IMPLICIT public default ctor when the
            // class declares none, so this single check covers both "no declared ctors" and "explicitly
            // declared public parameterless ctor" (R1.7).
            var hasPublicParameterlessCtor = symbol.InstanceConstructors.Any(
                static c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

            // global::-qualified FQNs. RowTypeFqn is always captured (defensively falling back to object
            // for the uncovered case); CrudTypeFqn is null for a read-only view or an unnamed TCrud so the
            // emitter knows there is no write model to provide a JsonTypeInfo for.
            var rowTypeFqn = rowType is not null
                ? rowType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : "global::System.Object";
            var crudTypeFqn = hasNamedCrudType
                ? crudType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;

            var @namespace = symbol.ContainingNamespace?.IsGlobalNamespace == true
                ? null
                : symbol.ContainingNamespace?.ToDisplayString();
            var viewFqn = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // -------------------------------------------------------------------------------------
            // Emittable_Shape analysis (task 2.3, R1.4/R1.5/R2.5).
            // -------------------------------------------------------------------------------------
            // Build the Serializable_DTO_Set — { TRow, ViewListResult<TRow>, PagedResult<TRow> } plus
            // TCrud (writable + named) — and classify every public serializable member of TRow and TCrud
            // against the Emittable_Shape set. The two Vista read envelopes are KNOWN shapes over TRow, so
            // their emittability follows TRow's; they are still modeled (member metadata + JSON names) for
            // the task 5.1 emitter. A view is COVERED only when every GATING DTO (TRow, and TCrud when it
            // is a named write model) is fully emittable — the safe default is "not emittable" over a
            // best-effort context that could drift from the reflection oracle (R1.5). The analysis runs
            // only for a genuine serialization candidate (named TQuery); a non-candidate keeps the placeholder
            // facet (empty Dtos, AllShapesEmittable == false) and stays on the reflection fallback (R1.3).
            var dtoModels = new List<DtoTypeModel>();
            var nonEmittable = new List<string>();

            // Auxiliary (non-object) types the generated resolver must ALSO provide a JsonTypeInfo for —
            // nullable value types and collections reachable from the DTO set (notably the envelope's Items
            // member IReadOnlyList<TRow>) — so the covered DTOs (de)serialize with NO reflection fallback in
            // the chain (R2.1, R8.1). Collected in fixed, first-occurrence order for deterministic output
            // (R7.4); deduplicated by FQN.
            var auxTypes = new List<AuxTypeModel>();
            var auxSeen = new HashSet<string>(System.StringComparer.Ordinal);
            var allShapesEmittable = false;

            if (hasNamedRowType && rowType is INamedTypeSymbol rowNamed)
            {
                var compilation = ctx.SemanticModel.Compilation;

                // TRow + the two Vista read envelopes (ViewListResult<TRow>, PagedResult<TRow>) — built by the
                // SHARED EmittableShapeAnalyzer so the emittable-shape rules are IDENTICAL to the Style A
                // coverage phase (D129), which byte-for-byte parity with the reflection oracle depends on.
                // TRow's members gate coverage and are recorded into NonEmittableMembers on failure; the two
                // envelopes are known shapes over TRow (fixed order: ViewListResult, then PagedResult,
                // matching the emitter's GetTypeInfo dispatch) whose offending members are routed to a
                // throwaway sink (no duplicate NonEmittableMembers entries) while their collection member
                // IReadOnlyList<TRow> (PagedResult.Items) IS collected into auxTypes so the no-fallback chain
                // can resolve it (R2.1, R8.1).
                var rowEmittable = EmittableShapeAnalyzer.BuildReadDtoSet(
                    compilation, rowNamed, dtoModels, nonEmittable, auxTypes, auxSeen);

                // TCrud — gates coverage only when the view is writable with a named write model (R1.2).
                var crudEmittable = true;
                if (hasNamedCrudType && crudType is INamedTypeSymbol crudNamed)
                {
                    crudEmittable = EmittableShapeAnalyzer.BuildDtoModel(crudNamed, nonEmittable, dtoModels, auxTypes, auxSeen);
                }

                allShapesEmittable = rowEmittable && crudEmittable;
            }

            var dtos = new EquatableArray<DtoTypeModel>(dtoModels.ToArray());
            var nonEmittableMembers = new EquatableArray<string>(nonEmittable.ToArray());
            var auxTypeModels = new EquatableArray<AuxTypeModel>(auxTypes.ToArray());

            return new ViewJsonContextModel(
                @namespace: @namespace,
                className: symbol.Name,
                viewFqn: viewFqn,
                rowTypeFqn: rowTypeFqn,
                crudTypeFqn: crudTypeFqn,
                isWritable: isWritable,
                isPartial: isPartial,
                isAbstract: symbol.IsAbstract,
                hasNamedRowType: hasNamedRowType,
                hasNamedCrudType: hasNamedCrudType,
                hasPublicParameterlessCtor: hasPublicParameterlessCtor,
                dtos: dtos,
                allShapesEmittable: allShapesEmittable,
                nonEmittableMembers: nonEmittableMembers,
                auxTypes: auxTypeModels,
                location: LocationInfo.From(classDecl.Identifier));
        }

        /// <summary>
        /// Source-output stage. Task 3.2 wires the non-blocking serialization-context diagnostics into the
        /// pipeline (R9.1, R9.2, R9.4), reading the coverage/emittability facet of the equatable model:
        /// <list type="bullet">
        ///   <item>
        ///     A class whose <c>TQuery</c> is anonymous/<c>object</c>
        ///     (<see cref="ViewJsonContextModel.HasNamedRowType"/> is <c>false</c>) is <em>not</em> a
        ///     serialization candidate (R1.3): no diagnostic is reported and it stays on the developer
        ///     <c>App_Json_Context</c> / reflection fallback.
        ///   </item>
        ///   <item>
        ///     A candidate with a non-emittable DTO member
        ///     (<see cref="ViewJsonContextModel.AllShapesEmittable"/> is <c>false</c> with recorded
        ///     <see cref="ViewJsonContextModel.NonEmittableMembers"/>) gets exactly one <c>VISTA0051</c>
        ///     (Warning) naming the offending type/member(s); no context is emitted and the view falls back
        ///     to the developer <c>App_Json_Context</c> / reflection resolver (R9.2).
        ///   </item>
        ///   <item>
        ///     A <em>covered</em> view (<see cref="ViewJsonContextModel.AllShapesEmittable"/> is <c>true</c>)
        ///     that has a public parameterless ctor
        ///     (<see cref="ViewJsonContextModel.HasPublicParameterlessCtor"/> is <c>true</c>, so a
        ///     <c>[ModuleInitializer]</c> can register its context) gets exactly one <c>VISTA0050</c> (Info)
        ///     naming the exact Serializable_DTO_Set — <c>{ TRow, ViewListResult&lt;TRow&gt;,
        ///     PagedResult&lt;TRow&gt; }</c> plus <c>TCrud</c> when writable with a named <c>TCrud</c> — as a
        ///     comma-joined list of <c>global::</c>-qualified names, so the developer knows the
        ///     <c>App_Json_Context</c> entry for that view is optional (R9.1). A covered view WITHOUT a
        ///     public parameterless ctor emits no <c>[ModuleInitializer]</c> and its context is never
        ///     registered (R1.7, R4.5), so it is not reported as covered.
        ///   </item>
        /// </list>
        /// All serialization-context diagnostics are Info/Warning — never Error — so this stage is
        /// non-blocking and the build always succeeds (R9.4). The reportable <see cref="Location"/> is
        /// reconstructed from the equatable <see cref="LocationInfo"/> via
        /// <see cref="LocationInfo.ToLocation"/>. For a covered view the per-view
        /// <c>IJsonTypeInfoResolver</c> is emitted via the shared <see cref="JsonContextEmitter"/> (task 5.1).
        /// </summary>
        private static void Emit(SourceProductionContext context, ViewJsonContextModel model)
        {
            var location = model.Location?.ToLocation() ?? Location.None;

            // Not a serialization candidate (anonymous/object TQuery): leave it on the developer
            // App_Json_Context / reflection fallback, no diagnostic (R1.3). VISTA0050/VISTA0051 apply only
            // to named-TQuery candidates.
            if (!model.HasNamedRowType)
            {
                return;
            }

            // Candidate with a non-emittable DTO member: VISTA0051 (Warning, non-blocking). No context is
            // emitted; the view falls back to the developer App_Json_Context / reflection resolver and the
            // build succeeds (R1.5, R9.2, R9.4). The message names the offending type/member(s) from
            // NonEmittableMembers. Guarded on a recorded entry so nothing is reported until the shape
            // analysis (task 2.3) populates the field — see the file-header placeholder note.
            if (!model.AllShapesEmittable)
            {
                if (model.NonEmittableMembers.Count > 0)
                {
                    var offendingMembers = string.Join(", ", model.NonEmittableMembers);
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.JsonContextMemberNotEmittable,
                        location,
                        model.ClassName,
                        offendingMembers));
                }

                return;
            }

            // Covered view without a public parameterless ctor: the generated [ModuleInitializer] cannot
            // instantiate the view to read its runtime Name, so no context is registered and the
            // App_Json_Context stays required for it (R1.7, R4.5). Do not claim coverage — no VISTA0050.
            if (!model.HasPublicParameterlessCtor)
            {
                return;
            }

            // Covered view: VISTA0050 (Info) naming the exact Serializable_DTO_Set now served by the
            // generated context, so the developer knows the App_Json_Context entry for that view is
            // optional (R9.1, R9.4).
            var serializableDtoSet = string.Join(", ", BuildSerializableDtoSetFqns(model));
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.GeneratedJsonContextForView,
                location,
                model.ClassName,
                serializableDtoSet));

            // Task 5.1: emit the per-view reflection-free IJsonTypeInfoResolver + its [ModuleInitializer]
            // for this covered view (via the shared JsonContextEmitter). The gates above guarantee the view
            // is a serialization candidate (HasNamedRowType), every gating DTO shape is emittable
            // (AllShapesEmittable), and the view can be instantiated by the [ModuleInitializer] to read its
            // runtime Name (HasPublicParameterlessCtor) — the exact coverage contract of
            // R2.1/R4.1/R4.5/R1.7.
            var source = BuildContextSource(model);
            context.AddSource(BuildHintName(model), SourceText.From(source, Encoding.UTF8));
        }

        /// <summary>
        /// Composes the ordered, <c>global::</c>-qualified Serializable_DTO_Set for a covered view:
        /// <c>{ TRow, ViewListResult&lt;TRow&gt;, PagedResult&lt;TRow&gt; }</c> plus <c>TCrud</c> when the
        /// view is writable with a named <c>TCrud</c> (R9.1). The <c>ViewListResult&lt;&gt;</c>/
        /// <c>PagedResult&lt;&gt;</c> FQNs mirror the ViewInvokerGenerator's VISTA0041 composition for
        /// parity. The fixed order makes the composed message deterministic across runs (R7.4).
        /// </summary>
        private static List<string> BuildSerializableDtoSetFqns(ViewJsonContextModel model)
        {
            var types = new List<string>
            {
                model.RowTypeFqn,
                ViewListResultOpenFqn + model.RowTypeFqn + ">",
                PagedResultOpenFqn + model.RowTypeFqn + ">",
            };

            if (model.HasNamedCrudType && model.CrudTypeFqn is not null)
            {
                types.Add(model.CrudTypeFqn);
            }

            return types;
        }

        /// <summary>
        /// Walks the base-type chain and returns the constructed <c>View&lt;TQuery&gt;</c> or
        /// <c>View&lt;TQuery, TCrud&gt;</c> base (so callers can read arity + type arguments), or
        /// <c>null</c> when the symbol derives from neither recognized View type. Recognition is by
        /// metadata name (encodes arity) + namespace, since the generator references no a2n.Vista assembly
        /// (R1.6, R7.1).
        /// </summary>
        private static INamedTypeSymbol FindViewBase(INamedTypeSymbol symbol)
        {
            for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            {
                if (IsRecognizedViewDefinition(current.OriginalDefinition))
                {
                    return current;
                }
            }

            return null;
        }

        /// <summary>
        /// Matches the unbound arity-1 (<c>View`1</c>) or arity-2 (<c>View`2</c>) View definition by
        /// metadata name + containing namespace (<c>a2n.Vista.Authoring</c>). FQN-only recognition
        /// (R1.6, R7.1).
        /// </summary>
        private static bool IsRecognizedViewDefinition(INamedTypeSymbol definition)
        {
            if (definition is null)
            {
                return false;
            }

            if (definition.MetadataName != ViewSingleMetadataName
                && definition.MetadataName != ViewCrudMetadataName)
            {
                return false;
            }

            var ns = definition.ContainingNamespace;
            return ns is not null
                   && !ns.IsGlobalNamespace
                   && ns.ToDisplayString() == ViewNamespace;
        }

        /// <summary>
        /// True when <paramref name="type"/> is a genuine named contract type: a named type that is not
        /// <c>object</c>, not anonymous, and not an error/type-parameter symbol (R1.1, R1.2, R1.3). When it
        /// is not (e.g. an <c>object</c>/anonymous <c>TQuery</c> or <c>TCrud</c>), the corresponding
        /// coverage flag is cleared so the view is treated as uncovered for that facet.
        /// </summary>
        private static bool IsNamedContractType(ITypeSymbol type)
            => type is INamedTypeSymbol named
               && !named.IsAnonymousType
               && named.SpecialType != SpecialType.System_Object
               && named.TypeKind != TypeKind.Error;

        // -----------------------------------------------------------------------------------------------
        // Per-view IJsonTypeInfoResolver emission — delegates to the shared JsonContextEmitter (task 5.2).
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds the per-view generated source <c>&lt;View&gt;_VistaJsonContext.g.cs</c> by delegating to
        /// the shared <see cref="JsonContextEmitter.BuildContextSource"/> — a <c>file sealed</c> class
        /// implementing <c>System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver</c> whose
        /// <c>GetTypeInfo(Type, options)</c> returns the <c>JsonMetadataServices</c>-built
        /// <c>JsonTypeInfo</c> for each type in the Serializable_DTO_Set (<c>TRow</c>,
        /// <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>, and — writable only —
        /// <c>TCrud</c>) plus the auxiliary arms, and <c>null</c> otherwise (R2.1). The
        /// <c>[ModuleInitializer]</c> registers the context into <c>GeneratedJsonContextStore</c> keyed by
        /// the view's RUNTIME <c>Name</c> — <c>new &lt;View&gt;().Name</c> — the typed Style B keying (D125);
        /// the Style A phase passes a constant name literal to the same shared emitter instead (D129, the
        /// one per-phase difference). Reflection-free, attribute-free, deterministic (R2.2, R7.3, R7.4).
        /// </summary>
        private static string BuildContextSource(ViewJsonContextModel model)
            => JsonContextEmitter.BuildContextSource(
                model.ClassName + "_VistaJsonContext",
                model.Dtos,
                model.AuxTypes,
                "new " + model.ViewFqn + "().Name");

        /// <summary>
        /// Builds a unique <c>AddSource</c> hint name for the view's generated JSON context. The namespace is
        /// folded into the name (dots replaced with underscores) so two views sharing a class name in
        /// different namespaces do not collide, mirroring the Phase 1/2/3/4 hint-name convention.
        /// </summary>
        private static string BuildHintName(ViewJsonContextModel model)
        {
            var prefix = string.IsNullOrEmpty(model.Namespace)
                ? string.Empty
                : model.Namespace.Replace('.', '_') + "_";

            return prefix + model.ClassName + "_VistaJsonContext.g.cs";
        }
    }
}
