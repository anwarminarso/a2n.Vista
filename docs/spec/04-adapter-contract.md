# Spec 04 — Adapter Contract (Pillar 2, client half)

> Status: **IMPLEMENTED (DataTables.NET reference adapter + `IViewAdapter` contract; D111–D114)**
> Date: 2026-06-20 (rev: reconciliation notes against Pillar 1); 2026-06-27 (adapter landed)
> Scope: the `IViewAdapter<TRequest, TResponse>` contract in `a2n.Vista.Core` and the reference adapters `a2n.Vista.Adapters.DataTablesNet` + `a2n.Vista.Adapters.QueryBuilder`. Translates **both directions**: grid-specific request ↔ `ViewQueryRequest` (Spec 02 §6.1), and `ViewQueryResult` (Spec 02 §6.2) ↔ grid response shape. **Not** included: concrete HTTP binding/content-negotiation (Spec 05), query engine (Spec 02), View authoring (Spec 01), source generator (Spec 03).
>
> **Reconciliation note (2026-06-27 — adapter landed; code is authoritative).** The DataTables.NET
> adapter is implemented; this document's design intent is superseded by the code where they differ
> (`docs/PROJECT-STATUS.md` §2.7):
> - The host-facing contract is the **non-generic `IViewAdapter`** + `ViewAdapter<TRequest,TResponse>`
>   base (`a2n.Vista.Core/Adapters/`), so AspNetCore dispatches adapters without referencing the grid
>   package. `ToResponse` consumes a neutral **`AdapterListResult`** (rows + `recordsFiltered`/`recordsTotal`),
>   **not** `ViewQueryResult<object>` (DR6: the engine returns `ViewListResult<TRow>`).
> - **`FilterOrigin` is not per-leaf** (DR9). Instead of "one tree, each leaf tagged", the adapter builds
>   up to **three sub-trees** and places them in the `ViewQueryRequest` `Filter`/`Search`/`Scope` slots
>   (D111); the executor compiles each under its origin. §6 invariant 1 and the per-leaf `Origin=...`
>   columns in §7.3/§7.4 are realized this way. `IncludeUnfilteredCount` (D69) does **not** exist — the
>   unfiltered total is always returned.
> - **HTTP surface (D112):** `POST {route}/datatable` (route suffix); the host builds `AdapterRequest`
>   from query + form-urlencoded (+ JSON body) via `AdapterRequestFactory`; registration is
>   `AddVistaAdapter<TAdapter>()`. A bind failure → 400 `adapter-bind-failed`.
> - **Deferred (D113):** the QueryBuilder schema emitter (`IViewMetadataAdapter`/`metadataQB`) — to be
>   built per grid component. The `jsonQB` parser (Filter channel) is implemented (D114, in the
>   DataTablesNet package).
>
> **Earlier reconciliation note (2026-06-20).** The adapter (Pillar 2) is **not yet implemented**. When it is built,
> align it with the actual Pillar 1 contract (Spec 01 §13.1): the engine result = **`ViewListResult<TRow>`**
> (`Page.TotalRows`=recordsFiltered, `TotalRowsUnfiltered`=recordsTotal), **not** `ViewQueryResult<object>`.
> `FilterLeaf` does **not** carry `Origin`; the channel is determined at compile time (`FilterCompiler.Compile(node, origin, view)`)
> with a 3-value `FilterOrigin` (`Filter`/`Search`/`Scope`). Pillar 1 currently validates the entire tree
> as `Filter`; multi-channel tree support (Search/Scope) from the adapter will enable per-channel validation
> in the engine (see the invariant in §6). `IViewWriter` does not exist (writes live in `IViewExecutor`).

---

## 1. Purpose

The adapter is Pillar 2's **"waist of the hourglass"**: the core does not care which grid is used, the adapter is what translates (ROADMAP Pillar 2). An adapter must:

1. **Be neutral on both sides** — speak only `ViewQueryRequest`/`ViewQueryResult`/`ViewMetadata` (Spec 01/02). It **never** touches the raw `IQueryable<TSource>` (Spec 01 D27) or EF.
2. **Be pure & testable** — the mapping is a pure POCO→POCO function; it can be unit-tested without HTTP/DB. The HTTP glue lives in the host (Spec 05).
3. **Tag the channel correctly** — when building a `FilterNode`, the adapter **must** set the `FilterOrigin` per leaf (`Filter`/`Search`/`Scope`) according to its origin (Spec 02 §6.1/§7). The wrong tag = the wrong whitelist = a security hole.
4. **Core-only deps** — the adapter package references only `a2n.Vista.Core` (ROADMAP D48). JSON parsing uses `System.Text.Json` + `JsonSerializerContext` source-gen (AOT-clean).
5. **Migration parity** — the DataTables adapter accepts/produces the same wire shape as DynData so that jQuery DataTables + QueryBuilder clients migrate with minimal effort (ref `dyndata-datatables-observed.md`).

## 2. Position in the Architecture

```text
   HTTP (Spec 05)                  Adapter (Spec 04)               Engine (Spec 02)
┌────────────────┐  AdapterRequest ┌──────────────┐ ViewQueryRequest ┌─────────────┐
│ HttpContext    │ ───────────────►│ BindRequest  │ ───────────────► │ IViewExecutor│
│ form/json/query│                 │ ToQuery      │                  │  QueryAsync  │
│                │ ◄───────────────│ ToResponse   │ ◄─────────────── │ ViewQueryResult│
└────────────────┘   TResponse     └──────────────┘ ViewQueryResult  └─────────────┘
                                          ▲
                                   ViewMetadata (Spec 01 §5.4)
```

The host (Spec 05) turns the `HttpContext` → a neutral `AdapterRequest`, calls the adapter, hands the `ViewQueryRequest`+`IViewScope` to the engine, then hands the `ViewQueryResult` back to the adapter to be formatted. The adapter does not know about HTTP.

| Document | Relationship |
|---|---|
| `01-view.md` | `ViewMetadata`/`FieldMetadata` (the source of whitelist & operators), `PagedResult` (the default neutral shape). |
| `02-filter-and-query.md` | `ViewQueryRequest` (the output of `ToQuery`), `ViewQueryResult` (the input of `ToResponse`), `FilterOrigin`, `FilterOperator`. |
| `dyndata-datatables-observed.md` | The DynData wire specification mirrored by the DataTables/QueryBuilder adapters. |
| `05-aspnetcore-mapping.md` | `HttpContext`→`AdapterRequest` binding, adapter selection (route/Accept), `TResponse` serialization. |

## 3. Terminology

| Term | Meaning |
|---|---|
| **Adapter** | An implementation of `IViewAdapter<TRequest, TResponse>` for one grid ecosystem. |
| **Adapter metadata** | `IViewMetadataAdapter<TSchema>` — produces a grid-specific schema (e.g. `metadataQB`) from `ViewMetadata`. |
| **`AdapterRequest`** | A neutral bag containing form/query values + raw JSON body + viewName. Built by the host from HTTP, consumed by the adapter (Core-only). |
| **`TRequest`** | A grid-specific request POCO (e.g. `DataTablesQuery`). The result of `BindRequest`. |
| **`TResponse`** | A grid-specific response POCO (e.g. `DataTablesResponse<T>`). |
| **Channel** | The `FilterOrigin` of a leaf (Spec 02 §6.1) — the adapter is what determines it. |

## 4. Non-Goals

- The `HttpContext`→`AdapterRequest` binding mechanism & content-negotiation → Spec 05.
- Query execution / SQL translation → Spec 02.
- The adapter's `JsonSerializerContext` generator → Spec 03 (contract), implementation in the adapter package.
- Adapters other than the reference ones (`AgGrid`, `MudBlazor`, etc.) → each has its own spec/package; this document defines the **contract** + two reference adapters.

## 5. Core Contract (Core)

### 5.1 `IViewAdapter<TRequest, TResponse>`

```csharp
namespace a2n.Vista;

public interface IViewAdapter<TRequest, TResponse>
{
    // Unique identity for resolution (route segment / Accept). E.g. "datatables".
    string Id { get; }

    // Optional route suffix for migration parity (e.g. "datatable" → {root}/{view}/datatable).
    // null → available only via negotiation on the route {root}/{view}/query (Spec 05).
    string? RouteSuffix { get; }

    // 1) Neutral HTTP bag → typed request POCO. Pure; no ASP.NET types.
    TRequest BindRequest(AdapterRequest raw);

    // 2) Request POCO → the engine's neutral request. MUST set FilterOrigin per leaf (§7).
    //    ViewMetadata is used to: skip non-field columns, pick Searchable fields, etc.
    ViewQueryRequest ToQuery(TRequest request, ViewMetadata view);

    // 3) Engine result → grid-specific response. request is passed back for echo
    //    (e.g. DataTables "draw").
    TResponse ToResponse(ViewQueryResult<object> result, TRequest request, ViewMetadata view);
}

// Neutral bag; built by the host from HttpContext (Spec 05), consumed by the adapter (Core-only).
public sealed record AdapterRequest(
    string ViewName,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,  // form-urlencoded + query string merged
    string? JsonBody);                                          // application/json body if present
```

Design notes:

- **Three separate steps** (`BindRequest`/`ToQuery`/`ToResponse`) so each phase can be unit-tested independently. `BindRequest` is pure parsing (string→POCO), `ToQuery` is pure semantics (POCO→tree).
- `AdapterRequest.Values` has already merged form & query (the host combines them). The adapter does not care whether the source is form or query string.
- `ToQuery` receives `ViewMetadata` so the adapter can: (a) filter the `Searchable` fields for the global-search subtree, (b) skip non-field UI columns (e.g. `Action`), (c) not guess the whitelist (validation stays in the engine §02 §7 — the adapter does **not** enforce, it only builds the correct tree).

### 5.2 `IViewMetadataAdapter<TSchema>`

```csharp
namespace a2n.Vista;

public interface IViewMetadataAdapter<TSchema>
{
    string Id { get; }
    TSchema ToSchema(ViewMetadata view);   // e.g. ViewMetadata → jQuery-QueryBuilder filters[]
}
```

Separates schema emission (e.g. DynData's `metadataQB`, AG Grid column defs) from query mapping. A single adapter package may provide both.

### 5.3 Registration

```csharp
services.AddVista(v =>
{
    v.RouteRoot("/api/views");
    v.UseAuthorizer<AppViewAuthorizer>();
    v.RegisterTemplate<NorthwindViews>();

    v.AddAdapter<DataTablesAdapter>();          // IViewAdapter, by Id "datatables"
    v.AddMetadataAdapter<QueryBuilderSchema>(); // IViewMetadataAdapter, by Id "querybuilder"
});
```

Adapter selection at request time (route suffix vs `Accept` header vs `?format=`) is defined in Spec 05. Default: without an adapter, the route `{root}/{view}/query` returns `PagedResult<T>` (the neutral shape, Spec 01 §10).

## 6. Adapter Invariants (mandatory for all adapters)

1. **One tree, tagged.** The output of `ToQuery.Filter` is a single `FilterNode`; each leaf has the correct `FilterOrigin`. Top-level = `FilterAnd(searchSubtree?, structuredFilter?, scopeSubtree?)` — each a different channel (Spec 02 §7).
2. **Do not enforce the whitelist.** The adapter **builds** the tree; the engine is what **rejects** (400) when a field/operator is not allowed. The adapter must not silently drop a leaf that "looks" invalid (except skipping non-field UI columns like `Action`) — so that the error contract stays consistent & the client gets feedback (in contrast to DynData, which silently skips, Spec 02 D60).
3. **`length=-1`/no-paging is rejected.** Passed through as-is → the engine rejects it (Spec 02 §12.1). The adapter must not "help" by using an unbounded page size.
4. **`recordsTotal`.** An adapter that needs the unfiltered total sets `IncludeUnfilteredCount = true` in the `ViewQueryRequest` (Spec 02 §6.1/§12.2).
5. **Skip non-field columns.** UI columns (e.g. DataTables `Action`, `searchable=false orderable=false data=""`) are skipped when mapping columns→fields (ref §7 item 6).
6. **AOT-clean.** Every grid DTO has a `JsonSerializerContext` source-gen; there is no `JsonSerializer.Deserialize(string, Type)` without a `JsonTypeInfo` (Spec 01 §9).

## 7. Reference Adapter — DataTables.NET

The `a2n.Vista.Adapters.DataTablesNet` package. The wire target = DynData (ref `dyndata-datatables-observed.md` §4–§6). `TRequest = DataTablesQuery`, `TResponse = DataTablesResponse<T>`.

### 7.1 POCO

```csharp
namespace a2n.Vista.Adapters.DataTablesNet;

public sealed class DataTablesQuery
{
    public int Draw { get; set; }
    public int Start { get; set; }
    public int Length { get; set; }
    public DtSearch Search { get; set; } = new();
    public List<DtColumn> Columns { get; set; } = new();
    public List<DtOrder> Order { get; set; } = new();

    // Extra DynData parameters (ref §4.2). usePGSQL is ignored (Spec 02 D17).
    public string? JsonQB { get; set; }         // jQuery-QueryBuilder JSON (Filter channel)
    public string? ExternalFilter { get; set; } // contextual/scoping JSON (Scope channel)
}

public sealed class DtSearch { public string Value { get; set; } = ""; public bool Regex { get; set; } }
public sealed class DtColumn
{
    public string Data { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Searchable { get; set; }
    public bool Orderable { get; set; }
    public DtSearch Search { get; set; } = new();
}
public sealed class DtOrder { public int Column { get; set; } public string Dir { get; set; } = "asc"; }

public sealed class DataTablesResponse<T>
{
    public int Draw { get; set; }
    public long RecordsTotal { get; set; }     // = ViewQueryResult.UnfilteredRows
    public long RecordsFiltered { get; set; }  // = ViewQueryResult.FilteredRows
    public IReadOnlyList<T> Data { get; set; } = Array.Empty<T>();
    public string? Error { get; set; }
}
```

### 7.2 `BindRequest` (form-urlencoded → POCO)

`AdapterRequest.Values` contains the DataTables bracket keys (`columns[0][data]`, `order[0][dir]`, `search[value]`, …). `BindRequest` parses these keys into a `DataTablesQuery` purely (regex index, without the ASP.NET model binder). `JsonQB`/`ExternalFilter` are taken from `Values["jsonQB"]`/`Values["externalFilter"]`. `usePGSQL` is read and then **discarded** (Spec 02 D17).

### 7.3 `ToQuery` (POCO → `ViewQueryRequest`)

| DataTables input | `ViewQueryRequest` output | Channel |
|---|---|---|
| `Start`, `Length` | `Page = Start/Length`, `PageSize = Length` (pass `-1` through as-is → the engine rejects it) | — |
| `Order[k]` → column by index | `Sort: SortSpec(field, Dir=="desc")`, skip non-field columns (`Action`) | — |
| `Search.Value` (global) | `FilterOr` of `FilterLeaf(field, Contains, value, Origin=Search)` for each **`IsSearchable && ClrType==string`** field in `ViewMetadata` | **Search** |
| `Columns[i].Search.Value` (per-column) | `FilterLeaf(field, Contains, value, Origin=Filter)` (D63) | **Filter** |
| `JsonQB.ruleData` | `FilterNode` tree via the QueryBuilder adapter (§8), each leaf `Origin=Filter` | **Filter** |
| `ExternalFilter` | subtree via the mini-language (§7.4), each leaf `Origin=Scope` | **Scope** |
| `usePGSQL` | **ignored** | — |

Top-level merge: `FilterAnd(searchSubtree?, perColumnFilters?, jsonQbTree?, externalFilterSubtree?)` (a null channel is skipped). `IncludeUnfilteredCount = true` (DataTables needs `recordsTotal`).

> The adapter builds the global-search subtree **only** over Searchable string fields (Spec 01 §8.1) — that is a View decision, not the client's. But the final enforcement still lives in the engine (§6 invariant 2).

### 7.4 `ExternalFilter` mini-language → `Scope` subtree

Replicates DynData's `ExternalFilterParser` (ref §5.3) but each leaf has `Origin=Scope` (validated by `IsScopable`, Spec 02 §7). Form: a JSON object `{ "Field": <spec> }`, all properties AND-ed.

| Value form | Example | Leaf |
|---|---|---|
| plain scalar | `{ "CategoryId": 12 }` | `Equals(CategoryId, 12)` |
| array without an operator | `{ "ProductId": [1,2,3] }` | `In(ProductId, [1,2,3])` |
| prefix `=` | `{ "Discontinued": "=1" }` | `Equals(.., 1)` |
| prefix `>`/`>=` | `{ "UnitPrice": "> 100" }` | `GreaterThan(..)` |
| prefix `<`/`<=` | `{ "UnitPrice": "<= 50" }` | `LessThanOrEqual(..)` |
| `%val%` | `{ "ProductName": "%Chai%" }` | `Contains("Chai")` |
| `val%` | `{ "ProductName": "Ch%" }` | `StartsWith("Ch")` |
| `%val` | `{ "ProductName": "%ai" }` | `EndsWith("ai")` |
| array WITH an operator | `{ "UnitPrice": [">=10","<=100"] }` | `And(>=10, <=100)` (range) |
| plain (no prefix/suffix) | `{ "City": "London" }` | `Equals("London")` |

Array rule (ref §5.3): if any element starts with `>`/`<`/`=` → the `In` mode is cancelled, each element is AND-ed as a single operator (range). Otherwise an array → `In`. Values are `Trim()`-ed.

**Firm difference from DynData:** a field that is not `Scopable` → the leaf is still built with `Origin=Scope`, then the engine **rejects with 400** `scope-field-not-allowed` (Spec 02 §7) — **not** a silent skip (ref §7 item 4, Spec 02 D60). A lookup field (e.g. `CategoryId`) must be `Hidden().Scopable()` in the View (Spec 01 §5.6).

### 7.5 `ToResponse`

```csharp
new DataTablesResponse<object> {
    Draw            = request.Draw,                       // echo
    RecordsTotal    = result.UnfilteredRows ?? result.FilteredRows,
    RecordsFiltered = result.FilteredRows,
    Data            = result.Items,
};
```

`RecordsTotal`/`RecordsFiltered` are `long` (Spec 01 §10) — the JS client is safe as long as < 2^53. `Error` is filled only for the DataTables-native error path (optional; the Vista default uses HTTP Problem Details, Spec 02 §15).

## 8. Reference Adapter — QueryBuilder

The `a2n.Vista.Adapters.QueryBuilder` package. Two roles: (a) parse `jsonQB` → `FilterNode` (used by DataTables §7.3); (b) an `IViewMetadataAdapter` that emits the jQuery-QueryBuilder `filters[]` schema from `ViewMetadata`.

### 8.1 Parse `jsonQB.ruleData` → `FilterNode`

A recursive structure (ref §5.2): `{ condition: "AND"|"OR", rules: [ rule | group ] }`. `AND`→`FilterAnd`, `OR`→`FilterOr`, a nested group → recursion. Each rule → `FilterLeaf(field, mapOp(operator), value, Origin=Filter)`.

jQuery-QB → `FilterOperator` operator mapping (ref §6.2):

| jQuery-QB | `FilterOperator` | Notes |
|---|---|---|
| `equal` | `Equals` | |
| `not_equal` | `NotEquals` | |
| `begins_with` | `StartsWith` | |
| `ends_with` | `EndsWith` | |
| `contains` | `Contains` | |
| `is_empty` | `IsNull` (non-string) / `Or(IsNull, Equals "")` (string) | **D64** |
| `is_not_empty` | `FilterNot(<is_empty>)` | **D64** |
| `less` / `less_or_equal` | `LessThan` / `LessThanOrEqual` | numeric/date |
| `greater` / `greater_or_equal` | `GreaterThan` / `GreaterThanOrEqual` | |
| `between` | `Between` (value = `[lo,hi]`) | Spec 02 §8.2 |
| `not_between` | `FilterNot(Between)` | |
| `in` / `not_in` | `In` / `FilterNot(In)` | value = array |

### 8.2 Emit the schema (`ViewMetadata` → `metadataQB`)

`ToSchema` produces `queryBuilderOptions.filters[]` **only** from fields where `IsFilterable == true` (in contrast to DynData, which emits every field), with `operators[]` derived from `AllowedOperators[field]` (the inverse of the §8.1 table) and `type`/`input` from `ClrType`. An `IsHidden` field that is still `IsFilterable` may or may not be included — **D65** (default: include it only when `Scopable` for a lookup, otherwise skip it from the UI builder).

The shape follows DynData (ref §3) so the client's jQuery-QueryBuilder component does not change:

```json
{
  "viewName": "vProductCategory",
  "metaData": [ { "FieldName": "...", "FieldLabel": "...", "FieldType": "...", "IsSearchable": true, "IsOrderable": true, "IsPrimaryKey": false } ],
  "queryBuilderOptions": { "filters": [ { "id": "...", "label": "...", "type": "string", "input": "text", "operators": ["equal","contains", "..."] } ] }
}
```

`metaData[].IsSearchable/IsOrderable` are mapped from `FieldMetadata.IsSearchable/IsSortable` — they now **reflect the real whitelist** (default-allow field projection, Spec 01 §4.4), rather than always being `true` as in DynData.

## 9. AOT

- The grid POCOs (`DataTablesQuery`, `DataTablesResponse<T>`, QueryBuilder nodes) have `[JsonSerializable]` in a `JsonSerializerContext` per adapter package → deserialization of `jsonQB`/`externalFilter` is AOT-clean.
- The operator mapping = a `static readonly` dictionary/switch — without reflection.
- `ToResponse.Data` is typed as `object` (the row is already materialized by the engine, Spec 02 §6.3). Serializing the item to JSON uses the view's `JsonTypeInfo` (source-gen, Spec 03) for the typed-DTO; for an anonymous projection (Style A) it falls onto the `[RequiresUnreferencedCode]` path (Spec 01 §4.5/§9) — consistently: what is non-AOT is the *anonymous serialization*, not the adapter itself.
- The reference adapter package references `a2n.Vista.Core` only (ROADMAP D48) — there is no EF/ASP.NET in the dependency graph.

## 10. Error Model

The adapter does **not** produce its own domain errors — filter/sort/paging errors are thrown by the engine (Spec 02 §15) and mapped by the host to Problem Details (Spec 05). The adapter only:

- Throws `AdapterBindException` (→ 400 `.../adapter-bind-failed`) when `BindRequest`/parsing `jsonQB`/`externalFilter` fails syntactically (broken JSON, invalid column index).
- Optional: for DataTables, the host may wrap Problem Details into `DataTablesResponse.Error` if the DataTables-native client expects it (negotiable, Spec 05).

## 11. Decision Log (continued from Spec 02 D62)

| # | Decision | Status | Notes |
|---|---|---|---|
| D63 | DataTables per-column search (`columns[i][search][value]`) → `FilterLeaf(Contains, Origin=Filter)`, validated as `Filterable` (not `Search`). | **Decided** | Closes Spec 02 §17 #5. Per-column = structured filter, not global search. |
| D64 | `is_empty` → `IsNull` (non-string) / `Or(IsNull, Equals "")` (string); `is_not_empty` → `FilterNot(...)`. | **Decided** | Closes Spec 02 §17 #4. |
| D65 | The QueryBuilder schema emits only `IsFilterable` fields; a `Hidden` field is included only when `Scopable` (lookup). | **Decided** | §8.2. In contrast to DynData (emits every field). |
| D66 | `IViewAdapter` is 3-step (`BindRequest`/`ToQuery`/`ToResponse`) + the neutral `AdapterRequest` bag. The adapter is Core-only; HTTP binding lives in the host (Spec 05). | **Decided** | §5.1. Upholds D48 (adapter without ASP.NET). |
| D67 | The adapter does **not** enforce the whitelist; it only builds a tree with the correct `FilterOrigin`. Enforcement & 400 live in the engine. | **Decided** | §6 invariant 2. A single source of truth for errors. |
| D68 | `ExternalFilter` (contextual) → the `Scope` channel (`IsScopable`), not `Filter`. A non-Scopable field → 400, not a silent skip. | **Decided** | §7.4. Spec 01 D47, Spec 02 D60. |
| D69 | The adapter sets `IncludeUnfilteredCount=true` when the grid needs `recordsTotal`; the default neutral shape (`PagedResult`) does not. | **Decided** | §7.3, Spec 02 §12.2. |
| D70 | `usePGSQL`/`EnableSearchIgnoreCase` from the client are discarded by the adapter (provider-detected, Spec 02 §10). | **Decided** | §7.2/§7.3. Spec 01 D17. |

## 12. Open Questions

1. **Adapter selection** (route suffix vs `Accept: application/vnd.datatables+json` vs `?format=`) — final in Spec 05. Candidate: an explicit `RouteSuffix` (DynData parity `/datatable`) + an `Accept` fallback.
2. **Non-text per-column filter** in DataTables (e.g. `columns[i][search][value]="10..50"` a custom range) — should the adapter parse a mini-language like `externalFilter`? Candidate: not in v1.0 (per-column = `Contains` only); use QueryBuilder for ranges.
3. **Streaming export via the adapter** — should the adapter also format a grid-specific export, or is the export always neutral (Spec 01 §11 / Spec 07)? Candidate: a neutral export, the adapter handles only the grid query/response.
4. **AG Grid / MudBlazor server-side** as a second reference adapter to validate the generalization of the contract (especially set-filter → the `distinct` endpoint, Spec 01 §14.3). Planned for v1.0 (ROADMAP stage 2).
5. **`is_empty` on a non-string nullable** (e.g. `int?`) — `IsNull` alone is already correct; confirm there is no "empty" case for numeric. (Leaning toward closing: yes, `IsNull`.)

## 13. Next / Forward References

- `03-source-generator.md` — the per-view `JsonSerializerContext` & `JsonTypeInfo` consumed by `ToResponse` (§9).
- `05-aspnetcore-mapping.md` — `HttpContext`→`AdapterRequest`, adapter selection, route conventions, `TResponse` serialization, Problem Details mapping (§10, §12 #1).
- `06-typescript-client.md` — the TS client that calls the adapter endpoint (shape `ViewQueryRequest`/`DataTablesQuery`).
