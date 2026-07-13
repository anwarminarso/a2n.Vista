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

        /// <summary>
        /// VISTA0030 (error): a typed Style B writable view's CRUD facet declares zero
        /// <c>MapWritable</c> mappings, so the generated write mapper would assign nothing. Because
        /// mass assignment is default-deny (D25/D95), an empty whitelist is almost certainly an
        /// authoring mistake rather than an intentional no-op; the compilation fails and no write mapper
        /// is emitted for the view (Phase 2 M9, R9.1, R9.5, D121/D122). Promotes the interim startup
        /// fail-fast guard in <c>ViewBuilderOfTCrud.ValidateWriteFacet</c> to a build-time diagnostic.
        /// </summary>
        public static readonly DiagnosticDescriptor WriteFacetHasNoMappings = new DiagnosticDescriptor(
            id: "VISTA0030",
            title: "Writable view declares no MapWritable mappings",
            messageFormat: "CRUD facet of view '{0}' declares zero MapWritable mappings; no write mapper is generated and the compilation fails",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Mass assignment in Vista is default-deny (D25/D95): a writable Style B view only assigns members explicitly whitelisted through MapWritable. A CRUD facet with zero MapWritable mappings would produce a write mapper that assigns nothing, which is almost always an authoring mistake. Add the intended MapWritable mappings, or remove the CRUD facet if the view is not writable.",
            helpLinkUri: HelpLinkBase + "VISTA0030.md");

        /// <summary>
        /// VISTA0031 (error): a <c>MapWritable</c> mapping targets a member that is not a
        /// <c>Scalar_Member</c> — a navigation (reference or collection) rather than a simple scalar.
        /// The generated mapper only performs direct scalar member assignments, and assigning a
        /// navigation would reopen the mass-assignment hole the whitelist exists to close (D25/D95). One
        /// diagnostic is reported per offending mapping; the compilation fails and no write mapper is
        /// emitted for the view (Phase 2 M9, R9.2, R9.5, D121/D122).
        /// </summary>
        public static readonly DiagnosticDescriptor WriteTargetNotScalar = new DiagnosticDescriptor(
            id: "VISTA0031",
            title: "MapWritable target is not a scalar member",
            messageFormat: "View '{0}' maps '{1}' to target '{2}', which is a navigation rather than a scalar member; MapWritable targets must be scalar members",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The generated write mapper performs only direct scalar member assignments. A MapWritable mapping whose target is a navigation (a reference or collection property) cannot be assigned safely and would reopen the mass-assignment surface the whitelist exists to close (D25/D95). Map the navigation's scalar foreign-key member instead, or remove the mapping.",
            helpLinkUri: HelpLinkBase + "VISTA0031.md");

        /// <summary>
        /// VISTA0032 (error): a <c>MapWritable</c> mapping targets a declared key member (from
        /// <c>.Key(...)</c> / <c>.PrimaryKey()</c>) or the concurrency token (from
        /// <c>WithConcurrencyToken</c>). Keys and the concurrency token are managed by the executor and
        /// EF; letting a client-supplied model overwrite them would break identity or optimistic
        /// concurrency (D25/D95). One diagnostic is reported per offending member; the compilation fails
        /// and no write mapper is emitted for the view (Phase 2 M9, R9.3, R9.5, D121/D122).
        /// </summary>
        public static readonly DiagnosticDescriptor WriteTargetIsKeyOrToken = new DiagnosticDescriptor(
            id: "VISTA0032",
            title: "MapWritable target is a key or the concurrency token",
            messageFormat: "View '{0}' maps to member '{1}', which is a declared key or the concurrency token; MapWritable must not target key or concurrency-token members",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Key members and the concurrency token are managed by the executor and EF, not by client-supplied write models. Allowing MapWritable to assign a declared key (from .Key(...) / .PrimaryKey()) or the concurrency token (from WithConcurrencyToken) would break row identity or optimistic concurrency (D25/D95). Remove the mapping that targets the key or token member.",
            helpLinkUri: HelpLinkBase + "VISTA0032.md");

        /// <summary>
        /// VISTA0033 (warning): a writable Style B view's <c>MapWritable</c> chain cannot be reproduced
        /// statically by the source generator (for example a selector that is not a simple member
        /// selection, or a chain shape the analyzer does not recognize). No write mapper is emitted for
        /// the view; the view stays functional and the write path falls back to the reflection-based
        /// mapper. No compilation error is raised and the generator continues with the remaining views
        /// (Phase 2 M9, R8.1–R8.4, D121). When the offending expression can be determined, it is
        /// included in the warning message.
        /// </summary>
        public static readonly DiagnosticDescriptor WriteChainNotStaticallyAnalyzable = new DiagnosticDescriptor(
            id: "VISTA0033",
            title: "Writable view MapWritable chain cannot be analyzed statically",
            messageFormat: "View '{0}' has a MapWritable chain the Vista source generator cannot analyze statically{1}; no write mapper is generated and the view falls back to reflection",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "To emit a reflection-free write mapper the source generator reproduces a view's MapWritable chain as compile-time source. It supports chains of simple member-selection mappings. A chain it cannot reproduce (a selector that is not a simple member selection, or an unrecognized chain shape) is skipped: no write mapper is generated, the view remains functional, and its write path uses the runtime reflection-based mapper. Simplify the MapWritable selectors to simple member selections to get the generated write mapper. Severity is Warning because the view still works via the fallback.",
            helpLinkUri: HelpLinkBase + "VISTA0033.md");

        /// <summary>
        /// VISTA0040 (info): a Style B view is recognized as an HTTP-surface base candidate but cannot
        /// receive a generated dispatch invoker because its projected row type (<c>TQuery</c>) — or a
        /// writable view's write model (<c>TCrud</c>) — is anonymous or <c>object</c> rather than a
        /// named type. No invoker is emitted for the view; it stays fully functional on the reflection
        /// dispatch fallback and only the AOT-clean HTTP surface is missed. The build succeeds. Severity
        /// is Info because an uncovered view is a valid, working view (M9 HTTP-surface phase, R1.1, R1.3,
        /// R9.1, R9.4, D123).
        /// </summary>
        public static readonly DiagnosticDescriptor HttpSurfaceCandidateUncovered = new DiagnosticDescriptor(
            id: "VISTA0040",
            title: "Style B view cannot receive a generated HTTP dispatch invoker",
            messageFormat: "View '{0}' has an anonymous or 'object' row type (or write model) and cannot receive a generated HTTP dispatch invoker; it falls back to reflection dispatch and only the AOT-clean HTTP surface is missed",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "The Vista source generator emits a reflection-free HTTP dispatch invoker for a typed Style B view whose projected row type (TQuery) is a named type (and, when writable, whose TCrud is a named type). A view whose TQuery — or writable TCrud — is anonymous or 'object' cannot be dispatched without reflection, so no invoker is generated: the view stays fully functional through the reflection dispatch fallback and only the AOT-clean HTTP path is missed. Give the view a named row (and write) type to receive the generated invoker. Severity is Info because the view still works via the fallback.",
            helpLinkUri: HelpLinkBase + "VISTA0040.md");

        /// <summary>
        /// VISTA0041 (info): serialization guidance for a covered typed Style B view. Because a source
        /// generator cannot feed the built-in System.Text.Json generator, Vista cannot auto-generate a
        /// working per-view serialization context; instead the developer authors a
        /// <c>JsonSerializerContext</c> and registers it via <c>AddVistaJsonContext(...)</c> to make the
        /// view's HTTP (de)serialization AOT-clean. This diagnostic names the exact
        /// <c>[JsonSerializable]</c> types to include for the view (<c>TRow</c>,
        /// <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>, and — when writable —
        /// <c>TCrud</c>) so authoring the context is mechanical. The build succeeds whether or not a
        /// context is supplied; until one is registered the view (de)serializes through the reflection
        /// fallback resolver (M9 HTTP-surface phase, R5.4, R9.2, R9.4, D124).
        /// </summary>
        public static readonly DiagnosticDescriptor HttpSurfaceSerializationGuidance = new DiagnosticDescriptor(
            id: "VISTA0041",
            title: "Serialization guidance for a covered Style B view",
            messageFormat: "For AOT-clean serialization of view '{0}', include these types via [JsonSerializable] in an App_Json_Context registered with AddVistaJsonContext(...): {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "A Roslyn source generator cannot consume the output of another source generator, so Vista cannot auto-generate a working System.Text.Json serialization context for a view. To make a covered typed Style B view's HTTP (de)serialization AOT-clean, author a JsonSerializerContext listing the view's DTOs via [JsonSerializable] and register it with AddVistaJsonContext(...). This diagnostic names the exact types to include: TRow, ViewListResult<TRow>, PagedResult<TRow>, and — for a writable view — TCrud. The build succeeds regardless; until a context is registered the view (de)serializes through the reflection fallback resolver. Severity is Info because it is guidance, not an error.",
            helpLinkUri: HelpLinkBase + "VISTA0041.md");

        /// <summary>
        /// VISTA0050 (info): a typed Style B view is <c>covered</c> for per-view serialization — the
        /// generator emits a reflection-free <c>IJsonTypeInfoResolver</c> (built via
        /// <c>JsonMetadataServices</c>, not <c>[JsonSerializable]</c>) that provides the
        /// <c>JsonTypeInfo</c> for the view's Serializable_DTO_Set (<c>TRow</c>,
        /// <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>, and — when writable —
        /// <c>TCrud</c>), auto-chained into the serialization seam ahead of the reflection fallback. This
        /// diagnostic names the view and the exact DTO set now served by the generated context so the
        /// developer knows the <c>App_Json_Context</c> entry for that view is optional. The build
        /// succeeds. Severity is Info because it is confirmation, not an error (M9 JsonTypeInfo phase,
        /// R9.1, R9.3, R9.4, D125/D126).
        /// </summary>
        public static readonly DiagnosticDescriptor GeneratedJsonContextForView = new DiagnosticDescriptor(
            id: "VISTA0050",
            title: "Per-view JsonTypeInfo generated for a covered Style B view",
            messageFormat: "View '{0}' is covered by a generated per-view JsonTypeInfo; its App_Json_Context entry is now optional for these types: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "The Vista source generator emits a reflection-free per-view serialization context (an IJsonTypeInfoResolver built via System.Text.Json.Serialization.Metadata.JsonMetadataServices, not the [JsonSerializable] attribute route) for a covered typed Style B view whose DTO shapes are all emittable. It provides the JsonTypeInfo for TRow, ViewListResult<TRow>, PagedResult<TRow>, and — for a writable view — TCrud, and is auto-chained into the serialization seam ahead of the reflection fallback. This diagnostic names the DTO set now served by the generated context, so a developer App_Json_Context entry for that view is optional. Severity is Info because it is confirmation, not an error.",
            helpLinkUri: HelpLinkBase + "VISTA0050.md");

        /// <summary>
        /// VISTA0051 (warning): a candidate typed Style B view has a DTO member whose shape the generator
        /// cannot emit reflection-free via <c>JsonMetadataServices</c> (for example a member requiring a
        /// bespoke/custom converter, an unsupported polymorphic shape, or an unresolved generic). The
        /// view is <c>not covered</c> for serialization generation: no per-view context is emitted and
        /// the view falls back to the developer <c>App_Json_Context</c> / reflection resolver. The view
        /// stays fully functional — only the AOT-clean auto-generation is missed — and the build
        /// succeeds. Severity is Warning (never Error) because the view is valid and working on the
        /// fallback; only per-view serialization auto-generation is missed (M9 JsonTypeInfo phase, R1.5,
        /// R9.2, R9.4, D125/D126).
        /// </summary>
        public static readonly DiagnosticDescriptor JsonContextMemberNotEmittable = new DiagnosticDescriptor(
            id: "VISTA0051",
            title: "Style B view DTO member cannot be emitted reflection-free",
            messageFormat: "View '{0}' has a DTO member the Vista source generator cannot emit reflection-free ({1}); no per-view JsonTypeInfo is generated and the view falls back to the developer App_Json_Context / reflection",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "To emit a reflection-free per-view serialization context the generator must build every DTO member's JsonTypeInfo via System.Text.Json.Serialization.Metadata.JsonMetadataServices. A DTO with a member the analyzer cannot fully resolve reflection-free (a member requiring a bespoke/custom converter, an unsupported polymorphic shape, or an unresolved generic) is not covered: correctness (byte-for-byte parity with the reflection oracle) beats coverage, so no best-effort context that could drift from the wire is emitted. The view stays fully functional and (de)serializes through the developer App_Json_Context / reflection fallback resolver; only the AOT-clean per-view serialization is missed. Simplify the offending DTO member to an emittable shape to receive the generated context. Severity is Warning because the view still works via the fallback.",
            helpLinkUri: HelpLinkBase + "VISTA0051.md");

        // ------------------------------------------------------------------------------------------------
        // Style A coverage family (VISTA0060–VISTA0063) — M9 Style A coverage, Decision Log D129/D130.
        //
        // These surface, non-blockingly, exactly where the permanent by-design RUC boundary lands for each
        // Style A (ViewTemplate<TDbContext> + AddView<TRow>(...)) view. An uncovered Style A view is a
        // valid, working view served by the reflection fallback — only the AOT-clean auto-generation is
        // missed — so every diagnostic in this family is Info or Warning, never Error (R8.5).
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// VISTA0060 (info): a Style A view (an <c>a2n.Vista.Authoring.ViewTemplate&lt;TDbContext&gt;</c>
        /// <c>AddView&lt;TRow&gt;(name, projection)</c> call site) is <c>covered</c> for shape-driven
        /// generation. The generator emits one or more reflection-free artifacts for it, and this
        /// diagnostic names the exact set produced: export accessors (when <c>TRow</c> is a named type),
        /// read-DTO <c>JsonTypeInfo</c> (when <c>TRow</c> is named and its DTOs are all emittable), and/or
        /// write-model <c>TCrud</c> <c>JsonTypeInfo</c> (when the view is writable via
        /// <c>WithCrud&lt;TCrud, TEntity&gt;()</c> and <c>TCrud</c> is emittable). Anything not listed
        /// stays on the reflection path by design. The build succeeds. Severity is Info because it is
        /// confirmation, not an error (M9 Style A coverage, R8.1, R8.5, D129/D130).
        /// </summary>
        public static readonly DiagnosticDescriptor StyleAViewCovered = new DiagnosticDescriptor(
            id: "VISTA0060",
            title: "Style A view covered by generated shape-driven artifacts",
            messageFormat: "Style A view '{0}' is covered by generated artifacts: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "The Vista source generator recognizes Style A views (ViewTemplate<TDbContext>.AddView<TRow>(name, projection) call sites) and, for the nameable subset, emits the same shape-driven artifacts it emits for typed Style B — export accessors, read-DTO JsonTypeInfo, and write-model TCrud JsonTypeInfo — registered into the existing Core stores. This diagnostic confirms a view is covered and names the exact artifact set generated for it: export accessors (a named TRow), read-DTO JsonTypeInfo (a named TRow whose DTOs are all emittable), and/or TCrud JsonTypeInfo (a writable view whose TCrud is emittable). Anything not listed stays on the reflection path by design. Severity is Info because it is confirmation, not an error.",
            helpLinkUri: HelpLinkBase + "VISTA0060.md");

        /// <summary>
        /// VISTA0061 (info): a Style A view has an <c>Anonymous_Type</c> / <c>object</c> read row
        /// (<c>TRow</c>), so its read serialization and export stay on the reflection path — permanently
        /// <c>[RequiresUnreferencedCode]</c> by design (D96/D130). A C# anonymous type has no
        /// source-writable name (its metadata name is not valid C# and is not stable across assemblies),
        /// so the generator cannot emit an export accessor, a member-access expression, or a
        /// <c>JsonTypeInfo</c> for it. This is not a fixable warning: it is the deliberate, permanent AOT
        /// asymmetry of Style A. The view's write model (<c>TCrud</c>, always a named type) is unaffected
        /// and may still be covered. The build succeeds. Severity is Info because the view is valid and
        /// working; only AOT-clean read auto-generation is unavailable by design (M9 Style A coverage,
        /// R1.4, R8.2, R8.5, D96/D130).
        /// </summary>
        public static readonly DiagnosticDescriptor StyleAAnonymousRowStaysReflection = new DiagnosticDescriptor(
            id: "VISTA0061",
            title: "Style A anonymous read row stays on the reflection path (RUC by design)",
            messageFormat: "Style A view '{0}' has an anonymous or 'object' read row type; its read serialization and export stay on the reflection path (RequiresUnreferencedCode) by design (D96) because an anonymous row type cannot be named in generated source",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "Style A projections are typically anonymous. A C# anonymous type has no source-writable name (its metadata name is not valid C# and is not stable across assemblies), so the generator cannot emit an export accessor, a member-access expression, or a JsonTypeInfo for an anonymous row type. A Style A view whose read row TRow is anonymous or 'object' therefore keeps its read serialization and export on the reflection path — permanently RequiresUnreferencedCode by design (D96/D130). This is not something to fix; it is the deliberate, permanent AOT asymmetry of Style A. The write model of a writable view (TCrud, always a named type) is unaffected and can still be covered. Project into a named row type (a DTO/record) if you want the read-side artifacts generated. Severity is Info because the view is valid and working; only AOT-clean read auto-generation is unavailable by design.",
            helpLinkUri: HelpLinkBase + "VISTA0061.md");

        /// <summary>
        /// VISTA0062 (info): a Style A <c>AddView</c> <c>name</c> argument is not a compile-time constant
        /// string, so the generator cannot key any artifact statically for that call site (the runtime
        /// name is unknowable at compile time, and a wrong key would silently miss). No artifact is
        /// emitted for the call site and the view stays on the reflection path. Use a compile-time
        /// constant string literal (or <c>const</c>) for the <c>AddView</c> name to make the view
        /// eligible for generated artifacts. The build succeeds. Severity is Info because the view is
        /// valid and working via the reflection fallback (M9 Style A coverage, R1.2, R8.3, R8.5, D129).
        /// </summary>
        public static readonly DiagnosticDescriptor StyleANonConstantViewName = new DiagnosticDescriptor(
            id: "VISTA0062",
            title: "Style A AddView name is not a compile-time constant",
            messageFormat: "An AddView call site in template '{0}' uses a name that is not a compile-time constant; generated artifacts cannot be keyed statically, so the view stays on the reflection path",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "The generator keys every Style A artifact statically by the AddView view name. When the name argument is not a compile-time constant string, there is no stable key to register the artifact under — the runtime name is unknowable at compile time, so a wrong key would silently miss — so the generator emits nothing for that call site and the view stays on the reflection path. Use a compile-time constant string literal (or a const) for the AddView name to make the view eligible for generated artifacts. Severity is Info because the view is valid and working via the reflection fallback.",
            helpLinkUri: HelpLinkBase + "VISTA0062.md");

        /// <summary>
        /// VISTA0063 (warning): a covered candidate Style A DTO (a named <c>TRow</c> for the read side, or
        /// a <c>TCrud</c> for the write side) has a member whose shape the generator cannot emit
        /// reflection-free via <c>JsonMetadataServices</c> (for example a member requiring a bespoke/custom
        /// converter, an unsupported polymorphic shape, or an unresolved generic). Because correctness
        /// (byte-for-byte parity with the reflection oracle) beats coverage, no best-effort
        /// <c>JsonTypeInfo</c> is emitted for that DTO; the view falls back to the developer
        /// <c>App_Json_Context</c> / reflection for it (a named-<c>TRow</c> view still receives its export
        /// accessor map). The build succeeds. Severity is Warning (never Error) — matching
        /// <see cref="JsonContextMemberNotEmittable"/> (VISTA0051), the typed Style B counterpart — because
        /// the view still works via the fallback and only the AOT-clean per-DTO serialization is missed
        /// (M9 Style A coverage, R1.7, R8.4, R8.5, D129/D130).
        /// </summary>
        public static readonly DiagnosticDescriptor StyleADtoMemberNotEmittable = new DiagnosticDescriptor(
            id: "VISTA0063",
            title: "Style A DTO member cannot be emitted reflection-free",
            messageFormat: "Style A view '{0}' has a DTO member the Vista source generator cannot emit reflection-free ({1}); no JsonTypeInfo is generated for that DTO and it falls back to the developer App_Json_Context / reflection",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "To emit a reflection-free JsonTypeInfo the generator must build every DTO member's metadata via System.Text.Json.Serialization.Metadata.JsonMetadataServices. A candidate Style A DTO (a named TRow, or a TCrud) with a member the analyzer cannot fully resolve reflection-free (a member requiring a bespoke/custom converter, an unsupported polymorphic shape, or an unresolved generic) is not covered for serialization: correctness (byte-for-byte parity with the reflection oracle) beats coverage, so no best-effort context that could drift from the wire is emitted for that DTO. The view stays fully functional and (de)serializes that DTO through the developer App_Json_Context / reflection fallback; a named-TRow view still receives its export accessor map. Simplify the offending member to an emittable shape to receive the generated context. Severity is Warning because the view still works via the fallback, matching VISTA0051 (the typed Style B counterpart).",
            helpLinkUri: HelpLinkBase + "VISTA0063.md");
    }
}
