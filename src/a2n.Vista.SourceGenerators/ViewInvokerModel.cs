// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Equatable incremental data model for the HTTP-surface dispatch-invoker pipeline (D123,
// source-generator-http-surface).
//
// WHY THIS EXISTS (incremental caching, R7.2 / Phase 1-2-3 caching contract):
//   The ViewInvokerGenerator is a third IIncrementalGenerator added to a2n.Vista.SourceGenerators. Its
//   semantic transform must produce a FULLY EQUATABLE value model so Roslyn caches unchanged views and an
//   unrelated edit does NOT regenerate every dispatch invoker. This record carries only strings/bools, an
//   equatable LocationInfo SURROGATE (not the non-value-equal Microsoft.CodeAnalysis.Location), and the
//   JSON-serializable type-name sequence wrapped in EquatableArray<T> so order-sensitive structural
//   equality holds.
//
//   Following the Phase 1/2/3 convention (see ViewModel in ViewAccessorGenerator.cs and WriteMapperModel),
//   the record uses get-only auto properties set through the constructor rather than positional/`init`
//   members: this avoids the System.Runtime.CompilerServices.IsExternalInit shim netstandard2.0 would
//   otherwise need. The project builds with <Nullable>disable</Nullable>, so members that are conceptually
//   nullable (Namespace, CrudTypeFqn, Location) are declared as plain reference types and their
//   nullability is documented in XML comments only.

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// Fully equatable description of a typed Style B view discovered by the <c>ViewInvokerGenerator</c>
    /// as an HTTP-surface dispatch candidate (D123). Equality is value-based and covers every declared
    /// member, including the (order-sensitive) <see cref="JsonSerializableTypeFqns"/> sequence, so the
    /// incremental pipeline can reuse cached output for an unchanged view (the Phase 1/2/3 caching
    /// contract, R7.2). Reconstruct a reportable location from <see cref="Location"/> via
    /// <see cref="LocationInfo.ToLocation"/> at diagnostic-report time.
    /// </summary>
    internal sealed record ViewInvokerModel
    {
        public ViewInvokerModel(
            string @namespace,
            string className,
            string viewFqn,
            string rowTypeFqn,
            string crudTypeFqn,
            bool isWritable,
            bool isPartial,
            bool isAbstract,
            bool hasNamedRowType,
            bool hasNamedCrudType,
            bool hasPublicParameterlessCtor,
            EquatableArray<string> jsonSerializableTypeFqns,
            LocationInfo location)
        {
            Namespace = @namespace;
            ClassName = className;
            ViewFqn = viewFqn;
            RowTypeFqn = rowTypeFqn;
            CrudTypeFqn = crudTypeFqn;
            IsWritable = isWritable;
            IsPartial = isPartial;
            IsAbstract = isAbstract;
            HasNamedRowType = hasNamedRowType;
            HasNamedCrudType = hasNamedCrudType;
            HasPublicParameterlessCtor = hasPublicParameterlessCtor;
            JsonSerializableTypeFqns = jsonSerializableTypeFqns;
            Location = location;
        }

        /// <summary>Declaring namespace, or <c>null</c> for the global namespace.</summary>
        public string Namespace { get; }

        /// <summary>The view class name (without namespace).</summary>
        public string ClassName { get; }

        /// <summary>Fully-qualified (<c>global::</c>-prefixed) name of the view type.</summary>
        public string ViewFqn { get; }

        /// <summary>
        /// Fully-qualified (<c>global::</c>-prefixed) name of <c>TRow</c> (the projected row type,
        /// <c>TQuery</c>). Used to close <c>ListAsync&lt;TRow&gt;</c>/<c>DetailAsync&lt;TRow&gt;</c> at
        /// compile time in the emitted invoker.
        /// </summary>
        public string RowTypeFqn { get; }

        /// <summary>
        /// Fully-qualified (<c>global::</c>-prefixed) name of <c>TCrud</c> (the write model), or
        /// <c>null</c> for a read-only view. Used to close <c>CreateAsync&lt;TCrud&gt;</c>/
        /// <c>UpdateAsync&lt;TCrud&gt;</c> in the emitted invoker.
        /// </summary>
        public string CrudTypeFqn { get; }

        /// <summary>
        /// Whether the view derives <c>View&lt;TQuery, TCrud&gt;</c> (arity-2, writable) rather than
        /// <c>View&lt;TQuery&gt;</c> (arity-1, read-only). Drives the emitted <c>IsWritable</c> member and
        /// whether write dispatch is generated.
        /// </summary>
        public bool IsWritable { get; }

        /// <summary>Whether the view is declared <c>partial</c>.</summary>
        public bool IsPartial { get; }

        /// <summary>Whether the view is declared <c>abstract</c> (abstract views are not candidates).</summary>
        public bool IsAbstract { get; }

        /// <summary>
        /// Whether <c>TQuery</c> is a named type. <c>false</c> when <c>TQuery</c> is <c>object</c>, an
        /// anonymous type, or otherwise not a named type — the view is uncovered and reported as
        /// VISTA0040, falling back to reflection (R1.1, R1.3, R9.1).
        /// </summary>
        public bool HasNamedRowType { get; }

        /// <summary>
        /// Whether the writable <c>TCrud</c> is a named type. <c>false</c> when a writable view's
        /// <c>TCrud</c> is <c>object</c>/anonymous — no generated write dispatch or write-model binding is
        /// emitted for it (R1.2). Always <c>false</c> for a read-only view.
        /// </summary>
        public bool HasNamedCrudType { get; }

        /// <summary>
        /// Whether the view has a public parameterless constructor. When <c>false</c>, the generated
        /// <c>[ModuleInitializer]</c> cannot instantiate the view to read its runtime <c>Name</c>, so
        /// neither the invoker nor the initializer is emitted and the view stays on the reflection
        /// fallback (R1.5), consistent with Phase 1/2/3 behavior.
        /// </summary>
        public bool HasPublicParameterlessCtor { get; }

        /// <summary>
        /// The exact <c>[JsonSerializable]</c> type names (fully-qualified, <c>global::</c>-prefixed) a
        /// developer should include in an <c>App_Json_Context</c> for this view — <c>TRow</c>,
        /// <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>, and (for a writable view)
        /// <c>TCrud</c>. Wrapped in <see cref="EquatableArray{T}"/> so the (order-sensitive) sequence
        /// participates in the record's value equality. Composed into the VISTA0041 serialization-guidance
        /// diagnostic (R5.4, R9.2).
        /// </summary>
        public EquatableArray<string> JsonSerializableTypeFqns { get; }

        /// <summary>
        /// Equatable surrogate for the view class identifier's source location, used to report
        /// diagnostics. A <see cref="LocationInfo"/> (not a raw <see cref="Microsoft.CodeAnalysis.Location"/>)
        /// so the model stays value-equal and incremental caching is preserved.
        /// </summary>
        public LocationInfo Location { get; }
    }
}
