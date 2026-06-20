# Spec 02 — Filter & Query Engine (Pillar 2, server half)

> Status: **PARTIALLY IMPLEMENTED (Pillar 1)** — reconciled with the code
> Date: 2026-06-20 (rev: synchronized to the `pilar-1-core` implementation)
> Scope: the neutral query execution engine in `a2n.Vista.Core` + `a2n.Vista.EntityFrameworkCore`. Turns the `ViewQueryRequest` (the neutral contract from Spec 01 §8) into a provider-translated `IQueryable`, with whitelist validation, value coercion, sort, and paging. **Not** included: View authoring (Spec 01), grid adapters (Spec 04), source generator (Spec 03), HTTP endpoints (Spec 05), write/CRUD path (Spec 05).
>
> **Reconciliation note (2026-06-20).** The read engine is already implemented (`EfViewExecutor`,
> `FilterCompiler`, `ProviderAwareFilterCompiler`). Some contracts sketched here
> **differ from the code**; the contracts that apply (see Spec 01 §13.1 DR1–DR10):
> - `ViewQueryRequest`/`FilterLeaf` **remain as in Spec 01 §8** — without `Origin` on the leaf and without
>   `IncludeUnfilteredCount`. `FilterOrigin` is a **public 3-value enum** (`Filter`/`Search`/`Scope`,
>   without `Trusted`) that is passed as a **parameter** to `FilterCompiler.Compile(node, origin, view)`
>   (DR9). The §6.1 refinement below is **not adopted**.
> - The List result = **`ViewListResult<TRow>`** (Spec 01 §10.1 DR6), not `ViewQueryResult<T>` (§6.2).
> - `IViewExecutor` is **generic** (`ListAsync<TRow>`/`DetailAsync<TRow>` + write), **not** the non-generic
>   erased-to-`object` `QueryAsync(ViewQueryExecution)` (§6.3 DR8). There is no `ViewQueryExecution`.
> - Trusted scope goes through `IViewScope` (not validated); there is no `Trusted` channel in `FilterOrigin`.
> - Pillar 1 validates the entire tree as `FilterOrigin.Filter` in `EfViewExecutor`; the per-channel
>   `Search`/`Scope` split is the job of the Pillar 2 adapter (Spec 04).

---

## 1. Purpose

This spec defines the **read engine** of Vista: a single path from a neutral request to a materialized result that is safe, deterministic, and AOT-friendly.

The engine must be:

1. **Neutral** — it does not know which grid is in use (DataTables/AG Grid/etc.). Its input is `ViewQueryRequest` (Spec 01 §8); translating from grid formats is the adapter's job (Spec 04).
2. **Secure-by-whitelist** — every field & operator in the request is validated against `ViewMetadata` (Spec 01 §5.4) before the expression is built. No field name is ever string-concatenated into SQL.
3. **Provider-aware** — the `Contains`/case-sensitivity strategy is chosen by the server based on the EF Core provider (Spec 01 §8.2), not a client flag.
4. **Deterministic** — paging is always stable (PK tiebreaker), and the count is consistent.
5. **AOT-clean on the hot path** — no `PropertyInfo.GetValue/SetValue`, no `Activator.CreateInstance`, no reflection while building predicates (Spec 01 §9). Member-access expressions are source-generated (Pillar 3); this spec defines their **behavior/contract**, not how to generate them.

## 2. Position in the Architecture

`02` is the **server half of Pillar 2**: "neutral query/response contract + standard filter expressions" (ROADMAP Pillar 2). Relationship to other documents:

| Document | Relationship |
|---|---|
| `01-view.md` | **Input.** Declares the contract types (`ViewQueryRequest`, `FilterNode`, `FilterOperator`, `PagedResult`) & `ViewMetadata`. `02` *executes* them; some types are *refined* here (explicitly marked). |
| `03-source-generator.md` | **Artifact provider.** Member-access expressions, accessors, and `CompiledView` are generated at compile time; `02` consumes them via a port. |
| `04-adapter-contract.md` | **Consumer.** The adapter produces `ViewQueryRequest` and maps the result to grid JSON. |
| `05-aspnetcore-mapping.md` | **Composition.** Calls `IViewAuthorizer` → builds `IViewScope` → hands off to `IViewExecutor` (Spec 01 D48). |

Package split (Spec 01 D48): the **ports** (`IViewExecutor`, `IViewScope`, `IQueryDialect`, all contract records) live in **Core** (free of EF & HTTP). The EF translation **implementation** (`IViewExecutor`, default dialect) lives in **`a2n.Vista.EntityFrameworkCore`**. The PostgreSQL dialect lives in a separate provider package (see §10).

## 3. Terminology

| Term | Meaning |
|---|---|
| **Engine / Executor** | The `IViewExecutor` implementation that runs the §5 pipeline. |
| **Channel** | The origin of a filter leaf: `Filter` (structured), `Search` (global), `Scope` (client-contextual), `Trusted` (server-injected). Determines which whitelist applies (§7). |
| **Dialect** | `IQueryDialect` — the string-match & case-sensitivity translation strategy per EF provider (§10). |
| **Coercion** | Conversion of `FilterLeaf.Value` (raw from the adapter: string/number/`JsonElement`) to the CLR type of the target field (§8). |
| **Member-access** | An `Expression` `q => q.Field` built at compile time (source-gen) per field in `ViewMetadata`. Never from `PropertyInfo` on the hot path. |
| **Filtered count** | The number of rows after all constraints (row filter + scope + filter + search). The basis for `TotalPages`. |
| **Unfiltered count** | The number of rows after server-trusted constraints (row filter + trusted scope) but **without** filter/search/client-scope. Used for DataTables `recordsTotal` (§12). |

## 4. Non-Goals

- Translating specific grid formats (DataTables `start/length`, `jsonQB`) → that is Spec 04.
- Write path: compiling `MapWritable` (`TCrud → TEntity`), concurrency token, bulk → Spec 05 + Pillar 3.
- How the source generator **produces** member-access/accessors → Spec 03. Only the contract is here.
- Streaming export → Spec 01 §11 + Spec 07.
- Keyset/seek pagination → Open Question §17.

## 5. Execution Pipeline

The standard order, run by `IViewExecutor.QueryAsync`. Steps 1–3 are performed by the caller (AspNetCore, Spec 05) and then handed off; 4–11 belong to the engine.

```text
[Adapter]      0. RequestGrid            → ViewQueryRequest          (Spec 04)
[AspNetCore]   1. IViewAuthorizer.IsAllowedAsync(ctx)  → allow/deny (403)
[AspNetCore]   2. IViewAuthorizer.ShapeQuery(ctx, scope)→ IViewScope (trusted row filters)
[AspNetCore]   3. hand off (ViewQueryRequest, IViewScope) to IViewExecutor
─────────────────────────────────────────────────────────────────────────
[Engine]       4. VALIDATE   each FilterLeaf & SortSpec vs ViewMetadata (per-channel whitelist) → 400 if violated
[Engine]       5. COERCE     FilterLeaf.Value → field CLR type           → 400 on type mismatch
[Engine]       6. SOURCE     baseQuery = View.Source(sp)                 // IQueryable<TSource>
[Engine]       7. PRE-FILTER baseQuery.Where(rowFilter).Where(trustedScope)   // on TSource, push-down SQL
[Engine]       8. PROJECT    .Select(projection)                         // → IQueryable<TQuery>
[Engine]       9. POST-FILTER .Where(filterTree)                         // on TQuery (Filter+Search+client Scope)
[Engine]      10. COUNT      FilteredRows = await q.LongCountAsync(ct)
                             UnfilteredRows = (optional) computed at the step 8 point
[Engine]      11. ORDER+PAGE .OrderBy(sort + PK tiebreaker).Skip(..).Take(..)
[Engine]      12. MATERIALIZE await .ToListAsync(ct) → mask (Spec 01 §5.2) → ViewQueryResult<TQuery>
```

Ordering notes:

- **Row filter & trusted scope on TSource** (step 7, pre-projection) — soft-delete/tenant live on the entity (Spec 01 D28). Natural SQL push-down.
- **Filter/Search/client-Scope on TQuery** (step 9, post-projection) — operate on the already-curated projection fields (Spec 01 §4.4). EF composes steps 7–11 into a single SQL statement; computed fields that cannot be expressed in SQL are handled by `WithProjectedRowFilter` (special case, Spec 01 §5.2).
- **Post-materialization mask** (step 12) — `MaskField` is a `TProp→TProp` transform in memory, not SQL.

## 6. Contracts (refinement of Spec 01 §8)

### 6.1 Request (refined) — ⚠️ NOT ADOPTED (see reconciliation)

> **Reconciliation (2026-06-20).** The refinement below (`Origin` on `FilterLeaf`, `IncludeUnfilteredCount`,
> `FilterOrigin.Trusted`) is **not implemented**. The code keeps `ViewQueryRequest`/`FilterLeaf`
> as in Spec 01 §8, uses the 3-value `FilterOrigin` as a **parameter** to `FilterCompiler.Compile`,
> and computes the two counts via `ViewListResult<TRow>` (§6.2 / Spec 01 §10.1 DR6). The block below
> is kept as a historical design note. If the Pillar 2 adapter (Spec 04) needs a multi-channel tree,
> moving `Origin` onto the leaf will be reconsidered at that time.

The `ViewQueryRequest` from Spec 01 §8 is refined: each `FilterLeaf` carries an `Origin` (formalizing the "internal FilterOrigin record" mentioned in Spec 01 §8.3), and the request adds `IncludeUnfilteredCount`.

```csharp
namespace a2n.Vista;

public sealed record ViewQueryRequest(
    FilterNode? Filter,                  // single tree resulting from the adapter merging channels
    IReadOnlyList<SortSpec> Sort,
    int Page,                            // 0-based
    int PageSize,
    bool IncludeUnfilteredCount = false, // true → engine also computes the total without filter/search/scope (recordsTotal)
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

// The leaf's origin channel → determines which whitelist applies (§7).
public enum FilterOrigin
{
    Filter  = 0,  // structured (QueryBuilder, per-column) → Filterable + AllowedOperators whitelist
    Search  = 1,  // global search box                      → Searchable (string) whitelist, op MUST be Contains
    Scope   = 2,  // contextual/lookup from the CLIENT       → Scopable whitelist (Spec 01 §5.6)
    Trusted = 3,  // injected by the server (ShapeQuery)     → NOT validated (trusted)
}
```

`FilterOperator` is unchanged from Spec 01 §8 (flags enum).

### 6.2 Result — `ViewListResult<TRow>` (code), `ViewQueryResult<T>` (proposed, not used)

> **Reconciliation (2026-06-20).** The code returns **`ViewListResult<TRow>`** (Spec 01 §10.1 DR6):
> `record ViewListResult<TRow>(PagedResult<TRow> Page, long TotalRowsUnfiltered)`. `Page.TotalRows`
> = `recordsFiltered`; `TotalRowsUnfiltered` = `recordsTotal`. The `ViewQueryResult<T>` type below
> is **not** created — `PagedResult<T>` + `ViewListResult<TRow>` already satisfy the two-count need.
> The block below is kept as a design note.

The engine returns a rich record that carries **two counts**. `PagedResult<T>` (Spec 01 §10) is the *default-shape projection* of this result; other adapters (DataTables) map it to their own shape.

```csharp
namespace a2n.Vista;

public sealed record ViewQueryResult<T>(
    IReadOnlyList<T> Items,
    long FilteredRows,         // total after ALL constraints → basis for TotalPages
    long? UnfilteredRows,      // null unless IncludeUnfilteredCount; total without filter/search/client-scope
    int Page,
    int PageSize)
{
    public long TotalPages => PageSize <= 0 ? 0 : (FilteredRows + PageSize - 1) / PageSize;

    // Projection to the neutral shape of Spec 01 §10.
    public PagedResult<T> ToPagedResult() =>
        new(Items, FilteredRows, Page, PageSize, TotalPages);
}
```

> Resolution for ref `dyndata-datatables-observed.md` §7 item 7: `FilteredRows` = `recordsFiltered`; `UnfilteredRows` (when requested) = `recordsTotal`.

### 6.3 Port `IViewExecutor` (Core)

> **Reconciliation (2026-06-20).** The implemented port is **generic** and accepts `ViewMetadata`
> directly (not `ViewQueryExecution`), and **merges write** (DR8):
>
> ```csharp
> namespace a2n.Vista.Ports;
>
> public interface IViewExecutor
> {
>     // List: validate+filter+sort+page, materialize one page + two counts.
>     Task<ViewListResult<TRow>> ListAsync<TRow>(
>         ViewMetadata view, ViewQueryRequest request, IViewScope scope, CancellationToken ct);
>
>     // Detail by-key (null → 404 in Spec 05).
>     Task<TRow?> DetailAsync<TRow>(
>         ViewMetadata view, object key, IViewScope scope, CancellationToken ct);
>
>     // Write (Pillar 1: throw / endpoint 501) — TCrud typed.
>     Task<object> CreateAsync<TCrud>(ViewMetadata view, TCrud model, IViewScope scope, CancellationToken ct) where TCrud : class;
>     Task<bool> UpdateAsync<TCrud>(ViewMetadata view, object key, TCrud model, IViewScope scope, string? concurrencyToken, CancellationToken ct) where TCrud : class;
>     Task<bool> DeleteAsync(ViewMetadata view, object key, IViewScope scope, string? concurrencyToken, CancellationToken ct);
> }
> ```
>
> There is no separate `IViewWriter` (contrasting the Spec 05 §7.1 D82 sketch); write lives in `IViewExecutor`.
> All members are marked `[RequiresUnreferencedCode]` (the reflection path until source-gen in Pillar 3).
> The non-generic block below is kept as a design note.

A non-generic port, resolved via DI at the composition root, implemented by the EF layer. `TQuery` is erased to `object` at the boundary (consistent with `IViewExporter`, Spec 01 §11.1); typed materialization is performed by a source-gen delegate (Pillar 3).

```csharp
namespace a2n.Vista;

public interface IViewExecutor
{
    // List/query facet (§5). viewName → ViewMetadata via IViewRegistry.
    Task<ViewQueryResult<object>> QueryAsync(
        ViewQueryExecution exec,
        CancellationToken ct = default);

    // Detail facet (Spec 01 §4.6). null if not found → 404 in Spec 05.
    Task<object?> GetByKeyAsync(
        string viewName,
        object key,
        IViewScope scope,
        CancellationToken ct = default);
}

// All execution inputs that have already been host-validated (auth passed, scope collected).
public sealed record ViewQueryExecution(
    string ViewName,
    ViewQueryRequest Request,
    IViewScope Scope,
    IServiceProvider Services);
```

`IViewScope` is unchanged from Spec 01 §5.6 (`AddRowFilter<TSource>`). Leaves added via scope enter the `Trusted` channel (not validated).

## 7. Per-Channel Validation & Whitelist

Validation (step 4) is the engine's **primary security gate**. It runs before coercion & expression building. Each `FilterLeaf` is evaluated according to its `Origin` against `ViewMetadata.Fields`:

| `Origin` | Field must be | Operator must be | Violation |
|---|---|---|---|
| `Filter` | `IsFilterable == true` | `Op ∈ AllowedOperators[field]` | 400 `filter-field-not-allowed` / `filter-operator-not-allowed` |
| `Search` | `IsSearchable == true` **and** `ClrType == string` | `Op == Contains` (forced) | 400 `search-field-not-allowed` |
| `Scope` | `IsScopable == true` | `Op ∈ AllowedOperators[field]` | 400 `scope-field-not-allowed` |
| `Trusted` | — | — | not validated (server-trusted, Spec 01 §5.6/D46) |

`SortSpec.Field` must be `IsSortable == true`; otherwise → 400 `sort-field-not-allowed`.

Additional rules:

1. **Unknown field** (not present in `ViewMetadata.Fields`) → always 400 `filter-field-not-allowed` (never silently skipped — the opposite of DynData `externalFilter`, ref §7 item 4).
2. **`IsHidden` does not block filter/scope** — a technical PK marked `Hidden().Scopable()` remains valid as a lookup key (Spec 01 §5.6). Hidden is only about *display/serialization*, not filterability.
3. **Recursive validation** traverses `FilterAnd/Or/Not` until all leaves are validated. A single violation aborts the entire request (fail-fast), and the error includes `field` + `operator` + `allowed` in `extensions` (Spec 01 §14.1).
4. **Anti-injection invariant**: a field name in a leaf is used **only** as a *lookup key* into the source-gen member-access map. There is no path where a field string becomes part of SQL text. An unregistered field has no member-access entry → it is automatically rejected at this step.

## 8. Value Model & Coercion (Sanitization)

`FilterLeaf.Value` arrives raw from the adapter (`string`, number, `bool`, `JsonElement`, or array). Step 5 coerces it to the field CLR type (`FieldMetadata.ClrType`) before it enters a constant expression.

### 8.1 Coercion rules

| Target | Accepted source | Rule |
|---|---|---|
| `string` | string | as-is (wildcard escaping is in §10, not here) |
| `int/long/short/byte` | number / numeric-string | `InvariantCulture`; overflow → 400 |
| `decimal/double/float` | number / numeric-string | `InvariantCulture` |
| `bool` | bool / `"true"`/`"false"`/`"1"`/`"0"` | case-insensitive |
| `DateTime/DateTimeOffset` | ISO-8601 string | `DateTimeStyles.RoundtripKind`; other formats → 400 |
| `Guid` | string | `Guid.TryParse`; failure → 400 |
| `enum` | name / underlying value | `Enum.TryParse` (case-insensitive); invalid → 400 |
| `T?` (nullable) | the above, or `null` | `null` is only legal for `IsNull`/`In`-member |

Coercion is **culture-invariant** (server-locale-independent) — closing the DynData `ListSeparator`/locale bug (Spec 01 §11.3 analog). Coercion failure → 400 `value-type-mismatch` with `field`, `expectedType`, `value`.

### 8.2 Multi-value forms

- **`In`**: `Value` must be an array/list. Each element is coerced to `ClrType`. The size is capped: default **1000** (`MaxInValues`), with a global override; more → 400 `payload-too-large` (413). Built as `list.Contains(member)` → EF translates it to SQL `IN`.
- **`Between`**: `Value` must be a 2-element array `[lo, hi]`, both non-null, coerced. Not 2 elements → 400. Built as `member >= lo && member <= hi`.
- **`IsNull`**: `Value` is ignored. Only valid for a nullable / reference-type field; on a non-nullable value-type → 400 `operator-not-applicable`.

### 8.3 Sanitization invariants

1. No client value becomes a SQL **identifier** (only a **parameter** value).
2. The filter string length is capped (default `MaxFilterStringLength = 4096`) → anything longer is rejected with 400 (anti-DoS against LIKE patterns).
3. The `FilterNode` tree depth is capped (default `MaxFilterDepth = 16`) & the total leaf count (default `MaxFilterLeaves = 128`) → more → 400. Closes nested-OR attacks that blow up the query plan.

## 9. Expression Building per Operator

After validation+coercion, each `FilterLeaf` becomes an `Expression<Func<TQuery, bool>>`. `member` = the source-gen member-access `q => q.Field`; `c` = the coerced constant.

| `FilterOperator` | Expression (semantics) | Null note |
|---|---|---|
| `Equals` | `member == c` | `c == null` → `member == null` |
| `NotEquals` | `member != c` | `c == null` → `member != null` |
| `GreaterThan` | `member > c` | comparable types only; on a `null` member → SQL `false` |
| `GreaterThanOrEqual` | `member >= c` | same |
| `LessThan` | `member < c` | same |
| `LessThanOrEqual` | `member <= c` | same |
| `Contains` | dialect string-match (§10) | null-guard for in-memory |
| `StartsWith` | dialect string-match (§10) | same |
| `EndsWith` | dialect string-match (§10) | same |
| `In` | `values.Contains(member)` | a `null` member → provider-dependent |
| `Between` | `member >= lo && member <= hi` | lo/hi must be non-null (§8.2) |
| `IsNull` | `member == null` | — |

Rules:

1. **`FilterNot(child)`** → `Expression.Not(...)` wrapping the sub-predicate (e.g., `is_not_empty`, `not_in` from the adapter, ref §6.2).
2. **Operator vs type**: a comparison operator (`>`,`>=`,`<`,`<=`,`Between`) on `string`/`bool`/`Guid` → 400 `operator-not-applicable` (other than what `AllowedOperators` permits). The field whitelist (§7) is the first line of defense; this check is the second line of defense for type consistency.
3. **In-memory null-guard**: for the InMemory/test provider, string-match is wrapped with `member != null && ...` to avoid a `NullReferenceException`; on a relational provider a null member naturally yields `unknown`/false — the guard stays safe & does not change the SQL result.
4. **Composition**: `FilterAnd/Or` → chained `AndAlso`/`OrElse` with the same parameter; an empty tree (`null` Filter) → no `Where`.

## 10. Provider-aware String Matching

The core of "provider-detected, not a client flag" (Spec 01 §8.2, D17). The client only sends intent (`Contains`/`StartsWith`/`EndsWith`); the engine chooses the translation.

### 10.1 Port `IQueryDialect` (Core)

```csharp
namespace a2n.Vista;

public enum StringMatchKind { Contains, StartsWith, EndsWith }

public interface IQueryDialect
{
    string ProviderName { get; }                 // e.g. "Microsoft.EntityFrameworkCore.SqlServer"
    bool CaseInsensitiveByDefault { get; }

    // Builds the string-match predicate for ONE string member.
    // The implementation chooses string.Contains (EF auto-escape) or a LIKE/ILIKE pattern
    // (manual escape via EscapeLikePattern).
    Expression BuildStringMatch(Expression member, string value, StringMatchKind kind);
}
```

### 10.2 Default strategy per provider

| Provider | `Contains` default | Mechanism |
|---|---|---|
| SQL Server | CI (default collation) | `string.Contains/StartsWith/EndsWith` (EF translate + **auto-escape**) |
| SQLite | CI (ASCII) native | same |
| MySQL / Pomelo | CI (default collation) | same |
| InMemory / test | CI | `string.Contains(StringComparison.OrdinalIgnoreCase)` + null-guard |
| **PostgreSQL (Npgsql)** | **CS** (LIKE) → needs ILIKE for CI | `EF.Functions.ILike(member, "%" + Escape(value) + "%")` |

The `DefaultStringMatch` default (Spec 01 §8.2): every provider uses the `string.Contains` path **except** PostgreSQL, whose case-insensitive matching requires `ILIKE`.

### 10.3 PostgreSQL = a dialect in a separate package

`string.Contains` on Npgsql translates to a `LIKE` that is **case-sensitive** in PostgreSQL. For CI parity with other providers, `EF.Functions.ILike` is needed — which lives in the `Npgsql.EntityFrameworkCore.PostgreSQL` package. So that Core/EF is not coupled to a single provider (Spec 01 D48):

- `a2n.Vista.EntityFrameworkCore` provides the **default dialect** (`string.Contains`) for SQL Server/SQLite/MySQL/InMemory.
- `a2n.Vista.EntityFrameworkCore.Npgsql` (a small separate package) provides `NpgsqlQueryDialect` (ILIKE). Registered via `services.AddVistaNpgsql()`.
- The engine resolves `IQueryDialect` based on `DbContext.Database.ProviderName`; if there is no specific dialect → the default dialect.

### 10.4 Wildcard escaping (required)

The `string.Contains/StartsWith/EndsWith` path (EF) escapes `%`/`_` automatically through parameterization — **safe**. The **raw-pattern** path (`EF.Functions.ILike`) does **not** — a client value `%`/`_`/`\` must be escaped manually so it does not become wildcard injection:

```csharp
// used ONLY on the raw-pattern ILIKE/LIKE path
static string EscapeLikePattern(string v) => v
    .Replace("\\", "\\\\")
    .Replace("%",  "\\%")
    .Replace("_",  "\\_");
// pattern: "%" + EscapeLikePattern(v) + "%", with ESCAPE '\'
```

A per-view override (e.g., force case-sensitive for a special-collation column) is available via field metadata — a candidate API in Open Question §17.

## 11. Sort Building

`SortSpec[]` → chained `OrderBy/OrderByDescending` + `ThenBy*`, using source-gen member-access.

1. **Validation**: each field must be `IsSortable` (§7). A field outside the projection → 400 (not a silently ignored sort like DynData's `OrderBy(string)`).
2. **PK tiebreaker (deterministic)**: the engine **always** adds the PK field (`FieldMetadata` marked `PrimaryKey`, Spec 01 §5.5) as the **last** sort key if it is not already in `Sort`. Without this, `Skip/Take` on non-unique sort values could return duplicate/missing rows across pages. A composite PK → added in sequence (declaration order).
3. **Default order**: if `Sort` is empty → order by PK ascending (deterministic). A View with no declared PK → the engine uses the first projection field + a **warning** (candidate: require a PK for stable paging, §17).
4. **Null ordering**: follows the provider default (e.g., SQL Server implicit `NULLS`). An explicit override → §17.

## 12. Paging & Counts

### 12.1 Offset paging

```csharp
long offset = (long)request.Page * request.PageSize;   // long: prevent int overflow (Spec 01 §10)
if (offset > int.MaxValue) → 400 "page-offset-too-large";
query.Skip((int)offset).Take(request.PageSize);
```

- `PageSize` is clamped to `HardLimits.MaxPageSize` (Spec 01 §5.4/§7). `PageSize <= 0` → 400. **`length = -1` (DynData "return all") is rejected** (Spec 01 §12.2).
- v1.0 supports offset paging only. Keyset/seek (for very large offsets) is deferred (§17).

### 12.2 Two counts

- **`FilteredRows`** is always computed: `LongCountAsync` on the query after step 9 (before order/page). The basis for `TotalPages`.
- **`UnfilteredRows`** only when `IncludeUnfilteredCount == true`: `LongCountAsync` on the query at the end of step 8 (after row filter + trusted scope, **before** filter/search/client-scope). This is DataTables `recordsTotal` (ref §6.3/§7.7).
- Both honor the `CancellationToken`. Two counts = two DB round-trips; an adapter that does not need `recordsTotal` leaves `IncludeUnfilteredCount = false` (default) to save one query.

### 12.3 Materialization

- `await query.ToListAsync(ct)` — async-only, `CancellationToken` required (Spec 01 §10). There is no sync overload.
- `.AsNoTracking()` is the default for the read path (read-only projection). There is no DynData `AsNoTrackingDynamic` (Spec 01 §12.4).
- There is no public `ToPagedResultAsync` extension in Core (Spec 01 §10.2) — paging is an internal engine detail.

## 13. Masking & Post-processing

`MaskField(field, predicate, masker)` (Spec 01 §5.2/D29) is applied **after** materialization (step 12), per-item, using a source-gen accessor/mutator (not `PropertyInfo`). `predicate` is evaluated once per request (`Func<IServiceProvider,bool>`), not per-row. Masking does **not** affect filter/sort/count — only the final shape that is sent. Implication: a masked field can still be filtered in SQL (e.g., search for an exact email) unless it is set `Filterable(false)` (Spec 01 §4.4 point 2 / D95).

## 14. AOT Constraints

In line with Spec 01 §9 and Pillar 3:

1. **Member-access** (`q => q.Field`) for each field in `ViewMetadata` is generated by source-gen as a static delegate/`Expression` — not `Expression.Property(p, PropertyInfo)` at runtime via reflection. Spec 02 defines the contract; Spec 03 defines the generator.
2. **Constant values** are built via `Expression.Constant` from typed coercion results — no reflection boxing on the hot path.
3. **Materialization & mask** use a source-gen accessor, not `PropertyInfo.GetValue/SetValue`.
4. The reflection-based fallback path (e.g., a View registered via `RegisterAssembly`, Spec 01 §5.3) is marked `[RequiresUnreferencedCode]`. The engine must have an equivalent source-gen path for all of the operations above.
5. An anonymous-projection View (Style A, Spec 01 §4.5) remains `[RequiresUnreferencedCode]` on serialization; **its filter/sort/paging is AOT-clean** because member-access is still generated from the projection shape.

## 15. Error Model (query-specific)

Extends the table in Spec 01 §14.1. All RFC 7807, with `type` under `https://a2n.dev/vista/errors/`. `extensions` is machine-readable (`field`, `operator`, `allowed`, `expectedType`).

| Condition | HTTP | `type` |
|---|---|---|
| Filter field is not `Filterable` / unknown | 400 | `.../filter-field-not-allowed` |
| Operator outside `AllowedOperators` | 400 | `.../filter-operator-not-allowed` |
| Search field is not `Searchable`/not a string | 400 | `.../search-field-not-allowed` |
| Scope field is not `Scopable` | 400 | `.../scope-field-not-allowed` |
| Sort field is not `Sortable` | 400 | `.../sort-field-not-allowed` |
| Operator not applicable to the type (e.g., `>` on `bool`) | 400 | `.../operator-not-applicable` |
| Coercion failed (type mismatch) | 400 | `.../value-type-mismatch` |
| `Between`/`In` value form is wrong | 400 | `.../malformed-value` |
| Tree too deep / too many leaves / string too long | 400 | `.../query-too-complex` |
| `In` exceeds `MaxInValues` | 413 | `.../payload-too-large` |
| Page offset overflow / `PageSize<=0` / `length=-1` | 400 | `.../invalid-paging` |

Principle: **fail-fast & specific**. A single violation aborts the request with field+operator detail so the adapter/client can fix it. There is no "silent skip" (contrasting DynData).

## 16. Decision Log (continued from Spec 01 D50)

> **Reconciliation (2026-06-20).** Some of the decisions below are **overridden** by the Pillar 1
> implementation (see Spec 01 §13.1): **D51** (`ViewQueryResult<T>`) → replaced by `ViewListResult<TRow>` (DR6);
> **D52** (`FilterOrigin` on `FilterLeaf`) → `FilterOrigin` becomes a 3-value enum-parameter, no `Trusted` (DR9);
> **D53** (non-generic port erased-to-`object`) → `IViewExecutor` generic + write merged (DR8);
> **D58** (`IncludeUnfilteredCount`) → two counts via `ViewListResult.TotalRowsUnfiltered` without a request flag.
> The other decisions (coercion, dialect, deterministic paging, complexity guards) remain in effect as the
> engine target; some are new and some are implemented in Pillar 1.

| # | Decision | Status | Note |
|---|---|---|---|
| D51 | `IViewExecutor.QueryAsync` returns `ViewQueryResult<T>` (Items + `FilteredRows` + optional `UnfilteredRows`). `PagedResult<T>` (Spec 01 §10) = the default-shape projection via `ToPagedResult()`. | **Decided** | Resolves ref `dyndata-datatables-observed.md` §7.7 (recordsTotal vs recordsFiltered). |
| D52 | `FilterLeaf` carries `FilterOrigin` (`Filter`/`Search`/`Scope`/`Trusted`); the engine validates per-channel (§7). | **Decided** | Formalizes the "internal FilterOrigin record" of Spec 01 §8.3. |
| D53 | `IViewExecutor` & `IViewScope` & `IQueryDialect` are **ports in Core**; the EF implementation is in `a2n.Vista.EntityFrameworkCore`. | **Decided** | Spec 01 D48. `TQuery` is erased to `object` at the port boundary. |
| D54 | Default string-match `string.Contains/StartsWith/EndsWith` (EF auto-escape). PostgreSQL CI via the separate dialect `a2n.Vista.EntityFrameworkCore.Npgsql` (ILIKE). | **Decided** | §10. Avoids coupling Core to a single provider. |
| D55 | The raw-pattern path (ILIKE/LIKE) **must** use `EscapeLikePattern` for `%`/`_`/`\`. | **Decided** | §10.4. Anti wildcard-injection. |
| D56 | Deterministic paging: the PK is always added as the last sort tiebreaker; an empty `Sort` → by PK asc. | **Decided** | §11. Prevents duplicate/missing rows across pages. |
| D57 | v1.0 offset paging only; `Skip((int)(long)Page*PageSize)`, offset > `int.MaxValue` → 400. Keyset/seek deferred. | **Decided** | §12.1, §17. |
| D58 | Two counts: `FilteredRows` always; `UnfilteredRows` only when `IncludeUnfilteredCount`. | **Decided** | §12.2. Saves a round-trip by default. |
| D59 | Culture-invariant coercion; `In` capped (`MaxInValues=1000`); type mismatch → 400. | **Decided** | §8. Closes locale & DoS bugs. |
| D60 | Anti-injection invariant: a field name is only a lookup key into source-gen member-access; never SQL text. An unregistered field → 400 (no silent skip). | **Decided** | §7.4, §15. Contrasts DynData `externalFilter`. |
| D61 | Query complexity guards: `MaxFilterDepth=16`, `MaxFilterLeaves=128`, `MaxFilterStringLength=4096` (all globally overridable). | **Decided** | §8.3. Anti query-plan blow-up. |
| D62 | Mask applied post-materialization via a source-gen accessor; does not affect filter/sort/count. | **Decided** | §13. Spec 01 D29. |

## 17. Open Questions

1. **Keyset/seek pagination** for large offsets (`Skip` is expensive in OLTP). v1.x candidate: `ViewQueryRequest.After` (a cursor token) based on PK+sort. Requires a stable cursor contract that is adapter-compatible.
2. **Require a PK for stable paging?** §11 point 3 currently *warns* when a View has no PK. Candidate: make it a build-time error (a source-gen diagnostic) because paging without a unique key is fundamentally non-deterministic.
3. **Null ordering & per-field collation override** — a metadata API (`f.NullsFirst()` / `f.Collation("...")`)? Currently follows the provider default (§10.4, §11.4).
4. **`is_empty`/`is_not_empty` semantics** (ref §6.2/§7.2): map to `IsNull`, an empty string, or both (`member == null || member == ""`)? The decision affects the QueryBuilder adapter (Spec 04). Candidate default: both for strings.
5. **Per-column search in DataTables** (`columns[i][search][value]`, ref §7.5): map to `FilterLeaf(Contains, Origin=Filter)` or the `Search` channel? It affects which whitelist applies. Candidate: `Filter` (per-column = structured filter, not global search).
6. **Distinct-values** (`GET .../distinct/{field}`, Spec 01 §14.3) — which query path serves it? Reuses the §7 validation (`field ∈ Filterable`, `take ≤ 1000`). Details in a separate spec.

## 18. Next / Forward References

- `03-source-generator.md` — the generator for member-access, accessors, `CompiledView`, `JsonSerializerContext` consumed by this engine (§14).
- `04-adapter-contract.md` — `IViewAdapter`: producing `ViewQueryRequest` (set `FilterOrigin` per leaf, §6.1), consuming `ViewQueryResult`/`PagedResult`. Includes DataTables & jQuery-QueryBuilder mapping (ref `dyndata-datatables-observed.md` §6).
- `05-aspnetcore-mapping.md` — composition of auth → `IViewScope` → `IViewExecutor`, the HTTP error model, the write/CRUD path & concurrency.
