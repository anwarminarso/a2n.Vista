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

            var source = BuildAccessorSource(model);
            context.AddSource(BuildHintName(model), SourceText.From(source, Encoding.UTF8));
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

            return new ViewModel(
                @namespace: symbol.ContainingNamespace?.IsGlobalNamespace == true
                    ? null
                    : symbol.ContainingNamespace?.ToDisplayString(),
                className: symbol.Name,
                isPartial: isPartial,
                hasPublicParameterlessCtor: hasPublicParameterlessCtor,
                tqueryFqn: tquery.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                properties: new EquatableArray<PropertyModel>(properties.ToArray()),
                location: LocationInfo.From(classDecl.Identifier));
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
            LocationInfo location)
        {
            Namespace = @namespace;
            ClassName = className;
            IsPartial = isPartial;
            HasPublicParameterlessCtor = hasPublicParameterlessCtor;
            TQueryFqn = tqueryFqn;
            Properties = properties;
            Location = location;
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
