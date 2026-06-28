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
    }
}
