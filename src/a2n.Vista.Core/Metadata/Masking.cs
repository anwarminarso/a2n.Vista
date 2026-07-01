// Licensed to the a2n.Vista project. Published artifact — English only.

namespace a2n.Vista.Metadata;

/// <summary>
/// Captured masking behavior for one projected field (Decision Log D29/D95, source-generator Phase 2
/// / D118). Holds the two runtime delegates declared at authoring time: a request-scoped
/// <see cref="ShouldMask"/> predicate and a value <see cref="Masker"/> transform. These are <b>runtime</b>
/// delegates, so they are intentionally <em>not</em> carried on the EF-free
/// <see cref="ViewMetadata"/>; the registration path surfaces an ordered list of them to the executor,
/// which applies them at materialization (post-projection, in memory).
/// </summary>
/// <param name="FieldName">The projected field this mask applies to.</param>
/// <param name="ShouldMask">
/// A predicate, evaluated once per request, that decides whether the field is masked for the current
/// caller/context.
/// </param>
/// <param name="Masker">
/// A pure transform applied to the field's pre-mask value when masking is in effect. Boxed to a
/// non-generic shape so the executor can apply it without knowing the field's CLR type.
/// </param>
public sealed record MaskSpec(
    string FieldName,
    Func<IServiceProvider, bool> ShouldMask,
    Func<object?, object?> Masker);

/// <summary>
/// AOT-clean read/write access to a masked field on a materialized row (source-generator Phase 2 /
/// D118). The generator emits these per masked field as a cast + property read for <see cref="Get"/> and
/// a <c>with</c>-style rebuild (for <c>init</c>/record rows) for <see cref="Set"/>; a reflection
/// fallback serves Style A / non-generated views. <see cref="Set"/> returns the (possibly new) row so
/// immutable record rows can be rebuilt.
/// </summary>
/// <param name="FieldName">The projected field this accessor targets.</param>
/// <param name="Get">Reads the original field value from a row.</param>
/// <param name="Set">Writes the masked value into a row and returns the resulting row.</param>
public sealed record MaskAccessor(
    string FieldName,
    Func<object, object?> Get,
    Func<object, object?, object> Set);
