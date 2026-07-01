// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Central holder for the source generator's DiagnosticDescriptors.
//
// Spec 03 D81 (R5.2) requires every diagnostic to use the "VISTA####" id prefix, the
// "a2n.Vista.SourceGenerators" category, and a help link URI. Keeping the descriptors in one place
// makes that contract easy to audit and reuse across the generator.

using Microsoft.CodeAnalysis;

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// Diagnostic descriptors emitted by the Vista source generators. Every descriptor follows the
    /// Spec 03 D81 contract: a <c>VISTA####</c> id, the <see cref="Category"/> category, and a
    /// help-link URI under the project docs.
    /// </summary>
    internal static class DiagnosticDescriptors
    {
        /// <summary>Shared diagnostic category (Spec 03 D81, R5.2).</summary>
        public const string Category = "a2n.Vista.SourceGenerators";

        /// <summary>
        /// Base docs URL for diagnostic help links. Each diagnostic appends its id, e.g.
        /// <c>.../docs/diagnostics/VISTA0001.md</c>. The repo URL mirrors
        /// <c>Directory.Build.props</c> (RepositoryUrl).
        /// </summary>
        private const string HelpLinkBase =
            "https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/";

        /// <summary>
        /// VISTA0001 (error): a typed Style B view must be declared <c>partial</c> so the generator can
        /// emit its companion accessor/registration code. A non-partial view is reported and skipped —
        /// no code is emitted for it (R5.1, Spec 03 D73).
        /// </summary>
        public static readonly DiagnosticDescriptor ViewMustBePartial = new DiagnosticDescriptor(
            id: "VISTA0001",
            title: "Style B view must be partial",
            messageFormat: "View '{0}' must be declared 'partial' for the Vista source generator to emit its accessors; the view is skipped",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Typed Style B views (deriving from a2n.Vista.Authoring.View<TQuery> or View<TQuery, TCrud>) must be 'partial' so the source generator can emit a companion type. Add the 'partial' modifier to the view class.",
            helpLinkUri: HelpLinkBase + "VISTA0001.md");

        /// <summary>
        /// VISTA0002 (info): a typed Style B view has no public parameterless constructor, so the
        /// generated <c>[ModuleInitializer]</c> cannot instantiate it to read its runtime <c>Name</c>
        /// and key the accessor registry. The view is skipped (no accessor map / registration emitted)
        /// rather than producing code that would not compile. This is a Phase 1 limitation — the
        /// module-initializer registration path requires a public parameterless ctor (design.md, R3.2).
        /// </summary>
        public static readonly DiagnosticDescriptor ViewMissingParameterlessCtor = new DiagnosticDescriptor(
            id: "VISTA0002",
            title: "Style B view needs a public parameterless constructor for accessor registration",
            messageFormat: "View '{0}' has no public parameterless constructor; the Vista source generator cannot register its accessors at module load and the view is skipped",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "The generated module initializer instantiates the view via its public parameterless constructor to read its runtime Name and key the accessor registry. A view without such a constructor cannot be registered this way in Phase 1 and is skipped. Add a public parameterless constructor to the view class to enable generated accessors.",
            helpLinkUri: HelpLinkBase + "VISTA0002.md");

        /// <summary>
        /// VISTA0003 (warning): the Vista source generator found a typed Style B view whose
        /// <c>From&lt;TSource&gt;(...)</c> projection cannot be statically reproduced (e.g. a
        /// non-member-initialization / non-named-constructor shape, or a binding that is not a simple
        /// member selection). No execution plan is emitted for the view — it stays metadata-only — and
        /// the generator continues with the remaining views. No compilation error is raised
        /// (Phase 2 M10, R1.6, R9.1, R9.2; Spec 03 §13).
        /// </summary>
        public static readonly DiagnosticDescriptor ProjectionNotStaticallyAnalyzable = new DiagnosticDescriptor(
            id: "VISTA0003",
            title: "Style B view projection cannot be analyzed statically",
            messageFormat: "View '{0}' has a projection the Vista source generator cannot reproduce statically{1}; no execution plan is generated and the view remains metadata-only",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The source generator reproduces a Style B view's From<TSource>(...) projection as compile-time source so the view becomes executable (List/Detail) on an AOT-clean path. It supports member-initialization (new TRow { Member = src.X, ... }) and named-constructor (new TRow(src.X, ...)) shapes with simple member selections. A projection it cannot reproduce is skipped: the view stays metadata-only and runs through the runtime-expression fallback. Simplify the projection to a member-init or named-constructor shape to get the generated executable plan.",
            helpLinkUri: HelpLinkBase + "VISTA0003.md");

        /// <summary>
        /// VISTA0020 (error): the Vista source generator can statically prove a typed Style B executable
        /// view is keyless — it declares no key (<c>.Key(...)</c> / a projected field's
        /// <c>.PrimaryKey()</c>) and projects from more than one source entity, so no single-source EF
        /// model primary key can be derived per D105. A keyless view cannot satisfy deterministic paging
        /// or Detail-by-key, so this is reported as an error. The runtime startup hook is the backstop
        /// for the single-source case that can only be decided against <c>DbContext.Model</c>
        /// (Phase 2 M10/M11, R6.4, R6.6, R9.3; Spec 03 D80, promoted to error for the provable case).
        /// </summary>
        public static readonly DiagnosticDescriptor ExecutableViewHasNoKey = new DiagnosticDescriptor(
            id: "VISTA0020",
            title: "Style B executable view has no derivable key",
            messageFormat: "Executable view '{0}' declares no key and projects from more than one source entity, so no key can be derived; declare a key with .Key(...) or a projected field's .PrimaryKey()",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "An executable Style B view needs a key for deterministic paging tiebreakers and Detail-by-key. A view that declares no key and projects from more than one source entity is provably keyless at compile time: single-source primary-key auto-derivation (D105) does not apply to multi-source views. Declare an explicit key via .Key(...) or mark a projected field with .PrimaryKey(). A single-source view with no declared key is validated at startup against the EF model instead.",
            helpLinkUri: HelpLinkBase + "VISTA0020.md");
    }
}
