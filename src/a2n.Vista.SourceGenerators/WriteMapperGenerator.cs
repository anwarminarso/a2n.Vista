// Licensed to the a2n.Vista project. Published artifact — English only.
//
// M9 Source Generator (Pillar 3), Phase 3 (D121/D122): the generated WRITE MAPPER.
//
// This is a SECOND IIncrementalGenerator in the a2n.Vista.SourceGenerators project (netstandard2.0),
// independent of the Phase 1/2 ViewAccessorGenerator. It targets typed "Style B" WRITABLE views
// (classes deriving a2n.Vista.Authoring.View<TQuery, TCrud> that declare a CRUD facet via
// CrudOn/MapWritable) and emits, per analyzable view, a reflection-free WriteMapper as C# source plus
// a [ModuleInitializer] that registers it into a2n.Vista.EntityFrameworkCore.Execution
// .GeneratedWriteMapperStore keyed by the view's runtime Name. Recognition is by fully-qualified name
// only; the generator references NO a2n.Vista project (D48, R11.2, R11.3).
//
// SCOPE OF THIS FILE AS OF TASK 2.2 (tasks.md §2.2, requirements R1.1, R1.2, R1.4, R1.6, R11.3, R11.4):
//   * Fast SYNTAX PREDICATE — a ClassDeclarationSyntax with a non-empty base list (unchanged from 2.1).
//   * SEMANTIC TRANSFORM — resolve the declared symbol and keep ONLY genuine write-mapper candidates:
//     a non-abstract, `partial` class that walks its base types (by fully-qualified metadata name) to
//     a2n.Vista.Authoring.View<TQuery, TCrud> (ARITY-2 ONLY — the write facet requires a typed TCrud)
//     AND declares a CRUD facet (a CrudOn / MapWritable / WithConcurrencyToken chain, recognized by the
//     containing type + namespace FQN). Non-candidates are dropped by returning `null` (R1.2). The
//     transform builds the fully equatable WriteMapperModel and populates the STRUCTURAL / TYPE fields:
//     Namespace, ClassName, ViewFqn, CrudTypeFqn, EntityTypeFqn (from CrudOn<TEntity>), IsPartial,
//     IsAbstract, HasNamedCrudType (false for object/anonymous, R1.4), HasPublicParameterlessCtor,
//     HasCrudFacet, and the equatable Location.
//
// SCOPE ADDED BY TASK 3.1 (the MapWritable_Analyzer; requirements R1.3, R1.4, R1.5, R2.1–R2.5, R5.3):
//   AnalyzeCrudFacet now performs the static (DSL-recognized) analysis of the CRUD-facet fluent chain
//   and POPULATES the analyzer-owned model fields (no longer placeholders):
//     * Mappings   — the ordered (CrudMember, EntityMember, TargetIsScalar) pairs in TEXTUAL declaration
//                    order (R2.1, R2.2), with compiler-inserted Convert/ConvertChecked unwrapped to the
//                    innermost member (R2.3). Each TargetIsScalar mirrors ReflectionWriteMapper.IsScalar
//                    EXACTLY (over the shared MapWritable<TProp>), so generator and oracle agree (R5.3).
//     * ConcurrencyTokenMember — the WithConcurrencyToken target member (R5.2), or null.
//     * DeclaredKeyMembers     — the STATICALLY declared keys from .Key(...) and per-field .PrimaryKey()
//                                (R5.1; design "Static key knowledge and the D105 edge").
//     * Analyzable — false when any MapWritable argument is not a Simple_Member_Selector after unwrapping
//                    (R2.4) or the view has no named TCrud (R1.4). A non-simple selector also yields an
//                    EMPTY mapping set (R2.4); zero MapWritable calls yield an empty set too (R2.5).
//   See AnalyzeCrudFacet / TryGetSimpleMemberName / IsScalarType below.
//
// SCOPE ADDED BY TASK 4.2 (diagnostic reporting + emission gating; requirements R5.1-R5.4, R8.1-R8.3,
// R9.1-R9.3, R9.5):
//   The source-output stage (Emit) now REPORTS the write-DSL analyzer diagnostics and DECIDES whether a
//   view may emit a mapper:
//     * VISTA0030 (Error)  — an analyzable candidate with zero declared MapWritable mappings (R9.1).
//     * VISTA0031 (Error)  — one per mapping whose target is not a Scalar_Member (R9.2).
//     * VISTA0032 (Error)  — one per mapping whose target is a declared key or the concurrency token
//                            (R9.3).
//     * VISTA0033 (Warning)— the view is a candidate but its MapWritable chain is not statically
//                            analyzable (a non-simple selector, R8); names the view + the offending
//                            expression; build stays green and the view falls back to reflection. No
//                            error diagnostics are also raised for an unanalyzable view.
//   Gating (design "Reconciling Requirement 5 with Requirement 9"): if ANY VISTA0030/0031/0032 error is
//   reported for a view, NO mapper is emitted (R9.5). The decision is exposed as the `internal` predicate
//   WriteMapperGenerator.ShouldEmitMapper(model) so the task 6.x emitter (and tests) consult one
//   authoritative "should this view emit?" rule. Views with no named TCrud (R1.4) or no public
//   parameterless ctor (R6.5) are skipped SILENTLY (no diagnostic); the mass-assignment safety errors are
//   still reported regardless of ctor.
//
// SCOPE ADDED BY TASK 6.1 (the write-mapper source emitter; requirements R3.1-R3.6, R4.2, R4.3, R4.6,
// R5.1-R5.6, R11.4, R11.5, R11.6):
//   For a view that passes the ShouldEmitMapper gate, Emit now PRODUCES the per-view generated source
//   `<View>_VistaWriteMapper.g.cs` — a `file static` class exposing a `global::a2n.Vista.Write.WriteMapper
//   Mapper` (Action<object, object>). The lambda down-casts the boxed `model`->TCrud and `entity`->TEntity
//   once, then emits exactly one `e.<EntityMember> = m.<CrudMember>;` per SAFE mapping (target neither a
//   declared key nor the concurrency token, and a Scalar_Member) in textual declaration order — no
//   de-duplication of distinct mappings, so an aliasing pair stays two ordered assignments matching the
//   reflection oracle (R4.6). An empty safe subset yields an empty lambda body (a conforming no-op
//   WriteMapper, R3.6/R5.5). Emission is reflection-free (no Activator.CreateInstance / PropertyInfo
//   Get/SetValue / Expression.Compile — R3.2-R3.4), uses only net8.0-available features (`file` types,
//   target-typed lambdas — R3.5), and uses fixed "\n" line endings so the output is byte-for-byte
//   deterministic (R3.1). See BuildMapperSource / GetSafeMappings / BuildHintName below.
//
// SCOPE ADDED BY TASK 6.2 (the [ModuleInitializer] registration; requirements R6.1, R6.2, R6.4, R6.5):
//   The emitted `file static` class now also carries exactly one [ModuleInitializer] `RegisterWriteMapper`
//   that instantiates the view via its public parameterless ctor, reads its runtime `.Name`, and calls
//   `GeneratedWriteMapperStore.Add(name, Mapper)` — mirroring the Phase 1/2 accessor/plan initializers.
//   It runs at module load, before DI and before the entry point (R6.4); the store is first-wins
//   idempotent so a duplicate name keeps the first registration (R6.3). ShouldEmitMapper already gates on
//   HasPublicParameterlessCtor, so a view with no public parameterless ctor emits NEITHER the mapper NOR
//   the initializer and leaves the store untouched (R6.5).

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
    /// Incremental generator that discovers typed Style B WRITABLE views (non-abstract <c>partial</c>
    /// classes deriving from <c>a2n.Vista.Authoring.View&lt;TQuery, TCrud&gt;</c> that declare a CRUD
    /// facet) and — in later phases — emits a reflection-free <c>WriteMapper</c> registered via a module
    /// initializer into <c>GeneratedWriteMapperStore</c> (D121). It recognizes Vista types by
    /// fully-qualified name only and references no other a2n.Vista project (D48, R11.2, R11.3).
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class WriteMapperGenerator : IIncrementalGenerator
    {
        // Metadata name of the recognized writable-view base type. Roslyn encodes arity in the metadata
        // name (View`2). Paired with the containing namespace by the semantic transform. The write
        // mapper requires a typed TCrud, so ONLY the arity-2 base is a write-mapper candidate (R1.2).
        private const string ViewCrudMetadataName = "View`2";
        private const string ViewNamespace = "a2n.Vista.Authoring";

        // FQN recognition anchors for the CRUD facet fluent chain. CrudOn is declared on the write-capable
        // class-per-view builder IViewBuilder<TQuery, TCrud>; MapWritable / WithConcurrencyToken are
        // declared on the facet builder ICrudBuilder<TQuery, TCrud, TEntity>. Both live in
        // a2n.Vista.Authoring. Matching by containing-type Name + namespace keeps recognition FQN-only —
        // the generator references no a2n.Vista assembly (R1.6, R11.3).
        private const string ViewBuilderInterfaceName = "IViewBuilder";
        private const string CrudBuilderInterfaceName = "ICrudBuilder";
        private const string FieldBuilderInterfaceName = "IFieldBuilder";
        private const string CrudOnMethodName = "CrudOn";
        private const string MapWritableMethodName = "MapWritable";
        private const string WithConcurrencyTokenMethodName = "WithConcurrencyToken";

        // Static-key recognition anchors (design "Static key knowledge and the D105 edge"). Key(...) is
        // declared on IViewBuilder<TQuery>; PrimaryKey() on IFieldBuilder<TProp> is reached through the
        // Field(selector, configure) callback. Both matched by containing-type Name + namespace FQN.
        private const string KeyMethodName = "Key";
        private const string FieldMethodName = "Field";
        private const string PrimaryKeyMethodName = "PrimaryKey";

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // CreateSyntaxProvider pipeline: cheap syntactic filter first, semantic resolution second.
            // The transform yields a fully equatable WriteMapperModel (or null to drop non-candidates),
            // so Roslyn's incremental cache can skip re-emitting views whose model is unchanged
            // (R11.x, mirroring Phase 1/2).
            var candidates = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateClass(node),
                    transform: static (ctx, ct) => Transform(ctx, ct))
                .Where(static model => model is not null)
                // Tag the equatable-model stage so the incremental host records its per-step cache
                // outcome. This is observability only — it does not change emission — and lets the
                // generator tests assert cache reuse (IncrementalStepRunReason.Cached/Unchanged),
                // proving the equatable value model (mirrors TrackingNames.ViewModel). See TrackingNames.
                .WithTrackingName(TrackingNames.WriteMapperModel);

            // Source-output stage. Task 6.x emits the per-view write-mapper source and its
            // [ModuleInitializer]. Until then this is a no-op so the generator is inert but present.
            context.RegisterSourceOutput(candidates, static (spc, model) => Emit(spc, model));
        }

        /// <summary>
        /// Fast syntax predicate (no semantics): a class declaration that has a non-empty base list.
        /// Cheap enough to run on every changed node; the semantic transform does the precise FQN-based
        /// filtering (non-abstract, partial, derives <c>View&lt;TQuery, TCrud&gt;</c>, declares a CRUD
        /// facet). Mirrors the Phase 1/2 predicate.
        /// </summary>
        private static bool IsCandidateClass(SyntaxNode node)
            => node is ClassDeclarationSyntax classDecl
               && classDecl.BaseList is not null
               && classDecl.BaseList.Types.Count > 0;

        /// <summary>
        /// Semantic transform (task 2.2): resolve the declared symbol and keep it only when it is a
        /// genuine write-mapper candidate — a non-abstract, <c>partial</c> class deriving (by
        /// fully-qualified metadata name) from <c>a2n.Vista.Authoring.View&lt;TQuery, TCrud&gt;</c>
        /// (arity-2 only) that declares a CRUD facet. Returns a fully equatable
        /// <see cref="WriteMapperModel"/> carrying the structural / type fields, or <c>null</c> to drop
        /// the class (R1.2). The MapWritable member-pair extraction is deferred to task 3.1 (see the
        /// TASK 3.1 SEAM block below).
        /// </summary>
        private static WriteMapperModel Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var classDecl = (ClassDeclarationSyntax)ctx.Node;

            if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol symbol)
            {
                return null;
            }

            // R1.2: candidates are classes only, and abstract views are never candidates.
            if (symbol.TypeKind != TypeKind.Class || symbol.IsAbstract)
            {
                return null;
            }

            // R1.2 (silent): a non-partial view is dropped here. The VISTA0001 "must be partial"
            // diagnostic is owned by the Phase 1 ViewAccessorGenerator, so this generator does not
            // re-report it — it simply produces no write mapper (design.md, Skipped-emission cases).
            var isPartial = classDecl.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword));
            if (!isPartial)
            {
                return null;
            }

            // Walk the base-type chain to the recognized arity-2 View<TQuery, TCrud> definition. A
            // read-only View<TQuery> (arity-1) is NOT a write-mapper candidate (write requires TCrud), so
            // non-arity-2 derivations are dropped (R1.2).
            var viewBase = FindViewCrudBase(symbol);
            if (viewBase is null)
            {
                return null;
            }

            // TCrud is the second type argument of View<TQuery, TCrud>.
            var crudType = viewBase.TypeArguments.Length > 1
                ? viewBase.TypeArguments[1]
                : null;

            // R1.4: TCrud must be a named, non-object, non-anonymous type to yield a generated mapper.
            // We still keep the candidate when it is not (HasNamedCrudType = false) so the emitter
            // (task 6.x) can skip it; the field carries the fact through the equatable model.
            var hasNamedCrudType = IsNamedCrudType(crudType);

            // Whether the view can be instantiated by the generated [ModuleInitializer] (task 6.2) to
            // read its runtime Name. InstanceConstructors includes the IMPLICIT public default ctor when
            // the class declares none, so this single check covers both "no declared ctors" and
            // "explicitly declared public parameterless ctor" (R6.5).
            var hasPublicParameterlessCtor = symbol.InstanceConstructors.Any(
                static c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

            // Scan the class body for the CRUD facet fluent chain (CrudOn / MapWritable /
            // WithConcurrencyToken), recognized by containing-type + namespace FQN. This both decides
            // HasCrudFacet (R1.2) and recovers TEntity from CrudOn<TEntity> for the emitter's cast.
            var hasCrudFacet = TryFindCrudFacet(classDecl, ctx.SemanticModel, ct, out var entityType);
            if (!hasCrudFacet)
            {
                // R1.2: a View<TQuery, TCrud> that declares no CRUD facet is not a candidate.
                return null;
            }

            // global::-qualified FQNs (R11.4 emission targets). ViewFqn is composed from namespace +
            // class name; TCrud / TEntity come from the resolved symbols. Fallbacks keep the model
            // constructible even in the (defensive) unresolved cases; the emitter/analyzer refine them.
            var crudTypeFqn = crudType is not null
                ? crudType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : "global::System.Object";
            var entityTypeFqn = entityType is not null
                ? entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : "global::System.Object";

            var @namespace = symbol.ContainingNamespace?.IsGlobalNamespace == true
                ? null
                : symbol.ContainingNamespace?.ToDisplayString();
            var viewFqn = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // ---------------------------------------------------------------------------------------
            // TASK 3.1 — the MapWritable_Analyzer.
            //
            // Statically analyze the CRUD-facet fluent chain (from syntax + semantics) to extract the
            // data the diagnostics (task 4.x) and emitter (task 6.x) consume:
            //   * mappings   — the ordered (CrudMember, EntityMember, TargetIsScalar) pairs in textual
            //                  declaration order (R2.1, R2.2), with compiler-inserted Convert/ConvertChecked
            //                  unwrapped to the innermost member (R2.3);
            //   * concurrencyTokenMember — the WithConcurrencyToken target member (or null) (R5.2);
            //   * declaredKeyMembers     — the statically declared keys from .Key(...) / .PrimaryKey()
            //                              (R5.1, design "Static key knowledge and the D105 edge");
            //   * analyzable — false when any MapWritable argument is not a Simple_Member_Selector after
            //                  unwrapping (R2.4), or the view has no named TCrud (R1.4). Drives the
            //                  VISTA0033 reflection fallback (R1.5, R8).
            // ---------------------------------------------------------------------------------------
            AnalyzeCrudFacet(
                classDecl,
                ctx.SemanticModel,
                hasNamedCrudType,
                ct,
                out var mappings,
                out var concurrencyTokenMember,
                out var declaredKeyMembers,
                out var analyzable,
                out var unanalyzableExpression);

            return new WriteMapperModel(
                @namespace: @namespace,
                className: symbol.Name,
                viewFqn: viewFqn,
                crudTypeFqn: crudTypeFqn,
                entityTypeFqn: entityTypeFqn,
                isPartial: isPartial,
                isAbstract: symbol.IsAbstract,
                hasNamedCrudType: hasNamedCrudType,
                hasPublicParameterlessCtor: hasPublicParameterlessCtor,
                hasCrudFacet: hasCrudFacet,
                analyzable: analyzable,
                mappings: mappings,
                concurrencyTokenMember: concurrencyTokenMember,
                declaredKeyMembers: declaredKeyMembers,
                unanalyzableExpression: unanalyzableExpression,
                location: LocationInfo.From(classDecl.Identifier));
        }

        /// <summary>
        /// Walks the base-type chain and returns the constructed <c>View&lt;TQuery, TCrud&gt;</c> base
        /// (so callers can read its type arguments), or <c>null</c> when the symbol does not derive from
        /// the recognized arity-2 View type. Recognition is by metadata name (encodes arity) + namespace,
        /// since the generator references no a2n.Vista assembly (R1.6, R11.3).
        /// </summary>
        private static INamedTypeSymbol FindViewCrudBase(INamedTypeSymbol symbol)
        {
            for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            {
                if (IsRecognizedViewCrudDefinition(current.OriginalDefinition))
                {
                    return current;
                }
            }

            return null;
        }

        /// <summary>
        /// Matches the unbound arity-2 View definition by metadata name (<c>View`2</c>) + containing
        /// namespace (<c>a2n.Vista.Authoring</c>). FQN-only recognition (R1.6, R11.3).
        /// </summary>
        private static bool IsRecognizedViewCrudDefinition(INamedTypeSymbol definition)
        {
            if (definition is null || definition.MetadataName != ViewCrudMetadataName)
            {
                return false;
            }

            var ns = definition.ContainingNamespace;
            return ns is not null
                   && !ns.IsGlobalNamespace
                   && ns.ToDisplayString() == ViewNamespace;
        }

        /// <summary>
        /// True when <paramref name="crudType"/> is a genuine named write contract: a named type that is
        /// not <c>object</c>, not anonymous, and not an error/type-parameter symbol (R1.4). When it is
        /// not, the view is kept as a candidate but flagged <c>HasNamedCrudType = false</c> so the
        /// emitter skips it.
        /// </summary>
        private static bool IsNamedCrudType(ITypeSymbol crudType)
            => crudType is INamedTypeSymbol named
               && !named.IsAnonymousType
               && named.SpecialType != SpecialType.System_Object
               && named.TypeKind != TypeKind.Error;

        /// <summary>
        /// Scans the view class body for the CRUD facet fluent chain and reports whether one is present
        /// (R1.2). A facet is recognized when any of <c>CrudOn</c> (on <c>IViewBuilder&lt;TQuery,
        /// TCrud&gt;</c>), <c>MapWritable</c>, or <c>WithConcurrencyToken</c> (on <c>ICrudBuilder&lt;
        /// TQuery, TCrud, TEntity&gt;</c>) is invoked, matched by containing-type Name + namespace FQN.
        /// When a <c>CrudOn&lt;TEntity&gt;</c> call is found, <paramref name="entityType"/> is set to its
        /// <c>TEntity</c> argument (the last call wins, mirroring the runtime "later CrudOn replaces the
        /// previous one" rule) so the emitter can cast the boxed entity. Extraction of the mapped member
        /// pairs is deferred to task 3.1.
        /// </summary>
        private static bool TryFindCrudFacet(
            ClassDeclarationSyntax classDecl,
            SemanticModel model,
            CancellationToken ct,
            out ITypeSymbol entityType)
        {
            entityType = null;
            var found = false;

            foreach (var invocation in classDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                ct.ThrowIfCancellationRequested();

                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                {
                    continue;
                }

                var calledName = memberAccess.Name.Identifier.ValueText;
                if (calledName != CrudOnMethodName
                    && calledName != MapWritableMethodName
                    && calledName != WithConcurrencyTokenMethodName)
                {
                    continue;
                }

                if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
                {
                    continue;
                }

                switch (method.Name)
                {
                    case CrudOnMethodName when IsViewBuilderMethod(method):
                        found = true;
                        if (method.TypeArguments.Length > 0)
                        {
                            // CrudOn<TEntity>() — capture TEntity (last call wins).
                            entityType = method.TypeArguments[0];
                        }

                        break;

                    case MapWritableMethodName when IsCrudBuilderMethod(method):
                    case WithConcurrencyTokenMethodName when IsCrudBuilderMethod(method):
                        found = true;
                        break;
                }
            }

            return found;
        }

        /// <summary>
        /// MapWritable_Analyzer (task 3.1). Scans the view's CRUD-facet fluent chain and extracts, in a
        /// single syntax pass:
        /// <list type="bullet">
        ///   <item>the ordered <c>(CrudMember, EntityMember, TargetIsScalar)</c> mappings in textual
        ///   declaration order (R2.1, R2.2), unwrapping compiler-inserted <c>Convert</c>/<c>ConvertChecked</c>
        ///   to the innermost member (R2.3);</item>
        ///   <item>the concurrency-token member from <c>WithConcurrencyToken</c> (R5.2);</item>
        ///   <item>the statically declared key members from <c>.Key(...)</c> and per-field
        ///   <c>.PrimaryKey()</c> marks (R5.1);</item>
        ///   <item>the <paramref name="analyzable"/> flag — cleared when any <c>MapWritable</c> argument is
        ///   not a <c>Simple_Member_Selector</c> after unwrapping (R2.4) or the view has no named
        ///   <c>TCrud</c> (R1.4).</item>
        /// </list>
        /// A view with zero <c>MapWritable</c> calls yields an empty mapping set (R2.5); a view with a
        /// non-simple selector yields an empty mapping set and <c>analyzable == false</c> (R2.4, so the
        /// VISTA0033 reflection fallback owns it).
        /// </summary>
        private static void AnalyzeCrudFacet(
            ClassDeclarationSyntax classDecl,
            SemanticModel model,
            bool hasNamedCrudType,
            CancellationToken ct,
            out EquatableArray<WriteMappingModel> mappings,
            out string concurrencyTokenMember,
            out EquatableArray<string> declaredKeyMembers,
            out bool analyzable,
            out string unanalyzableExpression)
        {
            // (span-ordered) collectors so the emitted sequences follow textual declaration order (R2.2).
            // A fluent chain nests left-associatively, so DescendantNodes visits the OUTERMOST (last)
            // invocation first; sorting by the invoked member-name position restores source order.
            var mappingItems = new List<(int Order, WriteMappingModel Mapping)>();
            var keyItems = new List<(int Order, string Member)>();
            concurrencyTokenMember = null;
            var allSelectorsSimple = true;

            // Track the FIRST (declaration-ordered) unanalyzable MapWritable expression so VISTA0033 can
            // name it (R8.2). Because DescendantNodes visits the outermost (last) invocation first, we
            // keep the smallest source position rather than the first encountered.
            var unanalyzableOrder = int.MaxValue;
            string unanalyzableText = null;

            foreach (var invocation in classDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                ct.ThrowIfCancellationRequested();

                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                {
                    continue;
                }

                var order = memberAccess.Name.Span.Start;
                switch (memberAccess.Name.Identifier.ValueText)
                {
                    case MapWritableMethodName:
                        {
                            if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
                                || !IsCrudBuilderMethod(method))
                            {
                                break;
                            }

                            var args = invocation.ArgumentList.Arguments;
                            if (args.Count != 2)
                            {
                                // Not the two-selector MapWritable overload — the view cannot be analyzed.
                                allSelectorsSimple = false;
                                CaptureUnanalyzable(order, invocation, ref unanalyzableOrder, ref unanalyzableText);
                                break;
                            }

                            var crudMember = TryGetSimpleMemberName(args[0].Expression);
                            var entityMember = TryGetSimpleMemberName(args[1].Expression);
                            if (crudMember is null || entityMember is null)
                            {
                                // R2.4: an argument that is not a Simple_Member_Selector after unwrapping
                                // makes the whole view not statically analyzable. Name the offending
                                // argument (the first non-simple selector) in the VISTA0033 warning.
                                allSelectorsSimple = false;
                                var offending = crudMember is null ? args[0].Expression : args[1].Expression;
                                CaptureUnanalyzable(offending.Span.Start, offending, ref unanalyzableOrder, ref unanalyzableText);
                                break;
                            }

                            // TargetIsScalar mirrors ReflectionWriteMapper.IsScalar EXACTLY, computed over
                            // TProp — the shared MapWritable<TProp> type argument, which is the oracle's
                            // `mapping.To.ReturnType` — so the generator and oracle agree (design "Scalar
                            // classification").
                            var targetType = method.TypeArguments.Length > 0 ? method.TypeArguments[0] : null;
                            var targetIsScalar = IsScalarType(targetType);

                            mappingItems.Add((order, new WriteMappingModel(
                                crudMember: crudMember,
                                entityMember: entityMember,
                                targetIsScalar: targetIsScalar,
                                location: LocationInfo.From(invocation))));
                            break;
                        }

                    case WithConcurrencyTokenMethodName:
                        {
                            if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
                                || !IsCrudBuilderMethod(method))
                            {
                                break;
                            }

                            var args = invocation.ArgumentList.Arguments;
                            if (args.Count == 1)
                            {
                                // The token member (or null when the selector is not simple — the token is
                                // best-effort here; a null leaves it out of the skip set).
                                concurrencyTokenMember = TryGetSimpleMemberName(args[0].Expression);
                            }

                            break;
                        }

                    case KeyMethodName:
                        {
                            if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
                                || !IsViewBuilderMethod(method))
                            {
                                break;
                            }

                            foreach (var argument in invocation.ArgumentList.Arguments)
                            {
                                // .Key(x => x.A, ...) — simple member selectors on TQuery, or
                                // .Key("A", ...) — string literal field names. Non-static forms are skipped.
                                var member = TryGetSimpleMemberName(argument.Expression)
                                             ?? TryGetConstantString(argument.Expression, model, ct);
                                if (member is not null)
                                {
                                    keyItems.Add((argument.Span.Start, member));
                                }
                            }

                            break;
                        }

                    case FieldMethodName:
                        {
                            if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
                                || !IsViewBuilderMethod(method))
                            {
                                break;
                            }

                            // Field(x => x.A, f => f.PrimaryKey()) — a per-field primary-key mark declares
                            // a static key member (R5.1). The key member is the field selector's member.
                            var args = invocation.ArgumentList.Arguments;
                            if (args.Count >= 2 && ConfigureMarksPrimaryKey(args[1].Expression, model, ct))
                            {
                                var member = TryGetSimpleMemberName(args[0].Expression);
                                if (member is not null)
                                {
                                    keyItems.Add((order, member));
                                }
                            }

                            break;
                        }
                }
            }

            // R2.4: if any selector was not simple, extract NO member pairs for the view.
            if (allSelectorsSimple)
            {
                mappingItems.Sort(static (a, b) => a.Order.CompareTo(b.Order));
                mappings = new EquatableArray<WriteMappingModel>(
                    mappingItems.Select(static item => item.Mapping).ToArray());
            }
            else
            {
                mappings = new EquatableArray<WriteMappingModel>(System.Array.Empty<WriteMappingModel>());
            }

            keyItems.Sort(static (a, b) => a.Order.CompareTo(b.Order));
            declaredKeyMembers = new EquatableArray<string>(
                keyItems.Select(static item => item.Member).ToArray());

            // R1.4 / R2.4: analyzable requires a named TCrud AND every MapWritable selector simple.
            analyzable = hasNamedCrudType && allSelectorsSimple;

            // Surface the offending expression only when the view is unanalyzable BECAUSE of a
            // non-simple selector (allSelectorsSimple == false). A view unanalyzable solely because it
            // has no named TCrud is skipped silently (no VISTA0033), so it carries no expression.
            unanalyzableExpression = allSelectorsSimple ? null : NormalizeExpressionText(unanalyzableText);
        }

        /// <summary>
        /// Records the earliest (by source position) unanalyzable <c>MapWritable</c> expression so the
        /// VISTA0033 warning can name a deterministic offending expression (R8.2). Called as the analyzer
        /// walks the chain in reverse source order; keeps the smallest position seen.
        /// </summary>
        private static void CaptureUnanalyzable(
            int order,
            SyntaxNode node,
            ref int currentOrder,
            ref string currentText)
        {
            if (node is null || order >= currentOrder)
            {
                return;
            }

            currentOrder = order;
            currentText = node.ToString();
        }

        /// <summary>
        /// Collapses a captured expression's source text to a single trimmed line so it renders cleanly
        /// in a diagnostic message (whitespace and newlines in the author's formatting are irrelevant to
        /// naming the offending expression). Returns <c>null</c> for null/blank input.
        /// </summary>
        private static string NormalizeExpressionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var builder = new System.Text.StringBuilder(text.Length);
            var lastWasSpace = false;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastWasSpace)
                    {
                        builder.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    builder.Append(ch);
                    lastWasSpace = false;
                }
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// Recognizes a <c>Simple_Member_Selector</c> and returns its selected member name, or
        /// <c>null</c> when <paramref name="expression"/> is not a single-parameter lambda whose body —
        /// after stripping compiler-inserted conversions and parentheses (R2.3) — is a member access
        /// rooted directly at the lambda parameter (<c>x =&gt; x.Member</c>). A nested access
        /// (<c>x =&gt; x.A.B</c>) is rejected because its innermost receiver is not the parameter.
        /// </summary>
        private static string TryGetSimpleMemberName(ExpressionSyntax expression)
        {
            var lambda = AsSingleParameterLambda(expression);
            if (lambda is null || lambda.Body is not ExpressionSyntax body)
            {
                return null;
            }

            if (Unwrap(body) is not MemberAccessExpressionSyntax memberAccess)
            {
                return null;
            }

            // "rooted at the lambda parameter": the receiver, after unwrapping, is an identifier that
            // names the lambda's single parameter.
            var parameterName = GetSingleParameterName(lambda);
            if (parameterName is null
                || Unwrap(memberAccess.Expression) is not IdentifierNameSyntax receiver
                || receiver.Identifier.ValueText != parameterName)
            {
                return null;
            }

            return memberAccess.Name.Identifier.ValueText;
        }

        /// <summary>
        /// Returns the compile-time string value of an argument used by the <c>.Key(params string[])</c>
        /// overload, or <c>null</c> when the argument is not a compile-time string constant.
        /// </summary>
        /// <remarks>
        /// The value is resolved through the semantic model rather than by matching a
        /// <see cref="LiteralExpressionSyntax"/>, so every constant spelling is recognized uniformly:
        /// a string literal, <c>nameof(Row.Id)</c> (an invocation, not a literal), a <c>const</c> field,
        /// and constant concatenation. Matching syntax only would silently miss <c>nameof(...)</c>, leave
        /// the key unrecorded, and let the generated mapper mass-assign the primary key because
        /// VISTA0032 (write target is a key or concurrency token) never fires.
        /// </remarks>
        private static string TryGetConstantString(
            ExpressionSyntax expression,
            SemanticModel model,
            CancellationToken ct)
        {
            if (expression is null)
            {
                return null;
            }

            ct.ThrowIfCancellationRequested();

            var constant = model.GetConstantValue(Unwrap(expression), ct);
            return constant.HasValue && constant.Value is string text && text.Length != 0 ? text : null;
        }

        /// <summary>
        /// True when the <c>Field(selector, configure)</c> configure argument marks the field as the
        /// primary key by invoking <c>IFieldBuilder&lt;TProp&gt;.PrimaryKey()</c> anywhere in its body.
        /// </summary>
        private static bool ConfigureMarksPrimaryKey(
            ExpressionSyntax configure,
            SemanticModel model,
            CancellationToken ct)
        {
            foreach (var invocation in configure.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                ct.ThrowIfCancellationRequested();

                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                    && memberAccess.Name.Identifier.ValueText == PrimaryKeyMethodName
                    && model.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method
                    && IsAuthoringMethodOn(method, FieldBuilderInterfaceName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the single-parameter lambda for a selector expression, or <c>null</c> when it is not a
        /// one-parameter lambda. Mirrors the Phase 1/2 <c>AsLambda</c> recognizer.
        /// </summary>
        private static LambdaExpressionSyntax AsSingleParameterLambda(ExpressionSyntax expression) => expression switch
        {
            SimpleLambdaExpressionSyntax simple => simple,
            ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count == 1 => paren,
            _ => null,
        };

        /// <summary>Resolves the (syntactic) name of a single-parameter lambda's parameter, or <c>null</c>.</summary>
        private static string GetSingleParameterName(LambdaExpressionSyntax lambda) => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count == 1
                => paren.ParameterList.Parameters[0].Identifier.ValueText,
            _ => null,
        };

        /// <summary>
        /// Unwraps parentheses and (author-written) conversion casts around an expression so the
        /// innermost underlying member is reached (R2.3). Compiler-inserted <c>Convert</c>/<c>ConvertChecked</c>
        /// nodes (present when a member type differs from the selector's shared <c>TProp</c>) are invisible
        /// in source syntax, so unwrapping the syntactic cast/parenthesis forms is sufficient. Mirrors the
        /// Phase 1/2 <c>Unwrap</c>.
        /// </summary>
        private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        break;
                    case CastExpressionSyntax cast:
                        expression = cast.Expression;
                        break;
                    default:
                        return expression;
                }
            }
        }

        /// <summary>
        /// Scalar classification that mirrors <c>ReflectionWriteMapper.IsScalar</c> EXACTLY so the
        /// generated mapper and the reflection oracle agree (design "Scalar classification"): after
        /// unwrapping <c>Nullable&lt;T&gt;</c>, a <c>string</c> or single-rank <c>byte[]</c> is scalar, and
        /// every other value type (primitive, enum, or struct) is scalar; any other reference type is a
        /// navigation (non-scalar).
        /// </summary>
        private static bool IsScalarType(ITypeSymbol type)
        {
            if (type is null)
            {
                return false;
            }

            var underlying = type;
            if (type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1)
            {
                underlying = named.TypeArguments[0];
            }

            if (underlying.SpecialType == SpecialType.System_String)
            {
                return true;
            }

            if (underlying is IArrayTypeSymbol array
                && array.Rank == 1
                && array.ElementType.SpecialType == SpecialType.System_Byte)
            {
                return true;
            }

            return underlying.IsValueType;
        }

        /// <summary>
        /// True when <paramref name="method"/> is declared on <c>a2n.Vista.Authoring.IViewBuilder&lt;
        /// ...&gt;</c> (the read/write class-per-view builder), matched by containing-type Name +
        /// namespace FQN. <c>CrudOn</c> is declared on the arity-2 <c>IViewBuilder&lt;TQuery, TCrud&gt;</c>.
        /// </summary>
        private static bool IsViewBuilderMethod(IMethodSymbol method)
            => IsAuthoringMethodOn(method, ViewBuilderInterfaceName);

        /// <summary>
        /// True when <paramref name="method"/> is declared on <c>a2n.Vista.Authoring.ICrudBuilder&lt;
        /// TQuery, TCrud, TEntity&gt;</c> (the write facet builder), matched by containing-type Name +
        /// namespace FQN. <c>MapWritable</c> and <c>WithConcurrencyToken</c> live here.
        /// </summary>
        private static bool IsCrudBuilderMethod(IMethodSymbol method)
            => IsAuthoringMethodOn(method, CrudBuilderInterfaceName);

        /// <summary>
        /// Shared FQN-only check: the method's containing type has the given (arity-stripped) Name and
        /// lives in the <c>a2n.Vista.Authoring</c> namespace. The generator references no a2n.Vista
        /// assembly, so recognition is by name + namespace only (R1.6, R11.3).
        /// </summary>
        private static bool IsAuthoringMethodOn(IMethodSymbol method, string containingTypeName)
        {
            var containingType = method.ContainingType;
            if (containingType is null || containingType.Name != containingTypeName)
            {
                return false;
            }

            var ns = containingType.ContainingNamespace;
            return ns is not null
                   && !ns.IsGlobalNamespace
                   && ns.ToDisplayString() == ViewNamespace;
        }

        /// <summary>
        /// Source-output stage (task 4.2). Reports the write-DSL analyzer diagnostics
        /// (VISTA0030/0031/0032 errors, VISTA0033 fallback warning) and decides — via
        /// <see cref="ShouldEmitMapper"/> — whether the emitter (task 6.x) may produce a mapper for the
        /// view. The transform already dropped non-candidates, so <paramref name="model"/> is always a
        /// recognized typed Style B writable view.
        ///
        /// Emission gating (design "Reconciling Requirement 5 with Requirement 9"): if ANY of
        /// VISTA0030/0031/0032 is reported for the view (a blocking mass-assignment error), NO mapper is
        /// emitted (R9.5). An unanalyzable chain (VISTA0033, a warning) also emits no mapper but leaves
        /// the build green so the view falls back to the reflection mapper (R8). The actual source
        /// emission is task 6.x; here we only wire the diagnostics and expose the gating predicate the
        /// emitter will consult.
        /// </summary>
        private static void Emit(SourceProductionContext context, WriteMapperModel model)
        {
            ReportDiagnostics(context, model);

            if (!ShouldEmitMapper(model))
            {
                // No mapper for this view: either it is silently skipped (no named TCrud / no public
                // parameterless ctor), it falls back to reflection (VISTA0033), or it is blocked by a
                // VISTA0030/0031/0032 error. Nothing is emitted so the reflection path serves the view.
                return;
            }

            // TASK 6.1/6.2 — emit the per-view `file static` write-mapper source: a WriteMapper
            // (Action<object, object>) built from a single down-cast of each boxed seam parameter plus
            // one direct `entity.<EntityMember> = model.<CrudMember>;` assignment per SAFE mapping, in
            // textual declaration order, followed by the [ModuleInitializer] that registers `Mapper` into
            // GeneratedWriteMapperStore keyed by the view's runtime `Name`. Reaching here means the view
            // is a named-TCrud, instantiable, statically analyzable candidate with no blocking
            // mass-assignment error — safe to emit.
            var source = BuildMapperSource(model);
            context.AddSource(BuildHintName(model), SourceText.From(source, Encoding.UTF8));
        }

        /// <summary>
        /// Builds the per-view generated write-mapper source (task 6.1): a <c>file static</c> class
        /// exposing a <c>global::a2n.Vista.Write.WriteMapper Mapper</c> (an <c>Action&lt;object,
        /// object&gt;</c>). The mapper down-casts the boxed <c>model</c> to <c>TCrud</c> and <c>entity</c>
        /// to <c>TEntity</c> once, then emits exactly one direct member assignment
        /// <c>e.&lt;EntityMember&gt; = m.&lt;CrudMember&gt;;</c> per SAFE mapping — those whose target is
        /// neither a declared key nor the concurrency token and is a <c>Scalar_Member</c> — in textual
        /// declaration order (R3.1, R4.6, R5.1–R5.4). For a view that builds this safe subset equals the
        /// full whitelist (every unsafe mapping already errored the build), so the belt-and-suspenders
        /// filter is a no-op on a successful build (design "Reconciling Requirement 5 with Requirement
        /// 9"). When the safe subset is empty the lambda body is empty — a conforming no-op
        /// <c>WriteMapper</c> (R3.6, R5.5). Emission is reflection-free (no
        /// <c>Activator.CreateInstance</c>, no <c>PropertyInfo</c> <c>Get/SetValue</c>, no
        /// <c>Expression.Compile</c> — R3.2–R3.4) and uses only net8.0-available features (<c>file</c>
        /// types, target-typed lambdas — R3.5). Fixed <c>"\n"</c> line endings and declaration-ordered
        /// assignments make the output byte-for-byte deterministic (R3.1).
        /// <para>
        /// The same class also carries exactly one <c>[ModuleInitializer]</c> <c>RegisterWriteMapper</c>
        /// (task 6.2) that instantiates the view via its public parameterless constructor, reads its
        /// runtime <c>Name</c>, and registers <c>Mapper</c> into
        /// <c>GeneratedWriteMapperStore</c> keyed by that name at module load — before DI and before the
        /// entry point (R6.1, R6.2, R6.4), mirroring the Phase 1/2 initializers.
        /// </para>
        /// </summary>
        private static string BuildMapperSource(WriteMapperModel model)
        {
            // Fixed "\n" line endings (not Environment.NewLine) so generated text is byte-identical
            // across platforms, keeping the determinism property (task 6.4) and snapshot tests stable.
            const string nl = "\n";
            var mapperClassName = model.ClassName + "_VistaWriteMapper";

            var safeMappings = GetSafeMappings(model);

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>").Append(nl);
            sb.Append("#nullable enable").Append(nl);
            sb.Append(nl);

            // A file-local class: the `file` modifier scopes the type to this generated file so two views
            // sharing a class name in different namespaces never collide at the type level (C# 11+;
            // consumer TFMs net8/9/10 support it — R3.5, R11.5). No namespace is emitted; the mapper is an
            // internal implementation detail referenced only by its own (task 6.2) module initializer.
            sb.Append("file static class ").Append(mapperClassName).Append(nl);
            sb.Append("{").Append(nl);
            sb.Append("    public static readonly global::a2n.Vista.Write.WriteMapper Mapper = static (model, entity) =>").Append(nl);
            sb.Append("    {").Append(nl);

            // R3.6 / R5.5: an empty safe subset yields an empty lambda body — a valid no-op WriteMapper.
            // The `m`/`e` down-cast locals are emitted only when there is at least one assignment to use
            // them, so an empty body carries no unused locals.
            if (safeMappings.Count > 0)
            {
                // Down-cast once to the strongly-typed contract/entity; every assignment shares these
                // locals (mirrors the reflection oracle's single Convert per parameter).
                sb.Append("        var m = (").Append(model.CrudTypeFqn).Append(")model;").Append(nl);
                sb.Append("        var e = (").Append(model.EntityTypeFqn).Append(")entity;").Append(nl);

                // One direct assignment per safe mapping, in declaration order (R3.1, R4.6). Aliasing
                // (two source members targeting one entity member) is preserved as two ordered
                // assignments — the oracle applies both in the same relative order (last write wins).
                foreach (var mapping in safeMappings)
                {
                    sb.Append("        e.").Append(mapping.EntityMember)
                      .Append(" = m.").Append(mapping.CrudMember).Append(";").Append(nl);
                }
            }

            sb.Append("    };").Append(nl);
            sb.Append(nl);

            // [ModuleInitializer] registration (task 6.2, R6.1/R6.2/R6.4). The initializer keys the
            // mapper off the view's RUNTIME Name: it instantiates the view via its public parameterless
            // ctor (guaranteed present — ShouldEmitMapper gates on HasPublicParameterlessCtor, so a view
            // lacking one emits neither this initializer NOR the mapper, R6.5) and reads `.Name` once at
            // module load, before any DI container is constructed and before the entry point runs (R6.4).
            // The store is first-wins idempotent, so a duplicate name keeps the first registration (R6.3).
            // The method is `internal static void` and parameterless so it satisfies the ModuleInitializer
            // signature contract (CS8815/CS8816): static, parameterless, void, non-generic, and at least
            // internally visible. All emitted constructs (file-local type, [ModuleInitializer],
            // target-typed `new()`) are available on the lowest consumer TFM, net8.0 (R3.5, R11.5).
            // ViewFqn is already `global::`-qualified by the semantic transform.
            sb.Append("    [global::System.Runtime.CompilerServices.ModuleInitializer]").Append(nl);
            sb.Append("    internal static void RegisterWriteMapper()").Append(nl);
            sb.Append("        => global::a2n.Vista.EntityFrameworkCore.Execution.GeneratedWriteMapperStore.Add(").Append(nl);
            sb.Append("               new ").Append(model.ViewFqn).Append("().Name, Mapper);").Append(nl);
            sb.Append("}").Append(nl);

            return sb.ToString();
        }

        /// <summary>
        /// Computes the SAFE assignment subset of the view's whitelist (design "Effective (safe)
        /// assignment set"): the mappings whose target member is neither a declared key nor the
        /// concurrency token (R5.1, R5.2) and whose target is a <c>Scalar_Member</c> (R5.3). Order and
        /// multiplicity are preserved (declaration order, no de-duplication of distinct mappings), so an
        /// aliasing pair stays two ordered assignments matching the reflection oracle (R4.6). If a mapping
        /// belongs to more than one omission category it is still omitted exactly once (R5.4). For a view
        /// that builds successfully this subset equals <see cref="WriteMapperModel.Mappings"/> because
        /// every unsafe mapping already errored the build; the filter is the belt-and-suspenders half of
        /// the two-layer safety model.
        /// </summary>
        private static List<WriteMappingModel> GetSafeMappings(WriteMapperModel model)
        {
            var safe = new List<WriteMappingModel>();
            foreach (var mapping in model.Mappings)
            {
                if (mapping.TargetIsScalar && !IsKeyOrToken(model, mapping.EntityMember))
                {
                    safe.Add(mapping);
                }
            }

            return safe;
        }

        /// <summary>
        /// Builds a unique <c>AddSource</c> hint name for the view's generated write mapper. The namespace
        /// is folded into the name (dots replaced with underscores) so two views sharing a class name in
        /// different namespaces do not collide, mirroring the Phase 1/2 hint-name convention.
        /// </summary>
        private static string BuildHintName(WriteMapperModel model)
        {
            var prefix = string.IsNullOrEmpty(model.Namespace)
                ? string.Empty
                : model.Namespace.Replace('.', '_') + "_";

            return prefix + model.ClassName + "_VistaWriteMapper.g.cs";
        }

        /// <summary>
        /// Reports the write-DSL analyzer diagnostics for a candidate view (D122, R8, R9):
        /// <list type="bullet">
        ///   <item>views with no named <c>TCrud</c> are skipped silently (R1.4) — no diagnostic;</item>
        ///   <item>an unanalyzable <c>MapWritable</c> chain (a non-simple selector, R8) is reported as the
        ///   VISTA0033 <b>warning</b> only, naming the view and (when determinable) the offending
        ///   expression — no error diagnostics are also raised for it;</item>
        ///   <item>an analyzable view with zero declared mappings is reported as VISTA0030 (<b>error</b>,
        ///   R9.1);</item>
        ///   <item>each mapping whose target is not a <c>Scalar_Member</c> is reported as VISTA0031
        ///   (<b>error</b>, one per offending mapping, R9.2);</item>
        ///   <item>each mapping whose target is a declared key or the concurrency token is reported as
        ///   VISTA0032 (<b>error</b>, one per offending member, R9.3).</item>
        /// </list>
        /// A missing public parameterless constructor does not suppress the mass-assignment safety
        /// diagnostics (they are authoring errors reported during compilation) — it only gates emission
        /// (see <see cref="ShouldEmitMapper"/>; design "Skipped-emission cases").
        /// </summary>
        private static void ReportDiagnostics(SourceProductionContext context, WriteMapperModel model)
        {
            // R1.4: a candidate with no named TCrud (object/anonymous) is not this generator's concern —
            // skipped silently with no diagnostic (design "Skipped-emission cases").
            if (!model.HasNamedCrudType)
            {
                return;
            }

            // R8: an unanalyzable MapWritable chain is a WARNING-only fallback to reflection. Do NOT also
            // raise the VISTA0030/0031/0032 errors for it — the analyzer already cleared its mappings, so
            // those checks would misfire.
            if (!model.Analyzable)
            {
                var expressionSuffix = string.IsNullOrEmpty(model.UnanalyzableExpression)
                    ? string.Empty
                    : " (expression '" + model.UnanalyzableExpression + "')";

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.WriteChainNotStaticallyAnalyzable,
                    model.Location?.ToLocation() ?? Location.None,
                    model.ClassName,
                    expressionSuffix));
                return;
            }

            // R9.1: a CRUD facet that declares zero MapWritable mappings → exactly one VISTA0030 error,
            // reported at the view's location.
            if (model.Mappings.Count == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.WriteFacetHasNoMappings,
                    model.Location?.ToLocation() ?? Location.None,
                    model.ClassName));
            }

            // R9.2 / R9.3: per-mapping mass-assignment safety errors, one diagnostic per offending
            // mapping/member, reported at the mapping's own location where available.
            foreach (var mapping in model.Mappings)
            {
                var location = mapping.Location?.ToLocation()
                               ?? model.Location?.ToLocation()
                               ?? Location.None;

                // R9.2: a non-scalar (navigation) target → VISTA0031.
                if (!mapping.TargetIsScalar)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.WriteTargetNotScalar,
                        location,
                        model.ClassName,
                        mapping.CrudMember,
                        mapping.EntityMember));
                }

                // R9.3: a declared key or the concurrency token as target → VISTA0032.
                if (IsKeyOrToken(model, mapping.EntityMember))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.WriteTargetIsKeyOrToken,
                        location,
                        model.ClassName,
                        mapping.EntityMember));
                }
            }
        }

        /// <summary>
        /// The emission gating predicate the emitter (task 6.x) consults to decide whether a view gets a
        /// generated write mapper. A mapper is emitted only for a view that (1) has a named <c>TCrud</c>
        /// (R1.4), (2) can be instantiated by the generated <c>[ModuleInitializer]</c> to read its
        /// runtime <c>Name</c> (R6.5), (3) is statically analyzable — no non-simple selector, so no
        /// VISTA0033 reflection fallback (R1.5, R8), and (4) has no blocking mass-assignment error
        /// (VISTA0030/0031/0032 → R9.5). Any other view resolves to the reflection mapper at runtime.
        /// Exposed <c>internal</c> so the task 6.x emitter and the generator tests can consult a single,
        /// authoritative "should this view emit?" decision.
        /// </summary>
        internal static bool ShouldEmitMapper(WriteMapperModel model)
            => model is not null
               && model.HasNamedCrudType
               && model.HasPublicParameterlessCtor
               && model.Analyzable
               && !HasBlockingErrors(model);

        /// <summary>
        /// True when the view would report at least one VISTA0030/0031/0032 <b>error</b> — i.e. the
        /// build must fail and no mapper may be emitted (R9.5). Mirrors the conditions in
        /// <see cref="ReportDiagnostics"/> exactly, so the "reported an error" and "must not emit"
        /// decisions can never drift apart. Only meaningful for an analyzable, named-<c>TCrud</c> view;
        /// callers gate on those first.
        /// </summary>
        private static bool HasBlockingErrors(WriteMapperModel model)
        {
            // VISTA0030: zero declared mappings.
            if (model.Mappings.Count == 0)
            {
                return true;
            }

            // VISTA0031 (non-scalar target) or VISTA0032 (key/token target).
            foreach (var mapping in model.Mappings)
            {
                if (!mapping.TargetIsScalar || IsKeyOrToken(model, mapping.EntityMember))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="entityMember"/> is a statically declared key member (from
        /// <c>.Key(...)</c>/<c>.PrimaryKey()</c>) or the concurrency token (from
        /// <c>WithConcurrencyToken</c>) — the two members a <c>MapWritable</c> mapping must never target
        /// (VISTA0032, R9.3). Comparison is ordinal, matching member-name identity.
        /// </summary>
        private static bool IsKeyOrToken(WriteMapperModel model, string entityMember)
        {
            if (entityMember is null)
            {
                return false;
            }

            if (model.ConcurrencyTokenMember is not null
                && string.Equals(model.ConcurrencyTokenMember, entityMember, System.StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var key in model.DeclaredKeyMembers)
            {
                if (string.Equals(key, entityMember, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
