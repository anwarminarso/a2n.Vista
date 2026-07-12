using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using a2n.Vista.Metadata;

namespace a2n.Vista.AspNetCore.Serialization;

/// <summary>
/// The System.Text.Json options Vista uses to read action-endpoint request bodies and write responses
/// (Decision Log D110/D124). Case-insensitive property matching, enum-as-string, and the polymorphic
/// <see cref="FilterNodeJsonConverter"/> are registered here so the HTTP layer can (de)serialize the
/// neutral request envelopes without depending on the host's global JSON configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>The serialization seam (D124).</b> <see cref="Options"/> installs a
/// <see cref="JsonSerializerOptions.TypeInfoResolverChain"/> so every Vista type resolves its
/// <see cref="JsonTypeInfo"/> through an ordered chain:
/// </para>
/// <list type="number">
///   <item><description>the shipped <see cref="VistaStaticJsonContext"/> (fixed request/response
///   envelopes + the polymorphic <c>FilterNode</c> tree), processed by the built-in System.Text.Json
///   source generator so those types (de)serialize AOT-clean;</description></item>
///   <item><description>the source-generated per-view contexts drained from the Core-resident
///   <see cref="GeneratedJsonContextStore"/> (D126), chained <b>ahead of</b> the developer contexts and
///   the reflection fallback so a covered view's DTOs resolve AOT-clean with no developer
///   <c>App_Json_Context</c> required;</description></item>
///   <item><description>any developer-authored <c>App_Json_Context</c>(s) registered through
///   <see cref="AddContext"/> (via <c>IVistaEndpointBuilder.AddVistaJsonContext</c>), inserted
///   <b>ahead of</b> the reflection fallback so covered view DTOs resolve AOT-clean;</description></item>
///   <item><description>the reflection fallback (<see cref="DefaultJsonTypeInfoResolver"/>) appended
///   last so any uncovered runtime type still (de)serializes — the only reflection (RUC) serialization
///   branch, removable via <see cref="DisableReflectionFallback"/>.</description></item>
/// </list>
/// <para>
/// The <see cref="JsonSerializerOptions"/> configuration (web defaults, case-insensitive matching, the
/// <see cref="JsonStringEnumConverter"/>, and the <see cref="FilterNodeJsonConverter"/>) is identical to
/// the pre-seam behavior, so the source-gen and reflection resolvers emit byte-for-byte identical JSON
/// for the same options — only the mechanism by which a <see cref="JsonTypeInfo"/> is resolved changes.
/// </para>
/// <para>
/// <b>Configuration timing.</b> <see cref="AddContext"/> and <see cref="DisableReflectionFallback"/>
/// mutate the resolver chain and must therefore run at the composition root (from
/// <c>AddVistaEndpoints(...)</c>), before the first (de)serialization freezes <see cref="Options"/>.
/// </para>
/// </remarks>
public static class VistaJson
{
    // The reflection fallback resolver: the single RUC serialization branch. Held by reference so it can
    // be located in the chain (to insert app contexts ahead of it) and removed on opt-out.
    private static readonly IJsonTypeInfoResolver ReflectionFallbackResolver = CreateReflectionFallbackResolver();

    /// <summary>The shared, seam-backed options instance used by the Vista endpoint handlers.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());

        // Seam chain (D124/D126): the shipped fixed-envelope context resolves first; the generated
        // per-view contexts (drained from the Core store) resolve next; developer contexts are inserted
        // by AddContext ahead of the reflection fallback, which is appended last.
        options.TypeInfoResolverChain.Add(VistaStaticJsonContext.Default);
        DrainGeneratedContexts(options.TypeInfoResolverChain);
        options.TypeInfoResolverChain.Add(ReflectionFallbackResolver);
        return options;
    }

    /// <summary>
    /// Drains the Core-resident <see cref="GeneratedJsonContextStore"/> and chains each generated
    /// per-view context into the seam <b>ahead of</b> both the developer <c>App_Json_Context</c>(s)
    /// (inserted later by <see cref="AddContext"/>) and the reflection fallback, but <b>after</b> the
    /// shipped <see cref="VistaStaticJsonContext"/> so the fixed envelopes keep precedence (D126).
    /// </summary>
    /// <param name="chain">The seam's resolver chain being assembled.</param>
    /// <remarks>
    /// Each stored handle is a serializer-neutral <see cref="object"/> that is, by the
    /// <see cref="GeneratedJsonContextStore.Register(string, object)"/> contract, always an
    /// <see cref="IJsonTypeInfoResolver"/> emitted into the view's own assembly. The single unchecked
    /// cast below is that contract boundary — the only place a stored handle is reinterpreted as a
    /// System.Text.Json resolver type, keeping <c>a2n.Vista.Core</c> free of any STJ dependency.
    /// </remarks>
    private static void DrainGeneratedContexts(IList<IJsonTypeInfoResolver> chain)
    {
        foreach (var handle in GeneratedJsonContextStore.All)
        {
            chain.Add((IJsonTypeInfoResolver)handle);
        }
    }

    /// <summary>
    /// Chains a developer-authored <c>App_Json_Context</c> into the serialization seam, inserting it
    /// <b>ahead of</b> the reflection fallback (and after the shipped envelope context) so the runtime
    /// types it covers resolve AOT-clean. Registration is idempotent: adding the same context instance
    /// twice is a no-op. When the reflection fallback has been disabled, the context is appended last.
    /// </summary>
    /// <param name="context">The developer-authored source-generated context to chain in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Must be called before <see cref="Options"/> is first used to (de)serialize; the underlying
    /// <see cref="JsonSerializerOptions"/> freezes its resolver chain on first use.
    /// </remarks>
    public static void AddContext(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IList<IJsonTypeInfoResolver> chain = Options.TypeInfoResolverChain;
        if (chain.Contains(context))
        {
            return;
        }

        var fallbackIndex = chain.IndexOf(ReflectionFallbackResolver);
        if (fallbackIndex < 0)
        {
            // The reflection fallback was opted out — append the context at the end of the chain.
            chain.Add(context);
        }
        else
        {
            chain.Insert(fallbackIndex, context);
        }
    }

    /// <summary>
    /// Removes the reflection fallback (<see cref="DefaultJsonTypeInfoResolver"/>) from the seam so the
    /// serialization path carries no RUC branch. After this call, a runtime type that no chained
    /// source-generated context covers cannot be (de)serialized (its <see cref="JsonTypeInfo"/> resolves
    /// to <see langword="null"/>). Idempotent.
    /// </summary>
    /// <remarks>
    /// Intended for fully AOT/trim-clean applications (and the AOT probe) whose views are all covered
    /// typed Style B with registered contexts. Must be called at the composition root, before the first
    /// (de)serialization.
    /// </remarks>
    public static void DisableReflectionFallback()
    {
        Options.TypeInfoResolverChain.Remove(ReflectionFallbackResolver);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification =
            "The DefaultJsonTypeInfoResolver is the deliberate, opt-out-able reflection fallback and the "
            + "only RUC serialization branch of the seam (D124/R5.5); AOT/trim-clean apps remove it via "
            + "DisableReflectionFallback() and the AOT probe excludes it from its chain.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Calling members annotated with RequiresDynamicCodeAttribute may break functionality when AOT compiling",
        Justification =
            "The DefaultJsonTypeInfoResolver is the deliberate, opt-out-able reflection fallback and the "
            + "only RUC serialization branch of the seam (D124/R5.5); AOT/trim-clean apps remove it via "
            + "DisableReflectionFallback() and the AOT probe excludes it from its chain.")]
    private static IJsonTypeInfoResolver CreateReflectionFallbackResolver() => new DefaultJsonTypeInfoResolver();
}
