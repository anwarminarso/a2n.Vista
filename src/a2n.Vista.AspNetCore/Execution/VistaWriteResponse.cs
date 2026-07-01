namespace a2n.Vista.AspNetCore.Execution;

/// <summary>
/// The body returned by a successful write that exposes a primary key — notably Create, which returns
/// the newly inserted row's key and nothing else (Requirement R10.1, Decision Log D120). This is the
/// minimal-exposure write response: it carries <b>only</b> the affected row's primary-key value(s) and
/// never the raw entity, a masked field value, or any non-projected field (Requirements R10.2–R10.4).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Key"/> is the Core-neutral key shape produced by the executor:
/// </para>
/// <list type="bullet">
///   <item><description>a boxed scalar for a single-column primary key; or</description></item>
///   <item><description>an ordered field-name→value map (an
///   <see cref="System.Collections.Generic.IReadOnlyDictionary{TKey, TValue}"/>) for a composite key.</description></item>
/// </list>
/// <para>
/// It is serialized with <see cref="Serialization.VistaJson.Options"/> like every other Vista response.
/// </para>
/// </remarks>
/// <param name="Key">
/// The created row's primary-key value: a scalar for a single key, or a field-name→value map for a
/// composite key.
/// </param>
public sealed record VistaWriteResponse(object Key);
