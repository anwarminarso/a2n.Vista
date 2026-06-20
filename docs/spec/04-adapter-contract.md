# Spec 04 — Adapter Contract (Pilar 2, paruh klien)

> Status: **DRAFT**
> Tanggal: 2026-06-19
> Scope: kontrak `IViewAdapter<TRequest, TResponse>` di `a2n.Vista.Core` dan adapter referensi `a2n.Vista.Adapters.DataTablesNet` + `a2n.Vista.Adapters.QueryBuilder`. Menerjemahkan **dua arah**: request grid spesifik ↔ `ViewQueryRequest` (Spec 02 §6.1), dan `ViewQueryResult` (Spec 02 §6.2) ↔ shape response grid. **Bukan** termasuk: binding HTTP/content-negotiation konkret (Spec 05), engine query (Spec 02), authoring View (Spec 01), source generator (Spec 03).

---

## 1. Tujuan

Adapter adalah **"pinggang jam pasir"** Pilar 2: core tidak peduli grid apa yang dipakai, adapter yang menerjemahkan (ROADMAP Pilar 2). Adapter wajib:

1. **Netral di kedua sisi** — hanya bicara `ViewQueryRequest`/`ViewQueryResult`/`ViewMetadata` (Spec 01/02). **Tidak pernah** menyentuh `IQueryable<TSource>` mentah (Spec 01 D27) maupun EF.
2. **Pure & testable** — pemetaan adalah fungsi murni POCO→POCO; bisa di-unit-test tanpa HTTP/DB. Glue HTTP ada di host (Spec 05).
3. **Tag channel dengan benar** — saat membangun `FilterNode`, adapter **wajib** menyetel `FilterOrigin` per leaf (`Filter`/`Search`/`Scope`) sesuai asal (Spec 02 §6.1/§7). Salah tag = salah whitelist = lubang keamanan.
4. **Core-only deps** — paket adapter hanya referensi `a2n.Vista.Core` (ROADMAP D48). Parsing JSON memakai `System.Text.Json` + `JsonSerializerContext` source-gen (AOT-clean).
5. **Paritas migrasi** — adapter DataTables menerima/menghasilkan bentuk wire yang sama dengan DynData supaya klien jQuery DataTables + QueryBuilder migrasi minimal (ref `dyndata-datatables-observed.md`).

## 2. Posisi dalam Arsitektur

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

Host (Spec 05) mengubah `HttpContext` → `AdapterRequest` netral, memanggil adapter, menyerahkan `ViewQueryRequest`+`IViewScope` ke engine, lalu menyerahkan `ViewQueryResult` kembali ke adapter untuk diformat. Adapter tidak tahu HTTP.

| Dokumen | Hubungan |
|---|---|
| `01-view.md` | `ViewMetadata`/`FieldMetadata` (sumber whitelist & operator), `PagedResult` (shape netral default). |
| `02-filter-and-query.md` | `ViewQueryRequest` (output `ToQuery`), `ViewQueryResult` (input `ToResponse`), `FilterOrigin`, `FilterOperator`. |
| `dyndata-datatables-observed.md` | Spesifikasi wire DynData yang ditiru adapter DataTables/QueryBuilder. |
| `05-aspnetcore-mapping.md` | Binding `HttpContext`→`AdapterRequest`, pemilihan adapter (route/Accept), serialisasi `TResponse`. |

## 3. Terminologi

| Istilah | Arti |
|---|---|
| **Adapter** | Implementasi `IViewAdapter<TRequest, TResponse>` untuk satu ekosistem grid. |
| **Metadata adapter** | `IViewMetadataAdapter<TSchema>` — menghasilkan skema grid-spesifik (mis. `metadataQB`) dari `ViewMetadata`. |
| **`AdapterRequest`** | Bag netral berisi form/query values + raw JSON body + viewName. Dibangun host dari HTTP, dikonsumsi adapter (Core-only). |
| **`TRequest`** | POCO request grid-spesifik (mis. `DataTablesQuery`). Hasil `BindRequest`. |
| **`TResponse`** | POCO response grid-spesifik (mis. `DataTablesResponse<T>`). |
| **Channel** | `FilterOrigin` sebuah leaf (Spec 02 §6.1) — adapter yang menentukannya. |

## 4. Non-Goals

- Mekanisme binding `HttpContext`→`AdapterRequest` & content-negotiation → Spec 05.
- Eksekusi query / translasi SQL → Spec 02.
- Generator `JsonSerializerContext` adapter → Spec 03 (kontrak), implementasi di paket adapter.
- Adapter selain referensi (`AgGrid`, `MudBlazor`, dst.) → masing-masing spec/paket sendiri; dokumen ini menetapkan **kontrak** + dua adapter referensi.

## 5. Kontrak Inti (Core)

### 5.1 `IViewAdapter<TRequest, TResponse>`

```csharp
namespace a2n.Vista;

public interface IViewAdapter<TRequest, TResponse>
{
    // Identitas unik untuk resolusi (route segment / Accept). Mis. "datatables".
    string Id { get; }

    // Suffix route opsional untuk paritas migrasi (mis. "datatable" → {root}/{view}/datatable).
    // null → hanya tersedia via negotiation di route {root}/{view}/query (Spec 05).
    string? RouteSuffix { get; }

    // 1) Bag HTTP netral → POCO request typed. Murni; tanpa tipe ASP.NET.
    TRequest BindRequest(AdapterRequest raw);

    // 2) POCO request → request netral engine. WAJIB set FilterOrigin per leaf (§7).
    //    ViewMetadata dipakai untuk: skip kolom non-field, pilih field Searchable, dll.
    ViewQueryRequest ToQuery(TRequest request, ViewMetadata view);

    // 3) Hasil engine → response grid-spesifik. request di-pass kembali untuk echo
    //    (mis. DataTables "draw").
    TResponse ToResponse(ViewQueryResult<object> result, TRequest request, ViewMetadata view);
}

// Bag netral; dibangun host dari HttpContext (Spec 05), dikonsumsi adapter (Core-only).
public sealed record AdapterRequest(
    string ViewName,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,  // form-urlencoded + query string ter-merge
    string? JsonBody);                                          // body application/json bila ada
```

Catatan desain:

- **Tiga langkah terpisah** (`BindRequest`/`ToQuery`/`ToResponse`) supaya tiap fase di-unit-test independen. `BindRequest` murni parsing (string→POCO), `ToQuery` murni semantik (POCO→tree).
- `AdapterRequest.Values` sudah me-merge form & query (host yang gabungkan). Adapter tidak peduli sumbernya form atau query string.
- `ToQuery` menerima `ViewMetadata` agar adapter bisa: (a) menyaring field `Searchable` untuk subtree global search, (b) melewati kolom UI non-field (mis. `Action`), (c) tidak menebak whitelist (validasi tetap di engine §02 §7 — adapter **tidak** meng-enforce, hanya membentuk tree yang benar).

### 5.2 `IViewMetadataAdapter<TSchema>`

```csharp
namespace a2n.Vista;

public interface IViewMetadataAdapter<TSchema>
{
    string Id { get; }
    TSchema ToSchema(ViewMetadata view);   // mis. ViewMetadata → jQuery-QueryBuilder filters[]
}
```

Memisahkan emisi skema (mis. `metadataQB` DynData, AG Grid column defs) dari pemetaan query. Satu paket adapter boleh menyediakan keduanya.

### 5.3 Registrasi

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

Pemilihan adapter saat request (route suffix vs `Accept` header vs `?format=`) ditetapkan di Spec 05. Default: tanpa adapter, route `{root}/{view}/query` mengembalikan `PagedResult<T>` (shape netral Spec 01 §10).

## 6. Invarian Adapter (wajib dipenuhi semua adapter)

1. **One tree, tagged.** Output `ToQuery.Filter` adalah satu `FilterNode`; tiap leaf ber-`FilterOrigin` benar. Top-level = `FilterAnd(searchSubtree?, structuredFilter?, scopeSubtree?)` — masing-masing channel berbeda (Spec 02 §7).
2. **Tidak meng-enforce whitelist.** Adapter **membentuk** tree; engine yang **menolak** (400) bila field/operator tak diizinkan. Adapter tidak boleh diam-diam membuang leaf yang "kelihatan" tak valid (kecuali skip kolom non-field UI seperti `Action`) — supaya error contract konsisten & klien dapat feedback (kontras DynData yang skip diam-diam, Spec 02 D60).
3. **`length=-1`/no-paging ditolak.** Diteruskan apa adanya → engine menolak (Spec 02 §12.1). Adapter tidak boleh "membantu" dengan page size tak terbatas.
4. **`recordsTotal`.** Adapter yang butuh total-tanpa-filter set `IncludeUnfilteredCount = true` di `ViewQueryRequest` (Spec 02 §6.1/§12.2).
5. **Skip kolom non-field.** Kolom UI (mis. DataTables `Action`, `searchable=false orderable=false data=""`) dilewati saat memetakan kolom→field (ref §7 butir 6).
6. **AOT-clean.** Semua DTO grid punya `JsonSerializerContext` source-gen; tidak ada `JsonSerializer.Deserialize(string, Type)` tanpa `JsonTypeInfo` (Spec 01 §9).

## 7. Adapter Referensi — DataTables.NET

Paket `a2n.Vista.Adapters.DataTablesNet`. Target wire = DynData (ref `dyndata-datatables-observed.md` §4–§6). `TRequest = DataTablesQuery`, `TResponse = DataTablesResponse<T>`.

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

    // Parameter ekstra DynData (ref §4.2). usePGSQL diabaikan (Spec 02 D17).
    public string? JsonQB { get; set; }         // jQuery-QueryBuilder JSON (channel Filter)
    public string? ExternalFilter { get; set; } // contextual/scoping JSON (channel Scope)
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

`AdapterRequest.Values` berisi kunci DataTables bracket (`columns[0][data]`, `order[0][dir]`, `search[value]`, …). `BindRequest` mem-parsing kunci ini ke `DataTablesQuery` secara murni (regex index, tanpa model binder ASP.NET). `JsonQB`/`ExternalFilter` diambil dari `Values["jsonQB"]`/`Values["externalFilter"]`. `usePGSQL` di-baca lalu **dibuang** (Spec 02 D17).

### 7.3 `ToQuery` (POCO → `ViewQueryRequest`)

| Input DataTables | Output `ViewQueryRequest` | Channel |
|---|---|---|
| `Start`, `Length` | `Page = Start/Length`, `PageSize = Length` (teruskan `-1` apa adanya → engine tolak) | — |
| `Order[k]` → kolom by index | `Sort: SortSpec(field, Dir=="desc")`, skip kolom non-field (`Action`) | — |
| `Search.Value` (global) | `FilterOr` of `FilterLeaf(field, Contains, value, Origin=Search)` untuk tiap field **`IsSearchable && ClrType==string`** di `ViewMetadata` | **Search** |
| `Columns[i].Search.Value` (per-kolom) | `FilterLeaf(field, Contains, value, Origin=Filter)` (D63) | **Filter** |
| `JsonQB.ruleData` | `FilterNode` tree via QueryBuilder adapter (§8), tiap leaf `Origin=Filter` | **Filter** |
| `ExternalFilter` | subtree via mini-language (§7.4), tiap leaf `Origin=Scope` | **Scope** |
| `usePGSQL` | **diabaikan** | — |

Top-level merge: `FilterAnd(searchSubtree?, perColumnFilters?, jsonQbTree?, externalFilterSubtree?)` (null channel dilewati). `IncludeUnfilteredCount = true` (DataTables butuh `recordsTotal`).

> Adapter membangun subtree global-search **hanya** ke field Searchable string (Spec 01 §8.1) — itu keputusan View, bukan klien. Tapi enforcement final tetap di engine (§6 invarian 2).

### 7.4 `ExternalFilter` mini-language → `Scope` subtree

Mereplikasi `ExternalFilterParser` DynData (ref §5.3) tapi setiap leaf ber-`Origin=Scope` (divalidasi `IsScopable`, Spec 02 §7). Bentuk: JSON object `{ "Field": <spec> }`, semua property di-AND.

| Bentuk nilai | Contoh | Leaf |
|---|---|---|
| skalar polos | `{ "CategoryId": 12 }` | `Equals(CategoryId, 12)` |
| array tanpa operator | `{ "ProductId": [1,2,3] }` | `In(ProductId, [1,2,3])` |
| prefix `=` | `{ "Discontinued": "=1" }` | `Equals(.., 1)` |
| prefix `>`/`>=` | `{ "UnitPrice": "> 100" }` | `GreaterThan(..)` |
| prefix `<`/`<=` | `{ "UnitPrice": "<= 50" }` | `LessThanOrEqual(..)` |
| `%val%` | `{ "ProductName": "%Chai%" }` | `Contains("Chai")` |
| `val%` | `{ "ProductName": "Ch%" }` | `StartsWith("Ch")` |
| `%val` | `{ "ProductName": "%ai" }` | `EndsWith("ai")` |
| array DENGAN operator | `{ "UnitPrice": [">=10","<=100"] }` | `And(>=10, <=100)` (range) |
| polos (tanpa prefix/suffix) | `{ "City": "London" }` | `Equals("London")` |

Aturan array (ref §5.3): jika ada elemen berawalan `>`/`<`/`=` → mode `In` dibatalkan, tiap elemen di-AND sebagai operator tunggal (range). Selain itu array → `In`. Nilai di-`Trim()`.

**Perbedaan tegas dari DynData:** field yang tidak `Scopable` → leaf tetap dibentuk dengan `Origin=Scope`, lalu engine **menolak 400** `scope-field-not-allowed` (Spec 02 §7) — **bukan** skip diam-diam (ref §7 butir 4, Spec 02 D60). Field lookup (mis. `CategoryId`) harus di-`Hidden().Scopable()` di View (Spec 01 §5.6).

### 7.5 `ToResponse`

```csharp
new DataTablesResponse<object> {
    Draw            = request.Draw,                       // echo
    RecordsTotal    = result.UnfilteredRows ?? result.FilteredRows,
    RecordsFiltered = result.FilteredRows,
    Data            = result.Items,
};
```

`RecordsTotal`/`RecordsFiltered` adalah `long` (Spec 01 §10) — klien JS aman selama < 2^53. `Error` diisi hanya untuk jalur error DataTables-native (opsional; default Vista pakai Problem Details HTTP, Spec 02 §15).

## 8. Adapter Referensi — QueryBuilder

Paket `a2n.Vista.Adapters.QueryBuilder`. Dua peran: (a) parse `jsonQB` → `FilterNode` (dipakai DataTables §7.3); (b) `IViewMetadataAdapter` yang emit skema jQuery-QueryBuilder `filters[]` dari `ViewMetadata`.

### 8.1 Parse `jsonQB.ruleData` → `FilterNode`

Struktur rekursif (ref §5.2): `{ condition: "AND"|"OR", rules: [ rule | group ] }`. `AND`→`FilterAnd`, `OR`→`FilterOr`, group bersarang → rekursi. Tiap rule → `FilterLeaf(field, mapOp(operator), value, Origin=Filter)`.

Mapping operator jQuery-QB → `FilterOperator` (ref §6.2):

| jQuery-QB | `FilterOperator` | Catatan |
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

### 8.2 Emit skema (`ViewMetadata` → `metadataQB`)

`ToSchema` menghasilkan `queryBuilderOptions.filters[]` **hanya** dari field `IsFilterable == true` (kontras DynData yang emit semua field), dengan `operators[]` diturunkan dari `AllowedOperators[field]` (kebalikan tabel §8.1) dan `type`/`input` dari `ClrType`. Field `IsHidden` yang tetap `IsFilterable` boleh disertakan atau tidak — **D65** (default: sertakan hanya bila `Scopable` untuk lookup, selain itu skip dari UI builder).

Shape mengikuti DynData (ref §3) supaya komponen jQuery-QueryBuilder klien tak berubah:

```json
{
  "viewName": "vProductCategory",
  "metaData": [ { "FieldName": "...", "FieldLabel": "...", "FieldType": "...", "IsSearchable": true, "IsOrderable": true, "IsPrimaryKey": false } ],
  "queryBuilderOptions": { "filters": [ { "id": "...", "label": "...", "type": "string", "input": "text", "operators": ["equal","contains", "..."] } ] }
}
```

`metaData[].IsSearchable/IsOrderable` dipetakan dari `FieldMetadata.IsSearchable/IsSortable` — kini **mencerminkan whitelist nyata** (default-allow field projection, Spec 01 §4.4), bukan selalu `true` seperti DynData.

## 9. AOT

- POCO grid (`DataTablesQuery`, `DataTablesResponse<T>`, node QueryBuilder) punya `[JsonSerializable]` di `JsonSerializerContext` per paket adapter → deserialisasi `jsonQB`/`externalFilter` AOT-clean.
- Mapping operator = `static readonly` dictionary/switch — tanpa reflection.
- `ToResponse.Data` bertipe `object` (row sudah ter-materialisasi engine, Spec 02 §6.3). Serialisasi item ke JSON memakai `JsonTypeInfo` view (source-gen, Spec 03) untuk typed-DTO; untuk anonymous projection (gaya A) jatuh ke jalur `[RequiresUnreferencedCode]` (Spec 01 §4.5/§9) — konsisten: yang non-AOT adalah *serialisasi anonymous*, bukan adapter-nya.
- Paket adapter referensi `a2n.Vista.Core` saja (ROADMAP D48) — tidak ada EF/ASP.NET di dependency graph.

## 10. Error Model

Adapter **tidak** memproduksi error domain sendiri — error filter/sort/paging dilempar engine (Spec 02 §15) dan dipetakan host ke Problem Details (Spec 05). Adapter hanya:

- Melempar `AdapterBindException` (→ 400 `.../adapter-bind-failed`) bila `BindRequest`/parse `jsonQB`/`externalFilter` gagal sintaksis (JSON rusak, index kolom invalid).
- Opsional: untuk DataTables, host boleh membungkus Problem Details ke `DataTablesResponse.Error` bila klien DataTables-native mengharapkannya (negotiable, Spec 05).

## 11. Decision Log (lanjutan dari Spec 02 D62)

| # | Keputusan | Status | Catatan |
|---|---|---|---|
| D63 | Per-column search DataTables (`columns[i][search][value]`) → `FilterLeaf(Contains, Origin=Filter)`, divalidasi `Filterable` (bukan `Search`). | **Decided** | Menutup Spec 02 §17 #5. Per-kolom = filter terstruktur, bukan global search. |
| D64 | `is_empty` → `IsNull` (non-string) / `Or(IsNull, Equals "")` (string); `is_not_empty` → `FilterNot(...)`. | **Decided** | Menutup Spec 02 §17 #4. |
| D65 | Skema QueryBuilder hanya emit field `IsFilterable`; field `Hidden` disertakan hanya bila `Scopable` (lookup). | **Decided** | §8.2. Kontras DynData (emit semua field). |
| D66 | `IViewAdapter` 3-langkah (`BindRequest`/`ToQuery`/`ToResponse`) + `AdapterRequest` bag netral. Adapter Core-only; HTTP binding di host (Spec 05). | **Decided** | §5.1. Jaga D48 (adapter tanpa ASP.NET). |
| D67 | Adapter **tidak** meng-enforce whitelist; hanya membentuk tree ber-`FilterOrigin` benar. Enforcement & 400 di engine. | **Decided** | §6 invarian 2. Satu sumber kebenaran error. |
| D68 | `ExternalFilter` (contextual) → channel `Scope` (`IsScopable`), bukan `Filter`. Field tak-Scopable → 400, bukan skip diam-diam. | **Decided** | §7.4. Spec 01 D47, Spec 02 D60. |
| D69 | Adapter set `IncludeUnfilteredCount=true` bila grid butuh `recordsTotal`; default neutral shape (`PagedResult`) tidak. | **Decided** | §7.3, Spec 02 §12.2. |
| D70 | `usePGSQL`/`EnableSearchIgnoreCase` dari klien dibuang adapter (provider-detected, Spec 02 §10). | **Decided** | §7.2/§7.3. Spec 01 D17. |

## 12. Open Questions

1. **Pemilihan adapter** (route suffix vs `Accept: application/vnd.datatables+json` vs `?format=`) — final di Spec 05. Kandidat: `RouteSuffix` eksplisit (paritas DynData `/datatable`) + fallback `Accept`.
2. **Per-column filter non-text** DataTables (mis. `columns[i][search][value]="10..50"` range custom) — apakah adapter mem-parse mini-language seperti `externalFilter`? Kandidat: tidak di v1.0 (per-column = `Contains` saja); range pakai QueryBuilder.
3. **Streaming export via adapter** — apakah adapter ikut memformat export grid-spesifik atau export selalu netral (Spec 01 §11 / Spec 07)? Kandidat: export netral, adapter hanya query/response grid.
4. **AG Grid / MudBlazor server-side** sebagai adapter referensi kedua untuk memvalidasi generalisasi kontrak (terutama set-filter → `distinct` endpoint, Spec 01 §14.3). Direncanakan v1.0 (ROADMAP tahap 2).
5. **`is_empty` pada non-string nullable** (mis. `int?`) — `IsNull` saja sudah benar; konfirmasi tidak ada kasus "empty" untuk numeric. (Cenderung tutup: ya, `IsNull`.)

## 13. Next / Forward References

- `03-source-generator.md` — `JsonSerializerContext` & `JsonTypeInfo` per view yang dikonsumsi `ToResponse` (§9).
- `05-aspnetcore-mapping.md` — `HttpContext`→`AdapterRequest`, pemilihan adapter, route konvensi, serialisasi `TResponse`, pemetaan Problem Details (§10, §12 #1).
- `06-typescript-client.md` — klien TS yang memanggil endpoint adapter (shape `ViewQueryRequest`/`DataTablesQuery`).
