# Spec 05 — ASP.NET Core Mapping (komposisi HTTP: read, write, auth, error)

> Status: **DRAFT**
> Tanggal: 2026-06-20
> Scope: paket `a2n.Vista.AspNetCore`. Menjembatani HTTP ke pipeline netral: endpoint mapping (`MapView<TView>()`/`MapVistaViews()`), binding `HttpContext` → `AdapterRequest` (Spec 04 §5.1), komposisi auth → `IViewScope` → `IViewExecutor` (Spec 02 §5), **write/CRUD path** (create/update/delete), concurrency (`If-Match`/`ETag`), **bulk ops**, Detail by-key, export endpoint, metadata endpoint, error model HTTP (RFC 7807 konkret), dan registrasi OpenAPI. **Bukan** termasuk: authoring View (Spec 01), engine baca (Spec 02), source generator (Spec 03), pemetaan filter grid (Spec 04), implementasi EF (`IViewExecutor` concrete) — itu `a2n.Vista.EntityFrameworkCore`.

---

## 1. Tujuan

Spec ini adalah **composition root** Vista di sisi HTTP. Di sinilah tiga port yang sengaja dipisah (Spec 01 D48) bertemu via DI — `IViewExecutor` (Core/EF, Spec 02), `IViewAdapter` (Core, Spec 04), `IViewExporter` (Core, Spec 01 §11) — tanpa membuat Core/EF tahu apa pun tentang HTTP.

`a2n.Vista.AspNetCore` wajib:

1. **Tipis & deklaratif** — endpoint adalah hasil registrasi `ViewMetadata` (Spec 01 §5.4), bukan controller yang ditulis tangan. Satu jalur kode untuk semua view.
2. **Host-only deps** — satu-satunya paket Vista yang boleh menyentuh `HttpContext`. **Tidak** referensi EF (Spec 01 D48); CRUD diakses lewat port `IViewWriter`/`IViewExecutor`.
3. **Secure-by-default di gerbang** — tiap request melewati `IViewAuthorizer` (§6) sebelum menyentuh data. Tanpa authorizer terdaftar → default allow + warning startup (Spec 01 D43), bukan diam-diam.
4. **Satu sumber error** — semua kegagalan (validasi engine Spec 02 §15, bind adapter Spec 04 §10, auth, concurrency) dipetakan ke satu bentuk RFC 7807 (§9).
5. **AOT-clean** — endpoint didaftarkan dari registry source-gen (Spec 03 §7), tanpa reflection scan; tidak ada MVC controller discovery di hot path (§11).

## 2. Posisi dalam Arsitektur

```text
                          a2n.Vista.AspNetCore (Spec 05)
   ┌──────────────────────────────────────────────────────────────────────┐
   │ MapVistaViews() ─ per ViewMetadata (Spec 03 §7 registry) ─ map routes │
   │                                                                        │
   │  HTTP request                                                          │
   │   │  1. bind HttpContext → AdapterRequest        (Spec 04 §5.1)        │
   │   │  2. select adapter (route suffix/Accept/?fmt) (§5)                 │
   │   │  3. IViewAuthorizer.IsAllowedAsync(ctx)       (§6)  ── 403         │
   │   │  4. IViewAuthorizer.ShapeQuery(ctx, scope)    (§6)  → IViewScope   │
   │   ▼                                                                    │
   │  READ ─ adapter.ToQuery → IViewExecutor.QueryAsync ─ adapter.ToResponse│
   │  WRITE─ deserialize TCrud → IViewWriter.Create/Update/Delete           │
   │   │                                                                    │
   │   └── error → ProblemDetails (RFC 7807, §9)                            │
   └──────────────┬─────────────────────────────────┬───────────────────────┘
                  │ IViewExecutor / IViewWriter (port, Core) │ via DI
                  ▼                                 ▼
       a2n.Vista.EntityFrameworkCore        (resolusi di composition root)
```

| Dokumen | Hubungan |
|---|---|
| `01-view.md` | **Input.** `ViewMetadata` (route, facet, limits, auth), endpoint table §12.3, error klasifikasi §14.1, concurrency §14.2, `IViewAuthorizer`/`ViewAuthContext`/`ViewFacet` (tipe didefinisikan **di sini**, §6, lokasi D48). |
| `02-filter-and-query.md` | **Konsumsi.** Memanggil `IViewExecutor.QueryAsync`/`GetByKeyAsync` setelah membangun `ViewQueryExecution` (Spec 02 §6.3). Memetakan error engine §15 → HTTP. |
| `03-source-generator.md` | **Konsumsi.** Auto-registration (§7), OpenAPI document model (§10), `CompiledView.ApplyWritable` (write), `KeySelector` (Detail by-key). |
| `04-adapter-contract.md` | **Konsumsi.** Membangun `AdapterRequest`, memilih adapter, memanggil `BindRequest`/`ToQuery`/`ToResponse`, memetakan `AdapterBindException` → 400. |
| `dyndata-datatables-observed.md` | Paritas endpoint (`/datatable`, `/create`, `/update`, `/delete`) untuk migrasi. |

Pembagian paket (Spec 01 D48): `IViewWriter` (port tulis) hidup di **Core** seperti `IViewExecutor`; implementasi EF (CRUD + bulk via `ExecuteUpdate/DeleteAsync`) di **`a2n.Vista.EntityFrameworkCore`**. `IViewAuthorizer` (HTTP-bound, membawa `HttpContext`) hidup di **AspNetCore**.

## 3. Terminologi

| Istilah | Arti |
|---|---|
| **Endpoint group** | Kumpulan route untuk satu View, di-map dari satu `ViewMetadata`: query, detail, write, export, metadata. |
| **Composition root** | Titik DI di app host tempat `IViewExecutor`/`IViewWriter` (EF) + `IViewAuthorizer` (app) + adapter di-resolve. |
| **Negotiation** | Pemilihan adapter response untuk facet List (route suffix vs `Accept` vs `?format=`). §5. |
| **Write facet** | Operasi create/update/delete (Spec 01 §4.6); hanya untuk View ber-`CrudType` (typed). |
| **Trusted scope** | `IViewScope` yang diisi `IViewAuthorizer.ShapeQuery` (server-trusted, channel `Trusted`, Spec 02 §7). |
| **ETag** | Representasi string concurrency token (Spec 01 §14.2), dibawa header `ETag`/`If-Match`. |

## 4. Non-Goals

- Implementasi `IViewExecutor`/`IViewWriter` concrete (EF, provider detection, `ExecuteUpdate/Delete`) → `a2n.Vista.EntityFrameworkCore` (kandidat Spec 09).
- Semantik engine baca (validasi, coercion, paging) → Spec 02.
- Pemetaan filter grid spesifik (DataTables/QueryBuilder) → Spec 04.
- Cara generator **menghasilkan** registry/OpenAPI model → Spec 03; di sini hanya **konsumsi**-nya.
- TypeScript client → Spec 06. Export format detail → Spec 07.
- Identity/authentication (siapa user) — Vista mendelegasi ke ASP.NET Core auth; Vista hanya **authorize** (view, facet, user) via `IViewAuthorizer`.

## 5. Endpoint Mapping & Routing

### 5.1 Registrasi & mapping

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDb>(/* ... */);
builder.Services.AddVista(v =>
{
    v.RouteRoot("/api/views");                 // route global (Spec 01 §5.6)
    v.UseAuthorizer<AppViewAuthorizer>();      // satu pintu auth (§6)
    v.RegisterTemplate<NorthwindViews>();      // gaya A
    v.Register<CustomerListView>();            // gaya B (juga ditambah source-gen, Spec 03 §7)
    v.AddAdapter<DataTablesAdapter>();         // Spec 04 §5.3
});
builder.Services.AddVistaEntityFrameworkCore<AppDb>();  // daftarkan IViewExecutor/IViewWriter (EF)

var app = builder.Build();
app.MapVistaViews();                 // map SEMUA view terdaftar
// atau eksplisit (codegen-friendly):
app.MapView<CustomerListView>();
app.Run();
```

- `MapVistaViews()` meng-iterasi `IViewRegistry.All` (di-isi module initializer source-gen, Spec 03 §7) dan memetakan satu endpoint group per `ViewMetadata`. AOT-clean: tidak ada controller discovery.
- `MapView<TView>()` memetakan satu view eksplisit (idempoten dengan `MapVistaViews`; dedup by view name).
- Mengembalikan `IEndpointConventionBuilder` agar konsumen bisa melampirkan konvensi ASP.NET standar (rate limit, CORS, output cache) — **tapi bukan** `RequireAuthorization` per-view (auth lewat `IViewAuthorizer`, §6).

### 5.2 Konvensi route (turunan, Spec 01 §12.3)

`root = RouteRoot` (default `/api/views`), `{v} = viewName`, `{key}` = primary key (Spec 03 `KeySelector`).

| Facet | Method + Route | Syarat | Spec |
|---|---|---|---|
| List/query | `POST {root}/{v}/query` | selalu | 02, 04 |
| List (paritas DT) | `POST {root}/{v}/{suffix}` (mis. `/datatable`) | adapter ber-`RouteSuffix` | 04 §5.1 |
| Detail by-key | `GET {root}/{v}/{key}` | selalu (fallback List-by-PK, Spec 01 §4.6/D49) | 02 §6.3 |
| Metadata | `GET {root}/{v}/metadata` | selalu | §8 |
| Metadata (adapter) | `GET {root}/{v}/metadata/{adapterId}` | ada `IViewMetadataAdapter` | 04 §5.2 |
| Export | `POST {root}/{v}/export?format=csv\|xlsx` | ada exporter | §7.5, Spec 07 |
| Create | `POST {root}/{v}` | `CrudType != null` | §7.2 |
| Update | `PUT {root}/{v}/{key}` | `CrudType != null` | §7.3 |
| Delete | `DELETE {root}/{v}/{key}` | `CrudType != null` | §7.4 |
| Bulk update | `PATCH {root}/{v}/bulk` | CRUD + `AllowBulk` | §7.6 |
| Bulk delete | `POST {root}/{v}/bulk-delete` | CRUD + `AllowBulk` | §7.6 |
| Distinct (stub) | `GET {root}/{v}/distinct/{field}` | direservasi (Spec 01 §14.3) | v1.x |

Catatan desain:

- **List-query (`POST .../query`) dipisah dari Create (`POST .../`)** untuk menghindari MVC/Minimal-API routing collision (Spec 01 D32). Keduanya `POST`, path berbeda.
- **`query` pakai POST** (bukan GET) karena `FilterNode` tree (Spec 01 §8) terlalu kompleks untuk query string dan bisa melebihi batas URL.
- Detail/Update/Delete memakai `{key}` tunggal di v1.0. PK majemuk → encoding di §7.7 (sejalan Spec 03 §17 #3).

### 5.3 Binding `HttpContext` → `AdapterRequest`

Host membangun `AdapterRequest` netral (Spec 04 §5.1) lalu menyerahkannya ke adapter. Aturan merge `Values`:

1. **`POST .../query` form-urlencoded** (DataTables klasik): baca `request.Form` → `Values` (kunci bracket `columns[0][data]` apa adanya, Spec 04 §7.2). Query string di-merge (query menang untuk kunci yang sama? **tidak** — form menang; query hanya menambah kunci yang tak ada di form — D83).
2. **`POST .../query` `application/json`**: seluruh body masuk `AdapterRequest.JsonBody`; `Values` = query string saja.
3. **Pemilihan sumber** berdasarkan `Content-Type`; campuran (form + JSON body) tidak didukung → 415.
4. **Batas ukuran body** di-cap (`MaxRequestBodyBytes`, default 1 MiB untuk query/write; export request kecil) → 413 bila lebih. Anti-DoS, melengkapi guard kompleksitas tree engine (Spec 02 §8.3).

### 5.4 Pemilihan adapter (negotiation) — D84

Hanya relevan untuk facet **List** (Detail/Write selalu shape netral Vista). Urutan resolusi (pertama yang cocok menang):

1. **Route suffix** — request ke `{root}/{v}/{suffix}` memilih adapter ber-`RouteSuffix == suffix` (mis. `/datatable` → `DataTablesAdapter`). Paritas DynData; eksplisit, deterministik.
2. **`Accept` header** — `{root}/{v}/query` dengan `Accept: application/vnd.vista.datatables+json` memilih adapter by media type terdaftar.
3. **`?format=`** — `{root}/{v}/query?format=datatables` (escape hatch untuk klien yang tak bisa set header).
4. **Default** — tanpa ketiganya → shape netral `PagedResult<T>` (Spec 01 §10), tanpa adapter.

Tepat satu adapter terpilih per request. `?format=` tak dikenal / suffix tak terdaftar → 404 (route tak ada) atau 406 (`Accept` tak match) — D84.

## 6. Authorization (`IViewAuthorizer`) — definisi tipe

Tipe ini **didefinisikan di `a2n.Vista.AspNetCore`** (HTTP-bound, Spec 01 D48). Spec 01 §5.6 mendeklarasikan kontraknya; di sini bentuk final + semantik runtime.

```csharp
namespace a2n.Vista.AspNetCore;

public enum ViewFacet { List, Detail, Export, Create, Update, Delete }

public sealed record ViewAuthContext(
    ClaimsPrincipal User,
    string ViewName,
    ViewFacet Facet,
    HttpContext Http,
    IServiceProvider Services);

public interface IViewAuthorizer
{
    // Gerbang allow/deny per (view, facet, user). Dipanggil tiap request SEBELUM data disentuh.
    ValueTask<bool> IsAllowedAsync(ViewAuthContext ctx);

    // Inject filter row server-trusted (tenant, ownership) — terpusat, tak bisa di-bypass klien.
    // Leaf yang ditambahkan masuk channel Trusted (tidak divalidasi, Spec 02 §7).
    void ShapeQuery(ViewAuthContext ctx, IViewScope scope);
}
```

### 6.1 Posisi di pipeline (read & write)

Setiap endpoint group menjalankan, **sebelum** menyentuh `IViewExecutor`/`IViewWriter`:

```text
1. resolve ViewMetadata (registry)
2. ctx = ViewAuthContext(User, viewName, facet, HttpContext, Services)
3. if (!await authorizer.IsAllowedAsync(ctx)) → 403 forbidden        (Spec 01 §14.1)
4. scope = new ViewScope();  authorizer.ShapeQuery(ctx, scope)        (read & write)
5a. READ : exec = new ViewQueryExecution(viewName, request, scope, sp)
           → IViewExecutor.QueryAsync(exec)                          (Spec 02 §6.3)
5b. WRITE: IViewWriter.Create/Update/Delete(..., scope, ...)          (§7) — scope membatasi baris yang boleh disentuh
```

- **`ShapeQuery` juga berlaku untuk write** (D85): update/delete by-key di-`AND` dengan trusted filter, jadi user tidak bisa mengubah baris di luar tenant/ownership-nya walau tahu PK-nya. Tanpa ini, write akan bocor lintas-tenant.
- **`Facet` granular** (`Create`/`Update`/`Delete` terpisah, bukan satu `Write`) supaya authorizer bisa, mis., izinkan `Update` tapi tolak `Delete`.

### 6.2 Default & warning

| Kondisi | Perilaku |
|---|---|
| `UseAuthorizer<T>` terdaftar | `T` satu-satunya gerbang. Facet yang `IsAllowedAsync`-nya `false` → 403. |
| `UseAuthorizer` tidak dipanggil | **Default allow** (paritas DynData, Spec 01 D43). Warning startup: `"no IViewAuthorizer registered — all views are publicly accessible"`. `ShapeQuery` no-op (tanpa trusted filter). |

Sengaja **bukan** fail-closed (Spec 01 D43). Dokumentasi produksi mewajibkan `UseAuthorizer`. `IsAllowedAsync` dipanggil **sekali per request** (bukan per-baris); hasil tidak di-cache lintas request (klaim user bisa berubah).

### 6.3 Hubungan dengan ASP.NET Core auth

- **Authentication** (siapa user) tetap pipeline ASP.NET standar (`UseAuthentication`); `ctx.User` adalah `HttpContext.User`.
- `IViewAuthorizer` murni **authorization** (boleh/tidak). Implementor bebas memakai `IAuthorizationService`, policy, atau klaim langsung di dalam `IsAllowedAsync`.
- User belum terotentikasi tapi `IsAllowedAsync` butuh identitas → implementor kembalikan `false`; host memetakan ke **401** (bukan 403) bila `ctx.User.Identity?.IsAuthenticated == false`, selain itu **403** (D86).

## 7. Write / CRUD Path

Write **hanya** untuk View ber-`CrudType` (typed DTO, Spec 01 §4.5 invarian). Anonymous-only view tidak punya route write (tak di-map). Eksekusi memakai `CompiledView.ApplyWritable` (Spec 03 §8) — assignment `TCrud → TEntity` compile-time, **tanpa** reflection / mass-assignment.

### 7.1 Port `IViewWriter` (Core)

Port tulis non-generik, sejalan `IViewExecutor` (Spec 02 §6.3). `TCrud`/`TEntity` di-erase ke `object` di boundary; materialisasi typed via delegate source-gen.

```csharp
namespace a2n.Vista;

public interface IViewWriter
{
    // Create: bind crud (deserialized object) → entity baru via ApplyWritable, SaveChanges.
    // Kembalikan key entity baru (untuk Location header) + ETag (bila ada concurrency token).
    Task<WriteResult> CreateAsync(ViewWriteExecution exec, CancellationToken ct = default);

    // Update by-key: load entity (di-AND dengan scope), apply concurrency check, ApplyWritable, SaveChanges.
    Task<WriteResult> UpdateAsync(ViewWriteExecution exec, object key, string? ifMatch, CancellationToken ct = default);

    // Delete by-key: load entity (di-AND dengan scope), concurrency check, remove, SaveChanges.
    Task<WriteResult> DeleteAsync(string viewName, object key, IViewScope scope, string? ifMatch, IServiceProvider sp, CancellationToken ct = default);
}

public sealed record ViewWriteExecution(
    string ViewName,
    object Crud,            // TCrud ter-deserialisasi (object di boundary)
    IViewScope Scope,       // trusted filter dari ShapeQuery (§6.1)
    IServiceProvider Services);

public sealed record WriteResult(
    WriteStatus Status,     // Ok, NotFound, ConcurrencyConflict, ValidationFailed
    object? Key,            // key entity (Create → key baru; Update/Delete → echo)
    string? ETag,           // token concurrency ter-encode (§7.8), null bila view tak punya token
    IReadOnlyList<ValidationError>? Errors); // bila ValidationFailed

public enum WriteStatus { Ok, NotFound, ConcurrencyConflict, ValidationFailed }
```

Boundary `object` konsisten dengan Spec 02 §6.3 dan menjaga Core/AspNetCore bebas dari generic monomorphization via reflection.

### 7.2 Create — `POST {root}/{v}`

```text
1. auth: IsAllowedAsync(facet=Create) → 403 ; ShapeQuery → scope
2. deserialize body → TCrud via JsonTypeInfo source-gen (Spec 03 §9)   [415 bila bukan JSON; 400 bila malformed]
3. validate TCrud (IViewCrudValidator, Spec 01 §5.2) → 400 .../validation bila gagal
4. IViewWriter.CreateAsync(exec) → ApplyWritable(crud, newEntity); SaveChanges
5. 201 Created
   - Location: {root}/{v}/{newKey}
   - ETag: "<token>"   (bila WithConcurrencyToken)
   - body: representasi Detail (GET-by-key shape) entity baru
```

- **Hanya field ter-`MapWritable` yang di-set** (Spec 01 D25). Field `TCrud` lain diabaikan (default) atau ditolak (strict mode, Spec 03 VISTA0011). Field entity di luar whitelist **tidak pernah** tersentuh klien — ini inti anti mass-assignment.
- Create **tidak** memakai `key` dari klien; PK di-generate DB (identity) atau di-set server.

### 7.3 Update — `PUT {root}/{v}/{key}`

```text
1. auth: IsAllowedAsync(facet=Update) → 403 ; ShapeQuery → scope
2. deserialize body → TCrud ; validate
3. concurrency: header If-Match WAJIB bila view ber-token (§7.8) → 412 bila hilang
4. IViewWriter.UpdateAsync(exec, key, ifMatch):
     entity = source.Where(scope).FirstOrDefault(e => KeySelector(e) == key)   // scope di-AND
       → null → 404 not-found
     bila token: bandingkan ifMatch vs entity token → mismatch → 412 precondition-failed
     ApplyWritable(crud, entity); SaveChanges
       → DbUpdateConcurrencyException → 409 concurrency-conflict
5. 200 OK + ETag baru + body Detail shape
```

- **Update by-key di-`AND` dengan trusted scope** (D85): baris di luar tenant → terlihat `404` (bukan 403), tidak membocorkan keberadaan baris lintas-tenant.
- PUT adalah **full update** atas field whitelist; field whitelist yang absen di `TCrud` payload mengikuti semantik DTO (null/default) — partial update (PATCH per-field) **out of v1.0** (§12 OQ).

### 7.4 Delete — `DELETE {root}/{v}/{key}`

```text
1. auth: IsAllowedAsync(facet=Delete) → 403 ; ShapeQuery → scope
2. concurrency: If-Match WAJIB bila view ber-token → 412 bila hilang
3. IViewWriter.DeleteAsync(viewName, key, scope, ifMatch):
     entity = source.Where(scope).FirstOrDefault(KeySelector == key) → null → 404
     token mismatch → 412 ; SaveChanges → DbUpdateConcurrencyException → 409
4. 204 No Content
```

Soft-delete bukan urusan endpoint: bila view memodelkan soft-delete, itu diekspresikan sebagai `WithRowFilter<TSource>(_ => e => !e.IsDeleted)` (Spec 01 §5.2) + `WithCrud` yang men-set flag, bukan `DELETE` fisik. Endpoint `DELETE` selalu = hapus baris (atau gagal ke 404/409/412).

### 7.5 Export — `POST {root}/{v}/export?format=csv|xlsx`

```text
1. auth: IsAllowedAsync(facet=Export) → 403 ; ShapeQuery → scope
2. bind ViewQueryRequest (sama jalur read; filter/sort dari body) — TANPA paging (export = filtered set penuh)
3. enforce MaxExportRows: Take(maxRows + 1); > maxRows → 413 payload-too-large (Spec 01 §11.2)
4. resolve IViewExporter by format (Spec 01 §11.1) → 415 bila format tak terdaftar
5. stream: Response.ContentType = exporter.MimeType
           Content-Disposition: attachment; filename="{v}.{ext}"
           exporter.ExportAsync(rows: IAsyncEnumerable<object>, fields, accessors, Response.Body, options, ct)
```

Detail format/streaming/`LiteXlsxViewExporter` → Spec 07. Di sini hanya **glue HTTP**: content-type, disposition, streaming ke `Response.Body`, enforcement limit. `length=-1` tidak relevan (export tidak paging); hard-cap absolut 1.000.000 baris tetap berlaku (Spec 01 D19).

### 7.6 Bulk operations — D87

Bulk hanya bila View `CrudType != null` **dan** `AllowBulk(true)` (Spec 01 §5.2). Memakai EF 7+ `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (set-based, tanpa load entity ke memory) — implementasi di EF layer.

| Endpoint | Body | Eksekusi | Catatan |
|---|---|---|---|
| `PATCH {root}/{v}/bulk` | `{ filter: FilterNode, set: { field: value, ... } }` | `Where(scope ∧ filter).ExecuteUpdateAsync(set)` | `set` hanya field `MapWritable` (whitelist); selain itu → 400 |
| `POST {root}/{v}/bulk-delete` | `{ filter: FilterNode }` | `Where(scope ∧ filter).ExecuteDeleteAsync()` | filter divalidasi sama seperti read (Spec 02 §7) |

Aturan keamanan bulk:

1. **`filter` wajib non-kosong** (D87) — bulk tanpa filter = update/delete seluruh tabel; ditolak 400 `bulk-requires-filter`. Cegah "DELETE all" tak sengaja.
2. **Trusted scope tetap di-`AND`** — bulk tidak bisa menembus tenant boundary.
3. **`set` field di-whitelist** lewat `MapWritable` yang sama dengan single-write; concurrency token **tidak** dicek per-baris (bulk = set-based) — dokumentasikan sebagai trade-off (D87).
4. **Tidak ada hook/validator/interceptor per-baris** di bulk v1.0 (set-based bypass change tracker). Bila butuh per-row audit, pakai single-write. Response: `{ affected: <long> }`.

### 7.7 Key encoding (`{key}`)

- **PK tunggal**: di-encode di path. `int`/`long`/`Guid` apa adanya; `string` PK di URL-encode. Coercion ke `ClrType` PK memakai aturan Spec 02 §8 → gagal → 400 `invalid-key`.
- **PK majemuk** (Spec 03 §17 #3): di v1.0 di-encode sebagai segmen dipisah koma `{key1},{key2}` dengan urutan deklarasi PK; jumlah segmen ≠ jumlah PK → 400. (Kandidat alternatif tuple/base64 → §12 OQ.)

### 7.8 Concurrency (`ETag`/`If-Match`) — detail (Spec 01 §14.2)

- View ber-`WithConcurrencyToken(field)` mengekspos token sebagai **header `ETag`** pada response Detail (`GET .../{key}`) dan item Create/Update. Default header; opsi expose ke field DTO via `WithConcurrencyToken(..., exposeAs: "RowVersion")` (Spec 01 §15 #5).
- Encoding token → string ETag: `byte[] RowVersion` → **base64url**; `DateTime LastModifiedAt` → **ISO-8601**; `xmin` (PostgreSQL) → string angka. Selalu **strong ETag** (bukan weak `W/`).
- `PUT`/`DELETE` pada view ber-token **wajib** `If-Match: "<token>"`:
  - header hilang → **412** `precondition-failed` (jangan diam-diam lakukan last-write-wins).
  - token tak cocok saat load / `SaveChanges` melempar `DbUpdateConcurrencyException` → **409** `concurrency-conflict`.
- View tanpa token: `If-Match` diabaikan (tidak ada proteksi optimistic). Direkomendasikan token untuk semua write multi-user.

## 8. Metadata Endpoint

`GET {root}/{v}/metadata` → `ViewMetadata` (Spec 01 §5.4) sebagai JSON, untuk konsumsi klien dinamis & TS client (Spec 06). Field sensitif tidak bocor (metadata = bentuk field + flag, bukan data). `IsHidden` field tetap muncul di metadata (klien butuh tahu PK untuk routing Detail) tapi ditandai `IsHidden=true`.

`GET {root}/{v}/metadata/{adapterId}` → output `IViewMetadataAdapter<TSchema>.ToSchema` (Spec 04 §5.2), mis. `/metadata/querybuilder` → skema jQuery-QueryBuilder (`metadataQB` DynData). 404 bila `adapterId` tak terdaftar.

Metadata di-cache (immutable per build); `ETag`/`Cache-Control` boleh dipasang (kandidat, §12 OQ).

## 9. Error Model HTTP (RFC 7807 konkret)

Memenuhi Spec 01 §14.1 & Spec 02 §15: satu bentuk `application/problem+json`, `type` di bawah `https://a2n.dev/vista/errors/`. AspNetCore adalah **satu-satunya tempat** error domain (engine/adapter/writer) jadi HTTP — via `IExceptionHandler`/middleware Vista.

### 9.1 Bentuk JSON

```json
{
  "type": "https://a2n.dev/vista/errors/filter-field-not-allowed",
  "title": "Filter field not allowed",
  "status": 400,
  "detail": "Field 'Email' is not filterable on view 'customers'.",
  "instance": "/api/views/customers/query",
  "viewName": "customers",
  "field": "Email",
  "operator": "Contains",
  "allowed": ["Name", "CreatedAt"],
  "traceId": "00-<w3c-traceparent>-..."
}
```

- Properti standar RFC 7807: `type`, `title`, `status`, `detail`, `instance`.
- **`extensions` machine-readable** (Spec 02 §15): `viewName`, `field`, `operator`, `allowed`, `expectedType`, dst. — flat di root object (konvensi `ProblemDetails.Extensions` ASP.NET).
- `traceId` selalu disertakan (W3C trace context) untuk korelasi log. `detail` tidak boleh membocorkan internal (stack trace, SQL, nilai baris lain).

### 9.2 Pemetaan exception/status → HTTP

| Sumber | Kondisi | HTTP | `type` |
|---|---|---|---|
| Engine (Spec 02 §15) | filter/sort/scope/search/operator/value/paging/complexity | 400/413 | sesuai tabel Spec 02 §15 |
| Adapter (Spec 04 §10) | `AdapterBindException` (JSON rusak, index kolom invalid) | 400 | `.../adapter-bind-failed` |
| Auth (§6) | `IsAllowedAsync == false`, user authenticated | 403 | `.../forbidden` |
| Auth (§6) | `IsAllowedAsync == false`, user anonim | 401 | `.../unauthorized` |
| Write (§7) | `WriteStatus.ValidationFailed` | 400 | `.../validation` (per-field di `errors[]`) |
| Write (§7) | `WriteStatus.NotFound` (key tak ada / di luar scope) | 404 | `.../not-found` |
| Write (§7) | `If-Match` hilang pada view ber-token | 412 | `.../precondition-failed` |
| Write (§7) | token mismatch / `DbUpdateConcurrencyException` | 409 | `.../concurrency-conflict` |
| Bulk (§7.6) | `filter` kosong | 400 | `.../bulk-requires-filter` |
| Binding (§5.3) | content-type campuran / tak didukung | 415 | `.../unsupported-media-type` |
| Binding (§5.3) | body melebihi `MaxRequestBodyBytes` | 413 | `.../payload-too-large` |
| Negotiation (§5.4) | `?format=`/Accept tak match | 406 | `.../adapter-not-acceptable` |
| Key (§7.7) | key tak bisa di-coerce / segmen salah | 400 | `.../invalid-key` |
| Limits (Spec 01 §11.2) | export rows / page size | 413 | `.../payload-too-large` |
| Tak terduga | unhandled | 500 | `.../unexpected` (tanpa detail internal) |

### 9.3 DataTables error shape (opsional, paritas)

Untuk klien DataTables-native (Spec 04 §7.1 `DataTablesResponse.Error`), host **boleh** membungkus Problem Details ke `{ "draw": n, "error": "<title>" }` bila request datang via adapter DataTables **dan** `Accept` menunjukkan klien grid. Default tetap `application/problem+json` (D88); pembungkusan adalah negotiable opt-in agar tidak memecah kontrak error global.

## 10. OpenAPI Integration

`a2n.Vista.AspNetCore` mengonsumsi **OpenAPI document model** netral yang di-generate source-gen dari `ViewMetadata` (Spec 03 §10) dan mendaftarkannya ke pipeline OpenAPI ASP.NET (`Microsoft.AspNetCore.OpenApi`) **compile-time** — tanpa scan runtime.

- Per facet (§5.2) → satu operation OpenAPI: path, method, request schema (`TCrud` untuk write, `ViewQueryRequest`/`DataTablesQuery` untuk query), response schema (`TQuery`/`PagedResult<TQuery>`/Detail), error responses (§9.2).
- Schema `TQuery`/`TCrud` typed → `#/components/schemas/...` dari `JsonTypeInfo` source-gen.
- **Anonymous view** (gaya A): schema dari shape anonymous, nama komponen di-derive dari nama view (mis. `vProductCategoryRow`, Spec 03 §17 #5). Operasi tetap muncul; ini build-time artifact (RUC tidak relevan).
- Security scheme: bila `IViewAuthorizer` terdaftar, operasi ditandai `security` (bearer/cookie sesuai konfigurasi app) — informatif; enforcement tetap di `IsAllowedAsync`.

## 11. Constraint AOT

Selaras Spec 01 §9, Spec 03 §14:

1. **Endpoint mapping dari registry source-gen** (Spec 03 §7) — `MapVistaViews()` meng-iterasi `IViewRegistry.All` yang di-isi module initializer. **Tidak ada** `Assembly.GetTypes()`/controller discovery. `RegisterAssembly` (Spec 01 §5.3) tetap `[RequiresUnreferencedCode]`.
2. **Deserialisasi `TCrud`** via `JsonTypeInfo` source-gen (Spec 03 §9) — tidak ada `JsonSerializer.Deserialize(stream, Type)` non-typed di write path typed.
3. **Serialisasi response** typed via `JsonTypeInfo`; anonymous projection (gaya A) jatuh ke jalur `[RequiresUnreferencedCode]` (Spec 01 §4.5) — konsisten: yang non-AOT adalah *serialisasi anonymous*, bukan mapping HTTP.
4. **Key/concurrency** memakai `KeySelector`/accessor source-gen (Spec 03 §8), bukan `PropertyInfo`.
5. **Minimal API** (`MapXxx` delegate) lebih AOT-clean dari MVC controller (yang butuh reflection action discovery) → Vista memakai jalur Minimal API endpoint sebagai primer. MVC controller adapter (bila perlu paritas) adalah jalur sekunder non-AOT (kandidat, §12 OQ).

## 12. Decision Log (lanjutan dari Spec 03 D81)

| # | Keputusan | Status | Catatan |
|---|---|---|---|
| D82 | `IViewWriter` adalah **port di Core** (sejalan `IViewExecutor`); implementasi CRUD + bulk di `a2n.Vista.EntityFrameworkCore`. `TCrud`/`TEntity` di-erase ke `object` di boundary; `ApplyWritable` source-gen melakukan assignment typed. | **Decided** | §7.1. Spec 01 D48, Spec 03 §8. |
| D83 | Binding `POST .../query`: form-urlencoded → `Values`; **form menang** atas query string untuk kunci sama (query hanya menambah kunci baru). `application/json` → `JsonBody`. Campuran → 415. | **Decided** | §5.3. |
| D84 | Pemilihan adapter List: prioritas route suffix → `Accept` media type → `?format=` → default `PagedResult`. Tepat satu adapter/request; tak match → 404/406. | **Decided** | §5.4. Menutup Spec 04 §12 #1. |
| D85 | `ShapeQuery` (trusted scope) di-`AND` ke **write** by-key & bulk, bukan hanya read. Baris di luar scope → 404 (bukan 403) saat update/delete. | **Decided** | §6.1, §7.3/§7.4. Cegah kebocoran lintas-tenant pada write. |
| D86 | Auth ditolak: user anonim → **401**; user terotentikasi tapi tak berhak → **403**. | **Decided** | §6.3, §9.2. |
| D87 | Bulk (`PATCH .../bulk`, `POST .../bulk-delete`) butuh `AllowBulk` + **filter non-kosong** (400 bila kosong); trusted scope tetap di-`AND`; `set` di-whitelist `MapWritable`; concurrency/hook per-baris **tidak** berlaku (set-based via `ExecuteUpdate/DeleteAsync`). | **Decided** | §7.6. Persyaratan ROADMAP "Bulk operations". |
| D88 | Error default selalu `application/problem+json` (RFC 7807). DataTables `{draw,error}` shape hanya opt-in negotiable untuk klien grid-native. | **Decided** | §9.1, §9.3. Satu sumber error. |
| D89 | List-query = `POST {root}/{v}/query` (body); Create = `POST {root}/{v}`. Detail/Update/Delete by-key path tunggal; PK majemuk = segmen koma (v1.0). | **Decided** | §5.2, §7.7. Spec 01 D32. |
| D90 | `query` & `export` memakai **POST** (filter tree di body), bukan GET. `MaxRequestBodyBytes` (default 1 MiB) → 413. | **Decided** | §5.2, §5.3. |
| D91 | Endpoint di-map via **Minimal API** (delegate) dari registry source-gen sebagai jalur primer AOT-clean; tidak ada MVC controller discovery di hot path. | **Decided** | §11. |
| D92 | `WithConcurrencyToken`: ETag **strong**, encoding base64url (`byte[]`) / ISO-8601 (`DateTime`); `If-Match` wajib pada write view ber-token (412 bila hilang), mismatch → 409. | **Decided** | §7.8. Spec 01 §14.2/D30. |
| D93 | PUT = full update atas field whitelist; partial/JSON-Merge-Patch ditunda. Soft-delete dimodelkan via `WithRowFilter` + `WithCrud`, bukan verb `DELETE`. | **Decided** | §7.3, §7.4. |

## 13. Open Questions

1. **Partial update (PATCH per-field)** — v1.0 PUT = full whitelist update. PATCH JSON-Merge / JSON-Patch per-field butuh tracking field mana yang "hadir" di payload (`TCrud` tak bisa bedakan null-eksplisit vs absen). Kandidat: source-gen `Optional<T>` wrapper atau `JsonElement` raw. Tunda v1.x.
2. **PK majemuk encoding** — segmen koma (§7.7) rapuh bila nilai mengandung koma. Alternatif: base64url(JSON array) atau composite key di body untuk Detail-by-key (POST). Selaras Spec 03 §17 #3.
3. **MVC controller adapter** — sebagian konsumen butuh controller (filter pipeline MVC, model binding kustom). Sediakan `a2n.Vista.AspNetCore.Mvc` (non-AOT) atau Minimal API saja? Kandidat: Minimal API v1.0, MVC bila ada demand.
4. **Metadata caching** — `ETag`/`Cache-Control` untuk `GET .../metadata` (immutable per build). Aman, tapi perlu strategi invalidasi versi build.
5. **Rate limiting & request quotas per-view** — saat ini diserahkan ke konvensi ASP.NET (`IEndpointConventionBuilder`). Apakah Vista perlu hook per-view (mis. limit export concurrency) bawaan? Kandidat v1.x.
6. **Negotiation `Accept` media type registry** — bentuk final string media type per adapter (`application/vnd.vista.{id}+json`?) perlu dibakukan agar lintas-adapter konsisten.

## 14. Next / Forward References

- `06-typescript-client.md` — klien TS mengonsumsi `GET .../metadata` (§8) & OpenAPI (§10) untuk codegen DTO + filter API; memanggil endpoint §5.2.
- `07-export.md` — detail `IViewExporter`, streaming, `LiteXlsxViewExporter`; dipanggil dari §7.5.
- `08-migration-from-dyndata.md` — pemetaan endpoint `/datatable`,`/create`,`/update`,`/delete` DynData → route §5.2; paritas error shape §9.3.
- `09-efcore-integration.md` (kandidat) — implementasi `IViewExecutor`/`IViewWriter` (CRUD, `ExecuteUpdate/DeleteAsync`), provider detection, authoring `ViewTemplate<TDbContext>` DbContext-bound.
