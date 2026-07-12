// Licensed to the a2n.Vista project. Published artifact — English only.
//
// M9 Source Generator (Pillar 3) — the per-view JsonTypeInfo provider (D125,
// source-generator-json-typeinfo).
//
// This is the FOURTH IIncrementalGenerator in the a2n.Vista.SourceGenerators project (netstandard2.0),
// independent of the Phase 1/2 ViewAccessorGenerator, the Phase 3 WriteMapperGenerator, and the Phase 4
// ViewInvokerGenerator. It targets typed "Style B" views (classes deriving a2n.Vista.Authoring.View<TQuery>
// or View<TQuery, TCrud>) and will — in later tasks — emit, per covered view, a reflection-free
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
//   NOT YET IN SCOPE (deferred to later tasks):
//     * The per-view IJsonTypeInfoResolver emitter + its [ModuleInitializer] is TASK 5.1.
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
    /// and — in later tasks — emits a reflection-free per-view
    /// <c>System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver</c> (built via
    /// <c>JsonMetadataServices</c>) registered via a module initializer into <c>a2n.Vista.Core</c>'s
    /// <c>GeneratedJsonContextStore</c> (D125). It recognizes Vista types by fully-qualified name only and
    /// references no other a2n.Vista project (D48, R1.6, R7.1).
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

        // Reflection metadata names (arity-encoded) + namespaces of the two Vista read envelopes, used to
        // resolve their constructed symbols from the compilation for the Serializable_DTO_Set (task 2.3)
        // and to recognize them as known emittable shapes when they appear as a DTO member. FQN-only
        // recognition — no a2n.Vista assembly reference (R1.6, R7.1).
        private const string ViewListResultMetadataName = "a2n.Vista.Ports.ViewListResult`1";
        private const string PagedResultMetadataName = "a2n.Vista.Results.PagedResult`1";
        private const string ViewListResultSimpleMetadataName = "ViewListResult`1";
        private const string PagedResultSimpleMetadataName = "PagedResult`1";
        private const string ViewListResultNamespace = "a2n.Vista.Ports";
        private const string PagedResultNamespace = "a2n.Vista.Results";
        private const string CollectionsGenericNamespace = "System.Collections.Generic";

        // Fully-qualified names of the System.Text.Json attributes the shape analysis honors for parity
        // with the reflection oracle: [JsonPropertyName] overrides the naming policy; [JsonIgnore] drops a
        // member from the serializable set. Recognized by FQN only (R2.3, R6.4).
        private const string JsonPropertyNameAttributeFqn = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
        private const string JsonIgnoreAttributeFqn = "System.Text.Json.Serialization.JsonIgnoreAttribute";

        // JsonIgnoreCondition.Always == 1 (the default a bare [JsonIgnore] carries): the member is never
        // serialized and is dropped from the set. Any other condition (Never/WhenWriting*) still serializes.
        private const int JsonIgnoreConditionAlways = 1;

        // Single level of nested-POCO support (design v1 target): a top-level DTO member may itself be a
        // POCO (budget 1), but that nested POCO's members must be leaf shapes (budget 0 → no further POCOs).
        // Deeper nesting is deferred and classified NonEmittable — the safe default over the oracle (R1.5).
        private const int TopLevelPocoBudget = 1;

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
            // per-view IJsonTypeInfoResolver + its [ModuleInitializer]. Until then this is a no-op so the
            // generator is inert but present — the recognition/model pipeline is exercised by the
            // generator-driver tests (task 2.4).
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
        /// analysis (task 2.3) and diagnostic/emission (tasks 3.2/5.1) are deferred, so the DTO facet is
        /// populated with safe placeholders (empty <c>Dtos</c>, <c>AllShapesEmittable == false</c>, empty
        /// <c>NonEmittableMembers</c>).
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

                // TRow — its members gate coverage and are recorded into NonEmittableMembers on failure.
                var rowEmittable = BuildDtoModel(rowNamed, nonEmittable, dtoModels, auxTypes, auxSeen);

                // The two Vista read envelopes are known shapes over TRow (fixed order: ViewListResult,
                // then PagedResult, matching the emitter's GetTypeInfo dispatch). Their offending members
                // (when TRow is non-emittable) are already recorded via TRow, so envelope walking does not
                // add duplicate NonEmittableMembers entries (a throwaway sink is used). Their collection
                // member IReadOnlyList<TRow> (PagedResult.Items) IS collected into auxTypes so the no-fallback
                // chain can resolve it (R2.1, R8.1).
                AddEnvelopeModel(compilation, ViewListResultMetadataName, rowNamed, dtoModels, auxTypes, auxSeen);
                AddEnvelopeModel(compilation, PagedResultMetadataName, rowNamed, dtoModels, auxTypes, auxSeen);

                // TCrud — gates coverage only when the view is writable with a named write model (R1.2).
                var crudEmittable = true;
                if (hasNamedCrudType && crudType is INamedTypeSymbol crudNamed)
                {
                    crudEmittable = BuildDtoModel(crudNamed, nonEmittable, dtoModels, auxTypes, auxSeen);
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
        /// <see cref="LocationInfo.ToLocation"/>. The per-view <c>IJsonTypeInfoResolver</c> emitter and its
        /// <c>[ModuleInitializer]</c> remain deferred to task 5.1 — this stage reports diagnostics only and
        /// emits no source.
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
            // for this covered view. The gates above guarantee the view is a serialization candidate
            // (HasNamedRowType), every gating DTO shape is emittable (AllShapesEmittable), and the view can
            // be instantiated by the [ModuleInitializer] to read its runtime Name
            // (HasPublicParameterlessCtor) — the exact coverage contract of R2.1/R4.1/R4.5/R1.7.
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
        // Emittable_Shape analysis (task 2.3)
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds the equatable <see cref="DtoTypeModel"/> for one DTO in the Serializable_DTO_Set by
        /// walking its public serializable members (task 2.3): each member's JSON property name is resolved
        /// per the seam's naming policy for parity (<see cref="ResolveJsonPropertyName"/>), and each member
        /// type is classified against the Emittable_Shape set (<see cref="ClassifyType"/>). Returns
        /// <c>true</c> when every member is emittable; records a <c>Type.Member (memberTypeFqn)</c>
        /// description into <paramref name="nonEmittable"/> for each member that is not, so the caller can
        /// compose the VISTA0051 message and classify the view as not covered (R1.5). The DTO's
        /// object-construction kind is detected for R2.5 (<see cref="DetectConstruction"/>). The completed
        /// model is appended to <paramref name="into"/>.
        /// </summary>
        private static bool BuildDtoModel(
            INamedTypeSymbol dtoType,
            List<string> nonEmittable,
            List<DtoTypeModel> into,
            List<AuxTypeModel> auxTypes,
            HashSet<string> auxSeen)
        {
            var members = new List<DtoMemberModel>();
            var allEmittable = true;

            foreach (var property in EnumerateSerializableProperties(dtoType))
            {
                var memberTypeFqn = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var shape = ClassifyType(property.Type, TopLevelPocoBudget, out var emittable);
                if (!emittable)
                {
                    allEmittable = false;
                    nonEmittable.Add($"{dtoType.Name}.{property.Name} ({memberTypeFqn})");
                }
                else
                {
                    // Collect the auxiliary (nullable/collection) JsonTypeInfo arms this member needs so the
                    // covered DTO resolves with NO reflection fallback in the chain (R2.1, R8.1).
                    CollectAuxTypes(property.Type, auxTypes, auxSeen);
                }

                members.Add(new DtoMemberModel(
                    memberName: property.Name,
                    memberTypeFqn: memberTypeFqn,
                    jsonPropertyName: ResolveJsonPropertyName(property),
                    isReadOnly: IsReadOnlyMember(property),
                    shapeKind: shape));
            }

            into.Add(new DtoTypeModel(
                typeFqn: dtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                construction: DetectConstruction(dtoType),
                members: new EquatableArray<DtoMemberModel>(members.ToArray())));

            return allEmittable;
        }

        /// <summary>
        /// Resolves the constructed Vista read envelope (<c>ViewListResult&lt;TRow&gt;</c> or
        /// <c>PagedResult&lt;TRow&gt;</c>) from the compilation and models it as a DTO (task 2.3). The
        /// envelopes are known shapes over an emittable <c>TRow</c>, so their members are modeled for the
        /// task 5.1 emitter but their emittability is NOT gated separately (it follows <c>TRow</c>'s);
        /// offending members are therefore routed to a throwaway sink to avoid duplicating the
        /// <c>TRow</c>-derived entries already recorded by the caller. A no-op when the envelope type is not
        /// present in the compilation (defensive; a real view always references Core).
        /// </summary>
        private static void AddEnvelopeModel(
            Compilation compilation,
            string envelopeMetadataName,
            INamedTypeSymbol rowType,
            List<DtoTypeModel> into,
            List<AuxTypeModel> auxTypes,
            HashSet<string> auxSeen)
        {
            if (compilation.GetTypeByMetadataName(envelopeMetadataName) is not INamedTypeSymbol openEnvelope)
            {
                return;
            }

            var constructed = openEnvelope.Construct(rowType);
            var throwaway = new List<string>();

            // Walk the constructed envelope's members so its collection member (PagedResult.Items —
            // IReadOnlyList<TRow>) is collected into auxTypes; the envelope's emittability follows TRow's, so
            // offending members are routed to a throwaway sink to avoid duplicate NonEmittableMembers entries.
            BuildDtoModel(constructed, throwaway, into, auxTypes, auxSeen);
        }

        /// <summary>
        /// Enumerates the public serializable properties of a DTO in declaration order: public, readable
        /// (public getter), non-static, non-indexer instance properties that are not dropped by
        /// <c>[JsonIgnore]</c>. Mirrors the Phase 1 accessor generator's member selection (declared members
        /// only) and the System.Text.Json default (public instance properties; fields excluded, matching the
        /// seam options which do not set <c>IncludeFields</c>).
        /// </summary>
        private static IEnumerable<IPropertySymbol> EnumerateSerializableProperties(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol property
                    && !property.IsStatic
                    && !property.IsIndexer
                    && property.DeclaredAccessibility == Accessibility.Public
                    && property.GetMethod is not null
                    && property.GetMethod.DeclaredAccessibility == Accessibility.Public
                    && !IsJsonIgnored(property))
                {
                    yield return property;
                }
            }
        }

        /// <summary>
        /// The JSON property name for parity with the reflection oracle (R2.3, R6.4): the literal from
        /// <c>[JsonPropertyName("...")]</c> when present, otherwise the member name run through the seam's
        /// naming policy (<see cref="ToCamelCase"/>, the <see cref="System.Text.Json.JsonSerializerDefaults.Web"/>
        /// default the seam configures).
        /// </summary>
        private static string ResolveJsonPropertyName(IPropertySymbol property)
        {
            foreach (var attribute in property.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == JsonPropertyNameAttributeFqn
                    && attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is string explicitName)
                {
                    return explicitName;
                }
            }

            return ToCamelCase(property.Name);
        }

        /// <summary>
        /// Whether a member is dropped from the serializable set by <c>[JsonIgnore]</c>. A bare
        /// <c>[JsonIgnore]</c> carries <c>Condition = JsonIgnoreCondition.Always</c> (never serialized) and
        /// drops the member; <c>Condition = Never</c> (or a conditional <c>WhenWriting*</c>) keeps it, since
        /// those still serialize the member (matching the oracle).
        /// </summary>
        private static bool IsJsonIgnored(IPropertySymbol property)
        {
            foreach (var attribute in property.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != JsonIgnoreAttributeFqn)
                {
                    continue;
                }

                var condition = JsonIgnoreConditionAlways; // bare [JsonIgnore] defaults to Always.
                foreach (var named in attribute.NamedArguments)
                {
                    if (named.Key == "Condition" && named.Value.Value is int conditionValue)
                    {
                        condition = conditionValue;
                    }
                }

                return condition == JsonIgnoreConditionAlways;
            }

            return false;
        }

        /// <summary>
        /// Whether a member is read-only from the serializer's perspective — no public setter, or an
        /// <c>init</c>-only setter — which forces construction through the parameterized/<c>init</c> path
        /// (R2.5).
        /// </summary>
        private static bool IsReadOnlyMember(IPropertySymbol property)
            => property.SetMethod is null
               || property.SetMethod.DeclaredAccessibility != Accessibility.Public
               || property.SetMethod.IsInitOnly;

        /// <summary>
        /// Detects the DTO's object-construction kind for R2.5: <see cref="ObjectConstructionKind.Parameterless"/>
        /// when the type exposes a public parameterless constructor (System.Text.Json constructs via it and
        /// populates members through setters/<c>init</c>), otherwise
        /// <see cref="ObjectConstructionKind.Parameterized"/> — the case for positional records (including
        /// the Vista envelopes) and types whose only constructors take parameters.
        /// </summary>
        private static ObjectConstructionKind DetectConstruction(INamedTypeSymbol type)
        {
            var hasParameterlessCtor = type.InstanceConstructors.Any(
                static c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

            return hasParameterlessCtor
                ? ObjectConstructionKind.Parameterless
                : ObjectConstructionKind.Parameterized;
        }

        /// <summary>
        /// Classifies a member type against the Emittable_Shape set (design "Data Models"), returning its
        /// <see cref="MemberShapeKind"/> and, via <paramref name="emittable"/>, whether the generator can
        /// emit its <c>JsonTypeInfo</c> reflection-free. The safe default for anything the analyzer cannot
        /// fully resolve (interfaces, <c>object</c>/<c>dynamic</c>, delegates, unresolved generics/type
        /// parameters, dictionaries and other unsupported collections, bespoke-converter shapes, and
        /// nesting beyond the supported single POCO level) is
        /// <see cref="MemberShapeKind.NonEmittable"/>/<c>false</c> — parity over coverage (R1.4, R1.5).
        /// <paramref name="pocoBudget"/> bounds nested-POCO depth: a top-level DTO member is classified with
        /// <see cref="TopLevelPocoBudget"/>, and a nested POCO validates its own members with the budget
        /// decremented so deeper nesting is rejected.
        /// </summary>
        private static MemberShapeKind ClassifyType(ITypeSymbol type, int pocoBudget, out bool emittable)
        {
            // string.
            if (type.SpecialType == SpecialType.System_String)
            {
                emittable = true;
                return MemberShapeKind.String;
            }

            // Nullable value type (T?): emittable when the underlying type is an emittable scalar or enum.
            if (type is INamedTypeSymbol nullable
                && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && nullable.TypeArguments.Length == 1)
            {
                var underlyingKind = ClassifyType(nullable.TypeArguments[0], 0, out var underlyingEmittable);
                emittable = underlyingEmittable
                            && (underlyingKind == MemberShapeKind.Scalar || underlyingKind == MemberShapeKind.Enum);
                return MemberShapeKind.Nullable;
            }

            // Enum (serialized via the seam's JsonStringEnumConverter for parity).
            if (type.TypeKind == TypeKind.Enum)
            {
                emittable = true;
                return MemberShapeKind.Enum;
            }

            // BCL scalar.
            if (IsScalar(type))
            {
                emittable = true;
                return MemberShapeKind.Scalar;
            }

            // byte[] — System.Text.Json base64 default (matches the oracle); treated as a scalar leaf.
            if (type is IArrayTypeSymbol byteArray
                && byteArray.Rank == 1
                && byteArray.ElementType.SpecialType == SpecialType.System_Byte)
            {
                emittable = true;
                return MemberShapeKind.Scalar;
            }

            // Vista read envelope (ViewListResult<T>/PagedResult<T>) — a known shape over an emittable T.
            if (IsVistaEnvelope(type, out var envelopeElement))
            {
                ClassifyType(envelopeElement, pocoBudget, out var envelopeElementEmittable);
                emittable = envelopeElementEmittable;
                return MemberShapeKind.Nested;
            }

            // Collection (array / List<T> / IReadOnlyList<T> / IList<T> / ICollection<T> /
            // IReadOnlyCollection<T> / IEnumerable<T>) of an emittable element.
            if (TryGetEnumerableElement(type, out var element))
            {
                ClassifyType(element, pocoBudget, out var elementEmittable);
                emittable = elementEmittable;
                return MemberShapeKind.Collection;
            }

            // Single-level nested POCO: emittable when the budget allows and every member is emittable.
            if (pocoBudget > 0 && type is INamedTypeSymbol pocoType && IsEmittablePocoCandidate(pocoType))
            {
                var allMembersEmittable = true;
                foreach (var member in EnumerateSerializableProperties(pocoType))
                {
                    ClassifyType(member.Type, pocoBudget - 1, out var memberEmittable);
                    if (!memberEmittable)
                    {
                        allMembersEmittable = false;
                    }
                }

                emittable = allMembersEmittable;
                return MemberShapeKind.Nested;
            }

            // Anything else: not emittable reflection-free — falls back to the developer context / oracle.
            emittable = false;
            return MemberShapeKind.NonEmittable;
        }

        /// <summary>
        /// Whether the type is a BCL scalar the generator can emit directly via
        /// <c>CreatePropertyInfo&lt;T&gt;</c>: the primitive/numeric/<c>char</c> special types plus the
        /// common value scalars (<c>Guid</c>, <c>DateTime</c>, <c>DateTimeOffset</c>, <c>DateOnly</c>,
        /// <c>TimeOnly</c>, <c>TimeSpan</c>).
        /// </summary>
        private static bool IsScalar(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_Char:
                    return true;
            }

            switch (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            {
                case "global::System.Guid":
                case "global::System.DateTime":
                case "global::System.DateTimeOffset":
                case "global::System.DateOnly":
                case "global::System.TimeOnly":
                case "global::System.TimeSpan":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Recognizes the Vista read envelopes <c>a2n.Vista.Ports.ViewListResult&lt;T&gt;</c> and
        /// <c>a2n.Vista.Results.PagedResult&lt;T&gt;</c> by metadata name + namespace (FQN-only, R1.6/R7.1),
        /// yielding the single type argument in <paramref name="element"/> so its emittability can be
        /// checked.
        /// </summary>
        private static bool IsVistaEnvelope(ITypeSymbol type, out ITypeSymbol element)
        {
            element = null;
            if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
            {
                var definition = named.OriginalDefinition;
                var ns = definition.ContainingNamespace?.ToDisplayString();
                if ((definition.MetadataName == ViewListResultSimpleMetadataName && ns == ViewListResultNamespace)
                    || (definition.MetadataName == PagedResultSimpleMetadataName && ns == PagedResultNamespace))
                {
                    element = named.TypeArguments[0];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Yields the element type of a supported single-argument sequence: a rank-1 array, or a
        /// <c>System.Collections.Generic</c> <c>List&lt;T&gt;</c>/<c>IList&lt;T&gt;</c>/
        /// <c>IReadOnlyList&lt;T&gt;</c>/<c>ICollection&lt;T&gt;</c>/<c>IReadOnlyCollection&lt;T&gt;</c>/
        /// <c>IEnumerable&lt;T&gt;</c>. Dictionaries and other keyed/custom collections are deliberately
        /// excluded (they are not in the Emittable_Shape set) so they classify as non-emittable — the safe
        /// default over the oracle.
        /// </summary>
        private static bool TryGetEnumerableElement(ITypeSymbol type, out ITypeSymbol element)
        {
            element = null;

            if (type is IArrayTypeSymbol array && array.Rank == 1)
            {
                element = array.ElementType;
                return true;
            }

            if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
            {
                var definition = named.OriginalDefinition;
                if (definition.ContainingNamespace?.ToDisplayString() == CollectionsGenericNamespace)
                {
                    switch (definition.MetadataName)
                    {
                        case "List`1":
                        case "IList`1":
                        case "IReadOnlyList`1":
                        case "ICollection`1":
                        case "IReadOnlyCollection`1":
                        case "IEnumerable`1":
                            element = named.TypeArguments[0];
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Collects the auxiliary (non-object) <c>JsonTypeInfo</c> arms a serializable member needs so the
        /// covered DTO (de)serializes with NO reflection fallback in the chain (R2.1, R8.1). System.Text.Json
        /// builds scalar/string/enum property metadata from its built-in converters, but for "complex" member
        /// shapes — nullable value types and collections — it resolves the member's <c>JsonTypeInfo</c> from
        /// the resolver chain; without a dispatch arm those throw <c>NotSupportedException</c> when the
        /// reflection resolver is removed. This walks a member type and appends (deduplicated by FQN, in
        /// first-occurrence order for determinism, R7.4):
        /// <list type="bullet">
        ///   <item>a <see cref="AuxTypeKind.Nullable"/> entry for a nullable value type <c>T?</c>;</item>
        ///   <item>a <see cref="AuxTypeKind.Collection"/> entry for a supported collection, then recurses into
        ///   its element so nested collections/nullables are covered too.</item>
        /// </list>
        /// <c>byte[]</c> (a base64 scalar leaf), the Vista envelopes (top-level DTOs), scalars, strings, enums,
        /// and nested POCOs are intentionally NOT collected here (the first three resolve built-in / as their
        /// own object arm; enums ride the seam's registered converter). The element type of a collection
        /// (e.g. <c>TRow</c>, <c>string</c>) resolves from the rest of the chain — <c>TRow</c> from this same
        /// resolver's object arm, a scalar element from the built-in converter.
        /// </summary>
        private static void CollectAuxTypes(ITypeSymbol type, List<AuxTypeModel> auxTypes, HashSet<string> auxSeen)
        {
            // byte[]: a base64 leaf (built-in ByteArrayConverter). Emitted as a scalar leaf arm so the
            // no-fallback chain can resolve it (checked before the collection branch — it is NOT a collection).
            if (type is IArrayTypeSymbol maybeBytes
                && maybeBytes.Rank == 1
                && maybeBytes.ElementType.SpecialType == SpecialType.System_Byte)
            {
                AddScalarAux("byte[]", auxTypes, auxSeen);
                return;
            }

            // string / BCL scalar leaf: emit a value-info arm so a leaf reached via GetNullableConverter or a
            // collection element resolves from this resolver in the no-fallback chain (R2.1, R8.1). Adding an
            // arm for a leaf System.Text.Json could also resolve inline is harmless — the built-in converter
            // matches the oracle, so parity holds — and matches the built-in generator's completeness.
            if (type.SpecialType == SpecialType.System_String || IsScalar(type))
            {
                AddScalarAux(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), auxTypes, auxSeen);
                return;
            }

            // Enum: System.Text.Json still resolves an enum property's JsonTypeInfo from the resolver chain
            // when the reflection fallback is removed, so an enum leaf arm is required (R2.1, R8.1). The arm
            // uses the converter the seam's options resolve for the enum, so it rides the seam's registered
            // JsonStringEnumConverter for parity with the oracle (R2.3, R6.4).
            if (type.TypeKind == TypeKind.Enum)
            {
                var enumFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (auxSeen.Add(enumFqn))
                {
                    auxTypes.Add(new AuxTypeModel(enumFqn, AuxTypeKind.Enum, enumFqn, default));
                }

                return;
            }

            // Nullable value type (T?): needs a CreateValueInfo + GetNullableConverter<T> arm, and its
            // underlying scalar needs its own leaf arm (GetNullableConverter resolves it from the chain).
            if (type is INamedTypeSymbol nullable
                && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && nullable.TypeArguments.Length == 1)
            {
                var nullableFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var underlying = nullable.TypeArguments[0];
                var underlyingFqn = underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (auxSeen.Add(nullableFqn))
                {
                    auxTypes.Add(new AuxTypeModel(nullableFqn, AuxTypeKind.Nullable, underlyingFqn, default));
                }

                // The underlying scalar's leaf arm (enum underlyings ride the seam converter, no arm).
                CollectAuxTypes(underlying, auxTypes, auxSeen);
                return;
            }

            // Vista read envelopes are top-level DTOs (their own object arm), not auxiliary types.
            if (IsVistaEnvelope(type, out _))
            {
                return;
            }

            // Supported collection: needs the matching collection-info arm; recurse into the element so its
            // leaf/nested collection/nullable arm is collected too.
            if (TryGetCollectionShape(type, out var element, out var shape))
            {
                var collectionFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var elementFqn = element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (auxSeen.Add(collectionFqn))
                {
                    auxTypes.Add(new AuxTypeModel(collectionFqn, AuxTypeKind.Collection, elementFqn, shape));
                }

                CollectAuxTypes(element, auxTypes, auxSeen);
            }
        }

        /// <summary>
        /// Adds a scalar/string/<c>byte[]</c> leaf auxiliary arm (deduplicated by FQN), so the no-fallback
        /// chain can resolve the leaf's <c>JsonTypeInfo</c> from this resolver (R2.1, R8.1).
        /// </summary>
        private static void AddScalarAux(string leafFqn, List<AuxTypeModel> auxTypes, HashSet<string> auxSeen)
        {
            if (auxSeen.Add(leafFqn))
            {
                auxTypes.Add(new AuxTypeModel(leafFqn, AuxTypeKind.Scalar, leafFqn, default));
            }
        }

        /// <summary>
        /// Recognizes a supported single-argument collection member and yields both its element type and the
        /// <see cref="CollectionShapeKind"/> that selects the emitter's <c>JsonMetadataServices</c> helper
        /// (mirroring the built-in System.Text.Json source generator's per-shape choice). Rank-1 arrays,
        /// <c>List&lt;T&gt;</c>, and the <c>System.Collections.Generic</c> list/collection/enumerable
        /// interfaces are supported; dictionaries and other keyed/custom collections are excluded (they are
        /// not in the Emittable_Shape set), consistent with <see cref="TryGetEnumerableElement"/>.
        /// </summary>
        private static bool TryGetCollectionShape(ITypeSymbol type, out ITypeSymbol element, out CollectionShapeKind shape)
        {
            element = null;
            shape = default;

            if (type is IArrayTypeSymbol array && array.Rank == 1)
            {
                element = array.ElementType;
                shape = CollectionShapeKind.Array;
                return true;
            }

            if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
            {
                var definition = named.OriginalDefinition;
                if (definition.ContainingNamespace?.ToDisplayString() == CollectionsGenericNamespace)
                {
                    switch (definition.MetadataName)
                    {
                        case "List`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.List;
                            return true;
                        case "IList`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.IList;
                            return true;
                        case "ICollection`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.ICollection;
                            return true;
                        case "IReadOnlyList`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.IReadOnlyList;
                            return true;
                        case "IReadOnlyCollection`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.IReadOnlyCollection;
                            return true;
                        case "IEnumerable`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.IEnumerable;
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the type is a concrete, constructible POCO the generator may recurse into as a nested
        /// object: a non-abstract, non-anonymous class or struct that is not <c>object</c> and not an error
        /// type. Interfaces, delegates, <c>object</c>/<c>dynamic</c>, and type parameters are excluded and
        /// classify as non-emittable.
        /// </summary>
        private static bool IsEmittablePocoCandidate(INamedTypeSymbol type)
            => !type.IsAnonymousType
               && !type.IsAbstract
               && (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct)
               && type.SpecialType != SpecialType.System_Object
               && type.TypeKind != TypeKind.Error;

        /// <summary>
        /// Applies the seam's <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> camel-case naming
        /// policy to a member name for parity with the reflection oracle (R2.3, R6.4). This faithfully
        /// mirrors System.Text.Json's built-in <c>JsonNamingPolicy.CamelCase</c> conversion (including its
        /// acronym handling, e.g. <c>OrderID → orderID</c>) so the generated wire names match byte-for-byte.
        /// </summary>
        private static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
            {
                return name;
            }

            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (i == 1 && !char.IsUpper(chars[i]))
                {
                    break;
                }

                var hasNext = i + 1 < chars.Length;

                // Stop once the following character is already lower-case (acronym boundary).
                if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
                {
                    // If the following character is a space, lower-case the current one before stopping.
                    if (chars[i + 1] == ' ')
                    {
                        chars[i] = char.ToLowerInvariant(chars[i]);
                    }

                    break;
                }

                chars[i] = char.ToLowerInvariant(chars[i]);
            }

            return new string(chars);
        }

        // -----------------------------------------------------------------------------------------------
        // Per-view IJsonTypeInfoResolver emitter (task 5.1, R2.1-R2.5/R3.1/R3.3/R3.4/R4.1/R4.4/R4.5/R7.3-R7.5)
        // -----------------------------------------------------------------------------------------------

        // Fully-qualified prefixes for the System.Text.Json metadata surface the emitted resolver names.
        // Full global::-qualification (no `using` directives) mirrors the ViewInvokerGenerator's emission
        // style so the generated file never binds to an ambiguous name in the consumer assembly and stays
        // byte-for-byte deterministic (R7.4). System.Text.Json is part of the net8.0/net9.0/net10.0 shared
        // framework, so the emitted file needs no NuGet package and no ASP.NET Core reference (R7.3/R7.5).
        private const string MetaNs = "global::System.Text.Json.Serialization.Metadata.";
        private const string JsonOptionsFqn = "global::System.Text.Json.JsonSerializerOptions";

        /// <summary>
        /// Builds the per-view generated source <c>&lt;View&gt;_VistaJsonContext.g.cs</c> — a
        /// <c>file sealed</c> class implementing
        /// <c>System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver</c> whose
        /// <c>GetTypeInfo(Type, options)</c> returns the <c>JsonMetadataServices</c>-built
        /// <c>JsonTypeInfo</c> for each type in the Serializable_DTO_Set (<c>TRow</c>,
        /// <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>, and — writable only —
        /// <c>TCrud</c>) and <c>null</c> otherwise (defer to the next resolver in the chain, R2.1). The same
        /// class carries exactly one <c>[ModuleInitializer]</c> that registers a singleton into
        /// <c>a2n.Vista.Metadata.GeneratedJsonContextStore</c> keyed by <c>new View().Name</c> (R4.1). Fixed
        /// <c>"\n"</c> line endings and the model's fixed DTO/member order keep the output byte-for-byte
        /// deterministic (R7.4). Reflection-free and attribute-free: no <c>Activator.CreateInstance</c>, no
        /// <c>PropertyInfo</c>, no <c>Expression.Compile</c>, no <c>MakeGenericMethod</c>, no
        /// <c>[JsonSerializable]</c> (R2.2, R7.3).
        /// </summary>
        private static string BuildContextSource(ViewJsonContextModel model)
        {
            // Fixed "\n" line endings (not Environment.NewLine) so generated text is byte-identical across
            // platforms, keeping the determinism property stable (R7.4).
            const string nl = "\n";
            var contextClassName = model.ClassName + "_VistaJsonContext";

            // Deduplicate the Serializable_DTO_Set by fully-qualified type name preserving the model's fixed
            // order (TRow, ViewListResult<TRow>, PagedResult<TRow>, [TCrud]). A view whose TCrud equals its
            // TRow would otherwise emit two identical dispatch arms/factories; first-match-wins keeps the
            // resolver correct, and the dedup keeps the output minimal and deterministic (R7.4).
            var dtos = new List<DtoTypeModel>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var dto in model.Dtos)
            {
                if (seen.Add(dto.TypeFqn))
                {
                    dtos.Add(dto);
                }
            }

            // The auxiliary (non-object) types — nullable value types and collections reachable from the DTO
            // set (notably the envelope's Items collection IReadOnlyList<TRow>) — each get their own dispatch
            // arm + factory so the covered DTOs (de)serialize with NO reflection fallback in the chain (R2.1,
            // R8.1). Deduplicated against the object DTO set (an aux type is never an object DTO) preserving
            // the model's fixed first-occurrence order for deterministic output (R7.4).
            var auxTypes = new List<AuxTypeModel>();
            foreach (var aux in model.AuxTypes)
            {
                if (seen.Add(aux.TypeFqn))
                {
                    auxTypes.Add(aux);
                }
            }

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>").Append(nl);
            sb.Append("#nullable enable").Append(nl);
            sb.Append(nl);

            // A file-local sealed type: the `file` modifier scopes the type to this generated file so two
            // views sharing a class name in different namespaces never collide at the type level (C# 11+;
            // consumer TFMs net8/9/10 support it — R7.3, R7.5). No namespace is emitted; the resolver is an
            // internal implementation detail referenced only by its own [ModuleInitializer].
            sb.Append("file sealed class ").Append(contextClassName)
              .Append(" : ").Append(MetaNs).Append("IJsonTypeInfoResolver").Append(nl);
            sb.Append("{").Append(nl);

            // GetTypeInfo dispatch: one arm per DTO type, else null (defer to the next resolver, R2.1).
            sb.Append("    public ").Append(MetaNs).Append("JsonTypeInfo? GetTypeInfo(").Append(nl);
            sb.Append("        global::System.Type type,").Append(nl);
            sb.Append("        ").Append(JsonOptionsFqn).Append(" options)").Append(nl);
            sb.Append("    {").Append(nl);

            // Object DTO arms first (TRow, ViewListResult<TRow>, PagedResult<TRow>, [TCrud]), then the
            // auxiliary nullable/collection arms — a single contiguous factory index space keeps the emitted
            // names stable and the output deterministic (R7.4).
            for (var i = 0; i < dtos.Count; i++)
            {
                sb.Append("        if (type == typeof(").Append(dtos[i].TypeFqn).Append("))").Append(nl);
                sb.Append("        {").Append(nl);
                sb.Append("            return ").Append(FactoryName(i)).Append("(options);").Append(nl);
                sb.Append("        }").Append(nl);
                sb.Append(nl);
            }

            for (var a = 0; a < auxTypes.Count; a++)
            {
                sb.Append("        if (type == typeof(").Append(auxTypes[a].TypeFqn).Append("))").Append(nl);
                sb.Append("        {").Append(nl);
                sb.Append("            return ").Append(FactoryName(dtos.Count + a)).Append("(options);").Append(nl);
                sb.Append("        }").Append(nl);
                sb.Append(nl);
            }

            sb.Append("        return null;").Append(nl);
            sb.Append("    }").Append(nl);
            sb.Append(nl);

            for (var i = 0; i < dtos.Count; i++)
            {
                AppendTypeInfoFactory(sb, nl, dtos[i], i);
                sb.Append(nl);
            }

            for (var a = 0; a < auxTypes.Count; a++)
            {
                AppendAuxTypeInfoFactory(sb, nl, auxTypes[a], dtos.Count + a);
                sb.Append(nl);
            }

            // [ModuleInitializer] registration (R4.1). The initializer keys the context off the view's
            // RUNTIME Name: it instantiates the view via its public parameterless ctor (guaranteed present —
            // the Emit gate skips a view lacking one, R1.7/R4.5) and reads `.Name` once at module load,
            // before any DI container is constructed. GeneratedJsonContextStore.Register is first-wins
            // idempotent, so a duplicate name keeps the first registration. The method is `internal static
            // void` and parameterless so it satisfies the ModuleInitializer signature contract. ViewFqn is
            // already `global::`-qualified by the semantic transform.
            sb.Append("    [global::System.Runtime.CompilerServices.ModuleInitializer]").Append(nl);
            sb.Append("    internal static void RegisterJsonContext()").Append(nl);
            sb.Append("        => global::a2n.Vista.Metadata.GeneratedJsonContextStore.Register(").Append(nl);
            sb.Append("               new ").Append(model.ViewFqn).Append("().Name, new ").Append(contextClassName).Append("());").Append(nl);
            sb.Append("}").Append(nl);

            return sb.ToString();
        }

        /// <summary>
        /// Appends the factory method that builds one DTO's <c>JsonTypeInfo</c> via
        /// <c>JsonMetadataServices.CreateObjectInfo</c> + <c>CreatePropertyInfo&lt;TMember&gt;</c>. The
        /// construction path is chosen to round-trip records, init-only, and required members (R2.5),
        /// mirroring the built-in System.Text.Json source generator:
        /// <list type="bullet">
        ///   <item>
        ///     <b>Record / positional</b> (no public parameterless ctor): every member maps positionally to
        ///     the primary/parameterized constructor —
        ///     <c>ObjectWithParameterizedConstructorCreator = args =&gt; new T((T0)args[0], …)</c>.
        ///   </item>
        ///   <item>
        ///     <b>Parameterless + init-only/required</b> (public parameterless ctor with at least one
        ///     init-only/read-only member): construct via an object initializer over the init-only members —
        ///     <c>args =&gt; new T() { X = (TX)args[0], … }</c> — while writable members are populated by
        ///     their setters after construction (init-only setters cannot be invoked from a stand-alone
        ///     lambda, so they ride the constructor path exactly like the built-in generator).
        ///   </item>
        ///   <item>
        ///     <b>Parameterless</b> (public parameterless ctor, all members writable):
        ///     <c>ObjectCreator = () =&gt; new T()</c> and every member gets a setter.
        ///   </item>
        /// </list>
        /// All getters/setters are compile-time member access; the <c>options</c> the resolver was queried
        /// with is captured so the metadata honors the seam's <c>JsonSerializerOptions</c> (naming policy,
        /// enum converter) for parity (R2.3, R6.4).
        /// </summary>
        private static void AppendTypeInfoFactory(StringBuilder sb, string nl, DtoTypeModel dto, int index)
        {
            var typeFqn = dto.TypeFqn;
            var members = dto.Members;

            // A record / positional DTO has no public parameterless ctor (Construction == Parameterized):
            // every serializable member maps positionally to the primary constructor. Otherwise the DTO has
            // a public parameterless ctor and its init-only/read-only members (if any) must be set through an
            // object initializer inside the creator — the init-only setter cannot be invoked from a
            // stand-alone lambda (R2.5).
            var recordPositional = dto.Construction == ObjectConstructionKind.Parameterized && members.Count > 0;

            // The members bound through the constructor/creator (and therefore described by
            // ConstructorParameterMetadataInitializer, in this exact order): all members for a positional
            // record; the init-only/read-only members for a parameterless-with-init DTO.
            var ctorBoundMembers = new List<DtoMemberModel>();
            foreach (var member in members)
            {
                if (recordPositional || member.IsReadOnly)
                {
                    ctorBoundMembers.Add(member);
                }
            }

            var useParameterizedCreator = ctorBoundMembers.Count > 0;

            sb.Append("    private static ").Append(MetaNs).Append("JsonTypeInfo<").Append(typeFqn).Append("> ")
              .Append(FactoryName(index)).Append("(").Append(JsonOptionsFqn).Append(" options)").Append(nl);
            sb.Append("    {").Append(nl);
            sb.Append("        var objectInfo = new ").Append(MetaNs).Append("JsonObjectInfoValues<").Append(typeFqn).Append(">").Append(nl);
            sb.Append("        {").Append(nl);

            if (recordPositional)
            {
                // args => new T((T0)args[0], (T1)args[1], …)
                sb.Append("            ObjectWithParameterizedConstructorCreator = static args =>").Append(nl);
                sb.Append("                new ").Append(typeFqn).Append("(").Append(nl);
                for (var m = 0; m < ctorBoundMembers.Count; m++)
                {
                    sb.Append("                    (").Append(ctorBoundMembers[m].MemberTypeFqn).Append(")args[").Append(m).Append("]")
                      .Append(m == ctorBoundMembers.Count - 1 ? ")," : ",").Append(nl);
                }
            }
            else if (useParameterizedCreator)
            {
                // args => new T() { Init0 = (T0)args[0], … } — writable members are set via their setters.
                sb.Append("            ObjectWithParameterizedConstructorCreator = static args =>").Append(nl);
                sb.Append("                new ").Append(typeFqn).Append("()").Append(nl);
                sb.Append("                {").Append(nl);
                for (var m = 0; m < ctorBoundMembers.Count; m++)
                {
                    var member = ctorBoundMembers[m];
                    sb.Append("                    ").Append(member.MemberName).Append(" = (").Append(member.MemberTypeFqn)
                      .Append(")args[").Append(m).Append("],").Append(nl);
                }

                sb.Append("                },").Append(nl);
            }
            else
            {
                sb.Append("            ObjectCreator = static () => new ").Append(typeFqn).Append("(),").Append(nl);
            }

            // Property metadata (getters always; a real setter for a writable member, a throwing guard for a
            // constructor-bound init-only/read-only member — mirroring the built-in generator). The lambda
            // ignores the JsonSerializerContext argument and captures the resolver's `options` so the
            // metadata honors the seam's JsonSerializerOptions (naming policy, enum converter) for parity
            // (R2.3, R6.4).
            sb.Append("            PropertyMetadataInitializer = _ => new ").Append(MetaNs).Append("JsonPropertyInfo[]").Append(nl);
            sb.Append("            {").Append(nl);
            foreach (var member in members)
            {
                AppendPropertyInfo(sb, nl, typeFqn, member);
            }

            sb.Append("            },").Append(nl);

            if (useParameterizedCreator)
            {
                sb.Append("            ConstructorParameterMetadataInitializer = static () => new ").Append(MetaNs).Append("JsonParameterInfoValues[]").Append(nl);
                sb.Append("            {").Append(nl);
                for (var m = 0; m < ctorBoundMembers.Count; m++)
                {
                    var member = ctorBoundMembers[m];
                    sb.Append("                new ").Append(MetaNs).Append("JsonParameterInfoValues").Append(nl);
                    sb.Append("                {").Append(nl);
                    sb.Append("                    Name = ").Append(Literal(member.MemberName)).Append(",").Append(nl);
                    sb.Append("                    ParameterType = typeof(").Append(member.MemberTypeFqn).Append("),").Append(nl);
                    sb.Append("                    Position = ").Append(m).Append(",").Append(nl);
                    sb.Append("                },").Append(nl);
                }

                sb.Append("            },").Append(nl);
            }

            sb.Append("        };").Append(nl);
            sb.Append("        return ").Append(MetaNs).Append("JsonMetadataServices.CreateObjectInfo<").Append(typeFqn)
              .Append(">(options, objectInfo);").Append(nl);
            sb.Append("    }").Append(nl);
        }

        /// <summary>
        /// Appends one <c>JsonMetadataServices.CreatePropertyInfo&lt;TMember&gt;</c> element to the property
        /// metadata array: a compile-time getter (always) plus either a compile-time setter (for a writable
        /// member) or a throwing guard setter (for an init-only/read-only member that is populated through
        /// the constructor/creator path, R2.5 — an init-only setter cannot be invoked from a stand-alone
        /// lambda, exactly as the built-in generator emits). The JSON property name is emitted verbatim from
        /// the model (resolved per the seam's naming policy / <c>[JsonPropertyName]</c>) so the wire name
        /// matches the reflection oracle byte-for-byte (R2.3, R6.4).
        /// </summary>
        private static void AppendPropertyInfo(StringBuilder sb, string nl, string declaringTypeFqn, DtoMemberModel member)
        {
            var memberType = member.MemberTypeFqn;
            sb.Append("                ").Append(MetaNs).Append("JsonMetadataServices.CreatePropertyInfo<").Append(memberType).Append(">(").Append(nl);
            sb.Append("                    options,").Append(nl);
            sb.Append("                    new ").Append(MetaNs).Append("JsonPropertyInfoValues<").Append(memberType).Append(">").Append(nl);
            sb.Append("                    {").Append(nl);
            sb.Append("                        IsProperty = true,").Append(nl);
            sb.Append("                        IsPublic = true,").Append(nl);
            sb.Append("                        DeclaringType = typeof(").Append(declaringTypeFqn).Append("),").Append(nl);
            sb.Append("                        PropertyName = ").Append(Literal(member.MemberName)).Append(",").Append(nl);
            sb.Append("                        JsonPropertyName = ").Append(Literal(member.JsonPropertyName)).Append(",").Append(nl);
            sb.Append("                        Getter = static o => ((").Append(declaringTypeFqn).Append(")o).").Append(member.MemberName).Append(",").Append(nl);
            if (member.IsReadOnly)
            {
                // Init-only/read-only: the value is populated through the constructor/creator path, so the
                // property setter is a guard that throws if ever invoked directly (mirrors the built-in
                // System.Text.Json source generator).
                sb.Append("                        Setter = static (o, v) => throw new global::System.InvalidOperationException(")
                  .Append("\"Setting init-only or read-only members is not supported in source-generated metadata.\"),").Append(nl);
            }
            else
            {
                sb.Append("                        Setter = static (o, v) => ((").Append(declaringTypeFqn).Append(")o).").Append(member.MemberName).Append(" = v,").Append(nl);
            }

            sb.Append("                    }),").Append(nl);
        }

        /// <summary>
        /// Appends the factory method that builds one auxiliary (non-object) type's <c>JsonTypeInfo</c> via
        /// the matching <c>JsonMetadataServices</c> helper, so the covered DTOs (de)serialize with NO
        /// reflection fallback in the chain (R2.1, R8.1):
        /// <list type="bullet">
        ///   <item>
        ///     <b>Nullable value type</b> (<c>T?</c>): <c>CreateValueInfo&lt;T?&gt;(options,
        ///     JsonMetadataServices.GetNullableConverter&lt;T&gt;(options))</c> — the underlying converter is
        ///     resolved from the seam's <c>options</c> so parity holds (a nullable enum, for instance, rides
        ///     the seam's <c>JsonStringEnumConverter</c>).
        ///   </item>
        ///   <item>
        ///     <b>Collection</b>: the shape-specific collection-info helper over a
        ///     <c>JsonCollectionInfoValues&lt;TCollection&gt;</c> — <c>CreateArrayInfo&lt;T&gt;</c>,
        ///     <c>CreateListInfo&lt;List&lt;T&gt;, T&gt;</c> (with a <c>List&lt;T&gt;</c> creator),
        ///     <c>CreateIListInfo</c>/<c>CreateICollectionInfo</c> (mutable interfaces, with a
        ///     <c>List&lt;T&gt;</c> creator), or <c>CreateIEnumerableInfo</c> (the read-only
        ///     <c>IReadOnlyList</c>/<c>IReadOnlyCollection</c>/<c>IEnumerable</c> interfaces). The element
        ///     type's <c>JsonTypeInfo</c> is resolved from the rest of the chain (this resolver's object arm
        ///     for <c>TRow</c>, the built-in converter for a scalar element), mirroring the built-in
        ///     System.Text.Json source generator, so no <c>ElementInfo</c> is set explicitly.
        ///   </item>
        /// </list>
        /// Reflection-free and attribute-free like the object factories (R2.2, R7.3).
        /// </summary>
        private static void AppendAuxTypeInfoFactory(StringBuilder sb, string nl, AuxTypeModel aux, int index)
        {
            var typeFqn = aux.TypeFqn;

            sb.Append("    private static ").Append(MetaNs).Append("JsonTypeInfo<").Append(typeFqn).Append("> ")
              .Append(FactoryName(index)).Append("(").Append(JsonOptionsFqn).Append(" options)").Append(nl);
            sb.Append("    {").Append(nl);

            if (aux.Kind == AuxTypeKind.Scalar)
            {
                // A scalar / string / byte[] leaf: CreateValueInfo<T>(options, <built-in converter>). The
                // built-in converter matches the reflection oracle so parity holds (R2.3, R6.4).
                var converter = ScalarConverterName(typeFqn);
                sb.Append("        return ").Append(MetaNs).Append("JsonMetadataServices.CreateValueInfo<").Append(typeFqn).Append(">(").Append(nl);
                sb.Append("            options, ").Append(MetaNs).Append("JsonMetadataServices.").Append(converter).Append(");").Append(nl);
                sb.Append("    }").Append(nl);
                return;
            }

            if (aux.Kind == AuxTypeKind.Enum)
            {
                // An enum leaf. The seam serializes enums as STRING names (its options register a
                // JsonStringEnumConverter), so for byte-for-byte parity with the reflection oracle (R2.3,
                // R6.4) the arm's converter must be a string enum converter — NOT JsonMetadataServices'
                // GetEnumConverter, which is numeric. It is built DIRECTLY from the AOT-safe GENERIC
                // JsonStringEnumConverter<TEnum> factory (available in the net8/9/10 shared framework),
                // never via options.GetConverter/GetTypeInfo (which would re-enter this resolver and
                // recurse). The generic factory's defaults (no naming policy, integers allowed) match the
                // seam's `new JsonStringEnumConverter()`, so the wire form is identical.
                sb.Append("        return ").Append(MetaNs).Append("JsonMetadataServices.CreateValueInfo<").Append(typeFqn).Append(">(").Append(nl);
                sb.Append("            options,").Append(nl);
                sb.Append("            new global::System.Text.Json.Serialization.JsonStringEnumConverter<").Append(typeFqn)
                  .Append(">().CreateConverter(typeof(").Append(typeFqn).Append("), options)!);").Append(nl);
                sb.Append("    }").Append(nl);
                return;
            }

            if (aux.Kind == AuxTypeKind.Nullable)
            {
                // CreateValueInfo<T?>(options, GetNullableConverter<T>(options)). GetNullableConverter resolves
                // the underlying's JsonTypeInfo from the chain — this resolver provides the underlying scalar's
                // own leaf arm (collected alongside), so the no-fallback chain resolves it (R2.1, R8.1). For an
                // enum underlying the seam's registered JsonStringEnumConverter governs, preserving parity.
                sb.Append("        return ").Append(MetaNs).Append("JsonMetadataServices.CreateValueInfo<").Append(typeFqn).Append(">(").Append(nl);
                sb.Append("            options,").Append(nl);
                sb.Append("            ").Append(MetaNs).Append("JsonMetadataServices.GetNullableConverter<")
                  .Append(aux.ElementOrUnderlyingFqn).Append(">(options));").Append(nl);
                sb.Append("    }").Append(nl);
                return;
            }

            // Collection: build JsonCollectionInfoValues<TCollection> and dispatch to the shape-specific
            // JsonMetadataServices helper. A concrete List<T> gets an ObjectCreator; the mutable interfaces
            // (IList/ICollection) get a List<T> ObjectCreator; the read-only interfaces and arrays let
            // System.Text.Json materialize the backing store.
            var elementFqn = aux.ElementOrUnderlyingFqn;
            sb.Append("        var collectionInfo = new ").Append(MetaNs).Append("JsonCollectionInfoValues<").Append(typeFqn).Append(">").Append(nl);
            sb.Append("        {").Append(nl);
            switch (aux.CollectionShape)
            {
                case CollectionShapeKind.List:
                    sb.Append("            ObjectCreator = static () => new ").Append(typeFqn).Append("(),").Append(nl);
                    break;
                case CollectionShapeKind.IList:
                case CollectionShapeKind.ICollection:
                    sb.Append("            ObjectCreator = static () => new global::System.Collections.Generic.List<")
                      .Append(elementFqn).Append(">(),").Append(nl);
                    break;
            }

            sb.Append("        };").Append(nl);

            sb.Append("        return ").Append(MetaNs).Append("JsonMetadataServices.")
              .Append(CollectionHelperName(aux.CollectionShape)).Append("<");
            if (aux.CollectionShape == CollectionShapeKind.Array)
            {
                // CreateArrayInfo<TElement>(options, JsonCollectionInfoValues<TElement[]>).
                sb.Append(elementFqn);
            }
            else
            {
                sb.Append(typeFqn).Append(", ").Append(elementFqn);
            }

            sb.Append(">(options, collectionInfo);").Append(nl);
            sb.Append("    }").Append(nl);
        }

        /// <summary>
        /// The <c>JsonMetadataServices</c> collection-info factory-method name for a
        /// <see cref="CollectionShapeKind"/>, matching the built-in System.Text.Json source generator's
        /// per-shape choice (read-only interfaces ride <c>CreateIEnumerableInfo</c>).
        /// </summary>
        private static string CollectionHelperName(CollectionShapeKind shape)
        {
            switch (shape)
            {
                case CollectionShapeKind.Array:
                    return "CreateArrayInfo";
                case CollectionShapeKind.List:
                    return "CreateListInfo";
                case CollectionShapeKind.IList:
                    return "CreateIListInfo";
                case CollectionShapeKind.ICollection:
                    return "CreateICollectionInfo";
                default:
                    // IReadOnlyList / IReadOnlyCollection / IEnumerable.
                    return "CreateIEnumerableInfo";
            }
        }

        /// <summary>
        /// Maps a scalar/string type's fully-qualified display name (special-type keyword or
        /// <c>global::System.*</c> form) to the matching <c>JsonMetadataServices</c> built-in converter
        /// property, so a nullable value type's underlying <c>JsonTypeInfo</c> can be built inline and
        /// wrapped via <c>GetNullableConverter(JsonTypeInfo&lt;T&gt;)</c> (no chain lookup, no separate
        /// dispatch arm). Returns <c>null</c> for a non-scalar underlying (e.g. an enum), which the caller
        /// resolves via the <c>options</c> overload instead.
        /// </summary>
        private static string ScalarConverterName(string fqn)
        {
            switch (fqn)
            {
                case "bool":
                case "global::System.Boolean":
                    return "BooleanConverter";
                case "byte":
                case "global::System.Byte":
                    return "ByteConverter";
                case "sbyte":
                case "global::System.SByte":
                    return "SByteConverter";
                case "short":
                case "global::System.Int16":
                    return "Int16Converter";
                case "ushort":
                case "global::System.UInt16":
                    return "UInt16Converter";
                case "int":
                case "global::System.Int32":
                    return "Int32Converter";
                case "uint":
                case "global::System.UInt32":
                    return "UInt32Converter";
                case "long":
                case "global::System.Int64":
                    return "Int64Converter";
                case "ulong":
                case "global::System.UInt64":
                    return "UInt64Converter";
                case "float":
                case "global::System.Single":
                    return "SingleConverter";
                case "double":
                case "global::System.Double":
                    return "DoubleConverter";
                case "decimal":
                case "global::System.Decimal":
                    return "DecimalConverter";
                case "char":
                case "global::System.Char":
                    return "CharConverter";
                case "string":
                case "global::System.String":
                    return "StringConverter";
                case "global::System.Guid":
                    return "GuidConverter";
                case "global::System.DateTime":
                    return "DateTimeConverter";
                case "global::System.DateTimeOffset":
                    return "DateTimeOffsetConverter";
                case "global::System.DateOnly":
                    return "DateOnlyConverter";
                case "global::System.TimeOnly":
                    return "TimeOnlyConverter";
                case "global::System.TimeSpan":
                    return "TimeSpanConverter";
                case "byte[]":
                case "global::System.Byte[]":
                    return "ByteArrayConverter";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Deterministic factory-method name for the DTO at <paramref name="index"/> in the (fixed-order,
        /// de-duplicated) Serializable_DTO_Set. Index-based names avoid fragile FQN sanitization and keep the
        /// output byte-for-byte stable across runs (R7.4).
        /// </summary>
        private static string FactoryName(int index) => "CreateTypeInfo_" + index;

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

        /// <summary>
        /// Renders a C# string literal for <paramref name="value"/> with backslashes and double quotes
        /// escaped, so JSON property names and CLR member names emit safely into the generated source.
        /// </summary>
        private static string Literal(string value)
            => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
