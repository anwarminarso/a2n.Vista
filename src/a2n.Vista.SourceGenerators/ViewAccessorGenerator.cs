// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Phase 1 (M9, D117) incremental source generator for typed "Style B" views.
//
// SCOPE OF THIS FILE AS OF TASK 2.3 (tasks.md §2.1–2.3, requirements R1.1, R1.2, R1.3, R5.1, R5.2):
//   * Stand up the [Generator] IIncrementalGenerator.
//   * Fast SYNTAX PREDICATE — a ClassDeclarationSyntax that has a base list (no semantics).
//   * SEMANTIC TRANSFORM — resolve the symbol and keep non-abstract classes that derive
//     (walking base types by fully-qualified metadata name) from
//     a2n.Vista.Authoring.View<TQuery> or View<TQuery, TCrud>. The generator references NO
//     Vista project, so recognition is by FQN/metadata name only (Spec 03 D71).
//   * A FULLY EQUATABLE value model ({ Namespace, ClassName, IsPartial, TQueryFqn, Properties[],
//     Location } as records + an EquatableArray<T> wrapper) so Roslyn caches unchanged views and an
//     unrelated edit does NOT regenerate every view (R1.3, Spec 03 §12). The Location is carried as
//     an equatable LocationInfo SURROGATE (not the non-value-equal Microsoft.CodeAnalysis.Location),
//     reconstructed only at report time, so caching is preserved.
//   * VISTA0001 (error) — a non-partial Style B view is reported at its class location and SKIPPED
//     (no emission for it). See DiagnosticDescriptors.ViewMustBePartial (R5.1, R5.2, Property 4).
//
// SCOPE ADDED BY TASK 3.1 (tasks.md §3.1, requirements R2.1, R2.2):
//   * For each discovered PARTIAL typed view, emit one generated source file carrying a `file static`
//     accessor map: a Dictionary<string, Func<object, object?>> with one entry per public readable
//     TQuery property. Each accessor is a CAST + PROPERTY READ (no reflection): `static row =>
//     ((global::TQuery)row).Prop`. The map is keyed by property name (R2.2) in declaration order so
//     the output is deterministic for snapshot tests (task 4.1). The hint name incorporates the
//     namespace so two views sharing a class name in different namespaces do not collide.
//
// SCOPE ADDED BY TASK 3.2 (tasks.md §3.2, requirements R2.3, R3.2, R3.3, R1.4):
//   * Inside the same emitted `file static` class, emit a [ModuleInitializer] `Register()` that
//     registers the accessor Map into a2n.Vista.Metadata.ViewAccessorRegistry, KEYED BY the view's
//     RUNTIME Name — `new global::<view FQN>().Name`. The view is instantiated via its public
//     parameterless ctor and `.Name` read once at module load (cold path, no reflection emit).
//   * A partial view WITHOUT a public parameterless ctor cannot be instantiated this way, so emitting
//     a Register() would not compile; such a view is reported with VISTA0002 (info) and SKIPPED. The
//     model carries HasPublicParameterlessCtor (computed in Transform) to drive this branch.
//
// DEFERRED (do NOT implement here):
//   * Anonymous Style A coverage (R2.3 — out of scope for Phase 1; reflection path serves it).
//
// SCOPE ADDED BY TASK 4.1 (tasks.md §4.1, requirements R1.1–R1.5, R2.1, R2.3, R5.2, R7.1):
//   * For each analyzable, single-source typed view whose compilation also references the EF layer,
//     emit a SECOND generated file `<View>_VistaExecutionPlan.g.cs` carrying a `file sealed`
//     CompiledViewExecutionPlan_<View> that implements the non-RUC
//     a2n.Vista.EntityFrameworkCore.Execution.ICompiledViewExecutionPlan. Everything is built by
//     compile-time expression-node construction emitted AS C# (the consumer compiles the lambdas):
//       - the projection Expression<Func<TSource, TRow>> reproduced from the captured bindings;
//       - a member-access map (fieldName -> Expression<Func<TRow, TField>>) for filterable/sortable
//         fields (R2.1, R2.3);
//       - strongly-typed primary/then sort appliers that call closed-generic Queryable.OrderBy /
//         OrderByDescending / ThenBy / ThenByDescending directly (no MakeGenericMethod);
//       - MaskAccessor get/set per masked field (direct setter, `with`-rebuild for record/init rows,
//         or a runtime-throwing fallback) (R7.1);
//       - a CreateScopedQueryable that AND-s the authored server-trusted row filters (recovered from
//         the view via the new Core View.GetSourceRowFilters<TSource>) and the per-request scope
//         predicates pre-projection (R1.4);
//       - a [ModuleInitializer] that constructs the plan and registers it into
//         GeneratedExecutionPlanStore keyed by the view's runtime Name.
//     The plan is emitted ONLY when the EF layer is referenced (CompiledPlanSupported); a Core-only
//     consumer keeps just the Phase 1 accessor map and the view stays metadata-only there. No
//     Activator.CreateInstance / PropertyInfo / Expression.Property(string) / Compile() is emitted
//     (R1.2, R5.2), and only lowest-target-framework (net8.0) features are used (R1.5).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// Incremental generator that discovers typed Style B views (classes deriving from
    /// <c>a2n.Vista.Authoring.View&lt;TQuery&gt;</c> or <c>View&lt;TQuery, TCrud&gt;</c>) and — in later
    /// phases — emits shape-driven read accessors registered via a module initializer.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class ViewAccessorGenerator : IIncrementalGenerator
    {
        // Metadata names of the two recognized base types. Roslyn encodes arity in the metadata
        // name (View`1 / View`2). We pair these with the containing namespace below.
        private const string ViewSingleMetadataName = "View`1";
        private const string ViewCrudMetadataName = "View`2";
        private const string ViewNamespace = "a2n.Vista.Authoring";

        /// <summary>
        /// Metadata name of the EF-layer compiled-plan interface the generated plan implements. Its
        /// presence in the compilation tells the generator the consumer references the EF layer, so the
        /// plan (which names EF types) will compile; absent it, the plan is not emitted (task 4.1).
        /// </summary>
        private const string CompiledPlanInterfaceMetadataName =
            "a2n.Vista.EntityFrameworkCore.Execution.ICompiledViewExecutionPlan";

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // CreateSyntaxProvider pipeline: cheap syntactic filter first, semantic resolution second.
            // The transform yields a fully equatable ViewModel, so Roslyn's incremental cache can
            // skip re-emitting views whose model is unchanged (R1.3).
            var views = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateClass(node),
                    transform: static (ctx, ct) => Transform(ctx, ct))
                .Where(static model => model is not null)
                // Tag the equatable-model stage so the incremental host records its per-step cache
                // outcome. This is observability only — it does not change emission — and lets the
                // generator tests assert that an unrelated edit which leaves a view's model unchanged
                // serves this stage from cache (IncrementalStepRunReason.Cached/Unchanged), proving the
                // equatable value model (R1.3, Spec 03 §12). See TrackingNames.
                .WithTrackingName(TrackingNames.ViewModel);

            // Report diagnostics and (later) emit per view. Task 2.3 wires the VISTA0001 branch:
            // a non-partial Style B view is reported at its class location and skipped. Partial views
            // fall through to a no-op until task 3.x emits their accessor map + [ModuleInitializer].
            context.RegisterSourceOutput(views, static (spc, model) => Emit(spc, model));
        }

        /// <summary>
        /// Source-output stage. Enforces VISTA0001 (R5.1): a non-partial Style B view is reported at its
        /// class location and skipped (return early — no accessor code is emitted for it, so the build
        /// is not left with broken generated code, Property 4). It also enforces VISTA0002 (R3.2): a
        /// partial view without a public parameterless constructor is reported (info) and skipped,
        /// because the generated <c>[ModuleInitializer]</c> could not instantiate it to read its runtime
        /// <c>Name</c>. A partial view with a public parameterless ctor proceeds: task 3.1 emits the
        /// per-view <c>file static</c> accessor map (cast + property read per public readable TQuery
        /// property, R2.1/R2.2) and task 3.2 emits the <c>[ModuleInitializer]</c> that registers it into
        /// <c>ViewAccessorRegistry</c> keyed by the view's runtime <c>Name</c> (R3.2, R3.3).
        /// </summary>
        private static void Emit(SourceProductionContext context, ViewModel model)
        {
            if (!model.IsPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ViewMustBePartial,
                    model.Location?.ToLocation() ?? Location.None,
                    model.ClassName));
                return;
            }

            // A partial view without a public parameterless constructor cannot be instantiated by the
            // generated [ModuleInitializer] to read its runtime Name, so emitting a Register() would
            // produce code that does not compile. Report VISTA0002 (info) and skip emission for it
            // (R3.2; design.md Error Handling).
            if (!model.HasPublicParameterlessCtor)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ViewMissingParameterlessCtor,
                    model.Location?.ToLocation() ?? Location.None,
                    model.ClassName));
                return;
            }

            // Phase 2 plan diagnostics (task 3.3, R9.1–R9.4). These concern the generated execution
            // plan only; the Phase 1 accessor map below is emitted regardless so the export path keeps
            // working (coexistence, R4.5). Plan emission itself is task 4.1 — here we only report.
            if (model.ProjectionUnanalyzable)
            {
                // VISTA0003 (warning): the From<TSource>(...) projection is not statically reproducible.
                // No execution plan is generated for this view — it stays metadata-only — and the
                // remaining views continue to generate; no compilation error is raised (R1.6, R9.1, R9.2).
                var memberSuffix = string.IsNullOrEmpty(model.UnanalyzableMember)
                    ? string.Empty
                    : " (member '" + model.UnanalyzableMember + "')";

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ProjectionNotStaticallyAnalyzable,
                    model.Location?.ToLocation() ?? Location.None,
                    model.ClassName,
                    memberSuffix));
            }
            else if (model.Projection is not null && !model.HasDeclaredKey && !model.IsSingleSource)
            {
                // VISTA0020 (error): an analyzable (executable) view that declares no key and projects
                // from more than one source entity is provably keyless — single-source PK
                // auto-derivation (D105) does not apply to multi-source views. The single-source keyless
                // case can only be decided at runtime against DbContext.Model, so the startup hook is its
                // backstop (R6.4, R9.3).
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ExecutableViewHasNoKey,
                    model.Location?.ToLocation() ?? Location.None,
                    model.ClassName));
            }

            var source = BuildAccessorSource(model);
            context.AddSource(BuildHintName(model), SourceText.From(source, Encoding.UTF8));

            // Phase 2 (task 4.1, R1.1–R1.5, R2.1/R2.3, R5.2, R7.1): when the view's From<TSource>(...)
            // projection is statically reproducible AND it is single-source (exactly one EF entity, the
            // only shape CreateScopedQueryable can root a queryable on), emit the AOT-clean compiled
            // execution plan alongside the Phase 1 accessor map. The plan lives in its own generated file
            // (separate [ModuleInitializer]) so the two concerns stay independent and the snapshot tests
            // can target each in isolation. Unanalyzable / multi-source views were already reported
            // (VISTA0003 / VISTA0020) above and stay metadata-only (DR5).
            if (model.Projection is not null
                && !model.ProjectionUnanalyzable
                && model.IsSingleSource
                && model.SourceTypeFqn is not null
                && model.CompiledPlanSupported)
            {
                var planSource = BuildPlanSource(model);
                context.AddSource(BuildPlanHintName(model), SourceText.From(planSource, Encoding.UTF8));
            }
        }

        /// <summary>
        /// Builds the per-view generated source: a <c>file static</c> class exposing a
        /// <c>Dictionary&lt;string, Func&lt;object, object?&gt;&gt;</c> accessor map keyed by property
        /// name. Each accessor is a cast to the fully-qualified <c>TQuery</c> type followed by a property
        /// read — never reflection (R2.1, R2.2). Property order follows declaration order (already
        /// captured on the model) so the output is deterministic for snapshot tests (task 4.1). A view
        /// with no public readable properties yields an empty map (kept for consistency so the registry
        /// always sees an entry for the view). The same class also carries the task 3.2
        /// <c>[ModuleInitializer]</c> <c>Register()</c> that registers <c>Map</c> into
        /// <c>ViewAccessorRegistry</c> keyed by the view's runtime <c>Name</c>.
        /// </summary>
        private static string BuildAccessorSource(ViewModel model)
        {
            // Fixed "\n" line endings (not Environment.NewLine) so generated text is byte-identical
            // across platforms, keeping snapshot/golden tests (task 4.1) stable.
            const string nl = "\n";
            var accessorClassName = model.ClassName + "_VistaAccessors";

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>").Append(nl);
            sb.Append("#nullable enable").Append(nl);
            sb.Append(nl);

            // A file-local class: the `file` modifier scopes the type to this generated file, so two
            // views sharing a class name in different namespaces never collide at the type level
            // (C# 11+; consumer TFMs net8/9/10 support it — R1.4). No namespace is emitted; the accessor
            // class is an internal implementation detail referenced only by its own (later) module
            // initializer.
            sb.Append("file static class ").Append(accessorClassName).Append(nl);
            sb.Append("{").Append(nl);
            sb.Append("    public static readonly global::System.Collections.Generic.Dictionary<string, global::System.Func<object, object?>> Map = new()").Append(nl);
            sb.Append("    {").Append(nl);

            foreach (var property in model.Properties)
            {
                // ["Name"] = static row => ((global::TQuery)row).Name,
                sb.Append("        [\"").Append(property.Name).Append("\"] = static row => ((")
                  .Append(model.TQueryFqn).Append(")row).").Append(property.Name).Append(',').Append(nl);
            }

            sb.Append("    };").Append(nl);
            sb.Append(nl);

            // [ModuleInitializer] registration (task 3.2, R3.2/R3.3/R2.3). The method is keyed off the
            // view's RUNTIME Name: the initializer instantiates the view via its public parameterless
            // ctor (guaranteed present — VISTA0002 skips views lacking one) and reads `.Name` once at
            // module load (cold path, no reflection emit). It is `internal static void` and parameterless
            // so it satisfies the ModuleInitializer signature contract (CS8815/CS8816): static,
            // parameterless, void, non-generic, and at least internally visible (not private). All
            // emitted constructs (file-local type, [ModuleInitializer], target-typed `new()`) are
            // available on the lowest consumer TFM, net8.0 (R1.4).
            var viewFqn = string.IsNullOrEmpty(model.Namespace)
                ? "global::" + model.ClassName
                : "global::" + model.Namespace + "." + model.ClassName;

            sb.Append("    [global::System.Runtime.CompilerServices.ModuleInitializer]").Append(nl);
            sb.Append("    internal static void Register()").Append(nl);
            sb.Append("        => global::a2n.Vista.Metadata.ViewAccessorRegistry.Register(").Append(nl);
            sb.Append("               new ").Append(viewFqn).Append("().Name, Map);").Append(nl);
            sb.Append("}").Append(nl);

            return sb.ToString();
        }

        /// <summary>
        /// Builds a unique <c>AddSource</c> hint name for the view. The namespace is folded into the name
        /// so two views with the same class name in different namespaces do not collide. Dots are
        /// replaced with underscores to keep the hint a simple file-name token.
        /// </summary>
        private static string BuildHintName(ViewModel model)
        {
            var prefix = string.IsNullOrEmpty(model.Namespace)
                ? string.Empty
                : model.Namespace.Replace('.', '_') + "_";

            return prefix + model.ClassName + "_VistaAccessors.g.cs";
        }

        /// <summary>
        /// Builds the unique <c>AddSource</c> hint name for the view's compiled execution plan (task 4.1),
        /// kept distinct from the Phase 1 accessor hint so the two generated files never collide.
        /// </summary>
        private static string BuildPlanHintName(ViewModel model)
        {
            var prefix = string.IsNullOrEmpty(model.Namespace)
                ? string.Empty
                : model.Namespace.Replace('.', '_') + "_";

            return prefix + model.ClassName + "_VistaExecutionPlan.g.cs";
        }

        // -----------------------------------------------------------------------------------------
        // TASK 4.1 — emit the AOT-clean compiled execution plan (R1.1–R1.5, R2.1/R2.3, R5.2, R7.1).
        //
        // For each analyzable single-source typed Style B view, emit a `file sealed`
        // CompiledViewExecutionPlan_<View> implementing the non-RUC
        // a2n.Vista.EntityFrameworkCore.Execution.ICompiledViewExecutionPlan. Everything is built by
        // compile-time expression-node construction emitted AS C# (the consumer compiles the lambdas),
        // so the runtime path never touches Activator.CreateInstance, PropertyInfo,
        // Expression.Property(string), Expression.Lambda(...).Compile(), or MethodInfo.MakeGenericMethod
        // (R1.2, R5.2). A [ModuleInitializer] constructs the plan and registers it into
        // GeneratedExecutionPlanStore keyed by the view's runtime Name (instantiate-and-read, as Phase 1).
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Emits the per-view <c>CompiledViewExecutionPlan_&lt;View&gt;</c> source. Caller guarantees the
        /// model carries a reproducible single-source projection and a non-null
        /// <see cref="ViewModel.SourceTypeFqn"/>.
        /// </summary>
        private static string BuildPlanSource(ViewModel model)
        {
            // Fixed "\n" line endings (not Environment.NewLine) so generated text is byte-identical across
            // platforms, keeping the snapshot/golden tests (task 4.2) and determinism property (task 4.3)
            // stable.
            const string nl = "\n";

            var planClassName = model.ClassName + "_VistaExecutionPlan";
            var tquery = model.TQueryFqn;       // already global::-qualified
            var tsource = model.SourceTypeFqn;  // already global::-qualified

            var viewFqn = string.IsNullOrEmpty(model.Namespace)
                ? "global::" + model.ClassName
                : "global::" + model.Namespace + "." + model.ClassName;

            // The captured authored row-filter factory list type, strongly typed to TSource.
            var rowFilterType =
                "global::System.Func<global::System.IServiceProvider, global::System.Linq.Expressions.Expression<global::System.Func<"
                + tsource + ", bool>>>";
            var rowFilterListType =
                "global::System.Collections.Generic.IReadOnlyList<" + rowFilterType + ">";

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>").Append(nl);
            sb.Append("#nullable enable").Append(nl);
            sb.Append(nl);

            sb.Append("file sealed class ").Append(planClassName)
              .Append(" : global::a2n.Vista.EntityFrameworkCore.Execution.ICompiledViewExecutionPlan").Append(nl);
            sb.Append("{").Append(nl);

            // ---- projection: Expression<Func<TSource, TRow>> reproduced as C# (consumer compiles it) ----
            AppendProjection(sb, model, tquery, tsource, nl);
            sb.Append(nl);

            // ---- member-access map for filterable/sortable fields (R2.1, R2.3) ----
            AppendMemberAccessMap(sb, model, tquery, nl);
            sb.Append(nl);

            // ---- masked-field accessors (R7.1) ----
            AppendMaskAccessors(sb, model, tquery, nl);
            sb.Append(nl);

            // ---- captured authored row filters + constructor ----
            sb.Append("    private readonly string _viewName;").Append(nl);
            sb.Append("    private readonly ").Append(rowFilterListType).Append(" _rowFilters;").Append(nl);
            sb.Append(nl);
            sb.Append("    public ").Append(planClassName).Append("(string viewName, ")
              .Append(rowFilterListType).Append(" rowFilters)").Append(nl);
            sb.Append("    {").Append(nl);
            sb.Append("        _viewName = viewName;").Append(nl);
            sb.Append("        _rowFilters = rowFilters;").Append(nl);
            sb.Append("    }").Append(nl);
            sb.Append(nl);

            // ---- contract values (R1.3, R6.1/R6.5) ----
            sb.Append("    public string ViewName => _viewName;").Append(nl);
            sb.Append("    public global::System.Type RowType => typeof(").Append(tquery).Append(");").Append(nl);
            sb.Append("    public global::System.Type SourceType => typeof(").Append(tsource).Append(");").Append(nl);
            sb.Append("    public bool IsSingleSource => true;").Append(nl);
            sb.Append(nl);

            // ---- CreateScopedQueryable: authored filters + scope AND-ed pre-projection (R1.1, R1.4) ----
            AppendCreateScopedQueryable(sb, tsource, nl);
            sb.Append(nl);

            // ---- member-access resolver (R2.2) ----
            sb.Append("    public bool TryGetMemberAccess(string fieldName, out global::System.Linq.Expressions.LambdaExpression accessor)").Append(nl);
            sb.Append("    {").Append(nl);
            sb.Append("        if (MemberAccess.TryGetValue(fieldName, out var found))").Append(nl);
            sb.Append("        {").Append(nl);
            sb.Append("            accessor = found;").Append(nl);
            sb.Append("            return true;").Append(nl);
            sb.Append("        }").Append(nl);
            sb.Append(nl);
            sb.Append("        accessor = null!;").Append(nl);
            sb.Append("        return false;").Append(nl);
            sb.Append("    }").Append(nl);
            sb.Append(nl);

            // ---- strongly-typed sort appliers (no MakeGenericMethod) (R3.4, R3.5) ----
            AppendSortApplier(sb, model, tquery, isPrimary: true, nl);
            sb.Append(nl);
            AppendSortApplier(sb, model, tquery, isPrimary: false, nl);
            sb.Append(nl);

            // ---- mask accessors property ----
            sb.Append("    public global::System.Collections.Generic.IReadOnlyList<global::a2n.Vista.Metadata.MaskAccessor> MaskAccessors => MaskAccessorArray;").Append(nl);
            sb.Append(nl);

            // ---- [ModuleInitializer]: construct + register keyed by the view's runtime Name (R4.1) ----
            sb.Append("    [global::System.Runtime.CompilerServices.ModuleInitializer]").Append(nl);
            sb.Append("    internal static void RegisterExecutionPlan()").Append(nl);
            sb.Append("    {").Append(nl);
            sb.Append("        var view = new ").Append(viewFqn).Append("();").Append(nl);
            sb.Append("        var plan = new ").Append(planClassName)
              .Append("(view.Name, view.GetSourceRowFilters<").Append(tsource).Append(">());").Append(nl);
            sb.Append("        global::a2n.Vista.EntityFrameworkCore.Execution.GeneratedExecutionPlanStore.Add(plan.ViewName, plan);").Append(nl);
            sb.Append("    }").Append(nl);

            sb.Append("}").Append(nl);

            return sb.ToString();
        }

        /// <summary>
        /// Appends the <c>Projection</c> static field: an <c>Expression&lt;Func&lt;TSource, TRow&gt;&gt;</c>
        /// reproduced as C# source over the canonical <c>src</c> parameter (member-init or named-ctor
        /// shape), so the consumer compiles the expression tree (no runtime reflection, R1.2).
        /// </summary>
        private static void AppendProjection(StringBuilder sb, ViewModel model, string tquery, string tsource, string nl)
        {
            sb.Append("    private static readonly global::System.Linq.Expressions.Expression<global::System.Func<")
              .Append(tsource).Append(", ").Append(tquery).Append(">> Projection =").Append(nl);

            var bindings = model.Projection.Bindings;
            if (model.Projection.Kind == ProjectionKind.MemberInit)
            {
                sb.Append("        static (").Append(tsource).Append(' ').Append(CanonicalSourceParameter)
                  .Append(") => new ").Append(tquery).Append(nl);
                sb.Append("        {").Append(nl);
                foreach (var binding in bindings)
                {
                    sb.Append("            ").Append(binding.TargetMember).Append(" = ")
                      .Append(binding.SourceExpressionText).Append(',').Append(nl);
                }

                sb.Append("        };").Append(nl);
            }
            else
            {
                // Named-constructor (e.g. positional record): emit positional arguments in binding order.
                sb.Append("        static (").Append(tsource).Append(' ').Append(CanonicalSourceParameter)
                  .Append(") => new ").Append(tquery).Append('(').Append(nl);
                for (var i = 0; i < bindings.Count; i++)
                {
                    var suffix = i == bindings.Count - 1 ? ");" : ",";
                    sb.Append("            ").Append(bindings[i].SourceExpressionText).Append(suffix).Append(nl);
                }
            }
        }

        /// <summary>
        /// Appends the <c>MemberAccess</c> static map (<c>fieldName -&gt;
        /// Expression&lt;Func&lt;TRow, TField&gt;&gt;</c> as <c>static x =&gt; x.Field</c>) for the
        /// projected filterable/sortable fields (R2.1, R2.3). Built by direct lambda construction the
        /// consumer compiles — never <c>Expression.Property(string)</c>.
        /// </summary>
        private static void AppendMemberAccessMap(StringBuilder sb, ViewModel model, string tquery, string nl)
        {
            sb.Append("    private static readonly global::System.Collections.Generic.Dictionary<string, global::System.Linq.Expressions.LambdaExpression> MemberAccess = new(global::System.StringComparer.Ordinal)").Append(nl);
            sb.Append("    {").Append(nl);
            foreach (var field in model.Fields)
            {
                if (!field.IsFilterable && !field.IsSortable)
                {
                    continue;
                }

                sb.Append("        [\"").Append(field.Name)
                  .Append("\"] = (global::System.Linq.Expressions.Expression<global::System.Func<")
                  .Append(tquery).Append(", ").Append(field.ClrTypeFqn).Append(">>)(static (")
                  .Append(tquery).Append(" x) => x.").Append(field.Name).Append("),").Append(nl);
            }

            sb.Append("    };").Append(nl);
        }

        /// <summary>
        /// Appends the <c>MaskAccessorArray</c> static field: one
        /// <see cref="a2n.Vista.Metadata.MaskAccessor"/> per masked field with a cast + property read
        /// getter and a setter that is either a direct assignment (settable property), a <c>with</c>-style
        /// rebuild (record / <c>init</c>-only), or a runtime-throwing fallback (R7.1, open implementation
        /// call #3). Empty array when the view has no masked field.
        /// </summary>
        private static void AppendMaskAccessors(StringBuilder sb, ViewModel model, string tquery, string nl)
        {
            var masked = new List<PlanFieldModel>();
            foreach (var field in model.Fields)
            {
                if (field.IsMaskable)
                {
                    masked.Add(field);
                }
            }

            if (masked.Count == 0)
            {
                sb.Append("    private static readonly global::a2n.Vista.Metadata.MaskAccessor[] MaskAccessorArray = global::System.Array.Empty<global::a2n.Vista.Metadata.MaskAccessor>();").Append(nl);
                return;
            }

            sb.Append("    private static readonly global::a2n.Vista.Metadata.MaskAccessor[] MaskAccessorArray = new global::a2n.Vista.Metadata.MaskAccessor[]").Append(nl);
            sb.Append("    {").Append(nl);
            foreach (var field in masked)
            {
                sb.Append("        new global::a2n.Vista.Metadata.MaskAccessor(").Append(nl);
                sb.Append("            \"").Append(field.Name).Append("\",").Append(nl);
                sb.Append("            static row => ((").Append(tquery).Append(")row).").Append(field.Name).Append(',').Append(nl);

                if (field.HasWritableSetter)
                {
                    // Settable property: direct assignment on the cast row.
                    sb.Append("            static (row, value) =>").Append(nl);
                    sb.Append("            {").Append(nl);
                    sb.Append("                var typed = (").Append(tquery).Append(")row;").Append(nl);
                    sb.Append("                typed.").Append(field.Name).Append(" = (").Append(field.ClrTypeFqn).Append(")value!;").Append(nl);
                    sb.Append("                return typed;").Append(nl);
                    sb.Append("            }),").Append(nl);
                }
                else if (model.RowTypeIsRecord)
                {
                    // init-only / record member: rebuild the immutable row with a `with` expression.
                    sb.Append("            static (row, value) => ((").Append(tquery).Append(")row) with { ")
                      .Append(field.Name).Append(" = (").Append(field.ClrTypeFqn).Append(")value! }),").Append(nl);
                }
                else
                {
                    // No settable setter and not a record: no AOT-clean mutation is possible. Fail closed
                    // at runtime rather than silently leaking the original value (R7.6 spirit).
                    sb.Append("            static (row, value) => throw new global::System.NotSupportedException(").Append(nl);
                    sb.Append("                \"Masked field '").Append(field.Name).Append("' on row type '")
                      .Append(tquery).Append("' has no settable setter and the row type is not a record; cannot apply the mask.\")),").Append(nl);
                }
            }

            sb.Append("    };").Append(nl);
        }

        /// <summary>
        /// Appends the <c>CreateScopedQueryable</c> method: roots the queryable on
        /// <c>DbContext.Set&lt;TSource&gt;()</c> (D11), AND-s the authored server-trusted row filters and
        /// the per-request scope predicates pre-projection (R1.4), then applies the generated projection.
        /// Uses static <c>Queryable</c> calls so the file needs no <c>using</c> directives and the calls
        /// are unambiguous.
        /// </summary>
        private static void AppendCreateScopedQueryable(StringBuilder sb, string tsource, string nl)
        {
            sb.Append("    public global::System.Linq.IQueryable CreateScopedQueryable(").Append(nl);
            sb.Append("        global::Microsoft.EntityFrameworkCore.DbContext dbContext,").Append(nl);
            sb.Append("        global::System.IServiceProvider services,").Append(nl);
            sb.Append("        global::a2n.Vista.Ports.IViewScope scope)").Append(nl);
            sb.Append("    {").Append(nl);
            // No-tracking: a Vista read never hands the caller entities attached to the request-scoped
            // DbContext the write path shares, so masking a materialized row can never be persisted by a
            // later SaveChanges (audit finding BUG-07). Emitted as a static call on the closed generic, so
            // the path stays reflection-free.
            sb.Append("        global::System.Linq.IQueryable<").Append(tsource).Append("> source =").Append(nl);
            sb.Append("            global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(dbContext.Set<").Append(tsource).Append(">());").Append(nl);
            sb.Append(nl);
            sb.Append("        for (var i = 0; i < _rowFilters.Count; i++)").Append(nl);
            sb.Append("        {").Append(nl);
            sb.Append("            var predicate = _rowFilters[i](services)").Append(nl);
            sb.Append("                ?? throw new global::System.InvalidOperationException(").Append(nl);
            sb.Append("                    \"An authored row filter for view '\" + _viewName + \"' produced a null predicate.\");").Append(nl);
            sb.Append("            source = global::System.Linq.Queryable.Where(source, predicate);").Append(nl);
            sb.Append("        }").Append(nl);
            sb.Append(nl);
            sb.Append("        var scopeFilters = scope.GetRowFilters<").Append(tsource).Append(">();").Append(nl);
            sb.Append("        for (var i = 0; i < scopeFilters.Count; i++)").Append(nl);
            sb.Append("        {").Append(nl);
            sb.Append("            source = global::System.Linq.Queryable.Where(source, scopeFilters[i]);").Append(nl);
            sb.Append("        }").Append(nl);
            sb.Append(nl);
            sb.Append("        return global::System.Linq.Queryable.Select(source, Projection);").Append(nl);
            sb.Append("    }").Append(nl);
        }

        /// <summary>
        /// Appends a strongly-typed sort applier (<c>ApplyPrimarySort</c> or <c>ApplyThenSort</c>). Each
        /// projected field gets a <c>case</c> that calls the closed-generic
        /// <c>Queryable.OrderBy</c>/<c>OrderByDescending</c> (primary) or
        /// <c>Queryable.ThenBy</c>/<c>ThenByDescending</c> (secondary) directly — the C# compiler picks
        /// the closed generic, so no <c>MakeGenericMethod</c> is reached (R3.4, R3.5). Cases are emitted
        /// for every projected field so a model-derived key (D105) can always serve as the deterministic
        /// tiebreaker.
        /// </summary>
        private static void AppendSortApplier(StringBuilder sb, ViewModel model, string tquery, bool isPrimary, string nl)
        {
            var methodName = isPrimary ? "ApplyPrimarySort" : "ApplyThenSort";
            var inputType = isPrimary
                ? "global::System.Linq.IQueryable"
                : "global::System.Linq.IOrderedQueryable";
            var typedInput = isPrimary
                ? "global::System.Linq.IQueryable<" + tquery + ">"
                : "global::System.Linq.IOrderedQueryable<" + tquery + ">";
            var ascMethod = isPrimary ? "OrderBy" : "ThenBy";
            var descMethod = isPrimary ? "OrderByDescending" : "ThenByDescending";

            sb.Append("    public global::System.Linq.IOrderedQueryable ").Append(methodName)
              .Append('(').Append(inputType).Append(" source, string fieldName, bool descending)").Append(nl);
            sb.Append("    {").Append(nl);
            sb.Append("        var typed = (").Append(typedInput).Append(")source;").Append(nl);
            sb.Append("        switch (fieldName)").Append(nl);
            sb.Append("        {").Append(nl);
            foreach (var field in model.Fields)
            {
                sb.Append("            case \"").Append(field.Name).Append("\":").Append(nl);
                sb.Append("                return descending").Append(nl);
                sb.Append("                    ? global::System.Linq.Queryable.").Append(descMethod)
                  .Append("(typed, static (").Append(tquery).Append(" x) => x.").Append(field.Name).Append(')').Append(nl);
                sb.Append("                    : global::System.Linq.Queryable.").Append(ascMethod)
                  .Append("(typed, static (").Append(tquery).Append(" x) => x.").Append(field.Name).Append(");").Append(nl);
            }

            sb.Append("            default:").Append(nl);
            sb.Append("                throw new global::System.ArgumentException(").Append(nl);
            sb.Append("                    \"Field '\" + fieldName + \"' is not a sortable field of view '\" + _viewName + \"'.\",").Append(nl);
            sb.Append("                    nameof(fieldName));").Append(nl);
            sb.Append("        }").Append(nl);
            sb.Append("    }").Append(nl);
        }

        /// <summary>
        /// Fast syntax predicate (no semantics): a class declaration that has a base list. Cheap enough
        /// to run on every changed node; the semantic transform does the precise filtering.
        /// </summary>
        private static bool IsCandidateClass(SyntaxNode node)
            => node is ClassDeclarationSyntax classDecl
               && classDecl.BaseList is not null
               && classDecl.BaseList.Types.Count > 0;

        /// <summary>
        /// Semantic transform: resolve the declared symbol and keep it only when it is a non-abstract
        /// class deriving from a recognized Vista View base type (matched by FQN/metadata name). Returns
        /// an equatable <see cref="ViewModel"/> carrying everything downstream tasks need, or
        /// <c>null</c> to drop it.
        /// </summary>
        private static ViewModel Transform(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
        {
            var classDecl = (ClassDeclarationSyntax)ctx.Node;

            if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol symbol)
            {
                return null;
            }

            // R1.2: non-abstract only.
            if (symbol.IsAbstract || symbol.TypeKind != TypeKind.Class)
            {
                return null;
            }

            // Walk the base type chain looking for the recognized View<> / View<,> definition.
            var viewBase = FindViewBase(symbol);
            if (viewBase is null)
            {
                return null;
            }

            // TQuery is the first type argument of the recognized base. (TCrud, if present, is the
            // second; Phase 1 only needs TQuery's shape for read accessors.)
            var tquery = viewBase.TypeArguments.Length > 0
                ? viewBase.TypeArguments[0] as INamedTypeSymbol
                : null;
            if (tquery is null)
            {
                return null;
            }

            // Property extraction: public, readable, non-static, non-indexer instance properties of
            // TQuery (cast + property read downstream). Order is preserved (source/declaration order)
            // and the equality of the resulting model is order-sensitive.
            var properties = new List<PropertyModel>();
            foreach (var member in tquery.GetMembers())
            {
                if (member is IPropertySymbol property
                    && !property.IsStatic
                    && property.IsIndexer == false
                    && property.DeclaredAccessibility == Accessibility.Public
                    && property.GetMethod is not null
                    && property.GetMethod.DeclaredAccessibility == Accessibility.Public)
                {
                    properties.Add(new PropertyModel(
                        property.Name,
                        property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                }
            }

            var isPartial = classDecl.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword));

            // Whether the view can be instantiated by the generated [ModuleInitializer] (task 3.2) to
            // read its runtime Name. InstanceConstructors includes the IMPLICIT public default ctor when
            // the class declares none, so this single check covers both "no declared ctors" and
            // "explicitly declared public parameterless ctor"; it is false when every declared ctor
            // takes parameters or is non-public (R3.2 — views without one are skipped with VISTA0002).
            var hasPublicParameterlessCtor = symbol.InstanceConstructors.Any(
                static c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

            // Task 3.2 (R1.6, R9.1, R9.2; M11 feeds R6.x): reproduce the view's From<TSource>(...) /
            // FromQuery<TSource>(...) projection from syntax+semantics and mirror the per-field
            // FieldMetadata flags. AnalyzeView fills these in; when the projection is not statically
            // reproducible it leaves `projection` null (the emitter then skips the plan and the view
            // stays metadata-only) while still reporting the single-source/source-type facts so M11 PK
            // auto-derivation can use them.
            AnalyzeView(
                classDecl,
                ctx.SemanticModel,
                tquery,
                properties,
                ct,
                out var sourceTypeFqn,
                out var isSingleSource,
                out var projectionModel,
                out var planFields,
                out var projectionUnanalyzable,
                out var unanalyzableMember,
                out var hasDeclaredKey);

            return new ViewModel(
                @namespace: symbol.ContainingNamespace?.IsGlobalNamespace == true
                    ? null
                    : symbol.ContainingNamespace?.ToDisplayString(),
                className: symbol.Name,
                isPartial: isPartial,
                hasPublicParameterlessCtor: hasPublicParameterlessCtor,
                tqueryFqn: tquery.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                properties: new EquatableArray<PropertyModel>(properties.ToArray()),
                location: LocationInfo.From(classDecl.Identifier),
                sourceTypeFqn: sourceTypeFqn,
                isSingleSource: isSingleSource,
                projection: projectionModel,
                fields: planFields,
                projectionUnanalyzable: projectionUnanalyzable,
                unanalyzableMember: unanalyzableMember,
                hasDeclaredKey: hasDeclaredKey,
                rowTypeIsRecord: tquery.IsRecord,
                compiledPlanSupported:
                    ctx.SemanticModel.Compilation.GetTypeByMetadataName(CompiledPlanInterfaceMetadataName) is not null);
        }

        /// <summary>
        /// Walks the base-type chain and returns the constructed View base (so callers can read its type
        /// arguments), or <c>null</c> when the symbol does not derive from a recognized View type.
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
        /// Matches the unbound View definition by metadata name (encodes arity) + containing namespace.
        /// This is the FQN-only recognition required because the generator references no Vista assembly.
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

        // -----------------------------------------------------------------------------------------
        // TASK 3.2 — projection reproduction + static-analyzability gate (R1.6, R9.1, R9.2).
        //
        // The view's source and projection come from the fluent `From<TSource>(projection)` (or
        // `FromQuery<TSource>(source, projection)`) call inside the view's Configure override. This
        // analysis walks the class body for those calls (resolved semantically so only the genuine
        // a2n.Vista.Authoring.IViewBuilder methods match), captures TSource, reproduces the projection's
        // member bindings as node-reproducible C# source text, and mirrors the per-field FieldMetadata
        // flags (defaults + .Field(...) overrides + MaskField D95 defaults) so the plan emitter
        // (task 4.1) knows which fields get member-access expressions / sort appliers / mask accessors.
        //
        // The generator references no Vista assembly, so methods are matched by FQN/namespace only,
        // exactly like the base-type recognition above.
        // -----------------------------------------------------------------------------------------

        /// <summary>The canonical source-lambda parameter name the projection is normalized to.</summary>
        private const string CanonicalSourceParameter = "src";

        private const string ViewBuilderInterfaceName = "IViewBuilder";

        private const string FieldBuilderInterfaceName = "IFieldBuilder";

        /// <summary>
        /// Reproduces the view's <c>From&lt;TSource&gt;(...)</c> projection and mirrors per-field
        /// metadata. On success <paramref name="projection"/> carries the node-reproducible bindings and
        /// <paramref name="fields"/> the per-field flags; when the projection is not statically
        /// reproducible, <paramref name="projection"/> is left <c>null</c> (the emitter then skips the
        /// plan and the view stays metadata-only). <paramref name="sourceTypeFqn"/> /
        /// <paramref name="isSingleSource"/> are populated whenever exactly one source entity is
        /// resolved, independent of projection analyzability (they feed M11 PK auto-derivation).
        /// </summary>
        private static void AnalyzeView(
            ClassDeclarationSyntax classDecl,
            SemanticModel model,
            INamedTypeSymbol tquery,
            List<PropertyModel> properties,
            System.Threading.CancellationToken ct,
            out string sourceTypeFqn,
            out bool isSingleSource,
            out ProjectionModel projection,
            out EquatableArray<PlanFieldModel> fields,
            out bool projectionUnanalyzable,
            out string unanalyzableMember,
            out bool hasDeclaredKey)
        {
            sourceTypeFqn = null;
            isSingleSource = false;
            projection = null;
            fields = default;
            projectionUnanalyzable = false;
            unanalyzableMember = null;
            hasDeclaredKey = false;

            // Walk every fluent builder call in the class body, classifying the ones we care about by
            // their (semantically-resolved) method symbol. The projection lambda comes from the single
            // From/FromQuery call; per-field flags come from Field/MaskField calls; key declarations
            // come from .Key(...) (on the view builder) or a projected field's .PrimaryKey() (on the
            // field builder) and drive the VISTA0020 keyless gate (task 3.3, R9.3).
            var sourceTypes = new List<string>();
            LambdaExpressionSyntax projectionLambda = null;
            var fieldOverrides = new Dictionary<string, LambdaExpressionSyntax>(StringComparer.Ordinal);
            var maskedFields = new HashSet<string>(StringComparer.Ordinal);

            // Whether the view declares a From/FromQuery source projection at all. When it does but the
            // projection is not reproducible, that is the VISTA0003 "should have been analyzable" case
            // (R9.1); a view with no From at all is left silently metadata-only (no execution intent).
            var hasFromCall = false;

            foreach (var invocation in classDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                ct.ThrowIfCancellationRequested();

                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                {
                    continue;
                }

                var calledName = memberAccess.Name.Identifier.ValueText;
                if (calledName != "From" && calledName != "FromQuery"
                    && calledName != "Field" && calledName != "MaskField"
                    && calledName != "Key" && calledName != "PrimaryKey")
                {
                    continue;
                }

                if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
                {
                    continue;
                }

                var isViewBuilderMethod = IsViewBuilderMethod(method);
                var isFieldBuilderMethod = IsFieldBuilderMethod(method);
                if (!isViewBuilderMethod && !isFieldBuilderMethod)
                {
                    continue;
                }

                var arguments = invocation.ArgumentList?.Arguments;
                switch (method.Name)
                {
                    case "From" when isViewBuilderMethod && arguments is { Count: 1 }:
                    case "FromQuery" when isViewBuilderMethod && arguments is { Count: 2 }:
                        hasFromCall = true;
                        if (method.TypeArguments.Length > 0
                            && method.TypeArguments[0] is INamedTypeSymbol source)
                        {
                            var fqn = source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            if (!sourceTypes.Contains(fqn))
                            {
                                sourceTypes.Add(fqn);
                            }

                            // The projection is the last argument (From: arg 0; FromQuery: arg 1).
                            var projectionArg = arguments.Value[arguments.Value.Count - 1].Expression;
                            if (projectionLambda is null && AsLambda(projectionArg) is { } lambda)
                            {
                                projectionLambda = lambda;
                            }
                        }

                        break;

                    case "Field" when isViewBuilderMethod && arguments is { Count: 2 }:
                    {
                        var name = TryGetSelectedMemberName(AsLambda(arguments.Value[0].Expression));
                        if (name is not null && AsLambda(arguments.Value[1].Expression) is { } configure)
                        {
                            fieldOverrides[name] = configure;
                        }

                        break;
                    }

                    case "MaskField" when isViewBuilderMethod && arguments is { Count: >= 1 }:
                    {
                        var name = TryGetSelectedMemberName(AsLambda(arguments.Value[0].Expression));
                        if (name is not null)
                        {
                            maskedFields.Add(name);
                        }

                        break;
                    }

                    // An explicit view-level key (.Key(x => x.Id, ...) or .Key("Id", ...)) — R9.3.
                    case "Key" when isViewBuilderMethod:
                        hasDeclaredKey = true;
                        break;

                    // A projected field marked as the primary key (.Field(x => x.Id, f => f.PrimaryKey()))
                    // — the field-builder key declaration — R9.3.
                    case "PrimaryKey" when isFieldBuilderMethod:
                        hasDeclaredKey = true;
                        break;
                }
            }

            // Single-source = exactly one distinct EF source entity (no join). This is what M11 PK
            // auto-derivation requires; it is independent of whether the projection is reproducible.
            if (sourceTypes.Count == 1)
            {
                sourceTypeFqn = sourceTypes[0];
                isSingleSource = true;
            }

            if (projectionLambda is null)
            {
                // A From call was present (so the author intends execution) but its projection argument
                // is not an analyzable lambda — VISTA0003 applies (R9.1). A view with no From at all is
                // left silently metadata-only.
                projectionUnanalyzable = hasFromCall;
                return;
            }

            // Reproduce the projection bindings; null => not statically reproducible (VISTA0003 / skip).
            var bindings = TryReproduceProjection(projectionLambda, model, ct, out var kind, out unanalyzableMember);
            if (bindings is null)
            {
                projectionUnanalyzable = true;
                return;
            }

            unanalyzableMember = null;

            projection = new ProjectionModel(kind, new EquatableArray<ProjectionBinding>(bindings.ToArray()));

            // Mirror FieldMetadata defaults/overrides (ViewBuilder.Build) for the projected fields, in
            // projection order, so the plan emitter knows each field's filter/sort/mask facets.
            var propertyTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in properties)
            {
                propertyTypes[property.Name] = property.TypeFqn;
            }

            var planFields = new List<PlanFieldModel>(bindings.Count);
            foreach (var binding in bindings)
            {
                var name = binding.TargetMember;
                var clrTypeFqn = propertyTypes.TryGetValue(name, out var typeFqn)
                    ? typeFqn
                    : LookupMemberTypeFqn(tquery, name);

                ComputeFieldFlags(
                    name,
                    fieldOverrides,
                    maskedFields,
                    model,
                    ct,
                    out var isFilterable,
                    out var isSortable,
                    out var isMaskable);

                // Whether the projected property has a settable (public, non-init) setter, so the emitter
                // can choose a direct setter over a `with`-style rebuild for the mask accessor (task 4.1).
                var hasWritableSetter = isMaskable && HasWritableSetter(tquery, name);

                planFields.Add(new PlanFieldModel(
                    name, clrTypeFqn, isFilterable, isSortable, isMaskable, hasWritableSetter));
            }

            fields = new EquatableArray<PlanFieldModel>(planFields.ToArray());
        }

        /// <summary>
        /// True when <paramref name="method"/> is one of the fluent authoring methods declared on
        /// <c>a2n.Vista.Authoring.IViewBuilder&lt;...&gt;</c> (matched by FQN — the generator references
        /// no Vista assembly).
        /// </summary>
        private static bool IsViewBuilderMethod(IMethodSymbol method)
        {
            var containingType = method.ContainingType;
            if (containingType is null || containingType.Name != ViewBuilderInterfaceName)
            {
                return false;
            }

            var ns = containingType.ContainingNamespace;
            return ns is not null
                   && !ns.IsGlobalNamespace
                   && ns.ToDisplayString() == ViewNamespace;
        }

        /// <summary>
        /// True when <paramref name="method"/> is one of the fluent authoring methods declared on
        /// <c>a2n.Vista.Authoring.IFieldBuilder&lt;...&gt;</c> (matched by FQN — the generator references
        /// no Vista assembly). Used to recognize a projected field's <c>.PrimaryKey()</c> key
        /// declaration for the VISTA0020 keyless gate (R9.3).
        /// </summary>
        private static bool IsFieldBuilderMethod(IMethodSymbol method)
        {
            var containingType = method.ContainingType;
            if (containingType is null || containingType.Name != FieldBuilderInterfaceName)
            {
                return false;
            }

            var ns = containingType.ContainingNamespace;
            return ns is not null
                   && !ns.IsGlobalNamespace
                   && ns.ToDisplayString() == ViewNamespace;
        }
        /// the object-initializer shape (<c>s =&gt; new TRow { Member = s.X, ... }</c>) and the
        /// named-constructor shape (<c>s =&gt; new TRow(s.X, ...)</c>, e.g. positional records) are
        /// supported, mirroring <c>ViewBuilder.ExtractProjectedFields</c>. Each binding's source
        /// expression is normalized to the canonical source parameter <see cref="CanonicalSourceParameter"/>
        /// so the emitter can re-emit the lambda over a known parameter name. Returns <c>null</c> when the
        /// shape is not statically reproducible.
        /// </summary>
        private static List<ProjectionBinding> TryReproduceProjection(
            LambdaExpressionSyntax lambda,
            SemanticModel model,
            System.Threading.CancellationToken ct,
            out ProjectionKind kind,
            out string unanalyzableMember)
        {
            kind = ProjectionKind.MemberInit;
            unanalyzableMember = null;

            var parameterSymbol = GetSingleLambdaParameterSymbol(lambda, model, ct);
            if (lambda.Body is not ExpressionSyntax body)
            {
                return null;
            }

            if (Unwrap(body) is not ObjectCreationExpressionSyntax creation)
            {
                return null;
            }

            var hasInitializer = creation.Initializer is { } initializer
                && initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
                && initializer.Expressions.Count > 0;
            var hasCtorArguments = creation.ArgumentList is { } argList && argList.Arguments.Count > 0;

            // Disambiguate the shape: an object initializer is MemberInit; constructor arguments are
            // NamedCtor. A mix (or neither) is not reproduced here.
            if (hasInitializer && !hasCtorArguments)
            {
                kind = ProjectionKind.MemberInit;
                return TryReproduceMemberInit(creation, parameterSymbol, model, out unanalyzableMember);
            }

            if (hasCtorArguments && !hasInitializer)
            {
                kind = ProjectionKind.NamedCtor;
                return TryReproduceNamedCtor(creation, parameterSymbol, model, ct, out unanalyzableMember);
            }

            return null;
        }

        /// <summary>
        /// Reproduces an object-initializer projection: each <c>Member = &lt;expr&gt;</c> assignment
        /// becomes one binding. Returns <c>null</c> on any non-simple binding (nested/collection
        /// initializers, indexed members, etc.).
        /// </summary>
        private static List<ProjectionBinding> TryReproduceMemberInit(
            ObjectCreationExpressionSyntax creation,
            IParameterSymbol parameterSymbol,
            SemanticModel model,
            out string unanalyzableMember)
        {
            unanalyzableMember = null;
            var bindings = new List<ProjectionBinding>();
            foreach (var expression in creation.Initializer.Expressions)
            {
                if (expression is not AssignmentExpressionSyntax assignment
                    || assignment.Left is not IdentifierNameSyntax target)
                {
                    // Name the offending member where determinable (the assignment's left-hand side) so
                    // VISTA0003 can point the author at it.
                    if (expression is AssignmentExpressionSyntax badAssignment)
                    {
                        unanalyzableMember = badAssignment.Left.ToString();
                    }

                    return null;
                }

                bindings.Add(new ProjectionBinding(
                    target.Identifier.ValueText,
                    NormalizeSourceExpression(assignment.Right, parameterSymbol, model)));
            }

            return bindings.Count > 0 ? bindings : null;
        }

        /// <summary>
        /// Reproduces a named-constructor projection (e.g. a positional record): each constructor
        /// argument is paired with its constructor parameter name (which equals the generated property
        /// name for positional records). Returns <c>null</c> when the constructor cannot be resolved or
        /// an argument carries no resolvable parameter name.
        /// </summary>
        private static List<ProjectionBinding> TryReproduceNamedCtor(
            ObjectCreationExpressionSyntax creation,
            IParameterSymbol parameterSymbol,
            SemanticModel model,
            System.Threading.CancellationToken ct,
            out string unanalyzableMember)
        {
            unanalyzableMember = null;
            if (model.GetSymbolInfo(creation, ct).Symbol is not IMethodSymbol constructor)
            {
                return null;
            }

            var arguments = creation.ArgumentList.Arguments;
            var bindings = new List<ProjectionBinding>();
            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = arguments[i];

                // Resolve the target parameter: a named argument names it directly, otherwise it is
                // positional. Params/optional tails are not reproduced.
                IParameterSymbol parameter = null;
                if (argument.NameColon?.Name is { } namedArgument)
                {
                    parameter = constructor.Parameters.FirstOrDefault(
                        p => p.Name == namedArgument.Identifier.ValueText);
                }
                else if (i < constructor.Parameters.Length)
                {
                    parameter = constructor.Parameters[i];
                }

                if (parameter is null)
                {
                    // Name the offending constructor argument where determinable (a named argument's
                    // identifier) so VISTA0003 can point the author at it.
                    unanalyzableMember = argument.NameColon?.Name.Identifier.ValueText;
                    return null;
                }

                bindings.Add(new ProjectionBinding(
                    parameter.Name,
                    NormalizeSourceExpression(argument.Expression, parameterSymbol, model)));
            }

            return bindings.Count > 0 ? bindings : null;
        }

        /// <summary>
        /// Computes a projected field's filter/sort/mask flags, mirroring the defaults + overrides logic
        /// in <c>ViewBuilder.Build</c>/<c>FieldBuilder</c>: filterable/sortable default to <c>true</c>;
        /// a <c>.Field(...)</c> override's <c>Filterable</c>/<c>Sortable</c>/<c>Operators</c> wins; and a
        /// masked field (D95) defaults non-filterable unless an explicit filterable opt-in was given.
        /// </summary>
        private static void ComputeFieldFlags(
            string name,
            Dictionary<string, LambdaExpressionSyntax> fieldOverrides,
            HashSet<string> maskedFields,
            SemanticModel model,
            System.Threading.CancellationToken ct,
            out bool isFilterable,
            out bool isSortable,
            out bool isMaskable)
        {
            isFilterable = true;
            isSortable = true;
            isMaskable = false;
            var filterableExplicitlySet = false;
            var sortableExplicitlySet = false;

            if (fieldOverrides.TryGetValue(name, out var configure) && configure.Body is { } configureBody)
            {
                foreach (var invocation in configureBody.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
                {
                    ct.ThrowIfCancellationRequested();

                    if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                    {
                        continue;
                    }

                    var args = invocation.ArgumentList?.Arguments;
                    switch (memberAccess.Name.Identifier.ValueText)
                    {
                        case "Filterable":
                            isFilterable = ReadBoolArgument(args, defaultValue: true);
                            filterableExplicitlySet = true;
                            break;

                        case "Sortable":
                            isSortable = ReadBoolArgument(args, defaultValue: true);
                            sortableExplicitlySet = true;
                            break;

                        case "Operators":
                            // An explicit operator whitelist implies a deliberate filterable opt-in (D95).
                            filterableExplicitlySet = true;
                            break;
                    }
                }
            }

            if (maskedFields.Contains(name))
            {
                isMaskable = true;
                if (!filterableExplicitlySet)
                {
                    // D95: masked fields default non-filterable absent an explicit opt-in (R8.1).
                    isFilterable = false;
                }

                if (!sortableExplicitlySet)
                {
                    // D143: and non-sortable, closing the ORDER BY + paging probing vector. Kept in lockstep
                    // with ViewBuilder so generated metadata stays byte-identical to the reflection oracle.
                    isSortable = false;
                }
            }
        }

        /// <summary>
        /// Reads a single <c>bool</c> literal argument from a fluent call. Returns
        /// <paramref name="defaultValue"/> when the call has no argument (e.g. <c>Filterable()</c>) or the
        /// argument is not a <c>true</c>/<c>false</c> literal (a non-literal expression is treated
        /// conservatively as the default-allow).
        /// </summary>
        private static bool ReadBoolArgument(
            SeparatedSyntaxList<ArgumentSyntax>? args,
            bool defaultValue)
        {
            if (args is not { Count: >= 1 })
            {
                return defaultValue;
            }

            var expression = args.Value[0].Expression;
            if (expression.IsKind(SyntaxKind.TrueLiteralExpression))
            {
                return true;
            }

            if (expression.IsKind(SyntaxKind.FalseLiteralExpression))
            {
                return false;
            }

            return defaultValue;
        }

        /// <summary>
        /// Returns the lambda from an argument expression (a simple or parenthesized lambda), or
        /// <c>null</c> when the expression is not a lambda.
        /// </summary>
        private static LambdaExpressionSyntax AsLambda(ExpressionSyntax expression) => expression switch
        {
            SimpleLambdaExpressionSyntax simple => simple,
            ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count == 1 => paren,
            _ => null,
        };

        /// <summary>
        /// Extracts the member name selected by a <c>x =&gt; x.Member</c> selector lambda, or <c>null</c>
        /// when it is not a simple member access.
        /// </summary>
        private static string TryGetSelectedMemberName(LambdaExpressionSyntax lambda)
        {
            if (lambda?.Body is not ExpressionSyntax body)
            {
                return null;
            }

            return Unwrap(body) is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Name.Identifier.ValueText
                : null;
        }

        /// <summary>Resolves the single parameter symbol of a projection lambda, or <c>null</c>.</summary>
        private static IParameterSymbol GetSingleLambdaParameterSymbol(
            LambdaExpressionSyntax lambda,
            SemanticModel model,
            System.Threading.CancellationToken ct)
        {
            var parameterSyntax = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => simple.Parameter,
                ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count == 1
                    => paren.ParameterList.Parameters[0],
                _ => null,
            };

            return parameterSyntax is null
                ? null
                : model.GetDeclaredSymbol(parameterSyntax, ct) as IParameterSymbol;
        }

        /// <summary>
        /// Returns the node-reproducible C# text of a projection's source expression, with every
        /// reference to the projection lambda's parameter rewritten to <see cref="CanonicalSourceParameter"/>
        /// so the emitter can re-emit the projection over a known parameter name. References are matched
        /// semantically (not by raw text) so a member named like the parameter is never rewritten.
        /// </summary>
        private static string NormalizeSourceExpression(
            ExpressionSyntax expression,
            IParameterSymbol parameterSymbol,
            SemanticModel model)
        {
            if (parameterSymbol is null)
            {
                return expression.ToString();
            }

            var rewriter = new ParameterRenameRewriter(model, parameterSymbol);
            return rewriter.Visit(expression).ToString();
        }

        /// <summary>Looks up a TQuery member's fully-qualified type, falling back to <c>object</c>.</summary>
        private static string LookupMemberTypeFqn(INamedTypeSymbol tquery, string memberName)
        {
            foreach (var member in tquery.GetMembers(memberName))
            {
                switch (member)
                {
                    case IPropertySymbol property:
                        return property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    case IFieldSymbol field:
                        return field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }

            return "object";
        }

        /// <summary>
        /// Whether the named <c>TQuery</c> member is a property with a public, settable (non-<c>init</c>)
        /// setter. Such a property gets a direct mask setter (<c>row.Field = value</c>); an
        /// <c>init</c>-only / get-only property is rebuilt with a <c>with</c> expression instead (task 4.1,
        /// open implementation call #3). Walks the base type chain so inherited members are found.
        /// </summary>
        private static bool HasWritableSetter(INamedTypeSymbol tquery, string memberName)
        {
            for (var current = tquery; current is not null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers(memberName))
                {
                    if (member is IPropertySymbol property)
                    {
                        var setter = property.SetMethod;
                        return setter is not null
                            && setter.DeclaredAccessibility == Accessibility.Public
                            && !setter.IsInitOnly;
                    }
                }
            }

            return false;
        }

        /// <summary>Unwraps parentheses and conversion casts around an expression.</summary>
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
        /// Rewrites every identifier bound to a given lambda parameter to the canonical source parameter
        /// name, so a reproduced projection expression is self-contained against a known parameter.
        /// </summary>
        private sealed class ParameterRenameRewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _model;
            private readonly IParameterSymbol _parameter;

            public ParameterRenameRewriter(SemanticModel model, IParameterSymbol parameter)
            {
                _model = model;
                _parameter = parameter;
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (node.Identifier.ValueText == _parameter.Name
                    && SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(node).Symbol, _parameter))
                {
                    return SyntaxFactory.IdentifierName(CanonicalSourceParameter).WithTriviaFrom(node);
                }

                return base.VisitIdentifierName(node);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // EQUATABLE value model (task 2.2). These types carry only strings/bools and an EquatableArray<T>
    // of value-equal property records, so the incremental pipeline's structural equality lets Roslyn
    // reuse cached output for unchanged views — an unrelated edit elsewhere does not invalidate every
    // view (R1.3, Spec 03 §12).
    //
    // Records give value-based Equals/GetHashCode covering every declared member. Get-only auto
    // properties (set via the constructor) are used deliberately: they avoid `init` accessors and thus
    // the System.Runtime.CompilerServices.IsExternalInit shim that netstandard2.0 would otherwise need.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Fully equatable description of a discovered typed Style B view. Equality is value-based and
    /// covers the namespace, class name, partial flag, TQuery FQN, and the (order-sensitive) property
    /// sequence.
    /// </summary>
    internal sealed record ViewModel
    {
        public ViewModel(
            string @namespace,
            string className,
            bool isPartial,
            bool hasPublicParameterlessCtor,
            string tqueryFqn,
            EquatableArray<PropertyModel> properties,
            LocationInfo location,
            string sourceTypeFqn = null,
            bool isSingleSource = false,
            ProjectionModel projection = null,
            EquatableArray<PlanFieldModel> fields = default,
            bool projectionUnanalyzable = false,
            string unanalyzableMember = null,
            bool hasDeclaredKey = false,
            bool rowTypeIsRecord = false,
            bool compiledPlanSupported = false)
        {
            Namespace = @namespace;
            ClassName = className;
            IsPartial = isPartial;
            HasPublicParameterlessCtor = hasPublicParameterlessCtor;
            TQueryFqn = tqueryFqn;
            Properties = properties;
            Location = location;
            SourceTypeFqn = sourceTypeFqn;
            IsSingleSource = isSingleSource;
            Projection = projection;
            Fields = fields;
            ProjectionUnanalyzable = projectionUnanalyzable;
            UnanalyzableMember = unanalyzableMember;
            HasDeclaredKey = hasDeclaredKey;
            RowTypeIsRecord = rowTypeIsRecord;
            CompiledPlanSupported = compiledPlanSupported;
        }

        /// <summary>Declaring namespace, or <c>null</c> for the global namespace.</summary>
        public string Namespace { get; }

        /// <summary>The view class name (without namespace).</summary>
        public string ClassName { get; }

        /// <summary>Whether the view is declared <c>partial</c> (drives VISTA0001 in task 2.3).</summary>
        public bool IsPartial { get; }

        /// <summary>
        /// Whether the view has a public parameterless constructor (drives VISTA0002 in task 3.2). When
        /// <c>false</c>, the generated <c>[ModuleInitializer]</c> cannot instantiate the view to read its
        /// runtime <c>Name</c>, so the view is skipped with an info diagnostic (R3.2).
        /// </summary>
        public bool HasPublicParameterlessCtor { get; }

        /// <summary>Fully-qualified name of <c>TQuery</c> (the projected row type).</summary>
        public string TQueryFqn { get; }

        /// <summary>
        /// Public readable instance properties of <c>TQuery</c> (the accessor shape), in declaration
        /// order. Wrapped in <see cref="EquatableArray{T}"/> so the sequence participates in the
        /// record's value equality (order-sensitive).
        /// </summary>
        public EquatableArray<PropertyModel> Properties { get; }

        /// <summary>
        /// Equatable surrogate for the view class identifier's source location, used to report
        /// VISTA0001 (task 2.3). A <see cref="LocationInfo"/> (not a raw
        /// <see cref="Location"/>) so the model stays value-equal and incremental caching is preserved
        /// (R1.3); reconstruct the real location with <see cref="LocationInfo.ToLocation"/> at report
        /// time.
        /// </summary>
        public LocationInfo Location { get; }

        // -----------------------------------------------------------------------------------------
        // Phase 2 (M10, D118) plan-emitter data (task 3.1). These fields carry only strings/bools, a
        // value-equal ProjectionModel record, and an EquatableArray<PlanFieldModel>, so they preserve
        // the incremental cache exactly as the Phase 1 fields do (R10.1 byte-identical snapshots).
        // Task 3.1 ONLY extends the equatable model; the semantic analysis that POPULATES these
        // (reproducing the From<TSource>(...) projection, determining single-source, mirroring field
        // metadata) is task 3.2, and the emitter that consumes them is task 4.1. Until then the
        // Phase 1 Transform leaves them at their defaults (null / false / empty).
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Fully-qualified name of the single EF source entity <c>TSource</c> from the view's
        /// <c>From&lt;TSource&gt;(...)</c>, or <c>null</c> when the source cannot be statically analyzed
        /// (or the view is multi-source). Populated by task 3.2; consumed by the plan emitter (task 4.1)
        /// and PK auto-derivation (M11).
        /// </summary>
        public string SourceTypeFqn { get; }

        /// <summary>
        /// Whether the view projects from exactly one EF source entity with no join (R6.1/R6.5). Drives
        /// D105 single-source PK auto-derivation. Populated by task 3.2.
        /// </summary>
        public bool IsSingleSource { get; }

        /// <summary>
        /// The statically-reproducible projection of the view's <c>From&lt;TSource&gt;(...)</c> shape, or
        /// <c>null</c> when the projection is not statically reproducible — in which case the emitter
        /// raises VISTA0003 and skips the plan, leaving the view metadata-only (R1.6, R9.1, R9.2). A
        /// value-equal record so it participates in <see cref="ViewModel"/> equality. Populated by
        /// task 3.2.
        /// </summary>
        public ProjectionModel Projection { get; }

        /// <summary>
        /// Per-field plan metadata for the projected fields of <c>TQuery</c> (name, CLR type, and the
        /// filterable/sortable/maskable flags mirrored from <c>FieldMetadata</c>), in declaration order.
        /// Wrapped in <see cref="EquatableArray{T}"/> so the (order-sensitive) sequence participates in
        /// the record's value equality. Populated by task 3.2; drives member-access, sort appliers, and
        /// mask accessors in the emitter (task 4.1).
        /// </summary>
        public EquatableArray<PlanFieldModel> Fields { get; }

        // -----------------------------------------------------------------------------------------
        // Phase 2 (M10, D118) diagnostic facts (task 3.3, R9.1–R9.4). These bool/string members keep the
        // model value-equal (incremental cache preserved) and drive the diagnostics reported in Emit:
        // VISTA0003 when the projection is not statically reproducible, VISTA0020 when the executable
        // view is provably keyless. They are computed by AnalyzeView (task 3.2).
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Whether the view declares a <c>From&lt;TSource&gt;(...)</c> source projection (so the author
        /// intends execution) but that projection is not statically reproducible. Drives VISTA0003: the
        /// emitter reports a warning, skips the plan, and leaves the view metadata-only (R1.6, R9.1,
        /// R9.2). A view with no <c>From</c> at all leaves this <c>false</c> and is silently
        /// metadata-only.
        /// </summary>
        public bool ProjectionUnanalyzable { get; }

        /// <summary>
        /// The offending projected member name when it can be determined, surfaced in the VISTA0003
        /// message so the author can locate the unanalyzable binding; <c>null</c> when not determinable.
        /// </summary>
        public string UnanalyzableMember { get; }

        /// <summary>
        /// Whether the view declares a key — an explicit view-level <c>.Key(...)</c> or a projected
        /// field's <c>.PrimaryKey()</c>. Drives VISTA0020: an executable, multi-source view with no
        /// declared key is provably keyless (single-source PK auto-derivation, D105, does not apply), so
        /// the emitter reports an error (R9.3).
        /// </summary>
        public bool HasDeclaredKey { get; }

        /// <summary>
        /// Whether <c>TQuery</c> (the projected row type) is a <c>record</c>. Drives how the emitter
        /// writes a masked field's <see cref="a2n.Vista.Metadata.MaskAccessor"/> setter for an
        /// <c>init</c>-only member: a record row is rebuilt with a <c>with</c> expression; a non-record
        /// row with no settable setter falls back to a runtime-throwing setter (task 4.1, open
        /// implementation call #3).
        /// </summary>
        public bool RowTypeIsRecord { get; }

        /// <summary>
        /// Whether the compilation references the EF layer that hosts
        /// <c>a2n.Vista.EntityFrameworkCore.Execution.ICompiledViewExecutionPlan</c>. The generated
        /// compiled execution plan names EF types (the plan interface, the store, <c>DbContext</c>), so it
        /// can only compile in a consumer that references Core <b>and</b> EF (design Layering note). When
        /// the EF layer is absent (e.g. a Core-only consumer that still wants the Phase 1 export
        /// accessors), the plan is not emitted and the view stays metadata-only there. Computed in
        /// <c>Transform</c> from the compilation; value-equatable so the cache invalidates correctly when
        /// the reference is added or removed.
        /// </summary>
        public bool CompiledPlanSupported { get; }
    }

    /// <summary>
    /// The kind of projection the generator reproduced for a Style B view, mirroring the shapes
    /// <c>ViewBuilder.ExtractProjectedFields</c> accepts. Drives how the emitter rebuilds the
    /// <c>Expression&lt;Func&lt;TSource, TRow&gt;&gt;</c> as C# source (task 4.1).
    /// </summary>
    internal enum ProjectionKind
    {
        /// <summary>Member-initialization projection: <c>x =&gt; new TRow { Member = x.Source, ... }</c>.</summary>
        MemberInit,

        /// <summary>Named-constructor projection: <c>x =&gt; new TRow(x.Source, ...)</c>.</summary>
        NamedCtor,
    }

    /// <summary>
    /// A value-equal description of a view's statically-reproducible projection: its
    /// <see cref="ProjectionKind"/> and the ordered set of target-member &rarr; source-expression
    /// bindings. The bindings carry node-reproducible C# expression text so the emitter (task 4.1) can
    /// re-emit the projection as source the consumer compiles — no runtime reflection. A record so it is
    /// value-equal and null-comparable on <see cref="ViewModel"/>.
    /// </summary>
    internal sealed record ProjectionModel
    {
        public ProjectionModel(ProjectionKind kind, EquatableArray<ProjectionBinding> bindings)
        {
            Kind = kind;
            Bindings = bindings;
        }

        /// <summary>Whether the projection is a member-initialization or a named-constructor shape.</summary>
        public ProjectionKind Kind { get; }

        /// <summary>
        /// The ordered projection bindings (target member &lt;- source expression text). Wrapped in
        /// <see cref="EquatableArray{T}"/> so the (order-sensitive) sequence participates in value
        /// equality, preserving the incremental cache.
        /// </summary>
        public EquatableArray<ProjectionBinding> Bindings { get; }
    }

    /// <summary>
    /// A single projection binding: the target member on <c>TRow</c> and the node-reproducible C# source
    /// expression text that fills it (read against the <c>TSource</c> lambda parameter). A record so it is
    /// value-equal and implements <see cref="IEquatable{T}"/>, satisfying the
    /// <see cref="EquatableArray{T}"/> element constraint.
    /// </summary>
    internal sealed record ProjectionBinding
    {
        public ProjectionBinding(string targetMember, string sourceExpressionText)
        {
            TargetMember = targetMember;
            SourceExpressionText = sourceExpressionText;
        }

        /// <summary>The target member name on <c>TRow</c> (member-init) or constructor-parameter order key.</summary>
        public string TargetMember { get; }

        /// <summary>The node-reproducible C# expression text that produces the member's value.</summary>
        public string SourceExpressionText { get; }
    }

    /// <summary>
    /// Per-field plan metadata for a projected field of <c>TQuery</c>: its name, fully-qualified CLR type,
    /// and the filter/sort/mask flags mirrored from the view's <c>FieldMetadata</c> (D95 defaults and any
    /// explicit overrides). A record so it is value-equal and implements <see cref="IEquatable{T}"/>,
    /// satisfying the <see cref="EquatableArray{T}"/> element constraint. The emitter (task 4.1) uses
    /// these to decide which fields get member-access expressions, sort appliers, and mask accessors.
    /// </summary>
    internal sealed record PlanFieldModel
    {
        public PlanFieldModel(
            string name,
            string clrTypeFqn,
            bool isFilterable,
            bool isSortable,
            bool isMaskable,
            bool hasWritableSetter)
        {
            Name = name;
            ClrTypeFqn = clrTypeFqn;
            IsFilterable = isFilterable;
            IsSortable = isSortable;
            IsMaskable = isMaskable;
            HasWritableSetter = hasWritableSetter;
        }

        /// <summary>Field name (the projected member on <c>TQuery</c>).</summary>
        public string Name { get; }

        /// <summary>Fully-qualified CLR type of the field.</summary>
        public string ClrTypeFqn { get; }

        /// <summary>Whether a client filter may target this field (mirrors <c>FieldMetadata</c>; D95).</summary>
        public bool IsFilterable { get; }

        /// <summary>Whether a client sort may target this field (mirrors <c>FieldMetadata</c>).</summary>
        public bool IsSortable { get; }

        /// <summary>Whether this field carries a <c>MaskField</c> declaration (drives mask accessors).</summary>
        public bool IsMaskable { get; }

        /// <summary>
        /// Whether the projected property exposes a settable (public, non-<c>init</c>) setter. Drives the
        /// generated <see cref="a2n.Vista.Metadata.MaskAccessor"/> mutator (task 4.1): a settable property
        /// gets a direct setter; an <c>init</c>-only / record property gets a <c>with</c>-style rebuild
        /// (open implementation call #3). Only meaningful when <see cref="IsMaskable"/> is <c>true</c>.
        /// </summary>
        public bool HasWritableSetter { get; }
    }

    /// <summary>
    /// A single projected property of <c>TQuery</c>: its name (the accessor key) and fully-qualified
    /// type name. A record so it is value-equal and implements <see cref="IEquatable{T}"/>, satisfying
    /// the <see cref="EquatableArray{T}"/> element constraint.
    /// </summary>
    internal sealed record PropertyModel
    {
        public PropertyModel(string name, string typeFqn)
        {
            Name = name;
            TypeFqn = typeFqn;
        }

        /// <summary>Property name (the accessor key, tasks 2.3/3.x).</summary>
        public string Name { get; }

        /// <summary>Fully-qualified property type name.</summary>
        public string TypeFqn { get; }
    }

    /// <summary>
    /// A small readonly value-type wrapper around <c>T[]</c> that provides structural, order-sensitive
    /// value equality. This is the standard Roslyn incremental-generator pattern: a plain array (or
    /// <c>ImmutableArray&lt;T&gt;</c>) uses reference equality by default, which would defeat the
    /// pipeline's caching and regenerate every view on any change (R1.3). Wrapping the array here keeps
    /// the model genuinely equatable without taking a dependency on System.Collections.Immutable.
    /// </summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
        where T : IEquatable<T>
    {
        private readonly T[] _array;

        public EquatableArray(T[] array)
        {
            _array = array;
        }

        /// <summary>Number of elements (0 when the underlying array is <c>null</c>).</summary>
        public int Count => _array?.Length ?? 0;

        public T this[int index] => _array[index];

        /// <summary>Order-sensitive structural equality over the elements.</summary>
        public bool Equals(EquatableArray<T> other)
        {
            var left = _array;
            var right = other._array;

            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null)
            {
                // One null/empty vs the other: equal only if both have no elements.
                return (left?.Length ?? 0) == 0 && (right?.Length ?? 0) == 0;
            }

            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
            => obj is EquatableArray<T> other && Equals(other);

        /// <summary>Order-sensitive hash that reflects every element.</summary>
        public override int GetHashCode()
        {
            if (_array is null)
            {
                return 0;
            }

            unchecked
            {
                var hash = 17;
                foreach (var item in _array)
                {
                    hash = (hash * 31) + (item?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            var array = _array ?? Array.Empty<T>();
            return ((IEnumerable<T>)array).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

        public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
    }
}
