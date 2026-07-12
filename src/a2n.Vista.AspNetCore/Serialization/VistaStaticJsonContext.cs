using System.Text.Json.Serialization;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.Contracts;

namespace a2n.Vista.AspNetCore.Serialization;

/// <summary>
/// The shipped <see cref="JsonSerializerContext"/> covering Vista's fixed, view-independent request
/// envelopes and responses (Decision Log D124, the <c>Static_Envelope_Context</c>). Because this is
/// real source, the built-in System.Text.Json source generator processes it at compile time, so these
/// types (de)serialize AOT-clean without any per-application work.
/// </summary>
/// <remarks>
/// The polymorphic <see cref="FilterNode"/> tree is (de)serialized through the reflection-free
/// <see cref="FilterNodeJsonConverter"/> registered on <see cref="VistaJson.Options"/>; it is listed
/// here so a <see cref="FilterNode"/>-typed member on a covered envelope resolves through this context.
/// This context is chained into the Vista serialization seam ahead of the reflection fallback.
/// </remarks>
[JsonSerializable(typeof(VistaListRequestBody))]
[JsonSerializable(typeof(VistaDetailRequestBody))]
[JsonSerializable(typeof(VistaWriteRequestBody))]
[JsonSerializable(typeof(VistaWriteResponse))]
[JsonSerializable(typeof(VistaMetadataResponse))]
[JsonSerializable(typeof(VistaFieldMetadataResponse))]
[JsonSerializable(typeof(FilterNode))]
internal sealed partial class VistaStaticJsonContext : JsonSerializerContext
{
}
