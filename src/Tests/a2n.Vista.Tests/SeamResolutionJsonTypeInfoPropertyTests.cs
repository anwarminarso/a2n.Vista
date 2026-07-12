// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the serialization seam's resolution of source-generated per-view
/// <c>JsonTypeInfo</c> and the resulting optionality of the developer <c>App_Json_Context</c>
/// (spec source-generator-json-typeinfo, task 6.2; Decision Log D126; Requirements 5.1, 5.2, 5.3, 10.2).
/// <para>
/// A <c>Generated_View_Context</c> — a reflection-free
/// <see cref="IJsonTypeInfoResolver"/> built by hand via
/// <see cref="System.Text.Json.Serialization.Metadata.JsonMetadataServices"/> exactly as the
/// <c>ViewJsonContextGenerator</c> emits (no <c>[JsonSerializable]</c> attribute route) — covers one
/// view's Serializable_DTO_Set (<c>TRow</c>, <c>ViewListResult&lt;TRow&gt;</c>,
/// <c>PagedResult&lt;TRow&gt;</c>, and the writable <c>TCrud</c>). Registered into the Core-resident
/// <see cref="GeneratedJsonContextStore"/> and drained into the seam ahead of the reflection fallback,
/// this property proves that:
/// </para>
/// <list type="number">
///   <item><description>for any runtime type in the covered Serializable_DTO_Set, the seam resolves the
///   type's <see cref="JsonTypeInfo"/> from the drained <c>Generated_View_Context</c> — never from the
///   reflection fallback — <b>whether or not</b> a developer <c>App_Json_Context</c> is also registered
///   (R5.1, R5.2);</description></item>
///   <item><description>when both a generated context and a developer <c>App_Json_Context</c> cover the
///   same type, resolution is deterministic by the defined chain order and the JSON produced is
///   byte-for-byte identical whichever resolver wins — and identical to the reflection oracle (R5.3,
///   R10.2).</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared-static isolation.</b> <see cref="VistaJson.Options"/> is a process-wide static whose
/// resolver chain freezes on first use, so mutating it (or relying on the exact set of contexts the real
/// seam drained at first use) would be fragile and order-dependent across the whole test process. Each
/// case therefore builds a <b>fresh</b> <see cref="JsonSerializerOptions"/> that mirrors the seam chain
/// exactly — the same construction <see cref="VistaJson"/> performs (web defaults, case-insensitive
/// matching, the enum + <c>FilterNodeJsonConverter</c> converters) and the same order
/// (<c>Static_Envelope_Context</c> → generated contexts drained from
/// <see cref="GeneratedJsonContextStore"/> → optional developer <c>App_Json_Context</c> → reflection
/// fallback) — and drains the store through the very same opaque-handle → <see cref="IJsonTypeInfoResolver"/>
/// cast the AspNetCore seam performs, exercising the drain contract (R5.1).
/// </para>
/// <para>
/// <b>Winner observation.</b> The winning resolver for a top-level type is found by walking the mirrored
/// chain slot-by-slot and taking the first slot that returns a non-null <see cref="JsonTypeInfo"/> — the
/// exact rule the combined resolver applies — so no shared mutable recorder is disturbed by the lazy,
/// nested member-type resolution the combined resolver would trigger.
/// </para>
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The oracle drives the reflection resolver by design; trimming is not used for tests.")]
[SuppressMessage(
    "AOT",
    "IL3050:Calling members annotated with RequiresDynamicCodeAttribute may break functionality when AOT compiling",
    Justification = "The oracle drives the reflection resolver by design; AOT is not used for tests.")]
public sealed class SeamResolutionJsonTypeInfoPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    /// <summary>The role of a resolver slot in the mirrored seam chain.</summary>
    private enum ResolverKind
    {
        /// <summary>No slot covered the type.</summary>
        None,

        /// <summary>The shipped <see cref="VistaStaticJsonContext"/> (fixed envelopes), always first.</summary>
        Static,

        /// <summary>A source-generated per-view context drained from <see cref="GeneratedJsonContextStore"/>.</summary>
        Generated,

        /// <summary>A developer-authored <c>App_Json_Context</c> chained by <c>AddVistaJsonContext</c>.</summary>
        Developer,

        /// <summary>The reflection fallback (<see cref="DefaultJsonTypeInfoResolver"/>), always last.</summary>
        Reflection,
    }

    // The unique view name this test's Generated_View_Context is registered under in the process-wide
    // store. Guid-suffixed so it never collides with a sibling test or a real module-initializer
    // registration, and so the first-wins store keeps THIS context (R5.1 drain isolation).
    private static readonly string ViewName = "seam-json-typeinfo-prop3-" + Guid.NewGuid().ToString("N");

    // The reflection oracle resolver (RUC). Held once; trimming/AOT are not used for tests.
    private static readonly IJsonTypeInfoResolver ReflectionResolver = new DefaultJsonTypeInfoResolver();

    static SeamResolutionJsonTypeInfoPropertyTests()
    {
        // Register THIS test's Generated_View_Context into the Core-resident store exactly as a generated
        // [ModuleInitializer] would, so the mirrored-seam drain (GeneratedJsonContextStore.All → cast to
        // IJsonTypeInfoResolver) picks it up (R5.1). First-wins + a Guid-unique name keep it isolated.
        GeneratedJsonContextStore.Register(ViewName, new GeneratedViewContext());
    }

    // The four runtime types of the covered view's Serializable_DTO_Set that the property probes.
    private static readonly Type[] CoveredTypes =
    {
        typeof(SeamGenRow),
        typeof(ViewListResult<SeamGenRow>),
        typeof(PagedResult<SeamGenRow>),
        typeof(SeamGenCrud),
    };

    // Feature: source-generator-json-typeinfo, Property 3: The seam resolves covered per-view DTOs from
    // the generated context, making the developer context optional.
    //
    // Validates: Requirements 5.1, 5.2, 5.3, 10.2
    [Test]
    public void Seam_Resolves_Covered_DTOs_From_Generated_Context_Making_Developer_Context_Optional()
    {
        // Feature: source-generator-json-typeinfo, Property 3: The seam resolves covered per-view DTOs
        // from the generated context, making the developer context optional.
        var genCase =
            from row in GenRow
            from crud in GenCrud
            from listResult in GenListResult
            select (row, crud, listResult);

        genCase.Sample(
            tuple =>
            {
                var (row, crud, listResult) = tuple;
                var paged = listResult.Page;

                // R5.1/R5.2: every covered type resolves from the drained Generated_View_Context (never
                // the reflection fallback), whether or not a developer App_Json_Context is also present.
                foreach (var type in CoveredTypes)
                {
                    foreach (var developerPresent in new[] { false, true })
                    {
                        var winner = WinningKind(type, developerPresent: developerPresent, developerFirst: false);
                        if (winner == ResolverKind.None)
                        {
                            throw new Exception(
                                $"Type '{type}' resolved to no JsonTypeInfo in the seam " +
                                $"(developerPresent={developerPresent}); a covered DTO must resolve.");
                        }

                        if (winner != ResolverKind.Generated)
                        {
                            throw new Exception(
                                $"Type '{type}' was served from the {winner} resolver " +
                                $"(developerPresent={developerPresent}); a covered per-view DTO must resolve " +
                                "from the drained Generated_View_Context — never the reflection fallback or " +
                                "a developer context — so the developer App_Json_Context is optional " +
                                "(R5.1, R5.2).");
                        }
                    }
                }

                // R5.3/R10.2: byte-for-byte parity across every seam configuration and the oracle. The
                // JSON is identical whether the developer context is absent, present-and-behind (generated
                // wins), or present-and-ahead (developer wins) — all equal to the reflection oracle.
                AssertByteForByteParity(row);
                AssertByteForByteParity(crud);
                AssertByteForByteParity(listResult);
                AssertByteForByteParity(paged);
            },
            iter: Iterations);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> through the reflection oracle and through three seam
    /// configurations — developer context absent; developer context present behind the generated context
    /// (generated wins); developer context present ahead of the generated context (developer wins) — and
    /// asserts all four JSON strings are byte-for-byte identical (R5.3, R10.2).
    /// </summary>
    private static void AssertByteForByteParity<T>(T value)
    {
        var oracle = JsonSerializer.Serialize(value, BuildOracleOptions());

        var generatedNoDeveloper =
            JsonSerializer.Serialize(value, BuildSeamOptions(developerPresent: false, developerFirst: false));
        var generatedWinsWithDeveloper =
            JsonSerializer.Serialize(value, BuildSeamOptions(developerPresent: true, developerFirst: false));
        var developerWins =
            JsonSerializer.Serialize(value, BuildSeamOptions(developerPresent: true, developerFirst: true));

        if (!string.Equals(generatedNoDeveloper, oracle, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Serializing '{typeof(T)}' through the seam with NO developer context produced JSON " +
                $"differing from the reflection oracle.\n  generated: {generatedNoDeveloper}\n  oracle:    {oracle}");
        }

        if (!string.Equals(generatedWinsWithDeveloper, oracle, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Serializing '{typeof(T)}' through the seam with the generated context winning ahead of a " +
                $"registered developer context produced JSON differing from the oracle.\n" +
                $"  generated: {generatedWinsWithDeveloper}\n  oracle:    {oracle}");
        }

        if (!string.Equals(developerWins, oracle, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Serializing '{typeof(T)}' through the seam with the developer context winning ahead of the " +
                $"generated context produced JSON differing from the oracle — resolution must yield the same " +
                $"JSON whichever resolver wins.\n  developer: {developerWins}\n  oracle:    {oracle}");
        }
    }

    // -- Chain construction (mirrors VistaJson) ---------------------------------------------------------

    /// <summary>
    /// Walks the mirrored seam chain slot-by-slot and returns the kind of the first slot that provides a
    /// <see cref="JsonTypeInfo"/> for <paramref name="type"/> — the exact first-non-null rule the combined
    /// <see cref="JsonSerializerOptions.TypeInfoResolver"/> applies.
    /// </summary>
    private static ResolverKind WinningKind(Type type, bool developerPresent, bool developerFirst)
    {
        // A throwaway options instance the resolver slots build their JsonTypeInfo against. Only
        // per-slot GetTypeInfo is invoked (never serialization), so it is never frozen.
        var probeOptions = BuildSeamOptions(developerPresent, developerFirst);

        foreach (var (kind, resolver) in BuildChain(developerPresent, developerFirst))
        {
            if (resolver.GetTypeInfo(type, probeOptions) is not null)
            {
                return kind;
            }
        }

        return ResolverKind.None;
    }

    /// <summary>
    /// Builds the ordered, mirrored seam resolver chain: <c>Static_Envelope_Context</c> first, then the
    /// generated per-view contexts drained from <see cref="GeneratedJsonContextStore"/> (ahead of the
    /// developer context and the reflection fallback), the optional developer <c>App_Json_Context</c>,
    /// and the reflection fallback last. <paramref name="developerFirst"/> places the developer context
    /// ahead of the generated contexts to exercise the "developer wins" resolution and prove the JSON is
    /// identical either way.
    /// </summary>
    private static List<(ResolverKind Kind, IJsonTypeInfoResolver Resolver)> BuildChain(
        bool developerPresent,
        bool developerFirst)
    {
        var chain = new List<(ResolverKind, IJsonTypeInfoResolver)>
        {
            (ResolverKind.Static, VistaStaticJsonContext.Default),
        };

        if (developerPresent && developerFirst)
        {
            chain.Add((ResolverKind.Developer, SeamJsonDeveloperContext.Default));
        }

        // Drain the Core-resident store exactly as VistaJson does — casting each opaque handle to
        // IJsonTypeInfoResolver (the drain contract, R5.1). This test's context is one of them.
        foreach (var handle in GeneratedJsonContextStore.All)
        {
            chain.Add((ResolverKind.Generated, (IJsonTypeInfoResolver)handle));
        }

        if (developerPresent && !developerFirst)
        {
            chain.Add((ResolverKind.Developer, SeamJsonDeveloperContext.Default));
        }

        chain.Add((ResolverKind.Reflection, ReflectionResolver));
        return chain;
    }

    /// <summary>
    /// Builds a fresh <see cref="JsonSerializerOptions"/> mirroring the Vista seam configuration and the
    /// given chain shape. The reflection fallback stays enabled so nested member types (for example the
    /// <c>ViewListResult</c>/<c>PagedResult</c> collection element list) always resolve — the property's
    /// claim (a covered top-level DTO resolves from the generated context) is proven by
    /// <see cref="WinningKind"/> even with the fallback present.
    /// </summary>
    private static JsonSerializerOptions BuildSeamOptions(bool developerPresent, bool developerFirst)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());

        foreach (var (_, resolver) in BuildChain(developerPresent, developerFirst))
        {
            options.TypeInfoResolverChain.Add(resolver);
        }

        return options;
    }

    /// <summary>
    /// Builds the reflection oracle: the same seam <see cref="JsonSerializerOptions"/> configuration but
    /// with only the reflection resolver in the chain, so its output is the Behavioral_Oracle every seam
    /// configuration must match byte-for-byte.
    /// </summary>
    private static JsonSerializerOptions BuildOracleOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        return options;
    }

    // -- Value generators -------------------------------------------------------------------------------

    private static readonly string[] NamePool =
        { "", "Alice", "Bob", "naïve café", "a\"quoted\"b", "back\\slash", "tab\tend", "  spaced  " };

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    private static readonly Gen<SeamGenRow> GenRow =
        from id in Gen.Int[-100_000, 100_000]
        from name in Pick(NamePool)
        from status in Gen.Int[0, 2]
        from hasScore in Gen.Bool
        from scoreCents in Gen.Int[-1_000_000, 1_000_000]
        select new SeamGenRow
        {
            Id = id,
            Name = name,
            Status = (SeamStatus)status,
            Score = hasScore ? scoreCents / 100m : (decimal?)null,
        };

    private static readonly Gen<SeamGenCrud> GenCrud =
        from name in Pick(NamePool)
        from priceCents in Gen.Int[-1_000_000, 1_000_000]
        from active in Gen.Bool
        select new SeamGenCrud
        {
            Name = name,
            Price = priceCents / 100m,
            Active = active,
        };

    private static readonly Gen<PagedResult<SeamGenRow>> GenPaged =
        from rows in GenRow.List[0, 5]
        from totalRows in Gen.Long[0, 5_000_000]
        from pageIndex in Gen.Int[0, 100]
        from pageSize in Gen.Int[1, 200]
        from totalPages in Gen.Long[0, 50_000]
        select new PagedResult<SeamGenRow>(rows, totalRows, pageIndex, pageSize, totalPages);

    private static readonly Gen<ViewListResult<SeamGenRow>> GenListResult =
        from page in GenPaged
        from unfiltered in Gen.Long[0, 5_000_000]
        select new ViewListResult<SeamGenRow>(page, unfiltered);

    // -- Probe DTOs (a covered view's Serializable_DTO_Set) ---------------------------------------------

    /// <summary>A representative enum member, serialized through the seam's <see cref="JsonStringEnumConverter"/>.</summary>
    public enum SeamStatus
    {
        Active,
        Inactive,
        Pending,
    }

    /// <summary>
    /// The read row type (<c>TRow</c>): a mutable POCO with a scalar, a string, an enum, and a nullable
    /// value member — the least-error-prone construction shape (public parameterless ctor + setters).
    /// Member declaration order is fixed so the generated metadata order matches the reflection oracle.
    /// </summary>
    public sealed class SeamGenRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public SeamStatus Status { get; set; }

        public decimal? Score { get; set; }
    }

    /// <summary>The write model (<c>TCrud</c>): a mutable POCO covering a string, a decimal, and a bool.</summary>
    public sealed class SeamGenCrud
    {
        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public bool Active { get; set; }
    }

    // -- The Generated_View_Context (built via JsonMetadataServices, exactly as the generator emits) ----

    /// <summary>
    /// A hand-built stand-in for the emitted <c>&lt;View&gt;_VistaJsonContext</c>: a reflection-free
    /// <see cref="IJsonTypeInfoResolver"/> that provides the <see cref="JsonTypeInfo"/> for the covered
    /// view's Serializable_DTO_Set via <see cref="JsonMetadataServices"/> (no <c>[JsonSerializable]</c>
    /// attribute, no reflection), returning <see langword="null"/> for any other type so the seam defers
    /// to the next resolver in the chain — the exact shape the <c>ViewJsonContextGenerator</c> emits.
    /// </summary>
    private sealed class GeneratedViewContext : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type == typeof(SeamGenRow))
            {
                return RowInfo(options);
            }

            if (type == typeof(ViewListResult<SeamGenRow>))
            {
                return ListResultInfo(options);
            }

            if (type == typeof(PagedResult<SeamGenRow>))
            {
                return PagedResultInfo(options);
            }

            if (type == typeof(SeamGenCrud))
            {
                return CrudInfo(options);
            }

            return null;
        }

        private static JsonTypeInfo<SeamGenRow> RowInfo(JsonSerializerOptions options)
        {
            var objectInfo = new JsonObjectInfoValues<SeamGenRow>
            {
                ObjectCreator = static () => new SeamGenRow(),
                PropertyMetadataInitializer = _ => new JsonPropertyInfo[]
                {
                    JsonMetadataServices.CreatePropertyInfo<int>(options, new JsonPropertyInfoValues<int>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(SeamGenRow),
                        PropertyName = "Id",
                        JsonPropertyName = "id",
                        Getter = static o => ((SeamGenRow)o).Id,
                        Setter = static (o, v) => ((SeamGenRow)o).Id = v,
                    }),
                    JsonMetadataServices.CreatePropertyInfo<string>(options, new JsonPropertyInfoValues<string>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(SeamGenRow),
                        PropertyName = "Name",
                        JsonPropertyName = "name",
                        Getter = static o => ((SeamGenRow)o).Name,
                        Setter = static (o, v) => ((SeamGenRow)o).Name = v!,
                    }),
                    JsonMetadataServices.CreatePropertyInfo<SeamStatus>(options, new JsonPropertyInfoValues<SeamStatus>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(SeamGenRow),
                        PropertyName = "Status",
                        JsonPropertyName = "status",
                        Getter = static o => ((SeamGenRow)o).Status,
                        Setter = static (o, v) => ((SeamGenRow)o).Status = v,
                    }),
                    JsonMetadataServices.CreatePropertyInfo<decimal?>(options, new JsonPropertyInfoValues<decimal?>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(SeamGenRow),
                        PropertyName = "Score",
                        JsonPropertyName = "score",
                        Getter = static o => ((SeamGenRow)o).Score,
                        Setter = static (o, v) => ((SeamGenRow)o).Score = v,
                    }),
                },
            };

            return JsonMetadataServices.CreateObjectInfo<SeamGenRow>(options, objectInfo);
        }

        private static JsonTypeInfo<SeamGenCrud> CrudInfo(JsonSerializerOptions options)
        {
            var objectInfo = new JsonObjectInfoValues<SeamGenCrud>
            {
                ObjectCreator = static () => new SeamGenCrud(),
                PropertyMetadataInitializer = _ => new JsonPropertyInfo[]
                {
                    JsonMetadataServices.CreatePropertyInfo<string>(options, new JsonPropertyInfoValues<string>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(SeamGenCrud),
                        PropertyName = "Name",
                        JsonPropertyName = "name",
                        Getter = static o => ((SeamGenCrud)o).Name,
                        Setter = static (o, v) => ((SeamGenCrud)o).Name = v!,
                    }),
                    JsonMetadataServices.CreatePropertyInfo<decimal>(options, new JsonPropertyInfoValues<decimal>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(SeamGenCrud),
                        PropertyName = "Price",
                        JsonPropertyName = "price",
                        Getter = static o => ((SeamGenCrud)o).Price,
                        Setter = static (o, v) => ((SeamGenCrud)o).Price = v,
                    }),
                    JsonMetadataServices.CreatePropertyInfo<bool>(options, new JsonPropertyInfoValues<bool>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(SeamGenCrud),
                        PropertyName = "Active",
                        JsonPropertyName = "active",
                        Getter = static o => ((SeamGenCrud)o).Active,
                        Setter = static (o, v) => ((SeamGenCrud)o).Active = v,
                    }),
                },
            };

            return JsonMetadataServices.CreateObjectInfo<SeamGenCrud>(options, objectInfo);
        }

        private static JsonTypeInfo<PagedResult<SeamGenRow>> PagedResultInfo(JsonSerializerOptions options)
        {
            // PagedResult<T> is a positional record: every member binds to the primary constructor and
            // the property setters are init-only (guarded), exactly as the generator emits for R2.5.
            var objectInfo = new JsonObjectInfoValues<PagedResult<SeamGenRow>>
            {
                ObjectWithParameterizedConstructorCreator = static args =>
                    new PagedResult<SeamGenRow>(
                        (IReadOnlyList<SeamGenRow>)args[0]!,
                        (long)args[1]!,
                        (int)args[2]!,
                        (int)args[3]!,
                        (long)args[4]!),
                PropertyMetadataInitializer = _ => new JsonPropertyInfo[]
                {
                    JsonMetadataServices.CreatePropertyInfo<IReadOnlyList<SeamGenRow>>(options, new JsonPropertyInfoValues<IReadOnlyList<SeamGenRow>>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(PagedResult<SeamGenRow>),
                        PropertyName = "Items",
                        JsonPropertyName = "items",
                        Getter = static o => ((PagedResult<SeamGenRow>)o).Items,
                        Setter = static (o, v) => throw new InvalidOperationException(
                            "Setting init-only or read-only members is not supported in source-generated metadata."),
                    }),
                    JsonMetadataServices.CreatePropertyInfo<long>(options, new JsonPropertyInfoValues<long>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(PagedResult<SeamGenRow>),
                        PropertyName = "TotalRows",
                        JsonPropertyName = "totalRows",
                        Getter = static o => ((PagedResult<SeamGenRow>)o).TotalRows,
                        Setter = static (o, v) => throw new InvalidOperationException(
                            "Setting init-only or read-only members is not supported in source-generated metadata."),
                    }),
                    JsonMetadataServices.CreatePropertyInfo<int>(options, new JsonPropertyInfoValues<int>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(PagedResult<SeamGenRow>),
                        PropertyName = "PageIndex",
                        JsonPropertyName = "pageIndex",
                        Getter = static o => ((PagedResult<SeamGenRow>)o).PageIndex,
                        Setter = static (o, v) => throw new InvalidOperationException(
                            "Setting init-only or read-only members is not supported in source-generated metadata."),
                    }),
                    JsonMetadataServices.CreatePropertyInfo<int>(options, new JsonPropertyInfoValues<int>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(PagedResult<SeamGenRow>),
                        PropertyName = "PageSize",
                        JsonPropertyName = "pageSize",
                        Getter = static o => ((PagedResult<SeamGenRow>)o).PageSize,
                        Setter = static (o, v) => throw new InvalidOperationException(
                            "Setting init-only or read-only members is not supported in source-generated metadata."),
                    }),
                    JsonMetadataServices.CreatePropertyInfo<long>(options, new JsonPropertyInfoValues<long>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(PagedResult<SeamGenRow>),
                        PropertyName = "TotalPages",
                        JsonPropertyName = "totalPages",
                        Getter = static o => ((PagedResult<SeamGenRow>)o).TotalPages,
                        Setter = static (o, v) => throw new InvalidOperationException(
                            "Setting init-only or read-only members is not supported in source-generated metadata."),
                    }),
                },
                ConstructorParameterMetadataInitializer = static () => new JsonParameterInfoValues[]
                {
                    new() { Name = "Items", ParameterType = typeof(IReadOnlyList<SeamGenRow>), Position = 0 },
                    new() { Name = "TotalRows", ParameterType = typeof(long), Position = 1 },
                    new() { Name = "PageIndex", ParameterType = typeof(int), Position = 2 },
                    new() { Name = "PageSize", ParameterType = typeof(int), Position = 3 },
                    new() { Name = "TotalPages", ParameterType = typeof(long), Position = 4 },
                },
            };

            return JsonMetadataServices.CreateObjectInfo<PagedResult<SeamGenRow>>(options, objectInfo);
        }

        private static JsonTypeInfo<ViewListResult<SeamGenRow>> ListResultInfo(JsonSerializerOptions options)
        {
            var objectInfo = new JsonObjectInfoValues<ViewListResult<SeamGenRow>>
            {
                ObjectWithParameterizedConstructorCreator = static args =>
                    new ViewListResult<SeamGenRow>(
                        (PagedResult<SeamGenRow>)args[0]!,
                        (long)args[1]!),
                PropertyMetadataInitializer = _ => new JsonPropertyInfo[]
                {
                    JsonMetadataServices.CreatePropertyInfo<PagedResult<SeamGenRow>>(options, new JsonPropertyInfoValues<PagedResult<SeamGenRow>>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(ViewListResult<SeamGenRow>),
                        PropertyName = "Page",
                        JsonPropertyName = "page",
                        Getter = static o => ((ViewListResult<SeamGenRow>)o).Page,
                        Setter = static (o, v) => throw new InvalidOperationException(
                            "Setting init-only or read-only members is not supported in source-generated metadata."),
                    }),
                    JsonMetadataServices.CreatePropertyInfo<long>(options, new JsonPropertyInfoValues<long>
                    {
                        IsProperty = true,
                        IsPublic = true,
                        DeclaringType = typeof(ViewListResult<SeamGenRow>),
                        PropertyName = "TotalRowsUnfiltered",
                        JsonPropertyName = "totalRowsUnfiltered",
                        Getter = static o => ((ViewListResult<SeamGenRow>)o).TotalRowsUnfiltered,
                        Setter = static (o, v) => throw new InvalidOperationException(
                            "Setting init-only or read-only members is not supported in source-generated metadata."),
                    }),
                },
                ConstructorParameterMetadataInitializer = static () => new JsonParameterInfoValues[]
                {
                    new() { Name = "Page", ParameterType = typeof(PagedResult<SeamGenRow>), Position = 0 },
                    new() { Name = "TotalRowsUnfiltered", ParameterType = typeof(long), Position = 1 },
                },
            };

            return JsonMetadataServices.CreateObjectInfo<ViewListResult<SeamGenRow>>(options, objectInfo);
        }
    }
}

/// <summary>
/// A developer-authored <c>App_Json_Context</c> (built by the built-in System.Text.Json source
/// generator) covering the same Serializable_DTO_Set as the test's <c>Generated_View_Context</c>. It
/// stands in for a still-registered developer context so the property can prove the generated context
/// wins ahead of it (optionality) and that the JSON is identical whichever resolver serves the type.
/// </summary>
[JsonSerializable(typeof(SeamResolutionJsonTypeInfoPropertyTests.SeamGenRow))]
[JsonSerializable(typeof(ViewListResult<SeamResolutionJsonTypeInfoPropertyTests.SeamGenRow>))]
[JsonSerializable(typeof(PagedResult<SeamResolutionJsonTypeInfoPropertyTests.SeamGenRow>))]
[JsonSerializable(typeof(SeamResolutionJsonTypeInfoPropertyTests.SeamGenCrud))]
internal sealed partial class SeamJsonDeveloperContext : JsonSerializerContext
{
}
