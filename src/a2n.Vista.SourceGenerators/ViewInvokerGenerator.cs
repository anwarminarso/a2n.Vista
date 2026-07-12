// Licensed to the a2n.Vista project. Published artifact — English only.
//
// M9 Source Generator (Pillar 3), Phase 4 — the HTTP-surface dispatch invoker (D123,
// source-generator-http-surface).
//
// This is a THIRD IIncrementalGenerator in the a2n.Vista.SourceGenerators project (netstandard2.0),
// independent of the Phase 1/2 ViewAccessorGenerator and the Phase 3 WriteMapperGenerator. It targets
// typed "Style B" views (classes deriving a2n.Vista.Authoring.View<TQuery> or View<TQuery, TCrud>) and
// will — in later phases — emit, per covered view, a reflection-free IViewInvoker plus a
// [ModuleInitializer] that registers it into a2n.Vista.Core's ViewInvokerStore keyed by the view's
// runtime Name (D123). Recognition is by fully-qualified name only; the generator references NO
// a2n.Vista project (D48, R1.4, R7.1).
//
// SCOPE OF THIS FILE AS OF TASK 3.2 (tasks.md §3.2, requirements R1.1, R1.2, R1.3, R1.4, R7.1):
//   * Stand up the [Generator] IIncrementalGenerator.
//   * Fast SYNTAX PREDICATE — a ClassDeclarationSyntax that has a non-empty base list (no semantics),
//     mirroring Phases 1/2/3.
//   * SEMANTIC TRANSFORM — resolve the declared symbol and keep ONLY genuine dispatch candidates: a
//     non-abstract, `partial` class that walks its base types (by fully-qualified metadata name) to
//     a2n.Vista.Authoring.View<TQuery> (arity-1) or View<TQuery, TCrud> (arity-2). Non-candidates are
//     dropped by returning `null` (R1.1). The transform builds the FULLY EQUATABLE ViewInvokerModel and
//     populates every field, including the coverage flags (R1.2, R1.3):
//       - IsWritable        — arity-2 (the view carries a typed TCrud) vs arity-1 (read-only).
//       - HasNamedRowType   — false when TQuery is object/anonymous/not a named type (uncovered → the
//                             view is reported VISTA0040 and stays on the reflection fallback, R1.1/R1.3).
//       - HasNamedCrudType  — false for a read-only view, and for a writable view whose TCrud is
//                             object/anonymous; true only for a writable view with a named TCrud (R1.2).
//       - HasPublicParameterlessCtor — whether the generated [ModuleInitializer] can instantiate the
//                             view to read its runtime Name (R1.5; the emitter, task 6.1, skips a view
//                             without one, mirroring Phases 1/2/3).
//     All type names are captured `global::`-qualified. The equatable Location is a LocationInfo
//     surrogate (not the non-value-equal Microsoft.CodeAnalysis.Location) so incremental caching holds
//     (R7.2). The JsonSerializableTypeFqns set — { TRow, ViewListResult<TRow>, PagedResult<TRow> } plus
//     TCrud iff writable with a named TCrud — is composed here so the later VISTA0041 serialization
//     guidance (task 4.2) can name the exact [JsonSerializable] type set (R5.4, R9.2).
//
// SCOPE ADDED BY TASK 4.2 (tasks.md §4.2, requirements R5.4, R9.1, R9.2, R9.4):
//   * DIAGNOSTIC REPORTING is wired into the source-output stage (Emit). It is non-blocking (Info only):
//       - VISTA0040 (Info) — one per recognized base candidate that cannot receive dispatch (its TQuery
//         is anonymous/object, HasNamedRowType == false); no invoker; the view stays on the reflection
//         fallback and the build succeeds.
//       - VISTA0041 (Info) — one per covered view (HasNamedRowType == true), composing the exact
//         [JsonSerializable] type set (JsonSerializableTypeFqns) as a comma-joined global::-qualified
//         list into the message {1} placeholder; the build succeeds regardless.
//     The reportable Location is reconstructed from the equatable LocationInfo via ToLocation().
//
// SCOPE ADDED BY TASK 6.1 (tasks.md §6.1, requirements R2.1-R2.5, R3.1-R3.4, R4.5, R7.3, R7.4, R7.5):
//   The source-output stage (Emit) now EMITS, for every COVERED view (HasNamedRowType == true) that has a
//   public parameterless ctor (HasPublicParameterlessCtor == true), the per-view generated source
//   `<View>_VistaViewInvoker.g.cs` — a `file sealed` class implementing
//   global::a2n.Vista.Ports.IViewInvoker:
//     * ListAsync / DetailAsync close IViewExecutor.ListAsync<TRow> / DetailAsync<TRow> at compile time
//       (no MakeGenericMethod), `await` the returned Task<...> directly with ConfigureAwait(false) (no
//       Task<TResult>.Result reflection), and fill ViewInvocationListResult from result.Page.Items /
//       result.Page.TotalRows / result.TotalRowsUnfiltered by DIRECT member access (no ViewListResult<TRow>
//       reflection) — R2.1, R2.2.
//     * CreateAsync / UpdateAsync close IViewExecutor.CreateAsync<TCrud> / UpdateAsync<TCrud> at compile
//       time (R3.1) ONLY for a writable view with a named TCrud; identity comes from the request key and
//       the concurrency token is passed through unchanged (R3.2). A read-only view — and a writable view
//       whose TCrud is object/anonymous (R1.2) — reports IsWritable => false and its write members throw
//       InvalidOperationException (defense in depth; the HTTP layer routes such writes to the reflection
//       fallback, R3.3).
//     * Exactly one [ModuleInitializer] registers a singleton into a2n.Vista.Ports.ViewInvokerStore keyed
//       by the view's RUNTIME Name (`new <View>().Name`), first-wins idempotent (R4.5), mirroring the
//       Phase 1/2/3 store initializers.
//   The emitted code uses only net8.0-available features (`file` types, [ModuleInitializer], target-typed
//   `new()`), contains no Activator.CreateInstance / PropertyInfo / Expression.Compile / MakeGenericMethod,
//   references only Core + BCL types (no ASP.NET Core in the view assembly — R2.3/R7.5), and uses fixed
//   "\n" line endings so the output is byte-for-byte deterministic (R7.4). A covered view WITHOUT a public
//   parameterless ctor still receives its VISTA0041 guidance but emits NO invoker/initializer, leaving the
//   store untouched (R1.5). See BuildInvokerSource / BuildHintName below.

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
    /// and — in later phases — emits a reflection-free <c>IViewInvoker</c> registered via a module
    /// initializer into <c>a2n.Vista.Core</c>'s <c>ViewInvokerStore</c> (D123). It recognizes Vista types
    /// by fully-qualified name only and references no other a2n.Vista project (D48, R1.4, R7.1).
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class ViewInvokerGenerator : IIncrementalGenerator
    {
        // Metadata names of the two recognized base types. Roslyn encodes arity in the metadata name
        // (View`1 / View`2). We pair these with the containing namespace below. Arity-1 is a read-only
        // dispatch candidate; arity-2 additionally carries a typed TCrud (writable). Recognition is by
        // metadata name + namespace only — the generator references no a2n.Vista assembly (R1.4, R7.1).
        private const string ViewSingleMetadataName = "View`1";
        private const string ViewCrudMetadataName = "View`2";
        private const string ViewNamespace = "a2n.Vista.Authoring";

        // Fully-qualified, global::-prefixed open generic names of the envelope types a covered view
        // must be able to (de)serialize. Composed into JsonSerializableTypeFqns for the later VISTA0041
        // serialization guidance (task 4.2, R5.4/R9.2). Recognition is FQN-only; these are string
        // constants, not symbol references (R7.1).
        private const string ViewListResultOpenFqn = "global::a2n.Vista.Ports.ViewListResult<";
        private const string PagedResultOpenFqn = "global::a2n.Vista.Results.PagedResult<";

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // CreateSyntaxProvider pipeline: cheap syntactic filter first, semantic resolution second.
            // The transform yields a fully equatable ViewInvokerModel (or null to drop non-candidates),
            // so Roslyn's incremental cache can skip re-emitting views whose model is unchanged (R7.2,
            // mirroring Phases 1/2/3).
            var candidates = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateClass(node),
                    transform: static (ctx, ct) => Transform(ctx, ct))
                .Where(static model => model is not null)
                // Tag the equatable-model stage so the incremental host records its per-step cache
                // outcome. This is observability only — it does not change emission — and lets the
                // generator tests assert cache reuse (IncrementalStepRunReason.Cached/Unchanged),
                // proving the equatable value model (R7.2, mirroring TrackingNames.ViewModel /
                // TrackingNames.WriteMapperModel). See TrackingNames.
                .WithTrackingName(TrackingNames.ViewInvokerModel);

            // Source-output stage. Task 4.2 wires VISTA0040/VISTA0041 reporting and task 6.1 emits the
            // per-view dispatch invoker + [ModuleInitializer]. Until then this is a no-op so the
            // generator is inert but present (the recognition/model pipeline is exercised by the
            // generator-driver tests, task 3.3).
            context.RegisterSourceOutput(candidates, static (spc, model) => Emit(spc, model));
        }

        /// <summary>
        /// Fast syntax predicate (no semantics): a class declaration that has a non-empty base list.
        /// Cheap enough to run on every changed node; the semantic transform does the precise FQN-based
        /// filtering (non-abstract, partial, derives <c>View&lt;TQuery&gt;</c>/<c>View&lt;TQuery,
        /// TCrud&gt;</c>). Mirrors the Phase 1/2/3 predicate.
        /// </summary>
        private static bool IsCandidateClass(SyntaxNode node)
            => node is ClassDeclarationSyntax classDecl
               && classDecl.BaseList is not null
               && classDecl.BaseList.Types.Count > 0;

        /// <summary>
        /// Semantic transform (task 3.2): resolve the declared symbol and keep it only when it is a
        /// genuine dispatch candidate — a non-abstract, <c>partial</c> class deriving (by fully-qualified
        /// metadata name) from <c>a2n.Vista.Authoring.View&lt;TQuery&gt;</c> or <c>View&lt;TQuery,
        /// TCrud&gt;</c>. Returns a fully equatable <see cref="ViewInvokerModel"/> carrying the type
        /// fields and coverage flags, or <c>null</c> to drop the class (R1.1). No emission or diagnostic
        /// is done here (deferred to tasks 4.2 / 6.1).
        /// </summary>
        private static ViewInvokerModel Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var classDecl = (ClassDeclarationSyntax)ctx.Node;

            if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol symbol)
            {
                return null;
            }

            // R1.1: candidates are classes only, and abstract views are never dispatch candidates.
            if (symbol.TypeKind != TypeKind.Class || symbol.IsAbstract)
            {
                return null;
            }

            // R1.1 (silent): a non-partial view is dropped here. The VISTA0001 "must be partial"
            // diagnostic is owned by the Phase 1 ViewAccessorGenerator, so this generator does not
            // re-report it — it simply produces no dispatch invoker (mirroring the write-mapper
            // generator's drop rule).
            var isPartial = classDecl.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword));
            if (!isPartial)
            {
                return null;
            }

            // Walk the base-type chain to the recognized View<TQuery> or View<TQuery, TCrud> definition.
            // Recognition is by metadata name (encodes arity) + namespace, since the generator references
            // no a2n.Vista assembly (R1.4, R7.1). A class that derives neither is not a candidate.
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

            // Coverage flags (R1.2, R1.3). HasNamedRowType is false when TQuery is object/anonymous/not a
            // named type — the view is uncovered (VISTA0040, reflection fallback). HasNamedCrudType is
            // false for a read-only view and for a writable view whose TCrud is object/anonymous.
            var hasNamedRowType = IsNamedContractType(rowType);
            var hasNamedCrudType = isWritable && IsNamedContractType(crudType);

            // Whether the view can be instantiated by the generated [ModuleInitializer] (task 6.1) to
            // read its runtime Name. InstanceConstructors includes the IMPLICIT public default ctor when
            // the class declares none, so this single check covers both "no declared ctors" and
            // "explicitly declared public parameterless ctor" (R1.5).
            var hasPublicParameterlessCtor = symbol.InstanceConstructors.Any(
                static c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

            // global::-qualified FQNs. RowTypeFqn is always captured (defensively falling back to object
            // for the uncovered case); CrudTypeFqn is null for a read-only view or an unnamed TCrud so
            // the emitter knows there is no write model to close over.
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

            // Compose the [JsonSerializable] type set the later VISTA0041 guidance names (R5.4, R9.2):
            // { TRow, ViewListResult<TRow>, PagedResult<TRow> } plus TCrud iff writable with a named
            // TCrud. Only meaningful when TQuery is a named, coverable type; an uncovered view yields an
            // empty set (it never receives generated dispatch/serialization). Order is fixed so the
            // (order-sensitive) equatable sequence is deterministic across runs (R7.4).
            var jsonSerializableTypeFqns = BuildJsonSerializableTypeFqns(
                hasNamedRowType, rowTypeFqn, hasNamedCrudType, crudTypeFqn);

            return new ViewInvokerModel(
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
                jsonSerializableTypeFqns: new EquatableArray<string>(jsonSerializableTypeFqns.ToArray()),
                location: LocationInfo.From(classDecl.Identifier));
        }

        /// <summary>
        /// Source-output stage. Task 4.2 wires the non-blocking HTTP-surface diagnostics into the
        /// pipeline (R5.4, R9.1, R9.2, R9.4):
        /// <list type="bullet">
        ///   <item>
        ///     A recognized base candidate whose projected row type <c>TQuery</c> is anonymous/<c>object</c>
        ///     (<see cref="ViewInvokerModel.HasNamedRowType"/> is <c>false</c>) is <em>uncovered</em>: it
        ///     cannot receive a generated dispatch invoker, so exactly one <c>VISTA0040</c> (Info) is
        ///     reported at the view location and no invoker is emitted — the view stays on the reflection
        ///     dispatch fallback and the build succeeds (R1.1, R1.3, R9.1).
        ///   </item>
        ///   <item>
        ///     Every <em>covered</em> view (<see cref="ViewInvokerModel.HasNamedRowType"/> is <c>true</c>)
        ///     gets exactly one <c>VISTA0041</c> (Info) serialization guidance, composing the exact
        ///     <c>[JsonSerializable]</c> type set — <c>{ TRow, ViewListResult&lt;TRow&gt;,
        ///     PagedResult&lt;TRow&gt; }</c> plus <c>TCrud</c> when writable with a named <c>TCrud</c> — as
        ///     a comma-joined list of <c>global::</c>-qualified names into the message's <c>{1}</c>
        ///     placeholder (R5.4, R9.2). The build succeeds regardless.
        ///   </item>
        /// </list>
        /// Both descriptors are Info severity, so the HTTP-surface diagnostics are non-blocking (never
        /// Error, R9.4). The reportable <see cref="Location"/> is reconstructed from the equatable
        /// <see cref="LocationInfo"/> via <see cref="LocationInfo.ToLocation"/>. The dispatch-invoker
        /// emitter and its <c>[ModuleInitializer]</c> remain deferred to task 6.1 — this stage reports
        /// diagnostics only and emits no source.
        /// </summary>
        private static void Emit(SourceProductionContext context, ViewInvokerModel model)
        {
            var location = model.Location?.ToLocation() ?? Location.None;

            // Uncovered candidate (anonymous/object TQuery): one VISTA0040 (Info), no invoker; the view
            // falls back to reflection dispatch and the build succeeds (R1.1, R1.3, R9.1, R9.4).
            if (!model.HasNamedRowType)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.HttpSurfaceCandidateUncovered,
                    location,
                    model.ClassName));
                return;
            }

            // Covered view: one VISTA0041 (Info) naming the exact [JsonSerializable] type set the
            // developer should register via AddVistaJsonContext(...). The type set is composed in the
            // transform (BuildJsonSerializableTypeFqns) as { TRow, ViewListResult<TRow>,
            // PagedResult<TRow> } (+ TCrud iff writable with a named TCrud); join it into the message's
            // {1} placeholder as a comma-separated list of global::-qualified names (R5.4, R9.2, R9.4).
            var serializableTypes = string.Join(", ", model.JsonSerializableTypeFqns);
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HttpSurfaceSerializationGuidance,
                location,
                model.ClassName,
                serializableTypes));

            // Task 6.1: emit the dispatch invoker + its [ModuleInitializer] for a covered view. A covered
            // view WITHOUT a public parameterless ctor cannot be instantiated by the [ModuleInitializer]
            // to read its runtime Name, so nothing is emitted for it — it keeps its VISTA0041 guidance but
            // stays on the reflection dispatch fallback, leaving the store untouched (R1.5, mirroring
            // Phases 1/2/3).
            if (!model.HasPublicParameterlessCtor)
            {
                return;
            }

            var source = BuildInvokerSource(model);
            context.AddSource(BuildHintName(model), SourceText.From(source, Encoding.UTF8));
        }

        /// <summary>
        /// Builds the per-view generated dispatch invoker source: a <c>file sealed</c> class implementing
        /// <c>global::a2n.Vista.Ports.IViewInvoker</c> that closes <c>IViewExecutor.ListAsync&lt;TRow&gt;</c>/
        /// <c>DetailAsync&lt;TRow&gt;</c> (and, for a writable view with a named <c>TCrud</c>,
        /// <c>CreateAsync&lt;TCrud&gt;</c>/<c>UpdateAsync&lt;TCrud&gt;</c>) at compile time, awaits the
        /// returned task directly, and fills the type-erased <c>ViewInvocationListResult</c> shape by
        /// direct member access — no reflection (R2.1-R2.3, R3.1, R3.2). The same class carries
        /// exactly one <c>[ModuleInitializer]</c> that registers a singleton into
        /// <c>a2n.Vista.Ports.ViewInvokerStore</c> keyed by the view's runtime <c>Name</c> (R4.5). Fixed
        /// <c>"\n"</c> line endings and a fixed member order keep the output byte-for-byte deterministic
        /// (R7.4).
        /// </summary>
        private static string BuildInvokerSource(ViewInvokerModel model)
        {
            // Fixed "\n" line endings (not Environment.NewLine) so generated text is byte-identical across
            // platforms, keeping the determinism property (task 6.3) and snapshot tests stable.
            const string nl = "\n";
            var invokerClassName = model.ClassName + "_VistaViewInvoker";

            // Real (compile-time TCrud-closing) write dispatch is emitted only for a writable view with a
            // named TCrud. A read-only view, and a writable view whose TCrud is object/anonymous (R1.2),
            // report IsWritable => false and their write members throw — the HTTP layer routes such writes
            // to the reflection fallback (R3.3).
            var emitWriteDispatch = model.IsWritable && model.HasNamedCrudType && model.CrudTypeFqn is not null;

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>").Append(nl);
            sb.Append("#nullable enable").Append(nl);
            sb.Append(nl);

            // A file-local sealed type: the `file` modifier scopes the type to this generated file so two
            // views sharing a class name in different namespaces never collide at the type level (C# 11+;
            // consumer TFMs net8/9/10 support it — R7.3, R7.5). No namespace is emitted; the invoker is an
            // internal implementation detail referenced only by its own [ModuleInitializer].
            sb.Append("file sealed class ").Append(invokerClassName)
              .Append(" : global::a2n.Vista.Ports.IViewInvoker").Append(nl);
            sb.Append("{").Append(nl);

            // IsWritable drives whether the HTTP layer routes writes through this invoker (true) or falls
            // back to reflection (false).
            sb.Append("    public bool IsWritable => ").Append(emitWriteDispatch ? "true" : "false").Append(";").Append(nl);
            sb.Append(nl);

            // List: close ListAsync<TRow>, await directly, and fill ViewInvocationListResult by direct
            // member access (result.Page.Items / result.Page.TotalRows / result.TotalRowsUnfiltered) — no
            // reflection over ViewListResult<TRow>/PagedResult<TRow> (R2.1, R2.2).
            sb.Append("    public async global::System.Threading.Tasks.Task<global::a2n.Vista.Ports.ViewInvocationListResult> ListAsync(").Append(nl);
            sb.Append("        global::a2n.Vista.Ports.IViewExecutor executor,").Append(nl);
            sb.Append("        global::a2n.Vista.Metadata.ViewMetadata view,").Append(nl);
            sb.Append("        global::a2n.Vista.Contracts.ViewQueryRequest request,").Append(nl);
            sb.Append("        global::a2n.Vista.Ports.IViewScope scope,").Append(nl);
            sb.Append("        global::System.Threading.CancellationToken cancellationToken)").Append(nl);
            sb.Append("    {").Append(nl);
            sb.Append("        var result = await executor").Append(nl);
            sb.Append("            .ListAsync<").Append(model.RowTypeFqn).Append(">(view, request, scope, cancellationToken)").Append(nl);
            sb.Append("            .ConfigureAwait(false);").Append(nl);
            sb.Append("        return new global::a2n.Vista.Ports.ViewInvocationListResult(").Append(nl);
            sb.Append("            result,").Append(nl);
            sb.Append("            (global::System.Collections.Generic.IReadOnlyList<object?>)result.Page.Items,").Append(nl);
            sb.Append("            result.Page.TotalRows,").Append(nl);
            sb.Append("            result.TotalRowsUnfiltered);").Append(nl);
            sb.Append("    }").Append(nl);
            sb.Append(nl);

            // Detail: close DetailAsync<TRow>, await directly, return the boxed TRow? (null → 404) (R2.1).
            sb.Append("    public async global::System.Threading.Tasks.Task<object?> DetailAsync(").Append(nl);
            sb.Append("        global::a2n.Vista.Ports.IViewExecutor executor,").Append(nl);
            sb.Append("        global::a2n.Vista.Metadata.ViewMetadata view,").Append(nl);
            sb.Append("        object key,").Append(nl);
            sb.Append("        global::a2n.Vista.Ports.IViewScope scope,").Append(nl);
            sb.Append("        global::System.Threading.CancellationToken cancellationToken)").Append(nl);
            sb.Append("        => await executor").Append(nl);
            sb.Append("            .DetailAsync<").Append(model.RowTypeFqn).Append(">(view, key, scope, cancellationToken)").Append(nl);
            sb.Append("            .ConfigureAwait(false);").Append(nl);
            sb.Append(nl);

            AppendWriteMembers(sb, nl, model, emitWriteDispatch);

            // [ModuleInitializer] registration (R4.5). The initializer keys the invoker off the view's
            // RUNTIME Name: it instantiates the view via its public parameterless ctor (guaranteed present
            // — the Emit gate skips a view lacking one, R1.5) and reads `.Name` once at module load, before
            // any DI container is constructed and before the entry point runs. ViewInvokerStore.Register is
            // first-wins idempotent, so a duplicate name keeps the first registration (R4.5). The method is
            // `internal static void` and parameterless so it satisfies the ModuleInitializer signature
            // contract (CS8815/CS8816). ViewFqn is already `global::`-qualified by the semantic transform.
            sb.Append("    [global::System.Runtime.CompilerServices.ModuleInitializer]").Append(nl);
            sb.Append("    internal static void RegisterViewInvoker()").Append(nl);
            sb.Append("        => global::a2n.Vista.Ports.ViewInvokerStore.Register(").Append(nl);
            sb.Append("               new ").Append(model.ViewFqn).Append("().Name, new ").Append(invokerClassName).Append("());").Append(nl);
            sb.Append("}").Append(nl);

            return sb.ToString();
        }

        /// <summary>
        /// Appends the <c>CreateAsync</c>/<c>UpdateAsync</c> members. When
        /// <paramref name="emitWriteDispatch"/> is <see langword="true"/> (a writable view with a named
        /// <c>TCrud</c>) they close <c>IViewExecutor.CreateAsync&lt;TCrud&gt;</c>/<c>UpdateAsync&lt;TCrud&gt;</c>
        /// at compile time, down-cast the boxed model to <c>TCrud</c>, and await the result directly (R3.1,
        /// R3.2). Otherwise (a read-only view, or a writable view whose <c>TCrud</c> is object/anonymous —
        /// R1.2/R3.3) both members throw <see cref="System.InvalidOperationException"/> as defense in depth;
        /// the HTTP layer never routes a write through them because <c>IsWritable</c> is <see langword="false"/>.
        /// </summary>
        private static void AppendWriteMembers(StringBuilder sb, string nl, ViewInvokerModel model, bool emitWriteDispatch)
        {
            // Create.
            sb.Append("    public ").Append(emitWriteDispatch ? "async " : string.Empty)
              .Append("global::System.Threading.Tasks.Task<object> CreateAsync(").Append(nl);
            sb.Append("        global::a2n.Vista.Ports.IViewExecutor executor,").Append(nl);
            sb.Append("        global::a2n.Vista.Metadata.ViewMetadata view,").Append(nl);
            sb.Append("        object model,").Append(nl);
            sb.Append("        global::a2n.Vista.Ports.IViewScope scope,").Append(nl);
            sb.Append("        global::System.Threading.CancellationToken cancellationToken)").Append(nl);
            if (emitWriteDispatch)
            {
                sb.Append("        => await executor").Append(nl);
                sb.Append("            .CreateAsync<").Append(model.CrudTypeFqn).Append(">(view, (")
                  .Append(model.CrudTypeFqn).Append(")model, scope, cancellationToken)").Append(nl);
                sb.Append("            .ConfigureAwait(false);").Append(nl);
            }
            else
            {
                sb.Append("        => throw new global::System.InvalidOperationException(").Append(nl);
                sb.Append("               \"The Create facet is not available: this view has no generated write dispatch.\");").Append(nl);
            }

            sb.Append(nl);

            // Update.
            sb.Append("    public ").Append(emitWriteDispatch ? "async " : string.Empty)
              .Append("global::System.Threading.Tasks.Task<bool> UpdateAsync(").Append(nl);
            sb.Append("        global::a2n.Vista.Ports.IViewExecutor executor,").Append(nl);
            sb.Append("        global::a2n.Vista.Metadata.ViewMetadata view,").Append(nl);
            sb.Append("        object key,").Append(nl);
            sb.Append("        object model,").Append(nl);
            sb.Append("        global::a2n.Vista.Ports.IViewScope scope,").Append(nl);
            sb.Append("        string? concurrencyToken,").Append(nl);
            sb.Append("        global::System.Threading.CancellationToken cancellationToken)").Append(nl);
            if (emitWriteDispatch)
            {
                sb.Append("        => await executor").Append(nl);
                sb.Append("            .UpdateAsync<").Append(model.CrudTypeFqn).Append(">(view, key, (")
                  .Append(model.CrudTypeFqn).Append(")model, scope, concurrencyToken, cancellationToken)").Append(nl);
                sb.Append("            .ConfigureAwait(false);").Append(nl);
            }
            else
            {
                sb.Append("        => throw new global::System.InvalidOperationException(").Append(nl);
                sb.Append("               \"The Update facet is not available: this view has no generated write dispatch.\");").Append(nl);
            }

            sb.Append(nl);
        }

        /// <summary>
        /// Builds a unique <c>AddSource</c> hint name for the view's generated dispatch invoker. The
        /// namespace is folded into the name (dots replaced with underscores) so two views sharing a class
        /// name in different namespaces do not collide, mirroring the Phase 1/2/3 hint-name convention.
        /// </summary>
        private static string BuildHintName(ViewInvokerModel model)
        {
            var prefix = string.IsNullOrEmpty(model.Namespace)
                ? string.Empty
                : model.Namespace.Replace('.', '_') + "_";

            return prefix + model.ClassName + "_VistaViewInvoker.g.cs";
        }

        /// <summary>
        /// Walks the base-type chain and returns the constructed <c>View&lt;TQuery&gt;</c> or
        /// <c>View&lt;TQuery, TCrud&gt;</c> base (so callers can read arity + type arguments), or
        /// <c>null</c> when the symbol derives from neither recognized View type. Recognition is by
        /// metadata name (encodes arity) + namespace, since the generator references no a2n.Vista
        /// assembly (R1.4, R7.1).
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
        /// (R1.4, R7.1).
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
        /// <c>object</c>, not anonymous, and not an error/type-parameter symbol (R1.2, R1.3). When it is
        /// not (e.g. an <c>object</c>/anonymous <c>TQuery</c> or <c>TCrud</c>), the corresponding coverage
        /// flag is cleared so the view is treated as uncovered for that facet.
        /// </summary>
        private static bool IsNamedContractType(ITypeSymbol type)
            => type is INamedTypeSymbol named
               && !named.IsAnonymousType
               && named.SpecialType != SpecialType.System_Object
               && named.TypeKind != TypeKind.Error;

        /// <summary>
        /// Composes the ordered, <c>global::</c>-qualified <c>[JsonSerializable]</c> type set for a
        /// covered view: <c>{ TRow, ViewListResult&lt;TRow&gt;, PagedResult&lt;TRow&gt; }</c> plus
        /// <c>TCrud</c> when the view is writable with a named <c>TCrud</c> (R5.4, R9.2). Returns an empty
        /// list for an uncovered view (no named <c>TRow</c>), which never receives generated dispatch or
        /// serialization. The fixed order makes the sequence deterministic across runs (R7.4).
        /// </summary>
        private static List<string> BuildJsonSerializableTypeFqns(
            bool hasNamedRowType,
            string rowTypeFqn,
            bool hasNamedCrudType,
            string crudTypeFqn)
        {
            var types = new List<string>();
            if (!hasNamedRowType)
            {
                return types;
            }

            types.Add(rowTypeFqn);
            types.Add(ViewListResultOpenFqn + rowTypeFqn + ">");
            types.Add(PagedResultOpenFqn + rowTypeFqn + ">");

            if (hasNamedCrudType && crudTypeFqn is not null)
            {
                types.Add(crudTypeFqn);
            }

            return types;
        }
    }
}
