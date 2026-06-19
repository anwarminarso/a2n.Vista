# Spec 01 — View (Pilar 1)

> Status: **DRAFT**
> Tanggal: 2026-06-19 (rev: dua gaya authoring + model facet — Vista sebagai evolusi DynData)
> Scope: konsep `View` di `a2n.Vista.Core`. Tidak termasuk: adapter UI (Pilar 2), source generator (Pilar 3), integrasi EF Core lengkap. Spec ini fokus pada **kontrak publik** yang menjadi fondasi semua project lain.

---

## 1. Tujuan

Mendefinisikan **View** sebagai unit deklaratif inti `a2n.Vista`. View harus:

1. **Eksplisit** — tidak ada auto-expose. Developer harus mendeklarasikan tiap View.
2. **Type-safe di jalur tulis** — operasi tulis (create/edit) selalu strongly-typed `TCrud` + whitelist, bukan `IQueryable<dynamic>`. Jalur baca (grid/detail) boleh anonymous projection demi ergonomi (lihat 4.5).
3. **Sumber tunggal** untuk: metadata, endpoint contract, filter contract, UI binding, TS client codegen.
4. **Secure-by-default** — field whitelist deklaratif, CRUD opt-in, otorisasi mandatory.
5. **AOT-clean** — kontrak publik tidak boleh memaksa reflection runtime; semua jalur "panas" dilayani source generator (Pilar 3).

## 2. Terminologi

| Istilah | Arti |
|---------|------|
| **View** | Unit deklaratif: projection LINQ + metadata + (opsional) CRUD target. Menggantikan `QueryTemplate` dari DynData. |
| **TQuery** | Tipe DTO hasil projection (yang dikirim ke klien sebagai response item). |
| **TCrud** | Tipe DTO untuk operasi tulis (create/update). Berbeda dari `TQuery` untuk memisahkan read & write contract. |
| **Source** | Entity EF Core (atau `IQueryable<T>` lain) yang menjadi asal data. |
| **CrudTarget** | Entity tujuan operasi tulis. Boleh sama dengan `Source`, boleh subset, boleh tidak ada (read-only View). |
| **ViewBuilder** | Fluent API untuk mengonfigurasi sebuah View saat registrasi. |
| **IViewRegistry** | Tempat penampungan semua View terdaftar; di-resolve oleh endpoint mapper, adapter, dan codegen. |
| **ViewMetadata** | Snapshot deklaratif View setelah builder selesai — input untuk source generator & TS client. |
| **Adapter** | Komponen per-grid yang menerjemahkan request klien ke `ViewQueryRequest` netral dan response ke format grid. |
| **ViewTemplate** | Kelas authoring terpusat (gaya DynData): mendaftarkan banyak View via `AddView(...)` dalam satu tempat. Lihat 4.5. |
| **Facet** | Salah satu dari tiga kapabilitas sebuah View: **List** (read banyak), **Detail** (read satu by-key), **Write** (create/edit/delete). Lihat 4.6. |
| **Anonymous view** | View dengan projection anonymous (`select new { ... }`) — read-only kecuali dilampiri facet Write typed. |

## 3. Non-Goals (untuk spec ini)

- Implementasi adapter konkret apapun.
- Implementasi EF Core query translation.
- Implementasi source generator.
- Definisi format response (JSON shape) ke klien — itu domain Pilar 2.
- Otentikasi (siapa user) — Vista hanya mendelegasi ke ASP.NET Core identity.

## 4. Konsep Inti

### 4.1 View = projection + contract

Sebuah View **selalu** punya empat hal:

1. **Source query**: `Func<TServices, IQueryable<TSource>>` — bagaimana mendapatkan `IQueryable` dasar.
2. **Projection**: `Expression<Func<TSource, TQuery>>` — bentuk akhir yang dikirim ke klien.
3. **Filter contract**: daftar field `TQuery` yang boleh difilter klien + operator yang diizinkan.
4. **Metadata**: nama view, route, deskripsi, hard limits, auth requirement.

CRUD opsional:

- Jika `CrudTarget<TEntity>` di-set: View ini bisa create/update/delete.
- `TCrud` adalah DTO write yang field-nya **whitelist eksplisit** ke `TEntity`.
- Tanpa `CrudTarget`, View adalah **read-only** dan endpoint tulis tidak akan di-generate.

### 4.2 Raw DbSet = View tanpa projection

Tidak ada API "expose semua DbSet". Tapi kalau developer butuh akses langsung ke entity tanpa projection, dia tetap mendeklarasikan View — projection-nya identity (`x => x`). Hal ini menjaga *one path, one rule*: semua data yang keluar dari Vista lewat View.

### 4.3 Read DTO vs Write DTO

`TQuery` dan `TCrud` **wajib dipisah** secara tipe. Alasannya:

- Field yang aman ditampilkan ≠ field yang aman diubah klien.
- DynData mencampur keduanya → mass assignment bocor.
- Pemisahan ini juga rapi untuk TS client (`MyViewQueryDto` vs `MyViewCrudDto`).

View read-only memakai base class `View<TQuery>` (tanpa `TCrud`) — bukan `Unit`/`NoCrud` (lihat 5.1). Untuk tulis: `View<TQuery, TCrud>` (gaya B) atau `WithCrud<TCrud, TEntity>` (gaya A).

### 4.4 Searchable vs Filterable

DynData mencampur dua konsep berbeda dalam satu request. Vista tetap **memisahkan** keduanya secara konseptual, tapi (revisi D42) **default-nya allow** — bukan opt-in seperti versi spec awal. Kuncinya: di Vista batas keamanan adalah **projection**-nya, bukan tabel. Field yang ada di projection memang sudah dikurasi developer (tidak ada password hash, dll.), jadi default "semua field projection bisa filter/sort/search" jauh lebih aman daripada DynData yang meng-expose semua kolom entity.

| Konsep | Default Vista | Override |
|--------|---------------|----------|
| **Filter** (per-field, operator eksplisit) | Semua field projection filterable, operator default per tipe | `.Field(x => x.F, f => f.Operators(...))` atau `f.Filterable(false)` |
| **Sort** | Semua field projection sortable | `.Field(x => x.F, f => f.Sortable(false))` |
| **Search** (global, OR-`Contains` ke field string) | Semua field **string** projection ikut | `.Field(x => x.F, f => f.Searchable(false))` |

Pemisahan Filter vs Search tetap penting:

1. Operator filter (`Equals`, `In`, `Between`, dll.) tidak bisa "diakses lewat search box" — global search hanya `Contains` ke field string.
2. Field bisa di-opt-out dari search tapi tetap filterable (mis. field PII yang ditampilkan ter-mask: `.Field(x => x.Email, f => f.Searchable(false))`).
3. Hanya field **string** yang ikut global search; numeric/date tidak pernah (kecuali filter eksplisit).

### 4.5 Dua Gaya Authoring

Vista adalah **evolusi** DynData: ergonomi *view-first* DynData dipertahankan sebagai **gaya authoring pertama**, di samping gaya class-per-view yang strongly-typed. Keduanya menghasilkan `ViewMetadata` (5.4) yang sama dan melewati pipeline validasi/auth/limit yang sama.

**Gaya A — Central Template (anonymous, ala DynData).** Satu kelas `ViewTemplate<TDbContext>` mendaftarkan banyak View via `AddView(...)` dengan projection **anonymous** inline. Tidak perlu membuat class DTO; kolom view gampang di-adjust. Inilah gaya yang dirindukan pengguna DynData.

**Gaya B — Class-per-View (typed).** Satu kelas `View<TQuery>` / `View<TQuery, TCrud>` per view dengan DTO eksplisit. Lebih verbose, tapi AOT-clean penuh.

#### Aturan typing (invarian keamanan)

> **Projection anonymous hanya boleh melayani facet baca (List/Detail). Facet Write WAJIB `TCrud` typed + whitelist.**

Konsekuensi: View yang facet-nya hanya anonymous read adalah **read-only**. Tidak ada jalan dari anonymous projection ke operasi tulis — itu menutup mass-assignment sejak desain. Untuk menambah tulis, lampirkan facet Write typed (lihat 4.6 + 5.5 `WithCrud`). Rumusan ini me-refine ide awal "anonymous ⇒ seluruh view read-only" menjadi **per-facet**: baca boleh anonymous, tulis wajib typed.

| | Gaya A (central template) | Gaya B (class-per-view) |
|---|---|---|
| Projection baca | anonymous | typed `TQuery` |
| Bikin class DTO? | tidak (baca); ya (kalau ada tulis) | ya |
| Facet Write (CRUD) | hanya via `WithCrud<TCrud, TEntity>` (typed) | `View<TQuery, TCrud>` |
| AOT-clean | tidak (RUC, serialisasi reflection) | ya |
| Cocok untuk | back-office, banyak view, iterasi cepat, migrasi DynData | view kompleks, target Native AOT |

### 4.6 Model Facet

Satu **View** adalah satu *resource* bernama (mis. `"vProductCategory"`) yang punya sampai tiga **facet**:

| Facet | Kardinalitas | Typing | Endpoint (lihat 12.3) |
|-------|-------------|--------|------------------------|
| **List** | banyak, paged | anonymous / typed | `POST /api/views/{name}/query` |
| **Detail** | satu, by-key | anonymous / typed | `GET /api/views/{name}/{key}` |
| **Write** | satu (create/update/delete) | **typed only** | `POST/PUT/DELETE /api/views/{name}` |

Aturan:

1. **List wajib.** Setiap View minimal punya facet List (projection bacanya).
2. **Detail opsional.** Kalau tidak dideklarasi, Detail memakai projection List difilter by primary key. PK ditentukan via field metadata (`PrimaryKey()`). Facet Detail dengan projection sendiri (kolom lebih lengkap dari grid) tersedia di gaya B; di gaya A v0.x, Detail = List by-key.
3. **Write opsional & typed.** Tanpa facet Write → resource read-only. Write tidak pernah memakai projection anonymous.
4. **PK adalah jembatan antar-facet.** Baris List → tombol → Detail/Write semuanya pakai PK yang sama. Karena itu PK harus ada di projection List (boleh `Hidden()` seperti `ProductId` di DynData).
5. **Auth per-facet.** Default ke auth level-View; bisa di-override per facet (mis. baca `CanReadProducts`, tulis `CanEditProducts`).

Pemetaan ke "lanjur" UI: **List = grid**, **Detail = form display**, **Write = create/edit form**.

## 5. API Surface (Public Contract)

> Catatan: signature di bawah adalah **target spec**, belum diimplementasi. Nama tipe bersifat normatif, body bersifat ilustratif.

### 5.1 Tipe utama

```csharp
namespace a2n.Vista;

// Non-generik marker untuk registry & polymorphism (tidak pakai View<object>).
public interface IConfiguredView
{
    string Name { get; }
    Type QueryType { get; }
    Type? CrudType { get; }
    void ConfigureCore(IViewBuilderCore builder);
}

// Read-only View. Builder yang dipakai TIDAK punya CrudOn / MapWritable.
public abstract class View<TQuery> : IConfiguredView
    where TQuery : class
{
    // Dipanggil registry saat startup.
    protected internal abstract void Configure(IViewBuilder<TQuery> builder);
    // Implementasi IConfiguredView di-generate oleh source generator (Pilar 3).
}

// View dengan CRUD. Builder punya CrudOn dan harus dipakai untuk write path.
public abstract class View<TQuery, TCrud> : IConfiguredView
    where TQuery : class
    where TCrud : class
{
    protected internal abstract void Configure(IViewBuilder<TQuery, TCrud> builder);
}
```

Catatan:

- `View<TQuery>` **bukan** subclass dari `View<TQuery, TCrud>`. Pemisahan tipe builder mencegah developer memanggil `CrudOn(...)` di view read-only.
- Marker `NoCrud` (versi sebelumnya) dihilangkan. Read-only ditangani melalui base class terpisah, bukan generic dummy parameter.
- Registrasi & polymorphism via `IConfiguredView` non-generik — tidak ada `View<object>` (lihat 5.3).

### 5.2 ViewBuilder (Gaya B)

Dua interface dipisah eksplisit. Read-only view (`IViewBuilder<TQuery>`) tidak punya `CrudOn`, sehingga compile-error muncul kalau salah pakai. Bagian non-generik `IViewBuilderCore` ada agar `IConfiguredView.ConfigureCore(...)` (lihat 5.1) bisa di-codegen.

Sejalan dengan Gaya A (§5.5): **tidak ada** `Route()`/`RequireAuthorization()` (route global §5.6, auth terpusat §5.6), dan filter/sort/search **default-allow** untuk semua field projection — kustomisasi via `.Field(...)` (§4.4). `IFieldBuilder<TProp>` dipakai bersama dengan §5.5.

```csharp
// Bagian non-generik untuk source-gen interop (lihat 5.1 IConfiguredView).
public interface IViewBuilderCore
{
    IViewBuilderCore Named(string viewName);
    IViewBuilderCore MaxPageSize(int rows);
    IViewBuilderCore MaxExportRows(int rows);
}

// Read-only view builder.
public interface IViewBuilder<TQuery> : IViewBuilderCore
    where TQuery : class
{
    new IViewBuilder<TQuery> Named(string viewName);

    // Source query — WAJIB salah satu.
    IViewBuilder<TQuery> From<TSource>(
        Expression<Func<TSource, TQuery>> projection)
        where TSource : class;

    IViewBuilder<TQuery> FromQuery<TSource>(
        Func<IServiceProvider, IQueryable<TSource>> source,
        Expression<Func<TSource, TQuery>> projection)
        where TSource : class;

    // Konfigurasi per-field (opsional). Default: filterable + sortable +
    // (string) searchable, label auto. Override/opt-out via IFieldBuilder<TProp>
    // (lihat 5.5) — termasuk .Scopable(...) untuk contextual filter klien (§5.6).
    IViewBuilder<TQuery> Field<TProp>(
        Expression<Func<TQuery, TProp>> field,
        Action<IFieldBuilder<TProp>> configure);

    new IViewBuilder<TQuery> MaxPageSize(int rows);
    new IViewBuilder<TQuery> MaxExportRows(int rows);

    // Row-level security — pre-projection (rekomendasi). TSource = entity asal.
    // Soft-delete & tenant-filter umumnya hidup di TSource.
    IViewBuilder<TQuery> WithRowFilter<TSource>(
        Func<IServiceProvider, Expression<Func<TSource, bool>>> filterFactory)
        where TSource : class;

    // Row-level security — post-projection (kasus khusus, mis. computed field).
    IViewBuilder<TQuery> WithProjectedRowFilter(
        Func<IServiceProvider, Expression<Func<TQuery, bool>>> filterFactory);

    // Field masking — predicate (bool) + transformer (TProp -> TProp).
    IViewBuilder<TQuery> MaskField<TProp>(
        Expression<Func<TQuery, TProp>> field,
        Func<IServiceProvider, bool> shouldMask,
        Func<TProp, TProp> masker);
}

// View dengan CRUD. Inherit read-side knob dari read-only builder + jalur write.
public interface IViewBuilder<TQuery, TCrud> : IViewBuilder<TQuery>
    where TQuery : class
    where TCrud : class
{
    // CRUD — wajib dipanggil minimal satu kali pada View<TQuery, TCrud>.
    ICrudBuilder<TQuery, TCrud, TEntity> CrudOn<TEntity>(
        Expression<Func<TEntity, TQuery>>? projectionForRead = null)
        where TEntity : class;
}

public interface ICrudBuilder<TQuery, TCrud, TEntity>
    where TQuery : class
    where TCrud : class
    where TEntity : class
{
    // Write whitelist — WAJIB minimal satu. Tidak ada field default-mapped.
    ICrudBuilder<TQuery, TCrud, TEntity> MapWritable<TProp>(
        Expression<Func<TCrud, TProp>> from,
        Expression<Func<TEntity, TProp>> to);

    ICrudBuilder<TQuery, TCrud, TEntity> WithConcurrencyToken<TToken>(
        Expression<Func<TEntity, TToken>> tokenField);

    ICrudBuilder<TQuery, TCrud, TEntity> WithValidator<TValidator>()
        where TValidator : IViewCrudValidator<TCrud>;

    // Forecast v1.x audit log (hook Before/After Create/Update/Delete).
    ICrudBuilder<TQuery, TCrud, TEntity> WithInterceptor<TInterceptor>()
        where TInterceptor : IViewCrudInterceptor<TCrud, TEntity>;

    ICrudBuilder<TQuery, TCrud, TEntity> AllowBulk(bool allow = true);
}
```

Konsekuensi yang sengaja:

- **Default-allow**: filter/sort/search aktif untuk semua field projection; batasi via `.Field(x => x.F, f => f.Filterable(false)/.Searchable(false)/.Operators(...))` (§4.4, D42).
- **Tidak ada auth/route di builder**: route global (§5.6), auth via `IViewAuthorizer` (§5.6) — D43/D44.
- `WithRowFilter<TSource>` adalah jalur **utama** (pre-projection). Push-down ke SQL natural; soft-delete/tenant di entity, bukan DTO.
- `WithProjectedRowFilter` untuk kasus khusus saja.
- `MaskField` butuh tiga argumen: field selector, predicate, transformer (tanpa transformer masking tak punya semantik).
- `WithConcurrencyToken` (detail HTTP di Spec 05) & `WithInterceptor` (forecast audit v1.x).

### 5.3 Registry

```csharp
public interface IViewRegistry
{
    // AOT-clean path. Dipanggil source generator untuk tiap view yang
    // ditemukan di compile-time. TView terikat ke IConfiguredView (5.1),
    // bukan View<object> (yang tidak compatible dengan View<TQuery, TCrud>
    // karena tidak ada covariance).
    void Register<TView>()
        where TView : class, IConfiguredView, new();

    // Type-only overload untuk skenario reflection-bound (mis. test).
    // Tidak AOT-clean kecuali TView dianotasi DAM.
    [RequiresUnreferencedCode("Type-based view registration may use reflection.")]
    void Register(Type viewType);

    [RequiresUnreferencedCode("Assembly scan walks all types via reflection.")]
    void RegisterAssembly(Assembly assembly);

    ViewMetadata Get(string viewName);
    IReadOnlyCollection<ViewMetadata> All { get; }
}
```

DI extension yang ditargetkan di `a2n.Vista.AspNetCore`:

```csharp
services.AddVista(vista =>
{
    vista.RouteRoot("/api/views");            // route global; view route = {root}/{viewName}/... (§5.6)
    vista.UseAuthorizer<AppViewAuthorizer>(); // satu pintu auth (§5.6). Tanpa ini → default allow + warning.

    vista.RegisterTemplate<AppViews>();       // gaya A (central template) — direkomendasikan
    vista.Register<CustomerListView>();       // gaya B (class-per-view) — untuk view kompleks

    // Non-AOT shortcut untuk prototyping; men-trigger trim warning.
    // vista.RegisterAssemblyContaining<Program>();
});

app.MapVistaViews();              // generic mapper
app.MapView<CustomerListView>();  // explicit, recommended (codegen-friendly)
```

### 5.4 ViewMetadata (output)

```csharp
public sealed record ViewMetadata(
    string Name,
    string Route,
    Type QueryType,
    Type? CrudType,
    Type? CrudEntityType,
    IReadOnlyList<FieldMetadata> Fields,
    AuthorizationRequirement? Authorization,  // null = pakai authorizer pusat (§5.6); per-view override jarang
    HardLimits Limits,
    bool IsReadOnly);

public sealed record FieldMetadata(
    string Name,
    string Label,            // auto dari Name ("ProductName" → "Product Name"); override via .Field(..., f => f.Label(...))
    Type ClrType,
    bool IsFilterable,       // default true (semua field projection)
    bool IsSortable,         // default true
    bool IsSearchable,       // default true untuk field string
    bool IsScopable,         // default false; contextual/lookup key dari klien (§5.6)
    bool IsHidden,           // default false; hidden = tidak dikirim/ditampilkan (mis. PK teknis)
    bool IsWritable,
    bool IsMaskable,
    FilterOperator AllowedOperators);
```

`ViewMetadata` adalah **input utama** untuk:

- Source generator (Pilar 3) — codegen endpoint, expression builder, OpenAPI doc.
- `IViewAdapter<TRequest, TResponse>` (Pilar 2) — translate request klien.
- `a2n.Vista.Client.TypeScript` — codegen DTO + filter contract di TS.

### 5.5 Central Template API (Gaya A)

```csharp
namespace a2n.Vista;

// Authoring terpusat ala DynData. TDbContext jadi sumber IQueryable.
public abstract class ViewTemplate<TDbContext>
    where TDbContext : DbContext
{
    protected internal abstract void Configure(IViewTemplateBuilder<TDbContext> views);
}

public interface IViewTemplateBuilder<TDbContext>
    where TDbContext : DbContext
{
    // Read-only anonymous view. TRow di-infer compiler dari body lambda
    // (boleh anonymous type) — tidak perlu DTO eksplisit.
    IReadViewBuilder<TRow> AddView<TRow>(
        string name,
        Func<TDbContext, IServiceProvider, IQueryable<TRow>> query)
        where TRow : class;
}

// Builder facet baca. Field selector tetap strongly-typed walau TRow anonymous,
// karena lambda dievaluasi di scope yang sama dengan AddView.
//
// Catatan: TIDAK ada Route()/RequireAuthorization() di sini — route bersifat
// global (§5.6) dan auth terpusat (§5.6). Filter/sort/search SEMUA field aktif
// by default; kustomisasi lewat .Field(...).
public interface IReadViewBuilder<TRow>
    where TRow : class
{
    IReadViewBuilder<TRow> MaxPageSize(int rows);
    IReadViewBuilder<TRow> MaxExportRows(int rows);

    // Konfigurasi per-field (opsional). Default tiap field: filterable + sortable
    // + (jika string) searchable, label auto dari nama. .Field(...) untuk override.
    IReadViewBuilder<TRow> Field<TProp>(
        Expression<Func<TRow, TProp>> field,
        Action<IFieldBuilder<TProp>> configure);

    // Row-level security pre-projection (lihat 5.2). Untuk scope server-trusted
    // lintas-view, gunakan IViewAuthorizer.ShapeQuery (§5.6).
    IReadViewBuilder<TRow> WithRowFilter<TSource>(
        Func<IServiceProvider, Expression<Func<TSource, bool>>> filterFactory)
        where TSource : class;

    // Jembatan ke facet Write — WAJIB typed. Mengubah resource jadi read+write.
    // Satu-satunya pintu CRUD dari gaya central-template; tidak menerima
    // anonymous type (invarian 4.5).
    ICrudFacetBuilder<TCrud, TEntity> WithCrud<TCrud, TEntity>()
        where TCrud : class
        where TEntity : class;
}

// Konfigurasi per-field. Semua opsional; default sudah aman/benar.
public interface IFieldBuilder<TProp>
{
    IFieldBuilder<TProp> PrimaryKey();
    IFieldBuilder<TProp> Hidden();                        // tidak dikirim/ditampilkan
    IFieldBuilder<TProp> Label(string label);             // override label auto
    IFieldBuilder<TProp> Format(string formatString);

    // Opt-out / kustomisasi default (default semuanya true):
    IFieldBuilder<TProp> Filterable(bool allowed = true);
    IFieldBuilder<TProp> Sortable(bool allowed = true);
    IFieldBuilder<TProp> Searchable(bool allowed = true);   // hanya berdampak untuk field string
    IFieldBuilder<TProp> Operators(FilterOperator allowed); // batasi operator (implisit Filterable)

    // Izinkan field jadi contextual/lookup key dari KLIEN (default false, opt-in).
    // Filter scoping klien (padanan externalFilter DynData) hanya boleh ke field
    // Scopable — terpisah dari Filterable UI (§5.6, D47).
    IFieldBuilder<TProp> Scopable(bool allowed = true);
}

// Sama semantik dengan ICrudBuilder<TQuery, TCrud, TEntity> (5.2), tanpa TQuery
// karena facet baca di gaya A dilayani anonymous TRow.
public interface ICrudFacetBuilder<TCrud, TEntity>
    where TCrud : class
    where TEntity : class
{
    ICrudFacetBuilder<TCrud, TEntity> MapWritable<TProp>(
        Expression<Func<TCrud, TProp>> from,
        Expression<Func<TEntity, TProp>> to);
    ICrudFacetBuilder<TCrud, TEntity> WithConcurrencyToken<TToken>(
        Expression<Func<TEntity, TToken>> tokenField);
    ICrudFacetBuilder<TCrud, TEntity> WithValidator<TValidator>()
        where TValidator : IViewCrudValidator<TCrud>;
    ICrudFacetBuilder<TCrud, TEntity> AllowBulk(bool allow = true);
}
```

Catatan AOT: gaya A men-trigger `[RequiresUnreferencedCode]` pada jalur registrasi & serialisasi anonymous-type (lihat ROADMAP Pilar 3). Untuk Native AOT penuh, pakai gaya B. Facet Write di kedua gaya tetap AOT-clean karena mapping `TCrud → TEntity` di-source-gen dari `MapWritable`.

Registrasi `ViewTemplate` di DI (lihat juga 5.3 & 5.6):

```csharp
services.AddVista(vista =>
{
    vista.RouteRoot("/api/views");                  // route global (§5.6)
    vista.UseAuthorizer<NorthwindViewAuthorizer>(); // satu pintu auth (§5.6); tanpa ini → allow
    vista.RegisterTemplate<NorthwindViews>();       // gaya A (central template)
    vista.Register<CustomerListView>();             // gaya B (class-per-view)
});
```

### 5.6 Authorization & Routing (lintas-gaya)

Berlaku untuk Gaya A maupun B. Menggantikan `Route()` + `RequireAuthorization()` per-view (Decision Log D43/D44).

#### Routing global

```csharp
services.AddVista(v => v.RouteRoot("/api/views"));  // default "/api/views"
```

Route tiap view diturunkan: `{root}/{viewName}` untuk List/query, `{root}/{viewName}/{key}` untuk Detail, dst. (mengikuti pola DynData `/{prefix}/{controller}/{viewName}/{action}`, lihat 12.3). Tidak ada `Route()` per-view; `viewName` dari `AddView("...")` / `Named("...")`. Override per-view hanya escape-hatch (jarang).

#### Authorization — satu pintu (`IViewAuthorizer`)

Menggantikan auth per-view. Satu implementasi, didaftarkan sekali, jadi gerbang untuk **semua** view & facet — gaya `IDynDataAPIAuth` DynData.

```csharp
public enum ViewFacet { List, Detail, Export, Create, Update, Delete }

public sealed record ViewAuthContext(
    ClaimsPrincipal User,
    string ViewName,
    ViewFacet Facet,
    HttpContext Http,
    IServiceProvider Services);

public interface IViewAuthorizer
{
    // Gerbang allow/deny per (view, facet, user). Dipanggil tiap request.
    ValueTask<bool> IsAllowedAsync(ViewAuthContext ctx);

    // Padanan IDynDataAPIAuth.ApplyRequest: inject filter row/scope yang
    // server-trusted (tenant, ownership) — terpusat, bukan dari klien.
    // Inilah jalur "contextual filter" trusted (lihat referensi externalFilter).
    void ShapeQuery(ViewAuthContext ctx, IViewScope scope);
}

public interface IViewScope
{
    // Di-AND-kan ke query, di-push-down ke SQL. TSource = entity asal view.
    void AddRowFilter<TSource>(Expression<Func<TSource, bool>> filter) where TSource : class;
}
```

Registrasi & semantik default:

```csharp
services.AddVista(v => v.UseAuthorizer<AppViewAuthorizer>());
```

| Kondisi | Perilaku |
|---------|----------|
| `UseAuthorizer<T>` terdaftar | `T` adalah satu-satunya gerbang. Yang tidak di-`IsAllowedAsync` → ditolak (403). |
| `UseAuthorizer` **tidak** dipanggil | **Default allow** (ikut DynData). Vista mengeluarkan **warning startup** (`"no IViewAuthorizer registered — all views are publicly accessible"`) supaya tidak diam-diam terbuka di produksi. |

Catatan: ini sengaja **bukan** fail-closed (beda dari versi spec awal D4). Trade-off: ergonomi back-office + paritas DynData, dengan biaya default-open bila lupa konfigurasi. Dokumentasi produksi mewajibkan `UseAuthorizer`. Multi-tenant/row-scope memakai `ShapeQuery`, bukan filter dari klien.

**Lokasi tipe (D48):** `IViewAuthorizer`, `ViewAuthContext`, dan `ViewFacet` berada di **`a2n.Vista.AspNetCore`** (HTTP-bound — `ViewAuthContext` membawa `HttpContext`). `IViewScope` berada di **`a2n.Vista.Core`** (netral). Alur: AspNetCore memanggil `IsAllowedAsync`/`ShapeQuery`, membangun `IViewScope`, lalu menyerahkannya ke `IViewExecutor` (Core/EF). Dengan begitu Core tetap bebas dependensi HTTP & EF.

#### Contextual filter dari klien (lookup / drilldown) — `Scopable`

`externalFilter` DynData (lihat referensi DataTables) dipakai untuk lookup modal & drilldown dari klien. Di Vista, filter scoping **dari klien** hanya boleh menyentuh field yang dideklarasikan `Scopable` — **terpisah** dari `Filterable` UI:

```csharp
.Field(x => x.CategoryId, f => f.Hidden().Scopable())  // boleh jadi lookup key klien
```

- `Scopable` **default false** (opt-in) — beda dari `Filterable` yang default-allow. Lookup adalah jalur sensitif, jadi harus dideklarasi eksplisit.
- Adapter memetakan contextual filter klien → `FilterLeaf` yang divalidasi `field ∈ Scopable` (bukan `Filterable`). Pelanggaran → 400.
- Scope **server-trusted** (tenant, ownership) tetap lewat `IViewAuthorizer.ShapeQuery` — tidak butuh `Scopable` dan tidak bisa di-bypass klien.

## 6. Hello World End-to-End

```csharp
// 1. Entity (EF Core, milik aplikasi)
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

// 2. Query DTO (yang dikirim ke klien)
public class CustomerListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// 3. Crud DTO (yang diterima dari klien)
public class CustomerWriteDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

// 4. View definition
public class CustomerListView : View<CustomerListItem, CustomerWriteDto>
{
    protected internal override void Configure(
        IViewBuilder<CustomerListItem, CustomerWriteDto> b)
    {
        b.Named("customers")
         .From<Customer>(c => new CustomerListItem
         {
             Id = c.Id,
             Name = c.Name,
             CreatedAt = c.CreatedAt
         })
         // Filter/sort/search SEMUA field projection aktif by default.
         // Cukup override yang perlu:
         .Field(x => x.Id,        f => f.Hidden())                  // PK teknis, sembunyikan dari UI
         .Field(x => x.CreatedAt, f => f.Operators(FilterOperator.Range))
         .MaxPageSize(200)
         .MaxExportRows(10_000)
         // Route global ({root}/customers) & auth via authorizer pusat — tidak diset di sini.
         // Row filter di TSource (Customer), bukan TQuery — soft-delete ada di entity.
         .WithRowFilter<Customer>(_ => c => !c.IsDeleted)
         .CrudOn<Customer>()
              .MapWritable(w => w.Name,  e => e.Name)
              .MapWritable(w => w.Email, e => e.Email)
              .WithConcurrencyToken(e => e.RowVersion); // asumsikan ditambah
    }
}

// 5. Bootstrap (AOT-clean path)
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDb>(/* ... */);
builder.Services.AddVista(v =>
{
    v.RouteRoot("/api/views");            // route global (§5.6)
    v.UseAuthorizer<AppViewAuthorizer>(); // satu pintu auth (§5.6)
    v.Register<CustomerListView>();       // (source-gen menambahkan ini otomatis)
});

var app = builder.Build();
app.MapVistaViews();
app.Run();
```

Yang **tidak** muncul di Hello World tapi disengaja:

- `CustomerListItem` tidak punya field `Email` → tidak akan pernah dikirim klien. Mass assignment ke `Email` hanya bisa lewat `CustomerWriteDto.Email`, dan itu hanya pada endpoint CRUD yang sudah ter-authorize.
- Field `IsDeleted` tidak ada di `CustomerListItem` dan tidak di-`MapWritable` → tidak dapat di-set klien.
- `Filterable` eksplisit per field → klien tidak bisa filter `Email` walaupun ada di entity.

## 6A. Contoh: `vProductCategory` (gaya central-template)

Padanan langsung `NorthwindQueryTemplate.vProductCategory` dari DynData, di-port ke Vista gaya A. Read-only (anonymous projection), search/filter/sort opt-in, auth wajib.

```csharp
public class NorthwindViews : ViewTemplate<NorthwindDbContext>
{
    protected internal override void Configure(IViewTemplateBuilder<NorthwindDbContext> views)
    {
        views.AddView("vProductCategory", (db, sp) =>
                from p in db.Products
                join c in db.Categories on p.CategoryId equals c.CategoryId
                join s in db.Suppliers  on p.SupplierId equals s.SupplierId
                select new
                {
                    p.ProductId,
                    p.CategoryId,
                    c.CategoryName,
                    p.ProductName,
                    p.UnitPrice,
                    p.UnitsInStock,
                    p.SupplierId,
                    SupplierName    = s.CompanyName,
                    SupplierContact = s.ContactName
                })
            // Default: SEMUA field filter+sort+search, label auto, route {root}/vProductCategory,
            // auth via authorizer pusat. Cukup tandai yang khusus:
            .Field(x => x.ProductId,  f => f.PrimaryKey().Hidden())   // PK, sembunyikan
            .Field(x => x.CategoryId, f => f.Hidden())
            .Field(x => x.SupplierId, f => f.Hidden());
        // Tidak ada facet Write → read-only (List + Detail by ProductId).
    }
}
```

Beda dengan DynData (`AddQuery("vProductCategory", typeof(Product), ...)`):

- **Tidak ada `typeof(Product)`** → tidak ada CRUD di sini → tidak ada mass-assignment. Tetap read-only.
- **Filter/sort/search default aktif** untuk semua field projection (seperti DynData), tapi terbatas pada kolom yang **memang di-projeksi** — bukan semua kolom entity. Opt-out per field tersedia (`f.Searchable(false)`).
- **Auth terpusat** — tidak ada atribut auth di view; gerbang ada di `IViewAuthorizer` (§5.6).
- Metadata field via fluent `.Field(x => x.ProductId, ...)` strongly-typed (label auto), bukan callback string DynData.

### 6A.1 Menambah CRUD (naik ke facet Write, typed)

Kalau resource yang sama butuh create/edit, lampirkan facet Write typed via `WithCrud` — projection grid tetap anonymous, tulis lewat DTO + whitelist:

```csharp
views.AddView("vProductCategory", (db, sp) => /* ... projection anonymous sama ... */)
    .Field(x => x.ProductId, f => f.PrimaryKey().Hidden())
    // filter/sort/search semua field aktif default; override seperlunya
    .WithCrud<ProductWriteDto, Product>()
        .MapWritable(w => w.ProductName,  e => e.ProductName)
        .MapWritable(w => w.UnitPrice,    e => e.UnitPrice)
        .MapWritable(w => w.UnitsInStock, e => e.UnitsInStock)
        .MapWritable(w => w.CategoryId,   e => e.CategoryId)
        .MapWritable(w => w.SupplierId,   e => e.SupplierId);
// Route global ({root}/vProductCategory). Auth terpusat: IViewAuthorizer melihat
// ViewFacet (List/Detail vs Create/Update/Delete) → bisa beda kebijakan baca vs tulis (§5.6).

public class ProductWriteDto      // strong typed — wajib untuk tulis
{
    public string ProductName { get; set; } = "";
    public decimal? UnitPrice { get; set; }
    public short? UnitsInStock { get; set; }
    public int CategoryId { get; set; }
    public int SupplierId { get; set; }
}
```

Inilah tiga lanjur dari diskusi desain dalam satu resource: **List (grid, anonymous)**, **Detail (form display, anonymous)**, **Write (create/edit, typed)** — dihubungkan via `ProductId`.

## 7. Aturan Keamanan Default

| Aturan | Default |
|--------|---------|
| Field di response | Hanya yang ada di `TQuery`. |
| Field yang bisa difilter | **Semua field projection** (default allow). Opt-out: `.Field(x => x.F, f => f.Filterable(false))`. Batas aman = isi projection. |
| Field yang bisa di-sort | **Semua field projection** (default allow). Opt-out: `f.Sortable(false)`. |
| Field yang ikut global search | **Semua field string projection** (default allow). Opt-out: `f.Searchable(false)`. Berbeda dari versi spec awal (opt-in); aman karena projection sudah dikurasi. |
| Field yang bisa ditulis | Tidak ada. Harus opt-in via `MapWritable(...)`. **(Tetap default-deny — tulis ≠ baca.)** |
| Facet Write (CRUD) | Wajib typed `TCrud` + `MapWritable`. Projection anonymous **tidak pernah** jadi kontrak tulis. View anonymous-only = read-only. |
| Authorization | **Authorizer pusat** (`UseAuthorizer<T>`). Terdaftar → ia satu-satunya gerbang (yang tidak di-allow = ditolak). **Tidak** terdaftar → default **allow** (ikut DynData) + **warning startup**. Lihat §5.6. |
| Bulk operation | Off by default. Opt-in via `AllowBulk(true)`. |
| Export rows | Hard-capped global, override per view via `MaxExportRows`. **Berbeda dengan DynData** yang tidak ada cap. |
| Page size | Hard-capped global, override per view via `MaxPageSize`. **Berbeda dengan DynData** yang menerima `length=-1` (no paging). |
| Case-sensitivity filter/search | **Provider-detected di server**, bukan flag klien. Klien hanya kirim intent (`Contains`/`Equals`). Lihat Section 8. |
| Concurrency control (write) | Opt-in via `WithConcurrencyToken(...)`. Endpoint write me-respect header `If-Match`; konflik → 412 Precondition Failed. Detail di Spec 05. |
| Error contract | RFC 7807 Problem Details. Lihat Section 14. |

## 8. Filter & Search Contract (Hubungan ke Pilar 2)

Vista menetapkan **satu tree filter netral**, bukan tiga jalur paralel seperti DynData (`externalFilter` + `globalSearch` + `jsonQB`). Apapun bentuk request dari grid spesifik (DataTables, jQuery-QueryBuilder, AG Grid, OData), adapter (Pilar 2) menerjemahkannya ke struktur tunggal berikut sebelum sampai ke Core:

```csharp
public sealed record ViewQueryRequest(
    FilterNode? Filter,                  // tree tunggal, hasil merge filter + search dari adapter
    IReadOnlyList<SortSpec> Sort,
    int Page,
    int PageSize,
    IReadOnlyList<string>? SelectFields = null);

public abstract record FilterNode;
public sealed record FilterLeaf(string Field, FilterOperator Op, object? Value) : FilterNode;
public sealed record FilterAnd(IReadOnlyList<FilterNode> Children) : FilterNode;
public sealed record FilterOr(IReadOnlyList<FilterNode> Children) : FilterNode;
public sealed record FilterNot(FilterNode Child) : FilterNode;

[Flags]
public enum FilterOperator
{
    None = 0,
    Equals = 1, NotEquals = 2,
    GreaterThan = 4, GreaterThanOrEqual = 8,
    LessThan = 16, LessThanOrEqual = 32,
    Contains = 64, StartsWith = 128, EndsWith = 256,
    In = 512, Between = 1024, IsNull = 2048,
    // Convenience grouping
    Range = GreaterThanOrEqual | LessThanOrEqual | Between,
    Text = Equals | NotEquals | Contains | StartsWith | EndsWith | IsNull,
}
```

### 8.1 Search vs Filter di sisi adapter

Adapter bertugas mengubah request klien (mis. DataTables `search.value`) menjadi sub-tree `FilterOr` dari `FilterLeaf(Contains)` **hanya untuk field yang di-deklarasi `Searchable(...)`**, lalu meng-AND-kan dengan filter terstruktur (mis. dari Query Builder). Klien tidak menentukan field mana yang ikut search — itu keputusan View.

```text
Adapter input (DataTables):                Adapter output (ViewQueryRequest.Filter):
{                                          And(
  search: { value: "abc" },                  Or(
  columns: [Name, Status, ...],                Contains(Name, "abc"),
  filter: { Status = "Active" }                Contains(Description, "abc")  // only Searchable fields
}                                            ),
                                             Equals(Status, "Active")
                                           )
```

### 8.2 Case-sensitivity

Klien **tidak** mengirim flag `usePGSQL` / `ignoreCase` (kontras dengan DynData). Vista menentukan strategi di server berdasarkan provider EF Core:

| Provider | Default `Contains` translation |
|----------|--------------------------------|
| Npgsql (PostgreSQL) | `EF.Functions.ILike("%v%")` |
| SQL Server | `LIKE '%v%'` dengan collation default (CI by default di kebanyakan DB) |
| SQLite | `LIKE` (ASCII-CI native) |
| MySQL / Pomelo | `LIKE` dengan collation default |
| InMemory / test | `string.Contains(StringComparison.OrdinalIgnoreCase)` |

Override per-view tersedia jika perlu (mis. force case-sensitive untuk kolom dengan collation khusus).

### 8.3 Operator whitelist enforcement

Validasi dilakukan oleh `IViewExecutor` sebelum expression dibangun. Aturan eksplisit:

1. **Jalur filter klien** (filter terstruktur dari adapter): tiap `FilterLeaf(field, op, value)` harus memenuhi `field` filterable (default true, kecuali di-`Filterable(false)`) **dan** `op ∈ AllowedOperators[field]`. Pelanggaran → HTTP 400 dengan `field` & `operator` yang ditolak (lihat 14 — error model).
2. **Jalur global-search** (sub-tree `FilterOr(Contains, ...)` yang dibangun adapter dari `search.value`): field string searchable (default true, kecuali `Searchable(false)`) mengizinkan `Contains` **hanya pada jalur ini**.
3. **Pemisahan jalur** dilakukan adapter: ia menandai sub-tree mana yang berasal dari search vs filter (mis. record `FilterOrigin` internal atau menyusun sub-tree search di posisi tetap di pohon). `IViewExecutor` mengevaluasi tiap jalur dengan whitelist-nya.
4. Konsekuensi: field bisa di-opt-out dari salah satu jalur — search-only (`Filterable(false)`), filter-only (`Searchable(false)`), atau keduanya (default).

Contoh (default semua aktif; cukup opt-out yang khusus):

```csharp
b.Field(x => x.CreditCardLast4, f => f.Searchable(false))            // filterable, TIDAK ikut search box
 .Field(x => x.Description,     f => f.Filterable(false))            // searchable, tak bisa filter eksplisit
 .Field(x => x.Status,          f => f.Operators(FilterOperator.Equals)) // batasi operator
 .Field(x => x.CategoryId,      f => f.Hidden().Scopable());        // lookup key klien (§5.6)
```

**Jalur contextual/scope** (D47): sub-tree yang dibangun adapter dari `externalFilter`/lookup klien divalidasi terhadap **`Scopable`** (bukan `Filterable`). Scope server-trusted dari `IViewAuthorizer.ShapeQuery` (§5.6) tidak divalidasi whitelist — memang trusted.

Berbeda dengan DynData yang menerima filter ke field apa saja yang ada di property, dan yang mengikutkan semua field string ke global search secara otomatis.

## 9. Constraint AOT

Spec ini menetapkan agar implementasi **tidak melanggar**:

1. Tidak ada `Activator.CreateInstance(Type)` di hot path. Konstruksi `TQuery` lewat expression yang dikompilasi compile-time oleh source generator.
2. Tidak ada `JsonSerializer.Deserialize(string, Type)` tanpa `JsonTypeInfo`. Semua DTO punya `JsonSerializerContext` yang di-generate.
3. Tidak ada `PropertyInfo.GetValue/SetValue` di hot path. Mapping `TCrud → TEntity` dikompilasi compile-time dari `MapWritable(...)`.
4. Public surface yang tidak bisa AOT-clean diberi `[RequiresUnreferencedCode]` eksplisit dan harus punya jalur alternatif non-reflection.
5. `IViewRegistry.RegisterAssembly(...)` di-mark `[RequiresUnreferencedCode]`. Jalur AOT-friendly: `Register<TView>()` eksplisit (yang juga jalur yang dipanggil source generator).

## 10. Paging & Response Shape

DynData `PagingResult<T>` adalah bentuk yang sudah dipakai konsumen. Vista mempertahankan **bentuk** ini (dengan penyesuaian breaking yang sengaja) supaya migrasi minimal:

### 10.1 `PagedResult<T>`

```csharp
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    long TotalRows,        // long (DynData: int) — hindari overflow > 2B rows
    int PageIndex,         // 0-based
    int PageSize,
    long TotalPages);      // long (DynData: int) — konsistensi dengan TotalRows
```

Perbedaan disengaja dengan DynData:

| DynData | Vista | Alasan |
|---------|-------|--------|
| `int totalRows` | `long TotalRows` | Tabel > 2.1B baris (rare tapi nyata) overflow di DynData. |
| `int totalPages` | `long TotalPages` | Konsistensi. |
| `object context` field | **dihapus** | Untyped, anti-pattern, tidak pernah dipakai secara strongly-typed di DynData. |
| Mutable class (set di luar konstruktor) | `record` immutable | Thread-safety, defensive copy. |
| Sync `ToPagingResult` ada | **Async-only** | Blocking IO di EF Core anti-pattern. |
| Tidak ada `CancellationToken` | **Wajib di semua materializer** | Klien batal harus hentikan DB query. |
| `pageIndex * pageSize` (`int * int`) | Computed sebagai `long` | DynData `Skip(pageIndex * pageSize)` bisa overflow `int`. |

### 10.2 Materialization helper

Tidak ada extension method `IQueryable<T>.ToPagedResultAsync(...)` di public API Core — itu detail internal `IViewExecutor`. Alasan: extension method publik membuat developer tergoda memanggilnya dari mana saja, yang melewati path validasi/auth/limit Vista. Kalau developer butuh paging manual di luar View, mereka pakai EF Core LINQ langsung.

## 11. Export Contract

DynData punya endpoint export built-in (`csv`, `xlsx`) dengan custom `LiteExcelWriter` no-dependency. Vista mempertahankan kemampuan ini, tapi:

- **Pluggable exporter**: `IViewExporter` adalah kontrak terpisah, satu instance per format.
- **Default registrations**: `CsvViewExporter` dan `LiteXlsxViewExporter` (port `LiteExcelWriter` ke Core, tetap no-dependency).
- **Advanced exporter** (ClosedXML / OpenXmlSdk untuk multi-sheet, styling, formula) ada di paket terpisah `a2n.Vista.Exporters.ClosedXml` — bukan dependency Core.

### 11.1 Kontrak

```csharp
// Non-generic kontrak utama. Diresolve by Format string ("csv", "xlsx").
// Tidak generic supaya bisa di-resolve via DI tanpa reflection ke TQuery.
public interface IViewExporter
{
    string Format { get; }            // "csv", "xlsx", ...
    string MimeType { get; }
    string FileExtension { get; }

    // Erased TQuery: rows datang sebagai object dari pipeline streaming,
    // accessor per kolom diambil dari `fields` (delegate dari source-gen).
    Task ExportAsync(
        IAsyncEnumerable<object> rows,
        IReadOnlyList<FieldMetadata> fields,
        ExportColumnAccessors accessors,
        Stream destination,
        ExportOptions options,
        CancellationToken ct = default);
}

// Compile-time accessor map per view, dihasilkan source generator dari
// ViewMetadata. Tidak ada PropertyInfo.GetValue di hot path.
public sealed class ExportColumnAccessors
{
    public Type RowType { get; }
    public IReadOnlyDictionary<string, Func<object, object?>> ByField { get; }
    // ...
}

public sealed record ExportOptions(
    char Separator = ',',           // default RFC 4180
    Encoding? Encoding = null,      // default UTF-8 with BOM
    string? CultureName = null);    // default invariant; format date/number
```

- Input adalah `IAsyncEnumerable<object>` + accessor map per kolom — **bukan** `IQueryable<dynamic>` dan **bukan** generic method `ExportAsync<TQuery>`. Generic method di interface non-generic akan memaksa caller mono-morphize secara reflection saat resolve `IViewExporter` by string (dan itu balapan dengan pilar #3).
- Source generator menghasilkan `ExportColumnAccessors` per `View<TQuery>` sebagai partial init — accessor adalah delegate compile-time, bukan `PropertyInfo.GetValue`.
- `fields` dari `ViewMetadata` digunakan untuk header & formatting (`DisplayAttribute`, type formatting).
- Streaming: writer wajib men-stream baris ke `destination`, tidak boleh load semua ke memory.
- `CancellationToken` wajib di-cek tiap N baris (default `N = 1024`, override oleh implementor).

### 11.2 Hard limits

- `MaxExportRows` di-enforce **sebelum** pipeline export jalan: `qry.Take(maxRows + 1)`; kalau lebih, return `413 Payload Too Large` dengan saran narrow filter.
- Default global: 100.000 baris. Per-view override via `MaxExportRows(...)`.
- Hard cap absolut (tidak bisa di-bypass): 1.000.000 baris. Lebih dari itu, pakai background job (bukan endpoint sinkron).

### 11.3 Bug DynData yang TIDAK boleh ikut migrasi

`LinqExtension.ExportToCSV` (DynData) punya bug konkret. Setiap implementor `IViewExporter` di Vista **wajib** memenuhi properti berikut, divalidasi dengan test:

| Properti | DynData (LinqExtension.ExportToCSV) | Vista (mandatory) |
|----------|-------------------------------------|-------------------|
| Newline dalam nilai sel | `txt.Replace("\r", "").Replace("\n", "")` — **menghilangkan data** | RFC 4180: newline diizinkan **di dalam** quoted value. |
| Quote di dalam nilai | `Replace("\"", "\"\"")` — benar | Pertahankan: double-quote escape. |
| Separator | `CultureInfo.CurrentCulture.TextInfo.ListSeparator` — di server locale ID/DE jadi `;`, Excel locale lain rusak | Default `,` (RFC 4180). Override eksplisit per export call. |
| Encoding | Tidak ada BOM → Excel Windows non-UTF rusak karakter non-ASCII | UTF-8 **dengan BOM** default. |
| Materializer | `foreach (var item in query)` — load semua ke memory | `IAsyncEnumerable<TQuery>` streaming, await per batch. |
| Accessor per cell | `PropertyInfo.GetValue(item, null)` per row × per kolom | Delegate accessor dari source-gen (compile-time), `ref` mutable struct. |
| CancellationToken | Tidak ada | Wajib di kontrak; dicheck per N rows. |

### 11.4 Import — out of v1.0

Import (CSV/Excel → bulk insert) **bukan fitur v1.0**. Alasannya:

- DynData tidak punya fitur ini, jadi tidak ada *parity gap* untuk migrasi user.
- Desain yang aman butuh: row validation per-record, field whitelist (lebih ketat dari `MapWritable`), transactional batching, error reporting per-row, deduplikasi, mapping kolom file → field DTO.
- Lebih baik direncanakan setelah Core stabil.

Direncanakan untuk **v1.x** sebagai paket terpisah `a2n.Vista.Import` (CSV/Excel → `TCrud[]` validation pipeline → bulk insert via `ExecuteUpdateAsync`/`SaveChangesAsync`). Spec terpisah saat itu.

## 12. Migration Notes dari DynData

Spec ini sebagian **breaking** terhadap DynData. Konsumen `a2n.DynData` yang migrasi ke Vista akan mengalami:

### 12.1 Perubahan perilaku default

| DynData (otomatis) | Vista |
|---------------------|-------|
| Semua field string entity → global search | **Tetap default-allow**, tapi hanya field string **di projection** (bukan semua kolom entity). Opt-out per field via `.Field(x => x.F, f => f.Searchable(false))`. |
| Semua property → filterable | **Tetap default-allow**, terbatas field projection. Opt-out `f.Filterable(false)`. |
| Semua property → sortable | **Tetap default-allow**, terbatas field projection. Opt-out `f.Sortable(false)`. |
| Auto-expose semua `DbSet` | **Hilang** — setiap view di-`AddView(...)`/`Register<TView>()` eksplisit. |
| Endpoint CRUD aktif by default | **Hilang** — `WithCrud<TCrud,TEntity>()` / `CrudOn<TEntity>()` + `MapWritable(...)` eksplisit. |
| `IDynDataAPIAuth` (opsional) | `IViewAuthorizer` + `UseAuthorizer<T>` — gaya sama (satu pintu). Tanpa registrasi → default allow (paritas DynData). |

Catatan: filter/sort/search **tidak hilang** (beda dari versi spec awal yang opt-in). Yang berubah dari DynData hanyalah **cakupan**: dibatasi ke field yang di-projeksi, bukan seluruh kolom entity.

### 12.2 Format request

| DynData | Vista |
|---------|-------|
| `externalFilter` (JSON object datar) | `FilterNode` tree via adapter |
| `jsonQB` (jQuery-QueryBuilder format) | `FilterNode` tree via `a2n.Vista.Adapters.QueryBuilder` |
| DataTables shape (`start`, `length`, `columns[]`, `order[]`) | `ViewQueryRequest` via `a2n.Vista.Adapters.DataTablesNet` |
| `usePGSQL=true` flag dari klien | Tidak ada. Provider-detected di server. |
| `EnableSearchIgnoreCase=true` flag | Tidak ada. Provider-detected di server. |
| `length=-1` (return all) | Tolak. Page size hard-capped. |

### 12.3 Endpoint

Vista memisahkan **list-query** (read banyak, body filter) dari **create** (write satu) — keduanya `POST` dan harus path berbeda untuk menghindari MVC routing collision dan ambiguitas klien.

| DynData | Vista |
|---------|-------|
| `POST /dyndata/{controller}/{viewName}/datatable` | `POST /api/views/{viewName}/query` (response shape dipilih adapter via `Accept` header atau route prefix) |
| `POST /dyndata/{controller}/{viewName}/list` | `POST /api/views/{viewName}/query` (default JSON shape) |
| `POST /dyndata/{controller}/{viewName}/export` | `POST /api/views/{viewName}/export?format=csv\|xlsx` |
| `POST /dyndata/{controller}/{viewName}/read` | `GET /api/views/{viewName}/{key}` |
| `POST /dyndata/{controller}/{viewName}/create` | `POST /api/views/{viewName}` (jika `CrudOn` ada) |
| `POST /dyndata/{controller}/{viewName}/update` | `PUT /api/views/{viewName}/{key}` (concurrency: `If-Match`) |
| `POST /dyndata/{controller}/{viewName}/delete` | `DELETE /api/views/{viewName}/{key}` (concurrency: `If-Match`) |
| `GET /dyndata/{controller}/{viewName}/metadata` | `GET /api/views/{viewName}/metadata` |
| `GET /dyndata/{controller}/{viewName}/metadataQB` | Output adapter-spesifik (`a2n.Vista.Adapters.QueryBuilder` menghasilkan jQuery-QueryBuilder schema). |
| `GET /dyndata/{controller}/{viewName}/dropdown` | Out of v1.0. Stub kontrak: `GET /api/views/{viewName}/distinct/{field}` direservasi (lihat Section 14). |

### 12.4 Fungsi `LinqExtension.cs` & `AnonymousType.cs` — **TIDAK** di-port

DynData punya `Extensions/LinqExtension.cs` (1461 baris) yang berisi banyak ekstensi `IQueryable`. Hasil audit:

| Fungsi DynData | Verdict | Pengganti di Vista |
|----------------|---------|---------------------|
| `ToPagingResult` / `ToPagingResultAsync` (paging) | **Port konsep** (rewrite) | Lihat Section 10. Internal `IViewExecutor`, bukan extension publik. |
| `OrderBy(IQueryable, string key, bool asc)` + variants | **Tidak** | Sort default semua field projection (opt-out `.Field(x => x.F, f => f.Sortable(false))`); field di luar projection → HTTP 400. Expression via source-gen delegate. |
| `ThenBy(IQueryable, string key, ...)` variants | **Tidak** | Bagian dari sort whitelist di atas. |
| `Where(IQueryable, object whereExp, Type)` | **Tidak** | `IQueryable<TSource>` strongly-typed, expression dibangun dari `FilterNode` tree. |
| `AsNoTrackingDynamic(IQueryable<dynamic>)` | **Tidak** | `.AsNoTracking()` standar EF Core. |
| `Select(IQueryable, params string[] fieldNames)` + variants | **Tidak** | Projection compile-time di `From<TSource>(...)` + source-gen accessor untuk sparse `SelectFields`. |
| `InnerJoin` / `LeftJoin` / `RightJoin` / `FullJoin` (~750 baris) | **Tidak** | Developer pakai EF Core LINQ standar di delegate `FromQuery<TSource>(...)`. |
| `SelectRecursive<T>(IEnumerable<T>, Func<T, IEnumerable<T>>)` | Opsional | Utility umum, bisa di-drop atau ditaruh di `Vista.Core/Utilities` kalau dipakai banyak. |
| `ExportToCSV` / `ExportToExcel` | **Port konsep** (rewrite) | Lihat Section 11. Bug RFC 4180 dan reflection per-cell wajib diperbaiki. |
| `GroupByDateTimeInterval` (commented-out incomplete) | **Tidak** | Time-bucketing kandidat v1.x. |
| `AnonymousType.cs` (Reflection.Emit, ~29 KB) | **Tidak** | Vista pakai tipe statis di developer-defined DTO. Source-gen menghasilkan partial classes, bukan emit type runtime. AOT-incompatible secara fundamental. |

### 12.5 Compatibility layer (kandidat, bukan komitmen)

Paket opsional `a2n.Vista.Compat.DynData` bisa menyediakan:

- Route alias `/dyndata/{controller}/*` → forward ke Vista endpoint setara.
- Adapter `externalFilter` & `jsonQB` (sudah di-rencanakan untuk Pilar 2).
- Helper auto-register View dari `DbContext` (read-only, scaffold) untuk pengguna yang baru pindah.

Keputusan apakah paket ini dirilis bersama v1.0 ditangguhkan sampai ada feedback user DynData.

## 13. Decision Log

| # | Keputusan | Status | Catatan |
|---|-----------|--------|---------|
| D1 | Pisahkan `TQuery` dan `TCrud` di level tipe | **Decided** | Cegah mass-assignment seperti DynData. |
| D2 | Tidak ada auto-expose `DbSet` | **Decided** | View harus eksplisit. |
| D3 | `Filterable` & `Sortable` opt-in per field | **Superseded by D42** | Dibalik jadi default-allow + opt-out. |
| D4 | Authorization wajib di-set saat build | **Superseded by D43** | Diganti authorizer pusat; default-allow bila tak terdaftar. |
| D5 | `System.Text.Json` native di Core, Newtonsoft di paket terpisah | **Decided** | Sesuai ROADMAP. |
| D6 | CPM (Central Package Management) | **Decided** | `Directory.Packages.props` di repo root. |
| D7 | Test framework | **Decided: TUnit** | Modern, AOT-friendly — selaras dengan Pilar 3. Dipasang saat test project pertama dibuat. |
| D8 | Multi-target `net8.0;net9.0;net10.0` | **Decided** | Sudah di `Directory.Build.props`. |
| D9 | `<Nullable>disable</Nullable>` global | **Superseded by D9-revised** | Lihat D9-revised di bawah. |
| D10 | View identifier: string `Named("customers")` atau type-only | **Open** | String memudahkan TS client routing, tapi rawan typo. Kandidat: validate via source generator. |
| D11 | Bagaimana `From<TSource>(projection)` mendapatkan `IQueryable<TSource>` | **Open** | Resolusi via DI: butuh `DbContext` factory atau abstraction `IQueryableProvider<T>`. Kandidat: konvensi `services.GetRequiredService<IQueryable<TSource>>()` via shim. |
| D12 | Apakah `View` perlu generation dari source-gen (selain metadata)? | **Open** | Kandidat: source-gen menghasilkan `partial` View dengan registrasi otomatis. |
| D13 | Lokasi `ViewMetadata` runtime vs compile-time | **Open** | Spec saat ini menyebut runtime. Mungkin perlu varian compile-time-only untuk AOT. |
| D14 | `Searchable` terpisah dari `Filterable` (global search tidak auto-attack semua field string seperti DynData) | **Superseded by D42** | Konsep pemisahan Filter vs Search dipertahankan (§4.4), tapi default searchable dibalik jadi allow + opt-out. |
| D15 | Import (CSV/Excel → bulk insert) | **Decided: defer ke v1.x** | Section 11.4. Bukan parity gap DynData; perlu desain validasi yang matang. Direncanakan sebagai paket terpisah `a2n.Vista.Import`. |
| D16 | Exporter pluggable, default port `LiteExcelWriter` ke Core | **Decided** | Section 11. `IViewExporter` kontrak, default `CsvViewExporter` + `LiteXlsxViewExporter` no-dep. Advanced (ClosedXML/EPPlus) di paket terpisah. |
| D17 | Case-sensitivity & ILIKE/LIKE: provider-detected di server, bukan flag klien | **Decided** | Section 8.2. Klien hanya kirim intent (`Contains`/`Equals`), Vista pilih translation berdasarkan provider EF Core. |
| D18 | Single tree filter (`FilterNode`) menggantikan 3 jalur DynData (`externalFilter` + `globalSearch` + `jsonQB`) | **Decided** | Section 8. Adapter (Pilar 2) menerjemahkan format grid spesifik ke tree netral. |
| D19 | Hard cap absolut export 1.000.000 baris (tidak bisa di-bypass via konfigurasi) | **Decided** | Section 10.2. Lebih dari itu pakai background job. |
| D20 | Compatibility layer `a2n.Vista.Compat.DynData` (route alias `/dyndata/*` dst.) | **Open** | Section 12.5. Tergantung feedback user DynData. |
| D21 | `PagedResult<T>` immutable record, `long` totals, no `object context`, async-only materialization | **Decided** | Section 10. Breaking dari DynData `PagingResult<T>` (mutable class, `int`, sync overload). |
| D22 | `IViewExporter` mandatory properties: RFC 4180 CSV, UTF-8 BOM, streaming `IAsyncEnumerable`, source-gen accessor, `CancellationToken` | **Decided** | Section 11.3. Eksplisit tutup bug DynData. |
| D23 | `AnonymousType.cs` (Reflection.Emit runtime types) tidak di-port. Vista pakai source-gen partial classes. | **Decided** | Section 12.4. Fundamental anti-AOT. |
| D24 | Dynamic join via string field name (`InnerJoin`/`LeftJoin`/`RightJoin`/`FullJoin` di DynData) tidak di-port. Developer pakai LINQ EF langsung di delegate source query. | **Decided** | Section 12.4. ~750 baris kode dihilangkan, tipe statis menggantikan. |
| D25 | `MapWritable` exhaustiveness: default **ignore** untuk field `TCrud` yang tidak di-map; opt-in strict via `[VistaWritable(strict: true)]`. Source-gen mengeluarkan diagnostic `VISTA0010` (info). | **Decided** | Closes prior Open Question #5. |
| D26 | Read-only View dipisah ke base class `View<TQuery>` dengan builder `IViewBuilder<TQuery>` yang **tidak punya** `CrudOn`. `View<TQuery, TCrud>` adalah base terpisah, bukan subclass dari `View<TQuery>`. Marker `NoCrud` dihilangkan. | **Decided** | Section 5.1, 5.2. Cegah compile-time access ke CRUD knob di view read-only. |
| D27 | Adapter **tidak** mengakses raw `IQueryable<TSource>`. Adapter hanya bicara `ViewQueryRequest` dan `PagedResult<TQuery>`. Optimasi seperti `Include` adalah tanggung jawab `FromQuery<TSource>(...)` di View definition. | **Decided** | Closes prior Open Question #3. |
| D28 | Row filter default di **TSource** (pre-projection) via `WithRowFilter<TSource>(...)`. Post-projection `WithProjectedRowFilter` ada untuk kasus khusus. | **Decided** | Section 5.2, 6. Closes prior Open Question #2. |
| D29 | `MaskField(field, predicate, masker)` — masker `Func<TProp, TProp>` wajib. Tidak ada masking implicit (`null` / `"***"`). | **Decided** | Section 5.2. |
| D30 | `WithConcurrencyToken(field)` opt-in di `ICrudBuilder`. Endpoint write me-respect header `If-Match`. Konflik → 409 / 412. | **Decided** | Section 5.2, 14.2. |
| D31 | `WithInterceptor<T>` opt-in. Forecast audit log v1.x supaya v1.0 → v1.x non-breaking. | **Decided** | Section 5.2. |
| D32 | List-query endpoint dipisah dari create: `POST /api/views/{viewName}/query` vs `POST /api/views/{viewName}`. Hindari MVC routing collision. | **Decided** | Section 12.3. |
| D33 | Error contract: RFC 7807 Problem Details, `type` namespace di bawah `https://a2n.dev/vista/errors/`. | **Decided** | Section 14.1. Detail bentuk JSON di Spec 05. |
| D34 | `IViewExporter` non-generic. Generic method di interface non-generic akan memaksa reflection saat resolve by `Format`. Source-gen menghasilkan `ExportColumnAccessors` per view. | **Decided** | Section 11.1. |
| D35 | Distinct-values endpoint `GET /api/views/{viewName}/distinct/{field}` direservasi sebagai stub kontrak — implementasi v1.x. | **Decided** | Section 14.3. v1.x tidak breaking. |
| D36 | `Filterable<TProp>` overload tanpa default parameter generic. | **Superseded by D42/D45** | Standalone `Filterable(...)` dihapus; operator diatur via `.Field(..., f => f.Operators(...))`. |
| D9-revised | `<Nullable>enable</Nullable>` global sebelum implementasi `a2n.Vista.Core` substantial. Mengubah dari `disable` di `Directory.Build.props` adalah pre-requisite untuk PR pertama yang menyentuh public API. | **Decided** | Replaces D9 "Open". Library AOT-first tidak boleh menabung NRT debt. |
| D37 | Dua gaya authoring: central-template anonymous (gaya A, ala DynData) + class-per-view typed (gaya B). Keduanya hasilkan `ViewMetadata` sama. | **Decided** | Section 4.5. Vista = evolusi DynData, bukan rewrite. |
| D38 | Invarian typing: projection anonymous hanya untuk facet baca (List/Detail); facet Write WAJIB `TCrud` typed + `MapWritable`. View anonymous-only = read-only. | **Decided** | Section 4.5, 4.6. Tutup mass-assignment di level desain. Me-refine rumusan awal "anonymous ⇒ seluruh view read-only" menjadi per-facet (read boleh anonymous, write wajib typed). |
| D39 | Model facet: satu View = resource dengan ≤3 facet (List wajib, Detail opsional fallback-by-PK, Write opsional typed). PK menjembatani facet. Auth per-facet. | **Decided** | Section 4.6. List=grid, Detail=form display, Write=create/edit. |
| D40 | Gaya A men-trigger `[RequiresUnreferencedCode]` (registrasi + serialisasi anonymous). Native AOT penuh → gaya B. Facet Write tetap AOT-clean di dua gaya. | **Decided** | Section 4.5, 5.5. Selaras tradeoff ROADMAP Pilar 3. |
| D41 | Field metadata gaya A via fluent expression `.Field(x => x.Prop, f => f.PrimaryKey().Hidden())`, bukan callback string `meta.FieldName == "..."` (DynData). | **Decided** | Section 5.5, 6A. Lebih aman dari typo. |
| D42 | Filter/Sort/Search **default-allow** untuk semua field projection (opt-out via `.Field(..., f => f.Filterable(false)/.Searchable(false))`). Batas keamanan = isi projection yang sudah dikurasi. | **Decided** | Section 4.4, 7. **Supersedes D3 & D14** (dulu opt-in/default-deny). |
| D43 | Authorization **terpusat** via `IViewAuthorizer.IsAllowedAsync` + `ShapeQuery` (gaya `IDynDataAPIAuth`), didaftarkan `UseAuthorizer<T>`. Tanpa authorizer → **default allow** + warning startup (bukan fail-closed). | **Decided** | Section 5.6. **Supersedes D4**. Ikut DynData; trade-off ergonomi vs default-open. |
| D44 | Route **global** via `RouteRoot(...)`; route view diturunkan `{root}/{viewName}`. Tidak ada `Route()` per-view (escape-hatch saja). | **Decided** | Section 5.6, 12.3. |
| D45 | Konfigurasi per-field via satu builder `.Field(selector, f => f.Label(...).Hidden().Operators(...).Searchable(false))`; label auto dari nama field (PascalCase → "Title Case"). | **Decided** | Section 5.4, 5.5. Menggantikan rantai `.Filterable().Sortable().Searchable()` verbose. |
| D46 | `IViewAuthorizer.ShapeQuery` jadi rumah contextual/row filter server-trusted (tenant, ownership) — menjawab opsi (a) pertanyaan `externalFilter` (lihat referensi DataTables). Filter scoping dari klien tetap tunduk whitelist. | **Decided** | Section 5.6. |
| D47 | Contextual/lookup filter dari **klien** (padanan `externalFilter`) hanya ke field `Scopable` (opt-in, default false), terpisah dari `Filterable` UI. Scope server-trusted via `ShapeQuery`. | **Decided** | Section 5.6, 8.3. Opsi (c). |
| D48 | **Layering paket**: `Core` bebas EF & HTTP (kontrak netral + port `IViewExecutor`/`IViewScope`). `EntityFrameworkCore` implement `IViewExecutor` + authoring DbContext-bound. `IViewAuthorizer` di `AspNetCore` (HTTP-bound). Adapter & `Client.TypeScript` → `Core` saja. EF & AspNetCore **tidak** saling referensi (ketemu di `IViewExecutor`). | **Decided** | ROADMAP "Struktur Paket NuGet". Diterapkan di csproj. |
| D49 | Facet Detail v0.x = fallback ke projection List by-PK (gaya A). Facet Detail dengan projection sendiri ditunda. | **Decided** | Section 4.6. |
| D50 | `<Nullable>enable</Nullable>` di-set global (implementasi D9-revised). | **Done** | `Directory.Build.props`. |

## 14. Error Model & Concurrency

### 14.1 Error contract — RFC 7807 Problem Details

Semua endpoint Vista mengembalikan `application/problem+json` untuk error, dengan `type` di-namespace di bawah `https://a2n.dev/vista/errors/`. Contoh klasifikasi:

| Kondisi | HTTP | `type` |
|--------|------|--------|
| Filter ke field yang bukan `Filterable` | 400 | `.../filter-field-not-allowed` |
| Operator filter di luar `AllowedOperators` | 400 | `.../filter-operator-not-allowed` |
| Sort ke field yang bukan `Sortable` | 400 | `.../sort-field-not-allowed` |
| Validasi `TCrud` gagal | 400 | `.../validation` (per-field detail) |
| Tidak ter-otorisasi | 401 | `.../unauthorized` |
| Authorize policy gagal | 403 | `.../forbidden` |
| Not found (CRUD by key) | 404 | `.../not-found` |
| `If-Match` token salah / hilang | 412 | `.../precondition-failed` |
| Konflik concurrency saat `SaveChanges` | 409 | `.../concurrency-conflict` |
| Hard limit page size / export rows tercapai | 413 | `.../payload-too-large` |
| Error tak terduga | 500 | `.../unexpected` |

Setiap response menyertakan `extensions` machine-readable: nama field yang ditolak, operator yang tidak diizinkan, allowed list, dst. Detail bentuk JSON di Spec 05.

### 14.2 Concurrency control (write path)

- View dengan `WithConcurrencyToken(field)` menambahkan token ke response read (`GET /{key}` dan `query`) sebagai field DTO atau header `ETag` (default: header).
- Klien WAJIB mengirim `If-Match: <token>` saat `PUT` / `DELETE`.
- Endpoint mapper (Spec 05): tidak ada header → 412; header tidak match nilai DB saat `SaveChanges` → 409.
- Token boleh berupa `byte[] RowVersion` (SQL Server `rowversion`), `xmin` (PostgreSQL), atau kolom `DateTime LastModifiedAt` (database tanpa native rowversion). Encoding ke string ETag: base64url untuk `byte[]`, ISO-8601 untuk `DateTime`.

### 14.3 Distinct-values endpoint (stub)

Endpoint `GET /api/views/{viewName}/distinct/{field}?prefix=&take=50` direservasi untuk dukungan AG Grid set filter, MudBlazor SelectFilter, PrimeNG MultiSelect, dst. **Out of v1.0**, tapi route dan validasi (`field ∈ Filterable`, hard cap `take ≤ 1000`) ditetapkan sekarang supaya v1.x tidak breaking. Implementasi penuh: Spec 04 atau spec terpisah.

## 15. Open Questions

1. **Versioning route**: apakah `Route("/api/views/customers")` perlu konvensi `v1`? Kandidat: prefix global `services.AddVista(v => v.RouteRoot("/api/v1/views"))`. Defer ke Spec 05.
2. **Sparse `SelectFields` di `ViewQueryRequest`** (Section 8): kapan adapter boleh men-set ini? Spec saat ini tidak menjelaskan trade-off vs proyeksi compile-time `From<TSource>`. Kandidat: `SelectFields` adalah **subset** dari field di `TQuery`, tidak boleh menambah; source-gen menghasilkan accessor delegate per kombinasi.
3. **`From<TSource>` resolusi DI tanpa explicit factory** (D11): apakah cukup konvensi `services.GetRequiredService<DbContext>().Set<TSource>()`? Spec saat ini punya `FromQuery<TSource>(...)` sebagai escape hatch. Putuskan default sebelum implementasi `IViewExecutor`.
4. **`MapWritable` exhaustiveness** — **Decided: default ignore** (lihat Decision Log D25). Source-gen menghasilkan diagnostic info-level (`VISTA0010`) untuk field di `TCrud` yang tidak di-map. Opt-in strict via attribute `[VistaWritable(strict: true)]` di kelas `TCrud`.
5. **Bentuk concurrency token di response read**: header `ETag` (HTTP-idiomatic) vs field di DTO (klien JS lebih gampang). Kandidat default: header dengan opsi expose ke field via `WithConcurrencyToken(..., exposeAs: "RowVersion")`.

## 16. Next Spec Documents

Setelah spec ini stabil:

- `02-filter-and-query.md` — detail expression builder, provider-aware filter, sanitization, validasi operator whitelist.
- `03-source-generator.md` — kontrak codegen (input: `ViewMetadata`, output: registration + serialization context + OpenAPI).
- `04-adapter-contract.md` — `IViewAdapter<TRequest, TResponse>` (Pilar 2), termasuk adapter DataTables & QueryBuilder yang menjadi target migrasi DynData. Referensi perilaku nyata DynData (kontrak `metadataQB`/`datatable`, 3 jalur filter, payload `jsonQB`): [`../reference/dyndata-datatables-observed.md`](../reference/dyndata-datatables-observed.md).
- `05-aspnetcore-mapping.md` — `MapView<TView>()`, route konvensi, error model, response shape.
- `06-typescript-client.md` — bentuk codegen DTO + filter API di TS.
- `07-export.md` — detail `IViewExporter`, format default, streaming, `LiteXlsxViewExporter` migration dari DynData.
- `08-migration-from-dyndata.md` — extended migration guide dengan contoh konkret per fitur.
