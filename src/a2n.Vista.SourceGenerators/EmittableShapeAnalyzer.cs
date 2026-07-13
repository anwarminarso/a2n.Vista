// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Shared Emittable_Shape analysis for the source generators (D125 origin; reused by D129 Style A coverage).
//
// WHY THIS EXISTS (single source of truth for the emittable-shape rules — parity depends on it):
//   The per-view JsonTypeInfo phase (D125, ViewJsonContextGenerator) and the Style A coverage phase (D129,
//   StyleAShapeGenerator) must classify a DTO's members against the EXACT SAME Emittable_Shape set and
//   resolve each member's JSON property name with the EXACT SAME naming policy. If the two generators forked
//   divergent copies of these rules they could drift from each other and from the reflection oracle
//   (DefaultJsonTypeInfoResolver), silently breaking the byte-for-byte serialization parity both phases
//   guarantee. This static helper is therefore the ONE home for those rules; both generators call it so the
//   classification is identical by construction.
//
//   The analysis was originally authored as private static members of ViewJsonContextGenerator (D125). It is
//   extracted here VERBATIM (pure functions over Roslyn symbols — no instance state, no generator-specific
//   behavior), so the move preserves D125's output byte-for-byte: ViewJsonContextGenerator now delegates to
//   EmittableShapeAnalyzer.BuildReadDtoSet / BuildDtoModel, and StyleAShapeGenerator calls the same methods
//   to analyze its nameable Style A DTOs (a named TRow's read set and the always-named TCrud, D38).
//
// WHAT IT PRODUCES:
//   * BuildDtoModel(...)   — walks one DTO's public serializable members, resolves each JSON property name
//                            for parity, classifies each member type against the Emittable_Shape set, detects
//                            the DTO's object-construction kind (R3.4/R2.5), appends the equatable
//                            DtoTypeModel to `into`, collects the auxiliary (nullable/collection/leaf/enum)
//                            JsonTypeInfo arms into `auxTypes`, records "Type.Member (typeFqn)" descriptions
//                            into `nonEmittable` for offending members, and returns whether every member is
//                            emittable.
//   * BuildReadDtoSet(...) — the read Serializable_DTO_Set orchestration: BuildDtoModel(TRow) then the two
//                            Vista read envelopes (ViewListResult<TRow>, PagedResult<TRow>) as known shapes
//                            over TRow. Returns TRow's emittability (the envelopes' emittability follows it).
//
//   The Emittable_Shape set (design "Data Models", inherited from D125): BCL scalars, string, nullable value
//   types, enums (serialized via the seam's JsonStringEnumConverter), byte[], collections of an emittable
//   element, the Vista ViewListResult<TRow>/PagedResult<TRow> envelopes, and single-level nested emittable
//   POCOs. Anything requiring a bespoke/polymorphic converter or an unresolved generic — and any nesting
//   beyond one POCO level — is NonEmittable. Correctness beats coverage: the SAFE DEFAULT for anything the
//   analyzer cannot fully resolve is NonEmittable, never a best-effort context that could drift from the
//   oracle (R1.4/R1.5, R1.7).

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// The single, shared home for the Emittable_Shape member-classification and JSON-property-name rules
    /// used by both the per-view <c>JsonTypeInfo</c> generator (D125) and the Style A coverage generator
    /// (D129). Extracted verbatim from the D125 generator so the two phases classify DTOs identically and
    /// both stay byte-for-byte compatible with the reflection oracle (parity). Recognizes the Vista read
    /// envelopes and the System.Text.Json attributes by fully-qualified name only — no a2n.Vista project
    /// reference (D48, R1.6/R7.1).
    /// </summary>
    internal static class EmittableShapeAnalyzer
    {
        // Reflection metadata names (arity-encoded) of the two Vista read envelopes, used to resolve their
        // constructed symbols from the compilation for the read Serializable_DTO_Set. FQN-only recognition —
        // no a2n.Vista assembly reference (R1.6, R7.1).
        private const string ViewListResultMetadataName = "a2n.Vista.Ports.ViewListResult`1";
        private const string PagedResultMetadataName = "a2n.Vista.Results.PagedResult`1";

        // Simple metadata names + namespaces of the two Vista read envelopes, used to recognize them as known
        // emittable shapes when they appear as a DTO member (FQN-only, R1.6/R7.1).
        private const string ViewListResultSimpleMetadataName = "ViewListResult`1";
        private const string PagedResultSimpleMetadataName = "PagedResult`1";
        private const string ViewListResultNamespace = "a2n.Vista.Ports";
        private const string PagedResultNamespace = "a2n.Vista.Results";
        private const string CollectionsGenericNamespace = "System.Collections.Generic";

        // Fully-qualified names of the System.Text.Json attributes the shape analysis honors for parity with
        // the reflection oracle: [JsonPropertyName] overrides the naming policy; [JsonIgnore] drops a member
        // from the serializable set. Recognized by FQN only (R2.3, R6.4).
        private const string JsonPropertyNameAttributeFqn = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
        private const string JsonIgnoreAttributeFqn = "System.Text.Json.Serialization.JsonIgnoreAttribute";

        // JsonIgnoreCondition.Always == 1 (the default a bare [JsonIgnore] carries): the member is never
        // serialized and is dropped from the set. Any other condition (Never/WhenWriting*) still serializes.
        private const int JsonIgnoreConditionAlways = 1;

        // Single level of nested-POCO support (design v1 target): a top-level DTO member may itself be a
        // POCO (budget 1), but that nested POCO's members must be leaf shapes (budget 0 → no further POCOs).
        // Deeper nesting is deferred and classified NonEmittable — the safe default over the oracle (R1.5).
        private const int TopLevelPocoBudget = 1;

        /// <summary>
        /// Builds the read Serializable_DTO_Set for a named row: <c>TRow</c> first, then the two Vista read
        /// envelopes (<c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>) as known shapes over
        /// <c>TRow</c>. Appends each modeled DTO to <paramref name="into"/> (fixed order), records
        /// <c>TRow</c>'s offending members into <paramref name="nonEmittable"/>, and collects the auxiliary
        /// arms into <paramref name="auxTypes"/>. Returns whether <c>TRow</c>'s own members are all emittable
        /// — the envelopes' emittability follows <c>TRow</c>'s, so they are not gated separately (their
        /// offending members are routed to a throwaway sink to avoid duplicating the <c>TRow</c>-derived
        /// entries). This is the shared orchestration both generators use for a named read row.
        /// </summary>
        public static bool BuildReadDtoSet(
            Compilation compilation,
            INamedTypeSymbol rowType,
            List<DtoTypeModel> into,
            List<string> nonEmittable,
            List<AuxTypeModel> auxTypes,
            HashSet<string> auxSeen)
        {
            var rowEmittable = BuildDtoModel(rowType, nonEmittable, into, auxTypes, auxSeen);
            AddEnvelopeModel(compilation, ViewListResultMetadataName, rowType, into, auxTypes, auxSeen);
            AddEnvelopeModel(compilation, PagedResultMetadataName, rowType, into, auxTypes, auxSeen);
            return rowEmittable;
        }

        /// <summary>
        /// Builds the equatable <see cref="DtoTypeModel"/> for one DTO by walking its public serializable
        /// members: each member's JSON property name is resolved per the seam's naming policy for parity
        /// (<see cref="ResolveJsonPropertyName"/>), and each member type is classified against the
        /// Emittable_Shape set (<see cref="ClassifyType"/>). Returns <c>true</c> when every member is
        /// emittable; records a <c>Type.Member (memberTypeFqn)</c> description into
        /// <paramref name="nonEmittable"/> for each member that is not, so the caller can compose the
        /// coverage diagnostic and classify the DTO as not covered (R1.5/R1.7). The DTO's
        /// object-construction kind is detected for R2.5/R3.4 (<see cref="DetectConstruction"/>). The
        /// completed model is appended to <paramref name="into"/>, and the auxiliary (nullable/collection/
        /// leaf/enum) arms each emittable member needs are collected into <paramref name="auxTypes"/>.
        /// </summary>
        public static bool BuildDtoModel(
            INamedTypeSymbol dtoType,
            List<string> nonEmittable,
            List<DtoTypeModel> into,
            List<AuxTypeModel> auxTypes,
            HashSet<string> auxSeen)
        {
            var members = new List<DtoMemberModel>();
            var allEmittable = true;

            foreach (var property in EnumerateSerializableProperties(dtoType))
            {
                var memberTypeFqn = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var shape = ClassifyType(property.Type, TopLevelPocoBudget, out var emittable);
                if (!emittable)
                {
                    allEmittable = false;
                    nonEmittable.Add($"{dtoType.Name}.{property.Name} ({memberTypeFqn})");
                }
                else
                {
                    // Collect the auxiliary (nullable/collection) JsonTypeInfo arms this member needs so the
                    // covered DTO resolves with NO reflection fallback in the chain (R2.1, R8.1).
                    CollectAuxTypes(property.Type, auxTypes, auxSeen);
                }

                members.Add(new DtoMemberModel(
                    memberName: property.Name,
                    memberTypeFqn: memberTypeFqn,
                    jsonPropertyName: ResolveJsonPropertyName(property),
                    isReadOnly: IsReadOnlyMember(property),
                    shapeKind: shape));
            }

            into.Add(new DtoTypeModel(
                typeFqn: dtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                construction: DetectConstruction(dtoType),
                members: new EquatableArray<DtoMemberModel>(members.ToArray())));

            return allEmittable;
        }

        /// <summary>
        /// Resolves the constructed Vista read envelope (<c>ViewListResult&lt;TRow&gt;</c> or
        /// <c>PagedResult&lt;TRow&gt;</c>) from the compilation and models it as a DTO. The envelopes are
        /// known shapes over an emittable <c>TRow</c>, so their members are modeled for the emitter but their
        /// emittability is NOT gated separately (it follows <c>TRow</c>'s); offending members are therefore
        /// routed to a throwaway sink to avoid duplicating the <c>TRow</c>-derived entries already recorded by
        /// the caller. A no-op when the envelope type is not present in the compilation (defensive; a real
        /// view always references Core).
        /// </summary>
        private static void AddEnvelopeModel(
            Compilation compilation,
            string envelopeMetadataName,
            INamedTypeSymbol rowType,
            List<DtoTypeModel> into,
            List<AuxTypeModel> auxTypes,
            HashSet<string> auxSeen)
        {
            if (compilation.GetTypeByMetadataName(envelopeMetadataName) is not INamedTypeSymbol openEnvelope)
            {
                return;
            }

            var constructed = openEnvelope.Construct(rowType);
            var throwaway = new List<string>();

            // Walk the constructed envelope's members so its collection member (PagedResult.Items —
            // IReadOnlyList<TRow>) is collected into auxTypes; the envelope's emittability follows TRow's, so
            // offending members are routed to a throwaway sink to avoid duplicate NonEmittableMembers entries.
            BuildDtoModel(constructed, throwaway, into, auxTypes, auxSeen);
        }

        /// <summary>
        /// Enumerates the public serializable properties of a DTO in declaration order: public, readable
        /// (public getter), non-static, non-indexer instance properties that are not dropped by
        /// <c>[JsonIgnore]</c>. Mirrors the System.Text.Json default (public instance properties; fields
        /// excluded, matching the seam options which do not set <c>IncludeFields</c>).
        /// </summary>
        private static IEnumerable<IPropertySymbol> EnumerateSerializableProperties(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol property
                    && !property.IsStatic
                    && !property.IsIndexer
                    && property.DeclaredAccessibility == Accessibility.Public
                    && property.GetMethod is not null
                    && property.GetMethod.DeclaredAccessibility == Accessibility.Public
                    && !IsJsonIgnored(property))
                {
                    yield return property;
                }
            }
        }

        /// <summary>
        /// The JSON property name for parity with the reflection oracle (R2.3, R6.4): the literal from
        /// <c>[JsonPropertyName("...")]</c> when present, otherwise the member name run through the seam's
        /// naming policy (<see cref="ToCamelCase"/>, the <see cref="System.Text.Json.JsonSerializerDefaults.Web"/>
        /// default the seam configures).
        /// </summary>
        private static string ResolveJsonPropertyName(IPropertySymbol property)
        {
            foreach (var attribute in property.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == JsonPropertyNameAttributeFqn
                    && attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is string explicitName)
                {
                    return explicitName;
                }
            }

            return ToCamelCase(property.Name);
        }

        /// <summary>
        /// Whether a member is dropped from the serializable set by <c>[JsonIgnore]</c>. A bare
        /// <c>[JsonIgnore]</c> carries <c>Condition = JsonIgnoreCondition.Always</c> (never serialized) and
        /// drops the member; <c>Condition = Never</c> (or a conditional <c>WhenWriting*</c>) keeps it, since
        /// those still serialize the member (matching the oracle).
        /// </summary>
        private static bool IsJsonIgnored(IPropertySymbol property)
        {
            foreach (var attribute in property.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != JsonIgnoreAttributeFqn)
                {
                    continue;
                }

                var condition = JsonIgnoreConditionAlways; // bare [JsonIgnore] defaults to Always.
                foreach (var named in attribute.NamedArguments)
                {
                    if (named.Key == "Condition" && named.Value.Value is int conditionValue)
                    {
                        condition = conditionValue;
                    }
                }

                return condition == JsonIgnoreConditionAlways;
            }

            return false;
        }

        /// <summary>
        /// Whether a member is read-only from the serializer's perspective — no public setter, or an
        /// <c>init</c>-only setter — which forces construction through the parameterized/<c>init</c> path
        /// (R2.5).
        /// </summary>
        private static bool IsReadOnlyMember(IPropertySymbol property)
            => property.SetMethod is null
               || property.SetMethod.DeclaredAccessibility != Accessibility.Public
               || property.SetMethod.IsInitOnly;

        /// <summary>
        /// Detects the DTO's object-construction kind for R2.5/R3.4:
        /// <see cref="ObjectConstructionKind.Parameterless"/> when the type exposes a public parameterless
        /// constructor (System.Text.Json constructs via it and populates members through setters/<c>init</c>),
        /// otherwise <see cref="ObjectConstructionKind.Parameterized"/> — the case for positional records
        /// (including the Vista envelopes) and types whose only constructors take parameters.
        /// </summary>
        private static ObjectConstructionKind DetectConstruction(INamedTypeSymbol type)
        {
            var hasParameterlessCtor = type.InstanceConstructors.Any(
                static c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

            return hasParameterlessCtor
                ? ObjectConstructionKind.Parameterless
                : ObjectConstructionKind.Parameterized;
        }

        /// <summary>
        /// Classifies a member type against the Emittable_Shape set (design "Data Models"), returning its
        /// <see cref="MemberShapeKind"/> and, via <paramref name="emittable"/>, whether the generator can
        /// emit its <c>JsonTypeInfo</c> reflection-free. The safe default for anything the analyzer cannot
        /// fully resolve (interfaces, <c>object</c>/<c>dynamic</c>, delegates, unresolved generics/type
        /// parameters, dictionaries and other unsupported collections, bespoke-converter shapes, and
        /// nesting beyond the supported single POCO level) is
        /// <see cref="MemberShapeKind.NonEmittable"/>/<c>false</c> — parity over coverage (R1.4, R1.5).
        /// <paramref name="pocoBudget"/> bounds nested-POCO depth: a top-level DTO member is classified with
        /// <see cref="TopLevelPocoBudget"/>, and a nested POCO validates its own members with the budget
        /// decremented so deeper nesting is rejected.
        /// </summary>
        private static MemberShapeKind ClassifyType(ITypeSymbol type, int pocoBudget, out bool emittable)
        {
            // string.
            if (type.SpecialType == SpecialType.System_String)
            {
                emittable = true;
                return MemberShapeKind.String;
            }

            // Nullable value type (T?): emittable when the underlying type is an emittable scalar or enum.
            if (type is INamedTypeSymbol nullable
                && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && nullable.TypeArguments.Length == 1)
            {
                var underlyingKind = ClassifyType(nullable.TypeArguments[0], 0, out var underlyingEmittable);
                emittable = underlyingEmittable
                            && (underlyingKind == MemberShapeKind.Scalar || underlyingKind == MemberShapeKind.Enum);
                return MemberShapeKind.Nullable;
            }

            // Enum (serialized via the seam's JsonStringEnumConverter for parity).
            if (type.TypeKind == TypeKind.Enum)
            {
                emittable = true;
                return MemberShapeKind.Enum;
            }

            // BCL scalar.
            if (IsScalar(type))
            {
                emittable = true;
                return MemberShapeKind.Scalar;
            }

            // byte[] — System.Text.Json base64 default (matches the oracle); treated as a scalar leaf.
            if (type is IArrayTypeSymbol byteArray
                && byteArray.Rank == 1
                && byteArray.ElementType.SpecialType == SpecialType.System_Byte)
            {
                emittable = true;
                return MemberShapeKind.Scalar;
            }

            // Vista read envelope (ViewListResult<T>/PagedResult<T>) — a known shape over an emittable T.
            if (IsVistaEnvelope(type, out var envelopeElement))
            {
                ClassifyType(envelopeElement, pocoBudget, out var envelopeElementEmittable);
                emittable = envelopeElementEmittable;
                return MemberShapeKind.Nested;
            }

            // Collection (array / List<T> / IReadOnlyList<T> / IList<T> / ICollection<T> /
            // IReadOnlyCollection<T> / IEnumerable<T>) of an emittable element.
            if (TryGetEnumerableElement(type, out var element))
            {
                ClassifyType(element, pocoBudget, out var elementEmittable);
                emittable = elementEmittable;
                return MemberShapeKind.Collection;
            }

            // Single-level nested POCO: emittable when the budget allows and every member is emittable.
            if (pocoBudget > 0 && type is INamedTypeSymbol pocoType && IsEmittablePocoCandidate(pocoType))
            {
                var allMembersEmittable = true;
                foreach (var member in EnumerateSerializableProperties(pocoType))
                {
                    ClassifyType(member.Type, pocoBudget - 1, out var memberEmittable);
                    if (!memberEmittable)
                    {
                        allMembersEmittable = false;
                    }
                }

                emittable = allMembersEmittable;
                return MemberShapeKind.Nested;
            }

            // Anything else: not emittable reflection-free — falls back to the developer context / oracle.
            emittable = false;
            return MemberShapeKind.NonEmittable;
        }

        /// <summary>
        /// Whether the type is a BCL scalar the generator can emit directly via
        /// <c>CreatePropertyInfo&lt;T&gt;</c>: the primitive/numeric/<c>char</c> special types plus the
        /// common value scalars (<c>Guid</c>, <c>DateTime</c>, <c>DateTimeOffset</c>, <c>DateOnly</c>,
        /// <c>TimeOnly</c>, <c>TimeSpan</c>).
        /// </summary>
        private static bool IsScalar(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_Char:
                    return true;
            }

            switch (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            {
                case "global::System.Guid":
                case "global::System.DateTime":
                case "global::System.DateTimeOffset":
                case "global::System.DateOnly":
                case "global::System.TimeOnly":
                case "global::System.TimeSpan":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Recognizes the Vista read envelopes <c>a2n.Vista.Ports.ViewListResult&lt;T&gt;</c> and
        /// <c>a2n.Vista.Results.PagedResult&lt;T&gt;</c> by metadata name + namespace (FQN-only, R1.6/R7.1),
        /// yielding the single type argument in <paramref name="element"/> so its emittability can be
        /// checked.
        /// </summary>
        private static bool IsVistaEnvelope(ITypeSymbol type, out ITypeSymbol element)
        {
            element = null;
            if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
            {
                var definition = named.OriginalDefinition;
                var ns = definition.ContainingNamespace?.ToDisplayString();
                if ((definition.MetadataName == ViewListResultSimpleMetadataName && ns == ViewListResultNamespace)
                    || (definition.MetadataName == PagedResultSimpleMetadataName && ns == PagedResultNamespace))
                {
                    element = named.TypeArguments[0];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Yields the element type of a supported single-argument sequence: a rank-1 array, or a
        /// <c>System.Collections.Generic</c> <c>List&lt;T&gt;</c>/<c>IList&lt;T&gt;</c>/
        /// <c>IReadOnlyList&lt;T&gt;</c>/<c>ICollection&lt;T&gt;</c>/<c>IReadOnlyCollection&lt;T&gt;</c>/
        /// <c>IEnumerable&lt;T&gt;</c>. Dictionaries and other keyed/custom collections are deliberately
        /// excluded (they are not in the Emittable_Shape set) so they classify as non-emittable — the safe
        /// default over the oracle.
        /// </summary>
        private static bool TryGetEnumerableElement(ITypeSymbol type, out ITypeSymbol element)
        {
            element = null;

            if (type is IArrayTypeSymbol array && array.Rank == 1)
            {
                element = array.ElementType;
                return true;
            }

            if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
            {
                var definition = named.OriginalDefinition;
                if (definition.ContainingNamespace?.ToDisplayString() == CollectionsGenericNamespace)
                {
                    switch (definition.MetadataName)
                    {
                        case "List`1":
                        case "IList`1":
                        case "IReadOnlyList`1":
                        case "ICollection`1":
                        case "IReadOnlyCollection`1":
                        case "IEnumerable`1":
                            element = named.TypeArguments[0];
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Collects the auxiliary (non-object) <c>JsonTypeInfo</c> arms a serializable member needs so the
        /// covered DTO (de)serializes with NO reflection fallback in the chain (R2.1, R8.1). System.Text.Json
        /// builds scalar/string/enum property metadata from its built-in converters, but for "complex" member
        /// shapes — nullable value types and collections — it resolves the member's <c>JsonTypeInfo</c> from
        /// the resolver chain; without a dispatch arm those throw <c>NotSupportedException</c> when the
        /// reflection resolver is removed. This walks a member type and appends (deduplicated by FQN, in
        /// first-occurrence order for determinism, R7.4):
        /// <list type="bullet">
        ///   <item>a <see cref="AuxTypeKind.Nullable"/> entry for a nullable value type <c>T?</c>;</item>
        ///   <item>a <see cref="AuxTypeKind.Collection"/> entry for a supported collection, then recurses into
        ///   its element so nested collections/nullables are covered too.</item>
        /// </list>
        /// <c>byte[]</c> (a base64 scalar leaf), the Vista envelopes (top-level DTOs), scalars, strings, enums,
        /// and nested POCOs are handled per their kind (scalars/strings/enums get a leaf arm; the envelopes
        /// resolve as their own object arm). The element type of a collection (e.g. <c>TRow</c>, <c>string</c>)
        /// resolves from the rest of the chain — <c>TRow</c> from a resolver's object arm, a scalar element
        /// from the built-in converter.
        /// </summary>
        private static void CollectAuxTypes(ITypeSymbol type, List<AuxTypeModel> auxTypes, HashSet<string> auxSeen)
        {
            // byte[]: a base64 leaf (built-in ByteArrayConverter). Emitted as a scalar leaf arm so the
            // no-fallback chain can resolve it (checked before the collection branch — it is NOT a collection).
            if (type is IArrayTypeSymbol maybeBytes
                && maybeBytes.Rank == 1
                && maybeBytes.ElementType.SpecialType == SpecialType.System_Byte)
            {
                AddScalarAux("byte[]", auxTypes, auxSeen);
                return;
            }

            // string / BCL scalar leaf: emit a value-info arm so a leaf reached via GetNullableConverter or a
            // collection element resolves from the resolver in the no-fallback chain (R2.1, R8.1). Adding an
            // arm for a leaf System.Text.Json could also resolve inline is harmless — the built-in converter
            // matches the oracle, so parity holds — and matches the built-in generator's completeness.
            if (type.SpecialType == SpecialType.System_String || IsScalar(type))
            {
                AddScalarAux(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), auxTypes, auxSeen);
                return;
            }

            // Enum: System.Text.Json still resolves an enum property's JsonTypeInfo from the resolver chain
            // when the reflection fallback is removed, so an enum leaf arm is required (R2.1, R8.1). The arm
            // uses the converter the seam's options resolve for the enum, so it rides the seam's registered
            // JsonStringEnumConverter for parity with the oracle (R2.3, R6.4).
            if (type.TypeKind == TypeKind.Enum)
            {
                var enumFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (auxSeen.Add(enumFqn))
                {
                    auxTypes.Add(new AuxTypeModel(enumFqn, AuxTypeKind.Enum, enumFqn, default));
                }

                return;
            }

            // Nullable value type (T?): needs a CreateValueInfo + GetNullableConverter<T> arm, and its
            // underlying scalar needs its own leaf arm (GetNullableConverter resolves it from the chain).
            if (type is INamedTypeSymbol nullable
                && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && nullable.TypeArguments.Length == 1)
            {
                var nullableFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var underlying = nullable.TypeArguments[0];
                var underlyingFqn = underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (auxSeen.Add(nullableFqn))
                {
                    auxTypes.Add(new AuxTypeModel(nullableFqn, AuxTypeKind.Nullable, underlyingFqn, default));
                }

                // The underlying scalar's leaf arm (enum underlyings ride the seam converter, no arm).
                CollectAuxTypes(underlying, auxTypes, auxSeen);
                return;
            }

            // Vista read envelopes are top-level DTOs (their own object arm), not auxiliary types.
            if (IsVistaEnvelope(type, out _))
            {
                return;
            }

            // Supported collection: needs the matching collection-info arm; recurse into the element so its
            // leaf/nested collection/nullable arm is collected too.
            if (TryGetCollectionShape(type, out var element, out var shape))
            {
                var collectionFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var elementFqn = element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (auxSeen.Add(collectionFqn))
                {
                    auxTypes.Add(new AuxTypeModel(collectionFqn, AuxTypeKind.Collection, elementFqn, shape));
                }

                CollectAuxTypes(element, auxTypes, auxSeen);
            }
        }

        /// <summary>
        /// Adds a scalar/string/<c>byte[]</c> leaf auxiliary arm (deduplicated by FQN), so the no-fallback
        /// chain can resolve the leaf's <c>JsonTypeInfo</c> from a resolver (R2.1, R8.1).
        /// </summary>
        private static void AddScalarAux(string leafFqn, List<AuxTypeModel> auxTypes, HashSet<string> auxSeen)
        {
            if (auxSeen.Add(leafFqn))
            {
                auxTypes.Add(new AuxTypeModel(leafFqn, AuxTypeKind.Scalar, leafFqn, default));
            }
        }

        /// <summary>
        /// Recognizes a supported single-argument collection member and yields both its element type and the
        /// <see cref="CollectionShapeKind"/> that selects the emitter's <c>JsonMetadataServices</c> helper
        /// (mirroring the built-in System.Text.Json source generator's per-shape choice). Rank-1 arrays,
        /// <c>List&lt;T&gt;</c>, and the <c>System.Collections.Generic</c> list/collection/enumerable
        /// interfaces are supported; dictionaries and other keyed/custom collections are excluded (they are
        /// not in the Emittable_Shape set), consistent with <see cref="TryGetEnumerableElement"/>.
        /// </summary>
        private static bool TryGetCollectionShape(ITypeSymbol type, out ITypeSymbol element, out CollectionShapeKind shape)
        {
            element = null;
            shape = default;

            if (type is IArrayTypeSymbol array && array.Rank == 1)
            {
                element = array.ElementType;
                shape = CollectionShapeKind.Array;
                return true;
            }

            if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
            {
                var definition = named.OriginalDefinition;
                if (definition.ContainingNamespace?.ToDisplayString() == CollectionsGenericNamespace)
                {
                    switch (definition.MetadataName)
                    {
                        case "List`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.List;
                            return true;
                        case "IList`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.IList;
                            return true;
                        case "ICollection`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.ICollection;
                            return true;
                        case "IReadOnlyList`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.IReadOnlyList;
                            return true;
                        case "IReadOnlyCollection`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.IReadOnlyCollection;
                            return true;
                        case "IEnumerable`1":
                            element = named.TypeArguments[0];
                            shape = CollectionShapeKind.IEnumerable;
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the type is a concrete, constructible POCO the generator may recurse into as a nested
        /// object: a non-abstract, non-anonymous class or struct that is not <c>object</c> and not an error
        /// type. Interfaces, delegates, <c>object</c>/<c>dynamic</c>, and type parameters are excluded and
        /// classify as non-emittable.
        /// </summary>
        private static bool IsEmittablePocoCandidate(INamedTypeSymbol type)
            => !type.IsAnonymousType
               && !type.IsAbstract
               && (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct)
               && type.SpecialType != SpecialType.System_Object
               && type.TypeKind != TypeKind.Error;

        /// <summary>
        /// Applies the seam's <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> camel-case naming
        /// policy to a member name for parity with the reflection oracle (R2.3, R6.4). This faithfully
        /// mirrors System.Text.Json's built-in <c>JsonNamingPolicy.CamelCase</c> conversion (including its
        /// acronym handling, e.g. <c>OrderID → orderID</c>) so the generated wire names match byte-for-byte.
        /// </summary>
        private static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
            {
                return name;
            }

            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (i == 1 && !char.IsUpper(chars[i]))
                {
                    break;
                }

                var hasNext = i + 1 < chars.Length;

                // Stop once the following character is already lower-case (acronym boundary).
                if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
                {
                    // If the following character is a space, lower-case the current one before stopping.
                    if (chars[i + 1] == ' ')
                    {
                        chars[i] = char.ToLowerInvariant(chars[i]);
                    }

                    break;
                }

                chars[i] = char.ToLowerInvariant(chars[i]);
            }

            return new string(chars);
        }
    }
}
