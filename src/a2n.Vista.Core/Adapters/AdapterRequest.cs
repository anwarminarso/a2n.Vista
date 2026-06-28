using System.Collections.Generic;

namespace a2n.Vista.Adapters;

/// <summary>
/// A neutral, transport-agnostic bag of request data handed to an <see cref="IViewAdapter"/>. The host
/// (AspNetCore) builds it from the incoming request — merging the query string and any form-urlencoded
/// body into <see cref="Values"/> and capturing a JSON body in <see cref="JsonBody"/> — so the adapter
/// itself stays free of ASP.NET types (Decision Log D48/D66, Spec 04 §5.1).
/// </summary>
/// <param name="ViewName">The registered view the request targets.</param>
/// <param name="Values">
/// The merged form + query values, keyed by parameter name (a key may map to multiple values). For
/// DataTables this carries the bracket keys (<c>columns[0][data]</c>, <c>order[0][dir]</c>,
/// <c>search[value]</c>, …).
/// </param>
/// <param name="JsonBody">The raw <c>application/json</c> request body when present; otherwise <see langword="null"/>.</param>
public sealed record AdapterRequest(
    string ViewName,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    string? JsonBody);
