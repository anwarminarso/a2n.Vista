// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Shared per-view JsonTypeInfo context emitter for the source generators (D125 origin; reused by D129
// Style A coverage).
//
// WHY THIS EXISTS (single source of truth for the context-emission code — parity by construction):
//   The per-view JsonTypeInfo phase (D125, ViewJsonContextGenerator) and the Style A coverage phase (D129,
//   StyleAShapeGenerator) must emit the SAME reflection-free `file sealed IJsonTypeInfoResolver` shape:
//   a GetTypeInfo dispatch over the Serializable_DTO_Set + the auxiliary (nullable/collection/leaf/enum)
//   arms, each JsonTypeInfo built by hand via System.Text.Json.Serialization.Metadata.JsonMetadataServices
//   (NOT the [JsonSerializable] attribute route — the generator-of-generator constraint). If the two phases
//   forked divergent copies of this emitter they could drift from each other and from the reflection oracle
//   (DefaultJsonTypeInfoResolver), silently breaking the byte-for-byte serialization parity both phases
//   guarantee. This static helper is therefore the ONE home for that emission; both generators call it so
//   the emitted context is identical by construction (mirroring how the Emittable_Shape ANALYSIS was
//   extracted into EmittableShapeAnalyzer for the same reason, task 2.4).
//
//   The emitter was originally authored as private static members of ViewJsonContextGenerator (D125). It is
//   extracted here VERBATIM (pure functions over the equatable DtoTypeModel/AuxTypeModel shapes — no
//   instance state, no generator-specific behavior), so the move preserves D125's output byte-for-byte:
//   ViewJsonContextGenerator now delegates to JsonContextEmitter.BuildContextSource(...), and
//   StyleAShapeGenerator calls the same method to emit its covered Style A context.
//
// THE ONE PARAMETERIZED DIFFERENCE (design "Keying — the difference from Phases 1/5"):
//   The generated [ModuleInitializer] registers the context into a2n.Vista.Metadata.GeneratedJsonContextStore
//   keyed by the view name. A typed Style B view is a CLASS, so D125 keys it by `new <View>().Name`
//   (instantiate + read the runtime Name). A Style A view is an AddView CALL SITE, not a class — there is
//   nothing to instantiate — so D129 keys it by the CONSTANT view-name LITERAL lifted from AddView
//   (`"customers"`). The registration-key EXPRESSION is therefore the only per-phase input; everything else
//   (the context class name aside, supplied by the caller, and the GetTypeInfo dispatch arms, the
//   JsonMetadataServices factories, and the aux arms) is identical. The caller passes the full key
//   expression: `"new " + viewFqn + "().Name"` for Style B, `Literal(constantName)` for Style A.

using System.Collections.Generic;
using System.Text;

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// The single, shared home for the per-view <c>JsonTypeInfo</c> context-emission code used by both the
    /// per-view <c>JsonTypeInfo</c> generator (D125) and the Style A coverage generator (D129). Extracted
    /// verbatim from the D125 generator so the two phases emit identical contexts and both stay
    /// byte-for-byte compatible with the reflection oracle (parity). Builds a <c>file sealed</c>
    /// <c>System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver</c> by hand via
    /// <c>JsonMetadataServices</c> — reflection-free, attribute-free, no <c>[JsonSerializable]</c>. The only
    /// per-phase input is the <c>[ModuleInitializer]</c> registration-key expression (see
    /// <see cref="BuildContextSource"/>).
    /// </summary>
    internal static class JsonContextEmitter
    {
        // Fully-qualified prefixes for the System.Text.Json metadata surface the emitted resolver names.
        // Full global::-qualification (no `using` directives) mirrors the sibling generators' emission style
        // so the generated file never binds to an ambiguous name in the consumer assembly and stays
        // byte-for-byte deterministic (R7.4). System.Text.Json is part of the net8.0/net9.0/net10.0 shared
        // framework, so the emitted file needs no NuGet package and no ASP.NET Core reference (R7.3/R7.5).
        private const string MetaNs = "global::System.Text.Json.Serialization.Metadata.";
        private const string JsonOptionsFqn = "global::System.Text.Json.JsonSerializerOptions";

        /// <summary>
        /// Builds the per-view generated source — a <c>file sealed</c> class named
        /// <paramref name="contextClassName"/> implementing
        /// <c>System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver</c> whose
        /// <c>GetTypeInfo(Type, options)</c> returns the <c>JsonMetadataServices</c>-built
        /// <c>JsonTypeInfo</c> for each type in <paramref name="dtoSet"/> (the Serializable_DTO_Set) and each
        /// auxiliary type in <paramref name="auxSet"/> (the nullable/collection/leaf/enum arms those DTOs
        /// reach), and <c>null</c> otherwise (defer to the next resolver in the chain, R2.1/R3.1). The same
        /// class carries exactly one <c>[ModuleInitializer]</c> that registers a singleton into
        /// <c>a2n.Vista.Metadata.GeneratedJsonContextStore</c> keyed by
        /// <paramref name="registrationKeyExpression"/> — the ONLY per-phase difference: <c>new
        /// &lt;View&gt;().Name</c> for typed Style B (D125), the constant view-name literal for Style A
        /// (D129). Both <paramref name="dtoSet"/> and <paramref name="auxSet"/> are de-duplicated by
        /// fully-qualified type name preserving their given (fixed) order, so a view whose <c>TCrud</c>
        /// equals its <c>TRow</c> (or whose aux overlaps a DTO) emits a single, correct, minimal resolver.
        /// Fixed <c>"\n"</c> line endings keep the output byte-for-byte deterministic (R7.4). Reflection-free
        /// and attribute-free: no <c>Activator.CreateInstance</c>, no <c>PropertyInfo</c>, no
        /// <c>Expression.Compile</c>, no <c>MakeGenericMethod</c>, no <c>[JsonSerializable]</c> (R7.3).
        /// </summary>
        /// <param name="contextClassName">
        /// The <c>file sealed</c> resolver class name (already unique per generated file); the caller folds
        /// in the view/template identity so it is readable and stable.
        /// </param>
        /// <param name="dtoSet">The Serializable_DTO_Set object DTOs, in fixed order.</param>
        /// <param name="auxSet">The auxiliary (nullable/collection/leaf/enum) types the DTOs reach, in fixed order.</param>
        /// <param name="registrationKeyExpression">
        /// The C# expression the <c>[ModuleInitializer]</c> uses as the <c>GeneratedJsonContextStore</c>
        /// key — <c>"new " + viewFqn + "().Name"</c> (Style B) or a quoted constant literal (Style A).
        /// </param>
        public static string BuildContextSource(
            string contextClassName,
            IReadOnlyList<DtoTypeModel> dtoSet,
            IReadOnlyList<AuxTypeModel> auxSet,
            string registrationKeyExpression)
        {
            // Fixed "\n" line endings (not Environment.NewLine) so generated text is byte-identical across
            // platforms, keeping the determinism property stable (R7.4).
            const string nl = "\n";

            // Deduplicate the Serializable_DTO_Set by fully-qualified type name preserving the given fixed
            // order (TRow, ViewListResult<TRow>, PagedResult<TRow>, [TCrud]). A view whose TCrud equals its
            // TRow would otherwise emit two identical dispatch arms/factories; first-match-wins keeps the
            // resolver correct, and the dedup keeps the output minimal and deterministic (R7.4).
            var dtos = new List<DtoTypeModel>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var dto in dtoSet)
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
            // the given fixed first-occurrence order for deterministic output (R7.4).
            var auxTypes = new List<AuxTypeModel>();
            foreach (var aux in auxSet)
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

            // [ModuleInitializer] registration (R4.1). The initializer keys the context off the view name via
            // the caller-supplied expression: a typed Style B view instantiates itself and reads its runtime
            // `.Name` (`new <View>().Name`), while a Style A view uses the constant AddView name literal —
            // the one per-phase difference (design "Keying"). It runs once at module load, before any DI
            // container is constructed. GeneratedJsonContextStore.Register is first-wins idempotent, so a
            // duplicate name keeps the first registration. The method is `internal static void` and
            // parameterless so it satisfies the ModuleInitializer signature contract.
            sb.Append("    [global::System.Runtime.CompilerServices.ModuleInitializer]").Append(nl);
            sb.Append("    internal static void RegisterJsonContext()").Append(nl);
            sb.Append("        => global::a2n.Vista.Metadata.GeneratedJsonContextStore.Register(").Append(nl);
            sb.Append("               ").Append(registrationKeyExpression).Append(", new ").Append(contextClassName).Append("());").Append(nl);
            sb.Append("}").Append(nl);

            return sb.ToString();
        }

        /// <summary>
        /// Appends the factory method that builds one DTO's <c>JsonTypeInfo</c> via
        /// <c>JsonMetadataServices.CreateObjectInfo</c> + <c>CreatePropertyInfo&lt;TMember&gt;</c>. The
        /// construction path is chosen to round-trip records, init-only, and required members (R2.5/R3.4),
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
        /// enum converter) for parity (R3.3, R6.5).
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
            // (R3.3, R6.5).
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
        /// matches the reflection oracle byte-for-byte (R3.3, R6.5).
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
                // built-in converter matches the reflection oracle so parity holds (R3.3, R6.5).
                var converter = ScalarConverterName(typeFqn);
                sb.Append("        return ").Append(MetaNs).Append("JsonMetadataServices.CreateValueInfo<").Append(typeFqn).Append(">(").Append(nl);
                sb.Append("            options, ").Append(MetaNs).Append("JsonMetadataServices.").Append(converter).Append(");").Append(nl);
                sb.Append("    }").Append(nl);
                return;
            }

            if (aux.Kind == AuxTypeKind.Enum)
            {
                // An enum leaf. The seam serializes enums as STRING names (its options register a
                // JsonStringEnumConverter), so for byte-for-byte parity with the reflection oracle (R3.3,
                // R6.5) the arm's converter must be a string enum converter — NOT JsonMetadataServices'
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
        /// Renders a C# string literal for <paramref name="value"/> with backslashes and double quotes
        /// escaped, so JSON property names and CLR member names emit safely into the generated source.
        /// Delegates to the assembly-wide <see cref="SourceLiterals.Literal"/> so every emitter writes string
        /// literals one way (audit finding <c>DEAD-09</c>).
        /// </summary>
        private static string Literal(string value) => SourceLiterals.Literal(value);
    }
}
