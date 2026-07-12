using System.Text.Json.Serialization;
using a2n.Vista.Ports;
using a2n.Vista.Results;

namespace a2n.Vista.Examples.Northwind.Views;

/// <summary>
/// Developer-authored <c>App_Json_Context</c> for the Northwind sample's typed Style B view
/// (<see cref="WritableMemoView"/>). It lists that view's DTOs via <c>[JsonSerializable]</c> so their
/// runtime <c>JsonTypeInfo</c> is source-generated (not reflected), making the view's HTTP
/// (de)serialization AOT-clean (Decision Log D124).
/// </summary>
/// <remarks>
/// <para>
/// The set below is the <b>exact</b> type list the generator's <c>VISTA0041</c> guidance names for
/// <see cref="WritableMemoView"/> — a writable view: the projected row (<see cref="MemoRow"/>),
/// <see cref="ViewListResult{TRow}"/> and <see cref="PagedResult{TRow}"/> closed over that row, and the
/// typed write contract (<see cref="MemoWriteModel"/>).
/// </para>
/// <para>
/// It is registered at the composition root through
/// <c>IVistaEndpointBuilder.AddVistaJsonContext(NorthwindJsonContext.Default)</c>, which chains it into
/// the Vista serialization seam ahead of the reflection fallback. Combined with the generated
/// <c>IViewInvoker</c> that closes List/Detail/Create/Update over these types at compile time, the
/// <c>vWritableMemo</c> HTTP path then runs reflection-free.
/// </para>
/// <para>
/// The central-template (Style A) views (<c>vProductCategory</c>, <c>vOrderDetail</c>) project anonymous
/// rows and therefore stay on the reflection serialization fallback by design (D96); they need no entry
/// here.
/// </para>
/// </remarks>
[JsonSerializable(typeof(MemoRow))]
[JsonSerializable(typeof(ViewListResult<MemoRow>))]
[JsonSerializable(typeof(PagedResult<MemoRow>))]
[JsonSerializable(typeof(MemoWriteModel))]
public sealed partial class NorthwindJsonContext : JsonSerializerContext
{
}
