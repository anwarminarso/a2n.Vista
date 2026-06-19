# a2n.Vista — Project Brief

## Latar Belakang

`a2n.DynData` adalah library .NET yang mengubah `DbContext` Entity Framework Core menjadi REST API otomatis (datatable, paging, filter, CRUD, export) lengkap dengan client JavaScript untuk DataTables.js dan jQuery QueryBuilder. Library ini berfungsi, tapi review menyeluruh menemukan kelemahan signifikan:

- Default tidak aman untuk produksi (semua DbSet ter-expose otomatis)
- Mass assignment terbuka (deserialisasi langsung ke entity)
- Ketergantungan wajib pada Newtonsoft.Json
- Reflection-heavy (tidak AOT-friendly)
- Tiga controller generic dengan duplikasi kode 80%
- Wajib inherit `DynDbContext` (invasif)
- Fitur `QueryTemplate` (paling potensial) eksekusinya setengah jadi: API gemuk, type discovery rapuh, CRUD redirect tidak terhubung di controller
- Hanya terintegrasi dengan jQuery DataTables + QueryBuilder

Di kategori "auto CRUD/REST API dari ORM", kompetisi sangat ramai (EasyData, Hasura, PostgREST, Supabase, Directus, dll.). Bersaing head-to-head sebagai "auto CRUD generator" generik bukan strategi yang sehat.

## Keputusan

Membuat **`a2n.Vista`** sebagai **evolusi** DynData: mempertahankan ergonomi *view-first* yang menjadi kekuatannya, sekaligus me-redesign fondasi (keamanan, AOT, grid-agnostic) yang menjadi diferensiasi unik. Bukan sekadar menambal DynData, juga bukan membuang ergonominya.

## Tiga Pilar Utama

### Pilar 1 — View sebagai Citizen Utama
Konsep `QueryTemplate` di DynData diangkat menjadi konsep inti, bukan fitur tambahan. Vista adalah **evolusi** DynData: ergonomi authoring-nya dipertahankan, kelemahannya dibuang.

- Developer mendefinisikan **View** (projection LINQ) sebagai unit deklaratif.
- View adalah sumber tunggal untuk: metadata, endpoint, filter contract, UI binding.
- Raw `DbSet` adalah kasus khusus dari View (View tanpa projection).
- Setiap View di-define eksplisit → secure-by-default (tidak ada auto-expose).
- Field whitelist deklaratif → solusi mass-assignment built-in.

**Dua gaya authoring (keduanya menghasilkan `ViewMetadata` yang sama):**

1. **Anonymous projection — read-only (gaya DynData, dipertahankan).** Developer menulis projection `select new { ... }` inline **tanpa** membuat class DTO. Inilah kekuatan DynData: kolom view gampang di-adjust, iterasi cepat. **Aturan tegas: anonymous projection ⇒ tidak ada CRUD ⇒ View read-only.** Tanpa DTO eksplisit tidak ada kontrak tulis, jadi tidak ada permukaan mass-assignment.
2. **Typed DTO — read + CRUD.** Untuk view yang butuh tulis (create/update/delete), developer mendeklarasikan `TQuery` (dan `TCrud`) eksplisit. CRUD **hanya** tersedia di jalur ini, lengkap dengan whitelist `MapWritable`. Strongly-typed `View<TQuery, TCrud>`, bukan `IQueryable<dynamic>`.

Jadi kemudahan (anonymous, read-only) dan keamanan tulis (typed DTO + whitelist) adalah dua titik di spektrum yang sama, dipilih developer **per-view**. CRUD tidak pernah bersandar pada `dynamic`/anonymous projection.

### Pilar 2 — Integrasi UI yang Luas dan Grid-Agnostic
Pisahkan kontrak server dari adapter klien.

**Core server**: kontrak query/response yang netral, expression filter standar.

**Adapter klien terpisah per ekosistem UI**:
- `a2n.Vista.Adapters.DataTablesNet` — jQuery DataTables + QueryBuilder
- `a2n.Vista.Adapters.AgGrid` — AG Grid
- `a2n.Vista.Adapters.MudBlazor` — MudDataGrid server-side
- `a2n.Vista.Adapters.Telerik` — Telerik UI / Kendo Grid
- `a2n.Vista.Adapters.Syncfusion` — Syncfusion Grid
- `a2n.Vista.Adapters.TanStackTable` — TanStack Table (React/Vue/Solid)
- `a2n.Vista.Adapters.PrimeNG` / `PrimeReact` / `PrimeVue`
- `a2n.Vista.Adapters.Quasar` — QTable (Vue)
- `a2n.Vista.Adapters.OData` — translate ke `$filter` (langsung dukung banyak grid)
- `a2n.Vista.Adapters.GraphQL` — bonus

Filosofi: **core tidak peduli grid apa yang dipakai, adapter yang menerjemahkan.**

### Pilar 3 — AOT-First, Bukan AOT-as-Afterthought
- Source generator untuk metadata (no runtime reflection)
- Source generator untuk endpoint registration
- Strongly typed expression builder per view
- Target Native AOT compatibility dengan minimal `RequiresUnreferencedCode` annotation
- OpenAPI/Swagger doc di-generate compile-time

**Tradeoff anonymous projection vs AOT (sengaja):** View **typed-DTO** adalah jalur AOT-clean penuh — metadata, expression builder, dan serialisasi (via `JsonSerializerContext` source-gen) semuanya compile-time. View **anonymous-projection** (read-only) mengorbankan sebagian AOT-cleanliness: anonymous type belum punya jalur STJ source-gen, sehingga jalur ini di-mark `[RequiresUnreferencedCode]` dan ditujukan untuk skenario non-AOT / kecepatan iterasi. Developer yang menarget Native AOT penuh memakai typed DTO. Ini pilihan sadar: ergonomi DynData tetap ada untuk yang membutuhkannya, tanpa mengorbankan jalur AOT untuk yang menarget produksi AOT.

## Persyaratan Tambahan (Wajib di Konsep)

- **Minimal API support**: `app.MapVistaView<MyView>()`, bukan hanya controller
- **System.Text.Json native**, Newtonsoft optional di paket terpisah
- **Provider-agnostic filter**: deteksi otomatis ILike/Contains dari konfigurasi DB, bukan flag client
- **Authorization terpusat** via satu `IViewAuthorizer` (gaya `IDynDataAPIAuth` DynData) — didaftarkan sekali (`UseAuthorizer<T>`), jadi gerbang semua view + facet, plus hook row-scope server-trusted (`ShapeQuery`). Tanpa authorizer → default allow + warning startup.
- **Hard limits** built-in: max page size, max export rows
- **Row-level security hook** dan field masking deklaratif
- **Bulk operations** pakai `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (EF 7+)
- **TypeScript client generator** dari metadata view (strongly typed)
- **Tidak wajib inherit** base DbContext (komposisi via DI, bukan inheritance)

## Diferensiasi vs Kompetitor

| Pesaing | Posisi mereka | Kelemahan dari sudut pandang Vista |
|---------|--------------|-------------------------------------|
| EasyData | Auto-CRUD + UI komersial | UI vendor-locked, fitur view kompleks lemah |
| Hasura/Supabase | GraphQL/REST dari DB | Bukan .NET native, perlu infra eksternal |
| PostgREST | REST dari Postgres schema | Postgres-only, view di-define di SQL |
| OData (Microsoft) | Query language standar | Bukan auto-API, bukan AOT-first |
| AutoAPI / Auto.Rest.API | Auto REST dari DbSet | Maintenance lemah, tidak ada konsep view |

**Posisi Vista**: *"Library .NET untuk membangun back-office dengan view berbasis projection LINQ kompleks, integrasi grid agnostic, secure-by-default, dan AOT-clean."*

## Struktur Paket NuGet

```
a2n.Vista.Core                       ← engine: view, query, expression, metadata
a2n.Vista.AspNetCore                 ← endpoint mapping (MVC + Minimal API)
a2n.Vista.SourceGenerators           ← compile-time codegen, AOT
a2n.Vista.EntityFrameworkCore        ← integrasi EF Core
a2n.Vista.Newtonsoft                 ← optional, untuk legacy

a2n.Vista.Adapters.DataTablesNet
a2n.Vista.Adapters.AgGrid
a2n.Vista.Adapters.MudBlazor
a2n.Vista.Adapters.Telerik
a2n.Vista.Adapters.Syncfusion
a2n.Vista.Adapters.TanStackTable
a2n.Vista.Adapters.PrimeNG
a2n.Vista.Adapters.OData
a2n.Vista.Adapters.GraphQL

a2n.Vista.Client.TypeScript          ← TS codegen tool
```

**Aturan dependency (D48):**

- `Core` — **bebas EF & HTTP**. Kontrak netral + port `IViewExecutor`/`IViewScope`. Tidak referensi paket lain.
- `EntityFrameworkCore` → `Core`. Implement `IViewExecutor`, provider detection, CRUD/bulk, + authoring DbContext-bound (`ViewTemplate<TDbContext>`).
- `AspNetCore` → `Core`. Endpoint mapping + `IViewAuthorizer` (HTTP-bound). **Tidak** referensi EF.
- `Adapters.*`, `Client.TypeScript` → `Core` saja (kontrak netral, tanpa EF/ASP.NET).
- `SourceGenerators` — Roslyn (netstandard2.0), tanpa referensi proyek.
- `EntityFrameworkCore` & `AspNetCore` **tidak saling referensi**; ketemu di `IViewExecutor` (Core) via DI di composition root.

## Konvensi Namespace & Penamaan

```csharp
namespace a2n.Vista;
namespace a2n.Vista.AspNetCore;
namespace a2n.Vista.Adapters.AgGrid;
```

Terminologi internal:
- `View<T>` — unit utama (menggantikan `QueryTemplate`)
- `ViewBuilder` — fluent API konfigurasi view
- `IViewRegistry` — registry view
- `ViewMetadata` — metadata yang dihasilkan
- `IViewAdapter<TRequest, TResponse>` — kontrak adapter UI
- `MapView<TView>()` — minimal API extension

## Branding

- **Package ID**: `a2n.Vista.*` (konsisten dengan ekosistem maintainer)
- **Brand name di marketing/docs**: `Vista`
- **Tagline**: *"Define a view, get an API. Type-safe, AOT-friendly, grid-agnostic projections for ASP.NET Core."*
- **GitHub repo awal**: `anwarminarso/vista` atau `anwarminarso/a2n.vista`
- **Migrasi ke org `vista-net` di masa depan** kalau ada momentum komunitas

## Hubungan dengan a2n.DynData

`a2n.Vista` adalah **evolusi** (penerus major-version) dari `a2n.DynData`, bukan library tak terkait. Tujuannya: pengguna DynData merasa "di rumah" — gaya authoring view terpusat dengan anonymous projection dipertahankan — sambil menutup kelemahan keamanan & AOT. `a2n.DynData` ditandai legacy/maintenance-only dengan pointer ke `a2n.Vista`, disertai migration guide.

Pesan di README:

> a2n.Vista is the evolution of a2n.DynData — same view-first ergonomics, now with type-safe CRUD, AOT support, and grid-agnostic adapters.

## Strategi Branch & Rilis

- DynData tetap di-maintain bug-fix, tapi tidak ada major feature baru
- Vista dikembangkan sebagai repo baru, bukan branch DynData
- Rilis Vista direncanakan dalam tiga tahap:
  1. **v0.x — Foundation**: Core, AspNetCore, EF Core integration, source generator dasar, satu adapter referensi (DataTablesNet)
  2. **v1.0 — Production-ready**: security hardening, hard limits, OpenAPI, TS client generator, dua adapter besar (AG Grid, MudBlazor)
  3. **v1.x — Ecosystem**: adapter tambahan (Telerik, Syncfusion, TanStack, PrimeNG, OData, GraphQL), bulk ops, audit log, soft delete, SignalR live updates

## Yang Perlu Dilakukan Selanjutnya

1. Cek availability final: NuGet `a2n.Vista.*`, GitHub username/org, domain (opsional)
2. Outline arsitektur high-level: folder structure, sample API surface, contoh konkret `View<T>` definition
3. Setup repo skeleton: solution layout, CI matrix (.NET 8/9/10), test framework, NuGet publish workflow
4. Tulis spec untuk Pilar 1 (View) lebih dulu — ini fondasinya, harus matang sebelum yang lain
5. Prototype source generator paling minimal yang sanggup hilangkan reflection di hot path