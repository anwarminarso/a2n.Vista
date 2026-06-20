# Spec 02 — Filter & Query Engine (Pilar 2, paruh server)

> Status: **DRAFT**
> Tanggal: 2026-06-19
> Scope: mesin eksekusi query netral di `a2n.Vista.Core` + `a2n.Vista.EntityFrameworkCore`. Mengubah `ViewQueryRequest` (kontrak netral dari Spec 01 §8) menjadi `IQueryable` ter-translate provider, dengan validasi whitelist, value coercion, sort, dan paging. **Bukan** termasuk: authoring View (Spec 01), adapter grid (Spec 04), source generator (Spec 03), endpoint HTTP (Spec 05), write/CRUD path (Spec 05).

---

## 1. Tujuan

Spec ini mendefinisikan **engine baca** Vista: satu jalur tunggal dari request netral ke hasil ter-materialisasi yang aman, deterministik, dan AOT-friendly.

Mesin ini wajib:

1. **Netral** — tidak tahu grid apa (DataTables/AG Grid/dst.). Inputnya `ViewQueryRequest` (Spec 01 §8); penerjemahan dari format grid adalah tugas adapter (Spec 04).
2. **Secure-by-whitelist** — setiap field & operator dalam request divalidasi terhadap `ViewMetadata` (Spec 01 §5.4) sebelum expression dibangun. Tidak ada nama field yang pernah di-string-concat ke SQL.
3. **Provider-aware** — strategi `Contains`/case-sensitivity dipilih server berdasarkan provider EF Core (Spec 01 §8.2), bukan flag klien.
4. **Deterministik** — paging selalu stabil (tiebreaker PK), count konsisten.
5. **AOT-clean di hot path** — tidak ada `PropertyInfo.GetValue/SetValue`, tidak ada `Activator.CreateInstance`, tidak ada reflection saat membangun predikat (Spec 01 §9). Expression member-access di-source-gen (Pilar 3); spec ini menetapkan **perilaku/kontrak**-nya, bukan cara meng-generate-nya.

## 2. Posisi dalam Arsitektur

`02` adalah **paruh server Pilar 2**: "kontrak query/response netral + expression filter standar" (ROADMAP Pilar 2). Hubungan ke dokumen lain:

| Dokumen | Hubungan |
|---|---|
| `01-view.md` | **Input.** Mendeklarasikan tipe kontrak (`ViewQueryRequest`, `FilterNode`, `FilterOperator`, `PagedResult`) & `ViewMetadata`. `02` *mengeksekusi*-nya; beberapa tipe di-*refine* di sini (ditandai eksplisit). |
| `03-source-generator.md` | **Penyedia artefak.** Member-access expression, accessor, dan `CompiledView` di-generate compile-time; `02` mengonsumsinya via port. |
| `04-adapter-contract.md` | **Konsumen.** Adapter menghasilkan `ViewQueryRequest` dan memetakan hasil ke JSON grid. |
| `05-aspnetcore-mapping.md` | **Komposisi.** Memanggil `IViewAuthorizer` → membangun `IViewScope` → menyerahkan ke `IViewExecutor` (Spec 01 D48). |

Pembagian paket (Spec 01 D48): **port** (`IViewExecutor`, `IViewScope`, `IQueryDialect`, semua record kontrak) hidup di **Core** (bebas EF & HTTP). **Implementasi** translasi EF (`IViewExecutor`, dialect default) hidup di **`a2n.Vista.EntityFrameworkCore`**. Dialect PostgreSQL hidup di paket provider terpisah (lihat §10).

## 3. Terminologi

| Istilah | Arti |
|---|---|
| **Engine / Executor** | Implementasi `IViewExecutor` yang menjalankan pipeline §5. |
| **Channel** | Asal sebuah leaf filter: `Filter` (terstruktur), `Search` (global), `Scope` (kontekstual klien), `Trusted` (server-injected). Menentukan whitelist mana yang dipakai (§7). |
| **Dialect** | `IQueryDialect` — strategi translasi string-match & case-sensitivity per provider EF (§10). |
| **Coercion** | Konversi `FilterLeaf.Value` (mentah dari adapter: string/number/`JsonElement`) ke CLR type field target (§8). |
| **Member-access** | `Expression` `q => q.Field` yang dibangun compile-time (source-gen) per field di `ViewMetadata`. Tidak pernah dari `PropertyInfo` di hot path. |
| **Filtered count** | Jumlah baris setelah seluruh constraint (row filter + scope + filter + search). Dasar `TotalPages`. |
| **Unfiltered count** | Jumlah baris setelah constraint server-trusted (row filter + trusted scope) tapi **tanpa** filter/search/scope-klien. Untuk `recordsTotal` DataTables (§12). |

## 4. Non-Goals

- Penerjemahan format grid spesifik (DataTables `start/length`, `jsonQB`) → itu Spec 04.
- Write path: kompilasi `MapWritable` (`TCrud → TEntity`), concurrency token, bulk → Spec 05 + Pilar 3.
- Cara source generator **menghasilkan** member-access/accessor → Spec 03. Di sini hanya kontraknya.
- Export streaming → Spec 01 §11 + Spec 07.
- Keyset/seek pagination → Open Question §17.

## 5. Pipeline Eksekusi

Urutan baku, dijalankan `IViewExecutor.QueryAsync`. Langkah 1–3 dilakukan pemanggil (AspNetCore, Spec 05) lalu diserahkan; 4–11 milik engine.

```text
[Adapter]      0. RequestGrid            → ViewQueryRequest          (Spec 04)
[AspNetCore]   1. IViewAuthorizer.IsAllowedAsync(ctx)  → allow/deny (403)
[AspNetCore]   2. IViewAuthorizer.ShapeQuery(ctx, scope)→ IViewScope (trusted row filters)
[AspNetCore]   3. serahkan (ViewQueryRequest, IViewScope) ke IViewExecutor
─────────────────────────────────────────────────────────────────────────
[Engine]       4. VALIDATE   tiap FilterLeaf & SortSpec vs ViewMetadata (per-channel whitelist) → 400 bila langgar
[Engine]       5. COERCE     FilterLeaf.Value → CLR type field          → 400 bila type mismatch
[Engine]       6. SOURCE     baseQuery = View.Source(sp)                 // IQueryable<TSource>
[Engine]       7. PRE-FILTER baseQuery.Where(rowFilter).Where(trustedScope)   // di TSource, push-down SQL
[Engine]       8. PROJECT    .Select(projection)                         // → IQueryable<TQuery>
[Engine]       9. POST-FILTER .Where(filterTree)                         // di TQuery (Filter+Search+Scope klien)
[Engine]      10. COUNT      FilteredRows = await q.LongCountAsync(ct)
                             UnfilteredRows = (opsional) hitung di titik langkah 8
[Engine]      11. ORDER+PAGE .OrderBy(sort + PK tiebreaker).Skip(..).Take(..)
[Engine]      12. MATERIALIZE await .ToListAsync(ct) → mask (Spec 01 §5.2) → ViewQueryResult<TQuery>
```

Catatan urutan:

- **Row filter & trusted scope di TSource** (langkah 7, pre-projection) — soft-delete/tenant hidup di entity (Spec 01 D28). Push-down SQL natural.
- **Filter/Search/Scope-klien di TQuery** (langkah 9, post-projection) — beroperasi pada field projection yang sudah dikurasi (Spec 01 §4.4). EF mengomposisi langkah 7–11 menjadi satu SQL; computed field yang tak bisa di-SQL ditangani `WithProjectedRowFilter` (kasus khusus, Spec 01 §5.2).
- **Mask post-materialisasi** (langkah 12) — `MaskField` adalah transform `TProp→TProp` di memori, bukan SQL.

## 6. Kontrak (refinement Spec 01 §8)

### 6.1 Request (refined)

`ViewQueryRequest` dari Spec 01 §8 di-refine: tiap `FilterLeaf` membawa `Origin` (memformalkan "record FilterOrigin internal" yang disebut Spec 01 §8.3), dan request menambah `IncludeUnfilteredCount`.

```csharp
namespace a2n.Vista;

public sealed record ViewQueryRequest(
    FilterNode? Filter,                  // tree tunggal hasil merge channel oleh adapter
    IReadOnlyList<SortSpec> Sort,
    int Page,                            // 0-based
    int PageSize,
    bool IncludeUnfilteredCount = false, // true → engine hitung juga total tanpa filter/search/scope (recordsTotal)
    IReadOnlyList<string>? SelectFields = null);

public sealed record SortSpec(string Field, bool Descending);

public abstract record FilterNode;
public sealed record FilterLeaf(
    string Field,
    FilterOperator Op,
    object? Value,
    FilterOrigin Origin = FilterOrigin.Filter) : FilterNode;   // refinement: +Origin
public sealed record FilterAnd(IReadOnlyList<FilterNode> Children) : FilterNode;
public sealed record FilterOr(IReadOnlyList<FilterNode> Children)  : FilterNode;
public sealed record FilterNot(FilterNode Child) : FilterNode;

// Channel asal leaf → menentukan whitelist mana yang berlaku (§7).
public enum FilterOrigin
{
    Filter  = 0,  // terstruktur (QueryBuilder, per-column) → whitelist Filterable + AllowedOperators
    Search  = 1,  // global search box                      → whitelist Searchable (string), op WAJIB Contains
    Scope   = 2,  // contextual/lookup dari KLIEN           → whitelist Scopable (Spec 01 §5.6)
    Trusted = 3,  // di-inject server (ShapeQuery)          → TIDAK divalidasi (trusted)
}
```

`FilterOperator` tidak berubah dari Spec 01 §8 (flags enum).

### 6.2 Result (baru)

Engine mengembalikan record kaya yang membawa **dua count**. `PagedResult<T>` (Spec 01 §10) adalah *proyeksi default-shape* dari result ini; adapter lain (DataTables) memetakan ke shape-nya sendiri.

```csharp
namespace a2n.Vista;

public sealed record ViewQueryResult<T>(
    IReadOnlyList<T> Items,
    long FilteredRows,         // total setelah SEMUA constraint → dasar TotalPages
    long? UnfilteredRows,      // null kecuali IncludeUnfilteredCount; total tanpa filter/search/scope-klien
    int Page,
    int PageSize)
{
    public long TotalPages => PageSize <= 0 ? 0 : (FilteredRows + PageSize - 1) / PageSize;

    // Proyeksi ke shape netral Spec 01 §10.
    public PagedResult<T> ToPagedResult() =>
        new(Items, FilteredRows, Page, PageSize, TotalPages);
}
```

> Resolusi ref `dyndata-datatables-observed.md` §7 butir 7: `FilteredRows` = `recordsFiltered`; `UnfilteredRows` (saat diminta) = `recordsTotal`.

### 6.3 Port `IViewExecutor` (Core)

Port non-generik, di-resolve via DI di composition root, di-implement EF layer. `TQuery` di-erase ke `object` di boundary (sejalan `IViewExporter` Spec 01 §11.1); materialisasi typed dilakukan delegate source-gen (Pilar 3).

```csharp
namespace a2n.Vista;

public interface IViewExecutor
{
    // Facet List/query (§5). viewName → ViewMetadata via IViewRegistry.
    Task<ViewQueryResult<object>> QueryAsync(
        ViewQueryExecution exec,
        CancellationToken ct = default);

    // Facet Detail (Spec 01 §4.6). null bila tidak ketemu → 404 di Spec 05.
    Task<object?> GetByKeyAsync(
        string viewName,
        object key,
        IViewScope scope,
        CancellationToken ct = default);
}

// Semua input eksekusi yang sudah tervalidasi-host (auth lulus, scope terkumpul).
public sealed record ViewQueryExecution(
    string ViewName,
    ViewQueryRequest Request,
    IViewScope Scope,
    IServiceProvider Services);
```

`IViewScope` tidak berubah dari Spec 01 §5.6 (`AddRowFilter<TSource>`). Leaf yang ditambahkan via scope masuk channel `Trusted` (tidak divalidasi).

## 7. Validasi & Whitelist per-Channel

Validasi (langkah 4) adalah **gerbang keamanan utama** engine. Dijalankan sebelum coercion & expression. Setiap `FilterLeaf` dievaluasi menurut `Origin`-nya terhadap `ViewMetadata.Fields`:

| `Origin` | Field harus | Operator harus | Pelanggaran |
|---|---|---|---|
| `Filter` | `IsFilterable == true` | `Op ∈ AllowedOperators[field]` | 400 `filter-field-not-allowed` / `filter-operator-not-allowed` |
| `Search` | `IsSearchable == true` **dan** `ClrType == string` | `Op == Contains` (dipaksa) | 400 `search-field-not-allowed` |
| `Scope` | `IsScopable == true` | `Op ∈ AllowedOperators[field]` | 400 `scope-field-not-allowed` |
| `Trusted` | — | — | tidak divalidasi (server-trusted, Spec 01 §5.6/D46) |

`SortSpec.Field` harus `IsSortable == true`; jika tidak → 400 `sort-field-not-allowed`.

Aturan tambahan:

1. **Field tak dikenal** (tidak ada di `ViewMetadata.Fields`) → selalu 400 `filter-field-not-allowed` (tidak pernah skip diam-diam — kebalikan DynData `externalFilter`, ref §7 butir 4).
2. **`IsHidden` tidak menghalangi filter/scope** — PK teknis yang `Hidden().Scopable()` tetap valid sebagai lookup key (Spec 01 §5.6). Hidden hanya soal *tampilan/serialisasi*, bukan filterability.
3. **Validasi rekursif** menelusuri `FilterAnd/Or/Not` sampai semua leaf tervalidasi. Satu pelanggaran membatalkan seluruh request (fail-fast), error menyertakan `field` + `operator` + `allowed` di `extensions` (Spec 01 §14.1).
4. **Anti-injection invariant**: nama field di leaf **hanya** dipakai sebagai *key lookup* ke peta member-access source-gen. Tidak ada jalur di mana string field menjadi bagian teks SQL. Field tak terdaftar tidak punya entri member-access → otomatis ditolak di langkah ini.

## 8. Value Model & Coercion (Sanitization)

`FilterLeaf.Value` datang mentah dari adapter (`string`, angka, `bool`, `JsonElement`, atau array). Langkah 5 meng-coerce ke CLR type field (`FieldMetadata.ClrType`) sebelum masuk constant expression.

### 8.1 Aturan coercion

| Target | Sumber diterima | Aturan |
|---|---|---|
| `string` | string | apa adanya (escaping wildcard di §10, bukan di sini) |
| `int/long/short/byte` | number / numeric-string | `InvariantCulture`; overflow → 400 |
| `decimal/double/float` | number / numeric-string | `InvariantCulture` |
| `bool` | bool / `"true"`/`"false"`/`"1"`/`"0"` | case-insensitive |
| `DateTime/DateTimeOffset` | ISO-8601 string | `DateTimeStyles.RoundtripKind`; format lain → 400 |
| `Guid` | string | `Guid.TryParse`; gagal → 400 |
| `enum` | nama / nilai underlying | `Enum.TryParse` (case-insensitive); tidak valid → 400 |
| `T?` (nullable) | di atas, atau `null` | `null` hanya legal untuk `IsNull`/`In`-anggota |

Coercion **culture-invariant** (server-locale-independent) — menutup bug DynData `ListSeparator`/locale (Spec 01 §11.3 analog). Gagal coerce → 400 `value-type-mismatch` dengan `field`, `expectedType`, `value`.

### 8.2 Bentuk multi-nilai

- **`In`**: `Value` wajib array/list. Tiap elemen di-coerce ke `ClrType`. Ukuran di-cap: default **1000** (`MaxInValues`), override global; lebih → 400 `payload-too-large` (413). Dibangun sebagai `list.Contains(member)` → EF translate ke SQL `IN`.
- **`Between`**: `Value` wajib array 2-elemen `[lo, hi]`, keduanya non-null, di-coerce. Bukan 2-elemen → 400. Dibangun `member >= lo && member <= hi`.
- **`IsNull`**: `Value` diabaikan. Hanya valid untuk field nullable / reference type; pada non-nullable value-type → 400 `operator-not-applicable`.

### 8.3 Sanitization invariants

1. Tidak ada nilai klien yang menjadi **identifier** SQL (hanya **parameter** value).
2. Panjang string filter di-cap (default `MaxFilterStringLength = 4096`) → lebih panjang ditolak 400 (anti-DoS pola LIKE).
3. Kedalaman tree `FilterNode` di-cap (default `MaxFilterDepth = 16`) & total leaf (default `MaxFilterLeaves = 128`) → lebih → 400. Menutup serangan nested-OR yang meledakkan query plan.

## 9. Expression Building per Operator

Setelah validasi+coercion, tiap `FilterLeaf` menjadi `Expression<Func<TQuery, bool>>`. `member` = member-access source-gen `q => q.Field`; `c` = constant hasil coercion.

| `FilterOperator` | Expression (semantik) | Catatan null |
|---|---|---|
| `Equals` | `member == c` | `c == null` → `member == null` |
| `NotEquals` | `member != c` | `c == null` → `member != null` |
| `GreaterThan` | `member > c` | hanya tipe comparable; pada `null` member → SQL `false` |
| `GreaterThanOrEqual` | `member >= c` | idem |
| `LessThan` | `member < c` | idem |
| `LessThanOrEqual` | `member <= c` | idem |
| `Contains` | dialect string-match (§10) | null-guard untuk in-memory |
| `StartsWith` | dialect string-match (§10) | idem |
| `EndsWith` | dialect string-match (§10) | idem |
| `In` | `values.Contains(member)` | `null` anggota → tergantung provider |
| `Between` | `member >= lo && member <= hi` | lo/hi wajib non-null (§8.2) |
| `IsNull` | `member == null` | — |

Aturan:

1. **`FilterNot(child)`** → `Expression.Not(...)` membungkus sub-predikat (mis. `is_not_empty`, `not_in` dari adapter, ref §6.2).
2. **Operator vs tipe**: operator komparasi (`>`,`>=`,`<`,`<=`,`Between`) pada `string`/`bool`/`Guid` → 400 `operator-not-applicable` (selain yang diizinkan `AllowedOperators`). Whitelist field (§7) adalah pertahanan pertama; cek ini pertahanan kedua untuk konsistensi tipe.
3. **Null-guard in-memory**: untuk provider InMemory/tes, string-match dibungkus `member != null && ...` agar tidak `NullReferenceException`; di provider relasional null member menghasilkan `unknown`/false secara natural — guard tetap aman & tidak mengubah hasil SQL.
4. **Komposisi**: `FilterAnd/Or` → `AndAlso`/`OrElse` ber-rantai dengan parameter sama; pohon kosong (`null` Filter) → tanpa `Where`.

## 10. Provider-aware String Matching

Inti "provider-detected, bukan flag klien" (Spec 01 §8.2, D17). Klien hanya mengirim intent (`Contains`/`StartsWith`/`EndsWith`); engine memilih translasi.

### 10.1 Port `IQueryDialect` (Core)

```csharp
namespace a2n.Vista;

public enum StringMatchKind { Contains, StartsWith, EndsWith }

public interface IQueryDialect
{
    string ProviderName { get; }                 // mis. "Microsoft.EntityFrameworkCore.SqlServer"
    bool CaseInsensitiveByDefault { get; }

    // Membangun predikat string-match untuk SATU member string.
    // Implementasi memilih string.Contains (EF auto-escape) atau pola LIKE/ILIKE
    // (escape manual via EscapeLikePattern).
    Expression BuildStringMatch(Expression member, string value, StringMatchKind kind);
}
```

### 10.2 Strategi default per provider

| Provider | `Contains` default | Mekanisme |
|---|---|---|
| SQL Server | CI (collation default) | `string.Contains/StartsWith/EndsWith` (EF translate + **auto-escape**) |
| SQLite | CI (ASCII) native | idem |
| MySQL / Pomelo | CI (collation default) | idem |
| InMemory / tes | CI | `string.Contains(StringComparison.OrdinalIgnoreCase)` + null-guard |
| **PostgreSQL (Npgsql)** | **CS** (LIKE) → butuh ILIKE untuk CI | `EF.Functions.ILike(member, "%" + Escape(value) + "%")` |

Default `DefaultStringMatch` (Spec 01 §8.2): semua provider memakai jalur `string.Contains` **kecuali** PostgreSQL yang case-insensitive memerlukan `ILIKE`.

### 10.3 PostgreSQL = dialect di paket terpisah

`string.Contains` pada Npgsql menerjemahkan ke `LIKE` yang **case-sensitive** di PostgreSQL. Untuk paritas CI dengan provider lain, dibutuhkan `EF.Functions.ILike` — yang ada di paket `Npgsql.EntityFrameworkCore.PostgreSQL`. Agar Core/EF tidak terkopel ke satu provider (Spec 01 D48):

- `a2n.Vista.EntityFrameworkCore` menyediakan **dialect default** (`string.Contains`) untuk SQL Server/SQLite/MySQL/InMemory.
- `a2n.Vista.EntityFrameworkCore.Npgsql` (paket kecil terpisah) menyediakan `NpgsqlQueryDialect` (ILIKE). Registrasi via `services.AddVistaNpgsql()`.
- Engine me-resolve `IQueryDialect` berdasarkan `DbContext.Database.ProviderName`; bila tak ada dialect spesifik → dialect default.

### 10.4 Wildcard escaping (wajib)

Jalur `string.Contains/StartsWith/EndsWith` (EF) meng-escape `%`/`_` otomatis lewat parameterisasi — **aman**. Jalur **pola mentah** (`EF.Functions.ILike`) **tidak** — value klien `%`/`_`/`\` harus di-escape manual agar tidak jadi wildcard injection:

```csharp
// dipakai HANYA di jalur ILIKE/LIKE pola mentah
static string EscapeLikePattern(string v) => v
    .Replace("\\", "\\\\")
    .Replace("%",  "\\%")
    .Replace("_",  "\\_");
// pola: "%" + EscapeLikePattern(v) + "%", dengan ESCAPE '\'
```

Override per-view (mis. paksa case-sensitive untuk kolom collation khusus) tersedia via metadata field — kandidat API di Open Question §17.

## 11. Sort Building

`SortSpec[]` → `OrderBy/OrderByDescending` + `ThenBy*` ber-rantai, memakai member-access source-gen.

1. **Validasi**: tiap field `IsSortable` (§7). Field di luar projection → 400 (bukan sort diam-diam diabaikan seperti DynData yang `OrderBy(string)`).
2. **Tiebreaker PK (deterministik)**: engine **selalu** menambahkan field PK (`FieldMetadata` ber-`PrimaryKey`, Spec 01 §5.5) sebagai kunci sort **terakhir** bila belum ada di `Sort`. Tanpa ini, `Skip/Take` pada nilai sort non-unik bisa mengembalikan baris duplikat/hilang antar-halaman. PK majemuk → ditambahkan berurutan (urutan deklarasi).
3. **Default order**: bila `Sort` kosong → urut by PK ascending (deterministik). View tanpa PK terdeklarasi → engine memakai field pertama projection + **warning** (kandidat: wajibkan PK untuk paging stabil, §17).
4. **Null ordering**: ikut default provider (mis. SQL Server `NULLS` implicit). Override eksplisit → §17.

## 12. Paging & Counts

### 12.1 Offset paging

```csharp
long offset = (long)request.Page * request.PageSize;   // long: cegah overflow int (Spec 01 §10)
if (offset > int.MaxValue) → 400 "page-offset-too-large";
query.Skip((int)offset).Take(request.PageSize);
```

- `PageSize` di-clamp ke `HardLimits.MaxPageSize` (Spec 01 §5.4/§7). `PageSize <= 0` → 400. **`length = -1` (DynData "return all") ditolak** (Spec 01 §12.2).
- v1.0 hanya offset paging. Keyset/seek (untuk offset sangat besar) ditunda (§17).

### 12.2 Dua count

- **`FilteredRows`** selalu dihitung: `LongCountAsync` pada query setelah langkah 9 (sebelum order/page). Dasar `TotalPages`.
- **`UnfilteredRows`** hanya bila `IncludeUnfilteredCount == true`: `LongCountAsync` pada query di akhir langkah 8 (setelah row filter + trusted scope, **sebelum** filter/search/scope-klien). Ini `recordsTotal` DataTables (ref §6.3/§7.7).
- Keduanya menghormati `CancellationToken`. Dua count = dua round-trip DB; adapter yang tak butuh `recordsTotal` membiarkan `IncludeUnfilteredCount = false` (default) untuk hemat satu query.

### 12.3 Materialisasi

- `await query.ToListAsync(ct)` — async-only, `CancellationToken` wajib (Spec 01 §10). Tidak ada overload sync.
- `.AsNoTracking()` default untuk jalur baca (read-only projection). Tidak ada `AsNoTrackingDynamic` DynData (Spec 01 §12.4).
- Tidak ada extension publik `ToPagedResultAsync` di Core (Spec 01 §10.2) — paging adalah detail internal engine.

## 13. Masking & Post-processing

`MaskField(field, predicate, masker)` (Spec 01 §5.2/D29) diterapkan **setelah** materialisasi (langkah 12), per-item, memakai accessor/mutator source-gen (bukan `PropertyInfo`). `predicate` dievaluasi sekali per-request (`Func<IServiceProvider,bool>`), bukan per-baris. Masking **tidak** memengaruhi filter/sort/count — hanya bentuk akhir yang dikirim. Implikasi: field ter-mask tetap bisa difilter di SQL (mis. cari email persis) kecuali di-`Filterable(false)` (Spec 01 §4.4 poin 2).

## 14. Constraint AOT

Selaras Spec 01 §9 dan Pilar 3:

1. **Member-access** (`q => q.Field`) untuk tiap field di `ViewMetadata` di-generate source-gen sebagai delegate/`Expression` statik — bukan `Expression.Property(p, PropertyInfo)` runtime via reflection. Spec 02 menetapkan kontraknya; Spec 03 menetapkan generatornya.
2. **Constant value** dibangun via `Expression.Constant` dari hasil coercion typed — tidak ada boxing reflection di hot path.
3. **Materialisasi & mask** memakai accessor source-gen, bukan `PropertyInfo.GetValue/SetValue`.
4. Jalur fallback reflection-based (mis. View terdaftar via `RegisterAssembly`, Spec 01 §5.3) di-mark `[RequiresUnreferencedCode]`. Engine harus punya jalur source-gen yang setara untuk semua operasi di atas.
5. Anonymous-projection View (gaya A, Spec 01 §4.5) tetap `[RequiresUnreferencedCode]` pada serialisasi; **filter/sort/paging-nya AOT-clean** karena member-access tetap di-generate dari shape projection.

## 15. Error Model (query-specific)

Memperluas tabel Spec 01 §14.1. Semua RFC 7807, `type` di bawah `https://a2n.dev/vista/errors/`. `extensions` machine-readable (`field`, `operator`, `allowed`, `expectedType`).

| Kondisi | HTTP | `type` |
|---|---|---|
| Field filter bukan `Filterable` / tak dikenal | 400 | `.../filter-field-not-allowed` |
| Operator di luar `AllowedOperators` | 400 | `.../filter-operator-not-allowed` |
| Field search bukan `Searchable`/bukan string | 400 | `.../search-field-not-allowed` |
| Field scope bukan `Scopable` | 400 | `.../scope-field-not-allowed` |
| Field sort bukan `Sortable` | 400 | `.../sort-field-not-allowed` |
| Operator tak berlaku untuk tipe (mis. `>` pada `bool`) | 400 | `.../operator-not-applicable` |
| Coercion gagal (type mismatch) | 400 | `.../value-type-mismatch` |
| `Between`/`In` bentuk nilai salah | 400 | `.../malformed-value` |
| Tree terlalu dalam / leaf terlalu banyak / string terlalu panjang | 400 | `.../query-too-complex` |
| `In` melebihi `MaxInValues` | 413 | `.../payload-too-large` |
| Page offset overflow / `PageSize<=0` / `length=-1` | 400 | `.../invalid-paging` |

Prinsip: **fail-fast & spesifik**. Satu pelanggaran membatalkan request dengan detail field+operator agar adapter/klien bisa memperbaiki. Tidak ada "skip diam-diam" (kontras DynData).

## 16. Decision Log (lanjutan dari Spec 01 D50)

| # | Keputusan | Status | Catatan |
|---|---|---|---|
| D51 | `IViewExecutor.QueryAsync` mengembalikan `ViewQueryResult<T>` (Items + `FilteredRows` + opsional `UnfilteredRows`). `PagedResult<T>` (Spec 01 §10) = proyeksi default-shape via `ToPagedResult()`. | **Decided** | Menyelesaikan ref `dyndata-datatables-observed.md` §7.7 (recordsTotal vs recordsFiltered). |
| D52 | `FilterLeaf` membawa `FilterOrigin` (`Filter`/`Search`/`Scope`/`Trusted`); engine memvalidasi per-channel (§7). | **Decided** | Memformalkan "record FilterOrigin internal" Spec 01 §8.3. |
| D53 | `IViewExecutor` & `IViewScope` & `IQueryDialect` adalah **port di Core**; implementasi EF di `a2n.Vista.EntityFrameworkCore`. | **Decided** | Spec 01 D48. `TQuery` di-erase ke `object` di boundary port. |
| D54 | Default string-match `string.Contains/StartsWith/EndsWith` (EF auto-escape). PostgreSQL CI via dialect terpisah `a2n.Vista.EntityFrameworkCore.Npgsql` (ILIKE). | **Decided** | §10. Hindari kopling Core ke satu provider. |
| D55 | Jalur pola mentah (ILIKE/LIKE) **wajib** `EscapeLikePattern` untuk `%`/`_`/`\`. | **Decided** | §10.4. Anti wildcard-injection. |
| D56 | Paging deterministik: PK selalu ditambahkan sebagai sort tiebreaker terakhir; `Sort` kosong → by PK asc. | **Decided** | §11. Cegah duplikat/hilang antar-halaman. |
| D57 | v1.0 offset paging saja; `Skip((int)(long)Page*PageSize)`, offset > `int.MaxValue` → 400. Keyset/seek ditunda. | **Decided** | §12.1, §17. |
| D58 | Dua count: `FilteredRows` selalu; `UnfilteredRows` hanya bila `IncludeUnfilteredCount`. | **Decided** | §12.2. Hemat round-trip default. |
| D59 | Coercion culture-invariant; `In` di-cap (`MaxInValues=1000`); type mismatch → 400. | **Decided** | §8. Tutup bug locale & DoS. |
| D60 | Anti-injection invariant: nama field hanya key lookup ke member-access source-gen; tak pernah teks SQL. Field tak terdaftar → 400 (tak ada skip diam-diam). | **Decided** | §7.4, §15. Kontras DynData `externalFilter`. |
| D61 | Guard kompleksitas query: `MaxFilterDepth=16`, `MaxFilterLeaves=128`, `MaxFilterStringLength=4096` (semua override global). | **Decided** | §8.3. Anti query-plan blow-up. |
| D62 | Mask diterapkan post-materialisasi via accessor source-gen; tidak memengaruhi filter/sort/count. | **Decided** | §13. Spec 01 D29. |

## 17. Open Questions

1. **Keyset/seek pagination** untuk offset besar (`Skip` mahal di OLTP). Kandidat v1.x: `ViewQueryRequest.After` (cursor token) berbasis PK+sort. Perlu kontrak cursor stabil yang kompatibel adapter.
2. **PK wajib untuk paging stabil?** §11 poin 3 saat ini *warning* bila View tanpa PK. Kandidat: jadikan error build-time (source-gen diagnostic) karena paging tanpa kunci unik fundamental tak deterministik.
3. **Null ordering & collation override per-field** — API metadata (`f.NullsFirst()` / `f.Collation("...")`)? Saat ini ikut default provider (§10.4, §11.4).
4. **Semantik `is_empty`/`is_not_empty`** (ref §6.2/§7.2): map ke `IsNull`, string kosong, atau keduanya (`member == null || member == ""`)? Keputusan memengaruhi adapter QueryBuilder (Spec 04). Kandidat default: keduanya untuk string.
5. **Per-column search DataTables** (`columns[i][search][value]`, ref §7.5): map ke `FilterLeaf(Contains, Origin=Filter)` atau channel `Search`? Memengaruhi whitelist mana yang berlaku. Kandidat: `Filter` (per-kolom = filter terstruktur, bukan global search).
6. **Distinct-values** (`GET .../distinct/{field}`, Spec 01 §14.3) — query-path mana yang melayaninya? Reuse validasi §7 (`field ∈ Filterable`, `take ≤ 1000`). Detail di spec terpisah.

## 18. Next / Forward References

- `03-source-generator.md` — generator member-access, accessor, `CompiledView`, `JsonSerializerContext` yang dikonsumsi engine ini (§14).
- `04-adapter-contract.md` — `IViewAdapter`: produksi `ViewQueryRequest` (set `FilterOrigin` per leaf, §6.1), konsumsi `ViewQueryResult`/`PagedResult`. Termasuk mapping DataTables & jQuery-QueryBuilder (ref `dyndata-datatables-observed.md` §6).
- `05-aspnetcore-mapping.md` — komposisi auth → `IViewScope` → `IViewExecutor`, error model HTTP, write/CRUD path & concurrency.
