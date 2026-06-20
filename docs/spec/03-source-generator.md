# Spec 03 — Source Generator (Pilar 3)

> Status: **DRAFT**
> Tanggal: 2026-06-19
> Scope: Roslyn incremental source generator `a2n.Vista.SourceGenerators` yang menghilangkan reflection di hot path dan menjadikan Vista Native-AOT-clean. Menghasilkan: implementasi `IConfiguredView`, `ViewMetadata` compile-time, member-access & accessor map, `CompiledView` (projection/`MapWritable`), `JsonSerializerContext`, auto-registration, dan dokumen OpenAPI. **Bukan** termasuk: semantik query (Spec 02), kontrak adapter (Spec 04), endpoint HTTP (Spec 05). Dokumen ini menetapkan **apa yang di-generate & kontraknya**, bukan detail implementasi Roslyn baris-demi-baris.

---

## 1. Tujuan

Pilar 3 = "AOT-First, bukan AOT-as-afterthought" (ROADMAP). Generator wajib memenuhi constraint AOT Spec 01 §9 untuk **semua jalur panas**:

1. **Tanpa `Activator.CreateInstance`** — konstruksi `TQuery`/`TCrud` lewat kode ter-generate.
2. **Tanpa `PropertyInfo.GetValue/SetValue`** — baca/tulis field lewat accessor delegate compile-time.
3. **Tanpa `JsonSerializer.(De)serialize(.., Type)` non-typed** — tiap DTO punya `JsonTypeInfo` ter-generate.
4. **Tanpa `Expression.Property(..)` runtime** — member-access untuk filter/sort di-generate dari shape `TQuery`.
5. **Tanpa reflection scan** di registrasi — `Register<TView>()` dipanggil dari kode ter-generate (bukan `RegisterAssembly`, Spec 01 §5.3/§9).

Generator adalah **penyedia artefak** yang dikonsumsi engine (Spec 02), exporter (Spec 01 §11), dan adapter (Spec 04 §9).

## 2. Posisi dalam Arsitektur

```text
            COMPILE TIME (Roslyn)                         RUNTIME
┌──────────────────────────────────────┐     ┌───────────────────────────────┐
│ a2n.Vista.SourceGenerators           │     │ engine (Spec 02) ─┐           │
│  input:  View<>/ViewTemplate<> syntax │     │ exporter (§11) ───┼─ consume  │
│  output: partial IConfiguredView      │ ──► │ adapter (Spec 04)─┘ CompiledView│
│          ViewMetadata builder         │     │ AspNetCore (Spec 05) endpoints │
│          member-access + accessors     │     └───────────────────────────────┘
│          CompiledView (proj/MapWritable)│
│          JsonSerializerContext          │
│          module initializer (register)  │
│          OpenAPI document model          │
└──────────────────────────────────────┘
```

Constraint paket (ROADMAP D48): generator adalah **Roslyn analyzer** (`netstandard2.0`, `IIncrementalGenerator`), **tanpa referensi proyek Vista lain**. Ia mengenali tipe Vista lewat **nama fully-qualified** (string matching simbol), bukan referensi assembly. Kode yang di-generate ditempatkan di assembly konsumen (yang mendefinisikan View), bukan di generator.

| Dokumen | Hubungan |
|---|---|
| `01-view.md` | Input: `View<TQuery>`, `View<TQuery,TCrud>`, `ViewTemplate<TDbContext>`, fluent DSL. Output: `IConfiguredView`, `ViewMetadata`, `ExportColumnAccessors`. |
| `02-filter-and-query.md` | Konsumen: member-access (§14 Spec 02), `CompiledView`, dua-count engine. |
| `04-adapter-contract.md` | Konsumen: `JsonTypeInfo` untuk `ToResponse` (§9 Spec 04). |
| `05-aspnetcore-mapping.md` | Konsumen: auto-registration + OpenAPI document model. |

## 3. Terminologi

| Istilah | Arti |
|---|---|
| **Shape-driven** | Generasi dari **simbol tipe** (`TQuery`/`TCrud`/`TEntity`) — hanya butuh daftar properti. Robust, selalu berhasil. |
| **DSL-recognized** | Generasi dari **analisis body** `Configure`/`AddView` (fluent chain). Best-effort; diagnostic bila tak dikenali. |
| **`CompiledView`** | Bundle delegate ter-generate untuk satu view: source query, projection, member-access, accessors, `MapWritable` assignment. |
| **Member-access** | `Expression<Func<TQuery,TProp>>` / `Func<TQuery,object?>` per field — pengganti `Expression.Property` runtime. |
| **Accessor** | `Func<object,object?>` (baca) & `Action<object,object?>` (tulis/mask) per field — pengganti `PropertyInfo`. |
| **RUC** | `[RequiresUnreferencedCode]` — jalur yang tak AOT-clean (mis. serialisasi anonymous gaya A). |

## 4. Non-Goals

- Detail implementasi `IIncrementalGenerator` (pipeline node, caching) — disinggung §12, bukan normatif baris-demi-baris.
- Generasi kode write-endpoint HTTP (routing) → Spec 05; generator hanya menyediakan `MapWritable` assignment & metadata.
- Generasi klien TypeScript → Spec 06 (tool terpisah, mengonsumsi `ViewMetadata`/OpenAPI).
- Analisis projection arbitrer kompleks penuh → §17 (kedalaman analisis DSL adalah open question).

## 5. Input — Apa yang Dipindai Generator

Generator mengumpulkan **kandidat View** dari syntax (cepat, incremental):

1. **Gaya B (class-per-view):** kelas non-abstract yang mewarisi `a2n.Vista.View<TQuery>` atau `View<TQuery,TCrud>` (Spec 01 §5.1). Wajib `partial` (§7, VISTA0001).
2. **Gaya A (central template):** kelas yang mewarisi `ViewTemplate<TDbContext>` (Spec 01 §5.5); tiap pemanggilan `views.AddView("name", query)` di dalam `Configure` = satu view.

Untuk tiap kandidat, generator mengekstrak (kombinasi shape-driven + DSL-recognized, §6):

- `TQuery` (typed atau anonymous), `TCrud`, `TEntity`/`TSource`.
- Projection lambda (`From<TSource>(x => new TQuery{..})` / body `AddView`).
- Konfigurasi field (`.Field(x => x.F, f => f.PrimaryKey().Hidden()...)`).
- `MapWritable(w => w.P, e => e.P)` (facet Write).
- `Named(...)`/nama `AddView`, `MaxPageSize`, `MaxExportRows`.

## 6. Model Generasi — Shape-driven + DSL-recognized

### 6.1 Shape-driven (selalu, dari simbol tipe)

Hanya butuh daftar properti tipe → **selalu** berhasil, **selalu** AOT-clean:

| Artefak | Dari | Dipakai oleh |
|---|---|---|
| Member-access per properti `TQuery` | properti `TQuery` | filter/sort engine (Spec 02 §9, §11) |
| Accessor `Func<object,object?>` per properti `TQuery` | properti `TQuery` | export (Spec 01 §11), mask (§13) |
| `JsonTypeInfo`/`JsonSerializerContext` `TQuery`,`TCrud` | tipe typed | serialisasi response/request (Spec 04 §9) |
| Konstruktor `TQuery`/`TCrud` (no `Activator`) | tipe typed | materialisasi, model-bind |

> Anonymous `TQuery` (gaya A): member-access & accessor **tetap** bisa di-generate (shape anonymous terlihat di compilation), tapi `JsonSerializerContext` **tidak** (anonymous tak punya nama untuk direferensikan) → serialisasi jatuh ke STJ reflection (RUC). Konsisten Spec 01 §4.5/§9.

### 6.2 DSL-recognized (dari analisis body)

Butuh memahami fluent chain → best-effort, dengan diagnostic bila pola tak dikenali:

| Artefak | Pola dikenali | Fallback bila tak dikenali |
|---|---|---|
| Projection delegate `TSource→TQuery` | member-init `new TQuery { A = s.A, B = s.B }` / anonymous `new { s.A }` | VISTA0003 (warning) + projection interpreted (RUC) |
| `MapWritable` assignment `TEntity.P = TCrud.P` | `MapWritable(w => w.P, e => e.P)` selektor member sederhana | VISTA0012 (warning) + assignment interpreted (RUC) |
| Field config default | rantai `.PrimaryKey()/.Hidden()/.Operators(..)/.Searchable(false)` literal | dievaluasi runtime saat startup (cold path; tetap AOT-safe) |

Prinsip: **shape selalu compile-time; konfigurasi boleh runtime-startup (cold); hanya hot-path yang wajib delegate ter-generate.** Membaca `MemberExpression` `x => x.F` saat startup untuk membangun `ViewMetadata` adalah AOT-safe (bukan reflection-emit) — yang dilarang adalah `Compile()` expression di runtime & `PropertyInfo` di hot path.

## 7. Output — Partial `IConfiguredView` & Registrasi

View gaya B harus `partial`; generator melengkapi `IConfiguredView` (Spec 01 §5.1):

```csharp
// ditulis developer
public partial class CustomerListView : View<CustomerListItem, CustomerWriteDto>
{
    protected internal override void Configure(IViewBuilder<CustomerListItem, CustomerWriteDto> b) { /* ... */ }
}

// di-generate (ilustratif)
partial class CustomerListView : IConfiguredView
{
    public string Name => "customers";
    public Type QueryType => typeof(CustomerListItem);
    public Type? CrudType => typeof(CustomerWriteDto);
    public void ConfigureCore(IViewBuilderCore builder) { /* metadata bootstrap */ }
}
```

**Auto-registration** via module initializer ter-generate (Spec 01 §5.3 "source-gen menambахkan ini otomatis"):

```csharp
// di-generate per assembly konsumen
internal static class VistaGeneratedRegistration
{
    [ModuleInitializer]
    internal static void Register() => VistaRegistry.AddGenerated(
        typeof(CustomerListView), /* CompiledView bundle */ CompiledViews.Customers, ...);
}
```

`AddVista(...)` mengonsumsi registry ter-generate ini; `Register<TView>()` eksplisit tetap sah (idempoten, dedup by Name). `RegisterAssembly` tetap `[RequiresUnreferencedCode]` (jalur non-AOT, Spec 01 §9).

## 8. Output — `CompiledView` Bundle

Satu `CompiledView` per view, di-store ke kontrak Core yang dikonsumsi engine/exporter/adapter:

```csharp
namespace a2n.Vista;

// Bundle delegate compile-time. Tidak ada reflection di anggotanya.
public sealed class CompiledView
{
    public string Name { get; init; }
    public ViewMetadata Metadata { get; init; }

    // Source query factory (Spec 02 §5 langkah 6). object = IQueryable<TSource> erased.
    public Func<IServiceProvider, object> SourceQuery { get; init; }

    // Projection TSource→TQuery sebagai Expression (untuk EF translate, Spec 02 §5 langkah 8).
    public LambdaExpression Projection { get; init; }

    // Member-access per field: nama → Expression<Func<TQuery,object?>> (filter/sort, Spec 02 §9/§11).
    public IReadOnlyDictionary<string, LambdaExpression> MemberAccess { get; init; }

    // Accessor baca per field (export §11, mask §13).
    public ExportColumnAccessors Accessors { get; init; }

    // Mask mutator per field (Spec 01 §5.2/§13). null bila tak ada mask.
    public IReadOnlyDictionary<string, Action<object, IServiceProvider>>? Maskers { get; init; }

    // Write: assign TCrud→TEntity dari MapWritable (Spec 01 §5.2). null bila read-only.
    public Action<object /*crud*/, object /*entity*/>? ApplyWritable { get; init; }

    // Primary key accessor (Detail by-key & tiebreaker paging, Spec 02 §11).
    public Func<object, object>? KeySelector { get; init; }
}

public interface ICompiledViewStore
{
    bool TryGet(string viewName, out CompiledView view);
    IReadOnlyCollection<CompiledView> All { get; }
}
```

Catatan:

- `Projection`/`MemberAccess` tetap `LambdaExpression` (bukan delegate murni) karena **EF Core butuh expression tree** untuk translate ke SQL. Kuncinya: expression ini **dibangun compile-time oleh generator** (konstruksi node statik), **bukan** `Expression.Property(p, propertyInfo)` via reflection runtime. Tidak ada `Compile()` di hot path.
- `Accessors`/`Maskers`/`ApplyWritable`/`KeySelector` adalah **delegate murni** (in-memory, post-materialisasi) — tanpa expression, tanpa reflection.
- `SourceQuery` erased ke `object` di boundary Core (sejalan `IViewExecutor`, Spec 02 §6.3); EF layer meng-cast ke `IQueryable<TSource>`.

## 9. Output — JSON (System.Text.Json source-gen)

- Tiap `TQuery`/`TCrud` typed → `[JsonSerializable]` di `JsonSerializerContext` ter-generate per assembly → `JsonTypeInfo` tersedia untuk serialisasi response (Spec 04 §9) & deserialisasi `TCrud` (Spec 05 write path).
- STJ native (Spec 01 D5); Newtonsoft hanya di paket terpisah `a2n.Vista.Newtonsoft` (di luar jalur AOT).
- **Anonymous (gaya A):** tidak ada `JsonTypeInfo` → serialisasi reflection STJ, di-mark RUC (Spec 01 §4.5). Diagnostic VISTA0030 (info) saat build menarget AOT (`PublishAot=true`).

## 10. Output — OpenAPI Document Model

- Generator menghasilkan **model dokumen netral** (bukan `Microsoft.AspNetCore.OpenApi` — generator `netstandard2.0` tak boleh ref ASP.NET) dari `ViewMetadata`: path per facet (Spec 01 §12.3), schema `TQuery`/`TCrud`, parameter filter/sort, error responses (Spec 02 §15).
- `a2n.Vista.AspNetCore` (Spec 05) mengonsumsi model ini → mendaftarkannya ke pipeline OpenAPI ASP.NET compile-time (tanpa scan runtime).
- Anonymous view: schema diturunkan dari shape anonymous (best-effort); RUC tidak relevan untuk dokumen (build-time artifact).

## 11. Enforcement Invarian (compile-time)

Generator/analyzer menegakkan invarian Spec 01 yang bisa dicek statis:

1. **Invarian typing (Spec 01 §4.5/D38):** facet Write butuh `TCrud` typed. Karena `WithCrud<TCrud,TEntity>()`/`View<TQuery,TCrud>` menuntut tipe class (bukan anonymous), ini **sudah ditegakkan type-system**. Generator menambah diagnostic bila ada upaya menulis dari view anonymous-only (VISTA0031, error) — defense in depth.
2. **PrimaryKey untuk paging stabil (Spec 02 §11/§17 #2):** view tanpa field `PrimaryKey()` → VISTA0020 (warning v1.0; kandidat error). Tanpa PK, `KeySelector`/tiebreaker null → engine fallback + warning runtime.
3. **`MapWritable` exhaustiveness (Spec 01 D25):** field `TCrud` tak ter-`MapWritable` → VISTA0010 (info, default ignore). `[VistaWritable(strict: true)]` di `TCrud` → field tak ter-map jadi VISTA0011 (error).
4. **Nama view unik:** duplikat `Named`/`AddView` lintas-assembly konsumen → VISTA0040 (error).
5. **Field selector valid:** `.Field(x => expr)` di mana `expr` bukan akses properti tunggal `TQuery` → VISTA0050 (error).

## 12. Incremental Pipeline (informatif)

- `IIncrementalGenerator` dengan model data **equatable** (record value-based) supaya cache Roslyn efektif — perubahan body satu View tidak me-regenerate seluruh assembly.
- Tahap: `ForAttributeWithMetadataName`/predikat syntax cepat → ekstraksi simbol → model immutable → emit. Hindari `Compilation` global di node panas.
- Multi-target `net8.0;net9.0;net10.0` (Spec 01 D8): kode ter-generate memakai fitur yang tersedia di TFM terendah (mis. `[ModuleInitializer]` ada sejak net5). `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` dipasang sesuai jalur.
- Test: snapshot/golden-file dengan TUnit (Spec 01 D7) di proyek generator-test (terpisah, referensi generator sebagai analyzer).

## 13. Diagnostics Catalog

| ID | Severity | Kondisi | Aksi developer |
|---|---|---|---|
| `VISTA0001` | Error | View gaya B bukan `partial` | tambah `partial` |
| `VISTA0002` | Error | `Configure`/`AddView` tak ditemukan / signature salah | perbaiki override |
| `VISTA0003` | Warning | Projection tak bisa dianalisis statis | sederhanakan ke member-init, atau terima RUC |
| `VISTA0010` | Info | Field `TCrud` tak ter-`MapWritable` (default ignore) | abaikan atau map |
| `VISTA0011` | Error | Strict mode: field `TCrud` tak ter-map | map atau lepas `strict` |
| `VISTA0012` | Warning | `MapWritable` selektor bukan member sederhana | sederhanakan; jika tidak → assignment interpreted (RUC) |
| `VISTA0020` | Warning | View tanpa `PrimaryKey()` (paging non-deterministik) | tandai PK |
| `VISTA0030` | Info | Anonymous view di build AOT (`PublishAot`) | gunakan typed DTO untuk AOT penuh |
| `VISTA0031` | Error | Upaya facet Write pada projection anonymous | gunakan `WithCrud<TCrud,TEntity>` typed |
| `VISTA0040` | Error | Nama view duplikat | rename |
| `VISTA0050` | Error | `.Field` selektor bukan akses properti tunggal | perbaiki selektor |

Prefix `VISTA` + 4 digit; kategori `a2n.Vista.SourceGenerators`; semua punya help-link ke docs.

## 14. Constraint AOT yang Dijamin Generator

Ringkasan apa yang generator **jamin hilang** dari jalur panas (memenuhi Spec 01 §9):

| Anti-pattern | Diganti generator dengan |
|---|---|
| `Activator.CreateInstance(TQuery)` | konstruktor langsung di kode ter-generate |
| `PropertyInfo.GetValue/SetValue` | accessor `Func/Action` ter-generate (`CompiledView.Accessors`) |
| `Expression.Property(p, PropertyInfo)` runtime | member-access expression ter-bangun compile-time |
| `Expression.Lambda(..).Compile()` runtime | projection/member-access sebagai expression statik (EF translate, no compile) |
| `JsonSerializer.Serialize(obj, Type)` | `JsonTypeInfo` ter-generate |
| `Assembly.GetTypes()` scan registrasi | module initializer + `Register<TView>()` ter-generate |

Yang **tetap** RUC (sengaja, Spec 01 §4.5): serialisasi & schema anonymous-projection (gaya A). Filter/sort/paging gaya A tetap AOT-clean (member-access shape-driven, §6.1).

## 15. Decision Log (lanjutan dari Spec 04 D70)

| # | Keputusan | Status | Catatan |
|---|---|---|---|
| D71 | `IIncrementalGenerator`, `netstandard2.0`, tanpa referensi proyek Vista; kenali tipe via nama FQN. | **Decided** | ROADMAP D48. |
| D72 | Model generasi **shape-driven + DSL-recognized**. Shape selalu compile-time; konfigurasi field boleh runtime-startup (cold); hot-path wajib delegate ter-generate. | **Decided** | §6. |
| D73 | View gaya B wajib `partial`; generator melengkapi `IConfiguredView` + `ConfigureCore`. | **Decided** | §7, VISTA0001. Spec 01 §5.1. |
| D74 | `CompiledView` bundle (`SourceQuery`/`Projection`/`MemberAccess`/`Accessors`/`Maskers`/`ApplyWritable`/`KeySelector`) di-store via `ICompiledViewStore`, dikonsumsi engine/exporter/adapter. | **Decided** | §8. |
| D75 | `Projection`/`MemberAccess` tetap `LambdaExpression` (EF butuh tree) tapi **dibangun compile-time** — tanpa `Compile()`/`PropertyInfo` runtime. Accessor in-memory = delegate murni. | **Decided** | §8. Inti AOT + EF-translatable. |
| D76 | `JsonSerializerContext` per DTO typed ter-generate; anonymous → reflection STJ + RUC (VISTA0030). | **Decided** | §9. Spec 01 D5/§4.5. |
| D77 | Auto-registration via `[ModuleInitializer]` ter-generate; `Register<TView>()` eksplisit tetap sah (dedup by Name). `RegisterAssembly` tetap RUC. | **Decided** | §7. Spec 01 §5.3. |
| D78 | OpenAPI = model dokumen netral ter-generate (generator tak ref ASP.NET); dikonsumsi `a2n.Vista.AspNetCore`. | **Decided** | §10. |
| D79 | `MapWritable` → assignment ter-generate via analisis selektor member; tak terekstrak → VISTA0012 + interpreted fallback (RUC). | **Decided** | §6.2, §13. Spec 01 D25. |
| D80 | View tanpa `PrimaryKey()` → VISTA0020 (warning v1.0). Kandidat dipromosikan ke error. | **Decided (warning)** | §11, Spec 02 §17 #2. |
| D81 | Diagnostics prefix `VISTA####`, kategori `a2n.Vista.SourceGenerators`, ber-help-link. | **Decided** | §13. |

## 16. Hubungan dengan Open Questions Sebelumnya

- **Spec 02 §17 #2 (PK wajib?)** → diadres D80: warning VISTA0020 di v1.0, kandidat error. Generator-lah tempat enforcement-nya.
- **Spec 01 §15 #4 (`MapWritable` exhaustiveness)** → D79 + VISTA0010/0011 (sudah Decided sebagai D25; di sini mekanismenya).
- **Spec 01 §15 #2 (sparse `SelectFields`)** → §17 #2 di bawah (kombinatorik accessor).

## 17. Open Questions

1. **Kedalaman analisis projection DSL.** Sampai mana generator menganalisis projection non-trivial (method call, conditional, nested `new`)? v1.0 kandidat: dukung member-init & anonymous datar; sisanya VISTA0003 + RUC. Projection dengan navigasi (`s.Category.Name`) perlu didukung (umum di join) — perlu aturan jelas.
2. **Sparse `SelectFields` (Spec 01 §15 #2).** Accessor per-kombinasi field = kombinatorik. Kandidat: generate satu accessor map penuh + proyeksi runtime memilih subset (tanpa SQL re-projection), atau tunda sparse-select ke v1.x.
3. **`KeySelector` PK majemuk.** Bentuk `object` key untuk PK > 1 kolom (tuple? array?) — selaras Detail endpoint (Spec 05) & tiebreaker (Spec 02 §11).
4. **Cross-assembly view discovery.** Module initializer per-assembly: bagaimana `AddVista` di app utama menemukan view dari assembly library yang juga punya initializer? (Module initializer jalan saat assembly di-load — perlu pastikan assembly ter-referensi tidak ter-trim sebelum init.) Kandidat: generator app utama meng-emit referensi eksplisit, atau dokumentasikan `Register<TView>()` manual untuk view lintas-assembly.
5. **OpenAPI untuk anonymous view.** Schema dari shape anonymous cukup, tapi penamaan komponen (`#/components/schemas/...`) butuh nama stabil — derive dari nama view? (mis. `vProductCategoryRow`).

## 18. Next / Forward References

- `05-aspnetcore-mapping.md` — konsumsi auto-registration (§7), OpenAPI model (§10), `CompiledView.ApplyWritable` untuk write endpoint, `KeySelector` untuk Detail by-key.
- `06-typescript-client.md` — konsumsi `ViewMetadata`/OpenAPI (§10) untuk codegen DTO + filter API TS.
- `07-export.md` — konsumsi `ExportColumnAccessors` (§8) untuk streaming export (Spec 01 §11).
