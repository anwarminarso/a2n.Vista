# Full code audit — 2026-07-31

| | |
|---|---|
| **Date** | 2026-07-31 |
| **Scope** | All 7 shipped libraries, both implemented grid adapters, the source generators, the TypeScript client generator, and the sample hosts |
| **Volume** | ~35,000 lines of production C# (of 95,125 total; ~52,000 are tests) |
| **Build state at audit time** | Green on net8.0 / net9.0 / net10.0, **0 warnings**, Release |
| **Method** | Read-only source review, five parallel per-area passes. No code was modified; no application was executed |
| **Findings** | 6 security, 13 correctness, 9 dead code, 8 performance |

Because the solution builds clean with zero warnings, everything in this report is a defect the compiler
and the existing analyzers cannot see.

---

## 0. How to read this report

Findings are ordered by severity within each section and carry a stable ID (`SEC-01`, `BUG-04`, …) for
citation from commits and issues. Each finding states what the code does, why that is wrong, and the
concrete fix.

The three headline themes:

1. **Three of the six security findings breach the project's own secure-by-default pillar** — they are not
   missing hardening, they are documented guarantees that the code does not deliver (`SEC-01`, `SEC-03`,
   `SEC-04`).
2. **Two "same thing, two spellings" hazards** where one spelling is silently unsafe or broken
   (`SEC-05` `nameof` vs string literal; `BUG-09` `Key()` vs `PrimaryKey()`).
3. **The write path's HTTP surface leaks before it authorizes** (`BUG-03`), and its concurrency story is
   weaker than the docs claim (`BUG-04`, `BUG-05`).

### Recommended remediation order

| Order | Finding | Rationale |
|---|---|---|
| 1 | `SEC-01` | The only finding that leaks data across tenants. Small, self-contained fix (fail-open → fail-closed) |
| 2 | `SEC-02` + `SEC-03` | Both concern the OpenAPI surface; one coherent posture change |
| 3 | `SEC-05` | Effectively a one-line generator fix that closes primary-key mass assignment |
| 4 | `BUG-01` | Turns a 500 into the documented 400. Cheap, improves the error contract |
| 5 | `SEC-04`, `SEC-06`, `BUG-02` | Each needs a small design decision first (see the notes on each) |

`BUG-02` (adapter paging arithmetic) deliberately sits last of the high-severity items: fixing it properly
touches the `ViewQueryRequest` contract, so it needs a decision (carry an absolute offset vs reject an
oversized page size instead of clamping) before code changes.

### Remediation status (updated 2026-07-31)

Tranche 1 — every finding whose fix is self-contained and needs no new contract decision — is **fixed and
covered by regression tests**. Build green on net8.0/net9.0/net10.0; suites green
(`a2n.Vista.Tests` 527/527 net8 · 528/528 net10, `a2n.Vista.Client.TypeScript.Tests` 143/143,
`a2n.Vista.SourceGenerators.Tests` 114/114).

| Finding | Status | Fix |
|---|---|---|
| `SEC-01` | **Fixed** | `IViewScope.RowFilterCount` added (type-erased scope inspection); `ProjectedViewExecutionPlan` now fails closed on an authored **or** request-scoped row filter |
| `SEC-02` | **Fixed (posture)** | `MapVistaOpenApi()` attaches `RequireAuthorization()` by default; skipped under the D94 `AllowAnonymousAccess()` opt-in or the new explicit `VistaOpenApiOptions.RequireAuthorization = false`. Per-caller **document filtering through `IViewAuthorizer` is not done** — it makes the document per-identity and needs a caching decision |
| `SEC-03` | **Fixed** | `DtoSchemaPolicy.ForView` drops `IsHidden` members from the emitted row schema and annotates `IsMaskable` ones |
| `SEC-04` | **Fixed (D143)** | Masked fields default non-sortable, with a `Sortable(...)` opt-in override; mirrored in the source generator so generated metadata still matches the reflection oracle |
| `SEC-05` | **Fixed** | Key names resolved via `SemanticModel.GetConstantValue`, so `nameof(...)`, `const` fields, and constant concatenation are guarded exactly like a literal. The generator harness stub was also missing the `Key(params string[])` overload, which is why the literal path had no coverage either |
| `SEC-06` | **Fixed** | Two independent guards: `ViewNameGuard` rejects an unsafe derived view name as a typed `GenerationError.UnsafeViewName` at the model stage (this also removes the unhandled-`ArgumentException` CLI crash), and `OutputWriter` refuses any path resolving outside `--out` (`OutputPathEscapesRoot`) before staging |
| `BUG-01` | **Fixed** | The four fall-throughs now `Convert.ChangeType` inside the guarded block → `FilterValidationException` → 400 |
| `BUG-02` | **Fixed (D144)** | Option 1 chosen: `ViewQueryRequest.Offset` carries the absolute row offset (optional; `null` keeps the page model). Both adapters pass `start`/`startRow` verbatim, so clamping the page size can no longer move the window. DataTables also rejects `start < 0` |
| `BUG-03` | **Fixed (D145)** | `ViewRequestExecutor.AuthorizeFacetAsync` is the pre-auth step the mapper calls before the body read, bind, key read, and 428 gate (adapter handler too). The decision is memoized per request, so the authorizer is still consulted once per (view, facet) |
| `BUG-04` | **Fixed (D146)** | Startup fails closed when the declared token is not a model concurrency token, and the executor pins the entry's original token so the database performs the check atomically |
| `BUG-05` | **Fixed (D146)** | The post-write token rides the new request-scoped `IWriteTokenSink` and is what the update emits as `ETag`; a delete emits no `ETag`. No `IViewExecutor` port change, so the generated dispatch invoker is untouched |
| `BUG-06` | **Fixed** | Write bind failures emit fixed, Vista-authored text plus the stable code; the serializer message rides `InnerException` for server-side logging only |
| `BUG-07` | **Fixed (D147)** | See the tranche 4 note below |
| `BUG-08` | **Fixed** | Components keyed by `(Type, policy)` with deterministic namespace-derived disambiguation |
| `BUG-09` | **Fixed** | The write-facet guard gates on the resolved `keyFields`, so `Key(...)` satisfies it |
| `BUG-10` | **Fixed (D148)** | See the tranche 4 note below |
| `BUG-11` | **Fixed** | CSV neutralizes leading `= + - @ TAB CR`; the XLSX writer strips XML-illegal control characters (surrogate pairs preserved) |
| `BUG-12` | **Fixed** | A negated empty group keeps its `FilterNot`; only an un-negated empty group is a no-op |
| `BUG-13` | **Fixed** | The net10 branch maps `additionalProperties` like the net9 one |
| `DEAD-04` | **Fixed** | `HardLimits.MaxExportRows` clamps to `AbsoluteMaxExportRows` on every construction path, including `with` |
| `PERF-01` | **Partially fixed** | `Results.Stream` removes one full payload copy; true streaming to `Response.Body` remains |
| `DEAD-05` | **Fixed (D144)** | DataTables honours per-column `searchable`/`orderable` and rejects `regex=true` instead of executing it as a literal `Contains`. `DataTablesResponse.Error` is still unused |
| `BUG-07` | **Fixed (D147)** | All three execution plans read `AsNoTracking`, and the reflection mask rebuilds a get-only row instead of refusing it |
| `BUG-10` | **Fixed (D148)** | Hand-written `Equals`/`GetHashCode` over the declarative content; the startup-completed key is excluded so both are stable |
| `PERF-02` | **Fixed** | The reflection fallback memoizes the resolved member per `(row type, field name)`, negative results included |
| `PERF-04` | **Fixed** | `Configure` runs once per view against one cached builder; metadata, masks, the write facet, and row filters share that result |
| `DEAD-02` | **Fixed (D149)** | Reclassified: a designed member, not dead. The format hint now reaches `FieldMetadata`, the metadata facet, and the OpenAPI schema |
| `DEAD-06` | **Fixed** | Reclassified: an under-implementation of R3.1, not dead. Scanning now runs the same registration unit as `Register<TView>()`, with first test coverage |
| `DEAD-07` | **Reclassified — finding withdrawn** | Not dead code: `openapi-emitter` R12.2 requires an extension point, which is **unimplemented**. Removal reverted |
| `DEAD-01`, `DEAD-03`, `DEAD-08` | **Open (scope call)** | Each is a spec'd surface with no acceptance criterion behind it; removal is an owner scope decision plus a spec reconciliation |
| `PERF-05` | **Fixed** | One shared `ViewFieldLookup.For(view)`, memoized per metadata instance and frozen; replaces four independent per-call builders |
| `PERF-07` | **Fixed** | The metadata projection is memoized per view and its serialized payload + `ETag` computed once; the 304 path is now one string comparison |
| Other `DEAD-*` / `PERF-*` | **Open** | Each is either an API removal (breaking) or a design change; see the notes on each |

### Tranche 2 (2026-07-31) — the decision-bearing findings

`SEC-04`, `BUG-02`, `BUG-03`, `BUG-04`, `BUG-05` and `DEAD-05` are now settled and implemented as **D143–D146**
(`docs/PROJECT-STATUS.md` §5 carries the decision text, §2.23 the summary). Behaviour-visible consequences to
know about:

- A view that sorted on a masked field must now opt in with `Sortable()` (**D143**), and the generated
  execution plan no longer emits a member accessor for such a field.
- `ViewQueryRequest` gained an optional `Offset`; `Page` is ignored when it is set (**D144**). The adapters no
  longer derive a page index. `DataTablesQuery.Start < 0` and `search[regex]=true` are now bind errors.
- Write and adapter endpoints authorize before binding (**D145**), so an unauthorized caller receives `403`
  where it previously received `428` or `400`.
- A view whose `WithConcurrencyToken(...)` member is not an EF concurrency token now **fails startup**
  (**D146**). This surfaced five test fixtures with exactly that misconfiguration; both shipped samples were
  already correct. A successful delete no longer emits an `ETag`.

### Tranche 3 (2026-07-31) — the contract-free caching findings

`PERF-02`, `PERF-05` and `PERF-07` are fixed. All three are pure memoization of data that is immutable after
registration, so **no route, envelope, error shape, or public contract changes** and no decision was needed.
Each cache is a `ConditionalWeakTable` keyed by reference — record equality over `ViewMetadata` is not a
dependable cache key (`BUG-10`), and weak keying means a short-lived metadata instance (a test fixture, a
disposed host) leaks nothing.

- **`PERF-05`** — the four independent per-call field-lookup builders (`FilterCompiler` ×2, both grid
  adapters) collapse into one `ViewFieldLookup.For(view)` in Core, built once per metadata instance as a
  `FrozenDictionary`. Ordinal, last-wins matching is unchanged; frozen because the lookup is now shared
  across requests.
- **`PERF-02`** — `ExportColumns.Value(row, name)` memoizes the resolved `PropertyInfo` per
  `(row type, name)`, misses included. Reads still observe live row state.
- **`PERF-07`** — `VistaMetadataResponse.From` memoizes the projection per view, and the mapper caches the
  serialized payload with its `ETag` keyed on that response instance, so a 304 costs one string comparison
  instead of a full serialization plus a SHA-256. The shared response's field list is wrapped read-only.

### Tranche 4 (2026-07-31) — the metadata / authoring / read-path findings

`BUG-07`, `BUG-10` and `PERF-04` are fixed and settled as **D147–D148** (`PERF-04` needed no decision). They
were taken together because they touch the same code.

- **`BUG-07` (D147), two parts.** *Reads are no-tracking:* all three execution plans apply `AsNoTracking()`
  to the source — a direct generic call in `SplitViewExecutionPlan` and in the generated compiled plan (the
  `*_VistaExecutionPlan` goldens changed), reflective in the already-RUC `ProjectedViewExecutionPlan` whose
  Style A delegate erases the element type, where it runs once per request and never per row. That removes
  the persistence hazard the finding described as unverified: with no tracked row, the in-place mask cannot
  reach `SaveChanges`. *The reflection mask is non-destructive:* a get-only row is rebuilt through a
  constructor covering every readable property, so an **anonymous** Style A row is maskable for the first
  time (it previously threw). Full coverage keeps the rebuild lossless; an ambiguous case-insensitive
  parameter match is treated as no match, so a wrong member can never be written. The misleading `Apply`
  doc comment was corrected.
- **`BUG-10` (D148).** Hand-written `Equals`/`GetHashCode` over the declarative content, with `Fields`
  compared **element-wise** (the synthesized version compared the list by reference). The startup-completed
  `KeyFields` is excluded from both, so neither can change during an instance's lifetime; that is harmless
  because view names are globally unique and `Name` is compared.
- **`PERF-04`.** `Configure` runs **once** per view against one cached builder, and metadata, mask specs, the
  write facet, and row filters are all read back from it. This also fixes the correctness side-effect the
  finding called out — the metadata published to the registry is no longer a different instance from the one
  `Name` reads — and removes the dead `BuildMetadataCore` virtual whose doc claimed an override that cannot
  exist (`View<TQuery, TCrud>` is deliberately not a subclass, D26).

### Tranche 5 (2026-07-31) — the `DEAD-*` batch, and a correction to how this section was written

Tranche 5 started as an API-removal batch and became a **method correction**. Cross-checking each `DEAD-*`
item against `.kiro/specs/*/requirements.md` — which §3's original method never did — reclassified half of
them (see the method-correction box in §3):

- **`DEAD-02` → fixed (D149).** A designed member (`01-view.md` §5.2) with no defined semantics. Decided and
  wired: the server publishes the display-format hint, the client applies it; Vista never interprets it.
  Additive, and omitted from the payload when unset, so `/metadata` is byte-identical (1537 bytes) for a view
  that sets no hint.
- **`DEAD-06` → fixed.** An under-implementation of `pilar-1-hardening` R3.1, not dead code. `RegisterAssembly`
  now shares one registration body with `Register<TView>()`, and has test coverage for the first time.
- **`DEAD-07` → finding withdrawn.** `openapi-emitter` R12.2 *requires* an adapter-documentation extension
  point. The flag is the wrong shape for it, so the defect is an unimplemented requirement, not dead code.
- **`DEAD-01`, `DEAD-03`, `DEAD-08` → open, owner scope call.** Each is declared on a spec'd surface with no
  acceptance criterion behind its behaviour. Removal is defensible for all three (and for `DEAD-08` the design
  doc contradicts a tested security requirement), but each must reconcile its spec in the same change.

**Still open after tranche 5:** `DEAD-01`, `DEAD-03`, `DEAD-08` (scope calls), `DEAD-07`/R12.2 (implement the
extension point), `DEAD-09` (generator duplication — contains a real defect: the accessor-map emitters have
drifted on key escaping), and `PERF-03`, `PERF-06`, `PERF-08`.

---

## 1. Security findings

### SEC-01 — Row-level security is silently dropped for Style A views

| | |
|---|---|
| **Severity** | High |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.EntityFrameworkCore/Execution/ProjectedViewExecutionPlan.cs:87` |

`CreateScopedQueryable` accepts an `IViewScope scope` parameter and **never reads it**. Its fail-closed
guard covers only *authored* row filters:

```csharp
public IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope)
{
    ArgumentNullException.ThrowIfNull(scope);   // validated, then unused

    if (_authoredRowFilterCount > 0)
    {
        throw new NotSupportedException(/* ... fail closed ... */);
    }

    var queryable = _projectedFactory(dbContext, services) ?? throw new InvalidOperationException(/* ... */);
    return queryable;                            // scope filters never applied
}
```

Per-request, server-trusted predicates pushed in by `IViewAuthorizer.ShapeQuery` — tenant isolation,
ownership, any row-level security — are therefore discarded. `SplitViewExecutionPlan.cs:98` shows the
correct handling on the sibling plan:

```csharp
var scopeFilters = scope.GetRowFilters<TSource>();
for (var i = 0; i < scopeFilters.Count; i++)
{
    source = source.Where(scopeFilters[i]);
}
```

That the filters really are populated is confirmed by
`src/a2n.Vista.AspNetCore/Execution/ViewRequestExecutor.cs` (`authorizer?.ShapeQuery(context, scope)` in
`AuthorizeAndShapeAsync`).

**Impact.** A Style A (central-template) view returns rows outside the authorized scope. This is not a
degraded mode — it is a silent bypass, and it is the one finding in this report that can leak data across
tenants.

**Fix.** Extend the existing fail-closed guard so a populated scope is also refused, rather than ignored:

```csharp
if (_authoredRowFilterCount > 0 || scope.GetRowFilters<TSource>().Count > 0)
{
    throw new NotSupportedException(/* ... */);
}
```

The class's own remarks already recommend the fuller remedy (retain source and projection separately so a
`SplitViewExecutionPlan` can be built, per spec §4.1). Failing closed is the minimal correct change and
should land first, on its own, with a regression test.

---

### SEC-02 — The OpenAPI document endpoint is anonymous by default

| | |
|---|---|
| **Severity** | High |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.OpenApi/VistaOpenApiEndpointRouteBuilderExtensions.cs:68` |

`MapVistaOpenApi()` maps a bare `GET`:

```csharp
return endpoints.MapGet(
    options.EndpointPath,
    (Delegate)(() => Results.Text(cache.GetJson(), "application/json")));
```

There is no `RequireAuthorization()`, no `IViewAuthorizer` call, and no `ViewFacet.Metadata` gate. An
ASP.NET Core endpoint that carries no authorization metadata is anonymous even when `UseAuthentication`
and `UseAuthorization` are in the pipeline, so `GET /openapi/v1.json` publishes every view's route, its
operation set, whether it is writable, and its full row/CRUD property schemas to any caller.

The file's remarks argue the endpoint "sits inside the host's normal middleware pipeline, so the
application's authentication and authorization apply to it exactly as to any other endpoint." That is the
defect: for an endpoint with no authorization metadata, "as to any other endpoint" means *anonymous*.

Two aggravating factors:

- Both shipped samples map it with no convention attached (`src/Examples/Northwind/Program.cs:95`,
  `src/Examples/a2n.Vista.Examples.AgGridNorthwind/Program.cs:110`). Only
  `OpenApiServingCoexistenceTests` adds `.RequireAuthorization()`, so the tested configuration is not the
  documented one.
- `VistaStartupValidator` does not cover this endpoint, so the **D94 fail-closed startup posture does not
  apply to it**. A non-Development host that never calls `AllowAnonymousAccess()` still serves the
  document anonymously.

**Fix.** Default the mapped endpoint to `RequireAuthorization()` with an explicit opt-out, and run the
document through the authorizer — at minimum `ViewFacet.Metadata` per documented view, filtering out
denied views so the document reflects the caller's actual visibility.

---

### SEC-03 — Hidden fields are published in the OpenAPI document

| | |
|---|---|
| **Severity** | High |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.OpenApi/VistaOpenApiDocumentBuilder.cs:277` |

The emitter generates row and CRUD schemas straight off the CLR type:

```csharp
var rowSchema = generator.GenerateSchema(view.QueryType);
```

`DtoSchemaGenerator` reflects over every public property of that type. Meanwhile the metadata facet
deliberately drops hidden fields (`src/a2n.Vista.AspNetCore/Execution/VistaMetadataResponse.cs:44`):

```csharp
.Where(f => !f.IsHidden)
```

So a field marked `Hidden()` is absent from `GET {route}/metadata` yet present, with its name and type, in
`components.schemas`. The `ViewMetadata` field flags are ignored entirely by the emitter.

Combined with `SEC-02`, this is a complete disclosure path: an anonymous caller reads the schema of fields
the application deliberately withholds from its own authenticated metadata endpoint.

**Fix.** Filter the generated properties against `view.Fields`: drop `IsHidden`, and describe an
`IsMaskable` field as its masked wire shape rather than its underlying type.

---

### SEC-04 — Masked fields remain sortable, defeating the D95 anti-probing default

| | |
|---|---|
| **Severity** | High |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.Core/Authoring/ViewBuilder.cs:299` |

Decision D95 makes a masked field non-probeable by default so a client cannot reconstruct the hidden value
by probing responses. The implementation covers filter and search, but not sort:

```csharp
if (_maskedFields.Contains(name))
{
    field = field with { IsMaskable = true };

    if (state is null || !state.FilterableExplicitlySet)
    {
        field = field with { IsFilterable = false };
    }

    if (IsStringType(type) && (state is null || !state.SearchableExplicitlySet))
    {
        field = field with { IsSearchable = false };
    }
    // IsSortable is left at true
}
```

`EfViewExecutor` honours `IsSortable` (`ResolveSortableField`), so a client can `ORDER BY` a masked field
and page through the result to infer the relative ordering of the masked values — the same probing vector
D95 closes for filter and search. For a masked numeric or date column, ordering plus paging is close to a
binary search over the hidden values.

**Fix.** Default `IsSortable = false` for a masked field, with an explicit opt-in override mirroring the
existing `FilterableExplicitlySet` / `SearchableExplicitlySet` signals (a new `SortableExplicitlySet`).

**Design note.** This changes behaviour for any existing view that sorts on a masked field, so it warrants
a decision record alongside D95 rather than a silent fix.

---

### SEC-05 — `nameof(...)` in `.Key(...)` defeats the generator's primary-key mass-assignment guard

| | |
|---|---|
| **Severity** | High |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.SourceGenerators/WriteMapperGenerator.cs:686` |

The helper's documentation comment promises `nameof(...)` support; the implementation only matches string
literals:

```csharp
/// Returns the string value of a literal (or <c>nameof(...)</c>) argument used by the
/// <c>.Key(params string[])</c> overload, or <c>null</c> when the argument is not a compile-time
/// string constant.
private static string TryGetStringLiteral(ExpressionSyntax expression)
    => Unwrap(expression) is LiteralExpressionSyntax literal
       && literal.IsKind(SyntaxKind.StringLiteralExpression)
        ? literal.Token.ValueText
        : null;
```

`nameof(Row.Id)` is an `InvocationExpressionSyntax`, not a `LiteralExpressionSyntax`, so it returns `null`
and no key is recorded for the view. Consequently `VISTA0032` (write target is a key or concurrency token)
never fires, and the generated `WriteMapper` **mass-assigns the primary key**.

The severity comes from the asymmetry: `.Key("Id")` is guarded, `.Key(nameof(Row.Id))` is not. The safer,
refactor-friendly spelling is the unsafe one, and nothing warns.

**Fix.** Resolve the argument as a compile-time constant via the semantic model
(`SemanticModel.GetConstantValue`) instead of pattern-matching syntax, which handles `nameof`, `const`
fields, and string concatenation of constants in one step.

---

### SEC-06 — Path traversal in the TypeScript client generator's output paths

| | |
|---|---|
| **Severity** | High |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.Client.TypeScript/Emit/ViewClientEmitter.cs:711`, `src/a2n.Vista.Client.TypeScript/Write/OutputWriter.cs:175` |

`FileName` kebab-cases upper-case letters and copies **everything else verbatim**:

```csharp
if (char.IsUpper(c)) { /* ... insert '-', lower-case ... */ }
else { builder.Append(c); }   // '.', '/', '\' pass through unchanged
```

The view name it receives is derived from the OpenAPI document without validation
(`Modeling/OperationGraphBuilder.cs:410` — `DeriveViewName` returns `operationId[..^tail.Length]`). The
emitted relative path is then joined to the output root with no containment check:

```csharp
private static string ToHostPath(string root, string relativePath)
{
    string native = relativePath.Replace('/', Path.DirectorySeparatorChar);
    return Path.Combine(root, native);
}
```

An `operationId` of `../../../evil_list` therefore writes `evil.ts` three directories above `--out`, and
the staging phase's `CreateDirectory` creates the outside directory first.

**Threat model.** The OpenAPI document is external input — the generator accepts one over HTTPS. A
malicious or compromised document server gains file write outside the intended output directory on the
developer's or CI machine.

**Fix.** Two independent guards, both worth having:

1. Reject a view name that does not match `[A-Za-z_][A-Za-z0-9_]*` as a typed model-stage error.
2. Assert in `ToHostPath` that the resolved full path stays under `Path.GetFullPath(root)`.

**Related.** `ViewClientEmitter.cs:714` calls `ArgumentException.ThrowIfNullOrEmpty(viewName)`, which is
not caught anywhere in `PipelineRunner`/`CliHost`. A document with a path of `/list` and no `operationId`
produces an empty view name and crashes the CLI with an unhandled exception, contradicting the contract
that every fatal cause surfaces as a typed `GenerationError`.

---

## 2. Correctness findings

### BUG-01 — A typed filter value on a `Guid`/`DateTimeOffset`/`DateOnly`/`TimeOnly` field returns 500, not 400

| | |
|---|---|
| **Severity** | High |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.Core/Filter/FilterCompiler.cs:546` (and `:559`, `:566`, `:573`) |

`Coerce` returns the value **unchanged** when the input is not a string for four target types:

```csharp
if (underlying == typeof(Guid))
{
    return value is string guidText ? Guid.Parse(guidText) : value;   // non-string falls through
}
// same shape for DateTimeOffset, DateOnly, TimeOnly
```

The type mismatch therefore escapes the `try`/`catch` that converts conversion failures into
`FilterValidationException`. The caller is:

```csharp
private Expression ConstantFor(FilterLeaf leaf, Type underlying, Type memberType) =>
    MakeConstant(Coerce(leaf.Value, underlying, memberType, leaf), underlying, memberType);
```

and `MakeConstant` — outside `Coerce`'s try/catch — calls `Expression.Constant(value, underlying)`, which
throws `ArgumentException("Argument types do not match")`. `VistaProblemResults.TryCreate` maps
`ArgumentOutOfRangeException` but **not** `ArgumentException`, so the exception is unrecognized and
surfaces as **HTTP 500** instead of the documented 400.

Reproduction shape: `{"field":"OrderId","op":"Equals","value":42}` where `OrderId` is a `Guid`. The same
path is reachable from the EF layer via `FilterCompiler.CoerceValue` (used for Detail-by-key coercion), and
`Between` reaches it twice via `bounds[0]`/`bounds[1]`.

Note the contrast: `DateTime` correctly falls through to
`Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture)`; the other four do not.

**Fix.** Replace each `: value` fall-through with `Convert.ChangeType(...)` (matching the `DateTime` arm) so
a genuine mismatch throws inside the guarded block and becomes a `FilterValidationException` → 400.

---

### BUG-02 — Adapter paging converts an offset to a page index, then clamps page size independently

| | |
|---|---|
| **Severity** | High |
| **Status** | Verified |
| **Location** | `src/Adapters/a2n.Vista.Adapters.DataTablesNet/DataTablesAdapter.cs:61`, `src/Adapters/a2n.Vista.Adapters.AgGrid/AgGridAdapter.cs:110` |

Both adapters derive a page index from the client's requested page size:

```csharp
// DataTables
var pageSize = request.Length;
var page = request.Length > 0 ? request.Start / request.Length : 0;

// AG Grid
var pageSize = request.EndRow - request.StartRow;
var page = pageSize > 0 ? request.StartRow / pageSize : 0;
```

`EfViewExecutor.ResolvePageSize` then clamps the size **after** the page index was already computed from
the unclamped value:

```csharp
return Math.Min(requested, limits.MaxPageSize);
```

With the default `HardLimits.MaxPageSize` of 100, a DataTables request of `length=200&start=200` yields
`page = 1`, `pageSize = 100`, so `skip = 100`: the client asked for rows 200–399 and receives rows 100–199,
labelled as 201–400 by the response's own echo. The same applies to an AG Grid block of
`startRow=1000,endRow=2000`.

Integer division is separately lossy for an unaligned offset: `start=250&length=100` → `page = 2` →
`skip = 200`, silently snapping the window by 50 rows.

**Impact.** Wrong data returned with no error, and a duplicated/skipped-row window that a grid's infinite
scroll will happily render as if correct.

**Fix — needs a decision first.** Two coherent options:

1. Carry an absolute row offset on `ViewQueryRequest` alongside (or instead of) the page index, so no
   division happens and clamping the size cannot move the window.
2. Keep the page model and make the engine **reject** an oversized `pageSize` rather than clamp it, so the
   mismatch becomes a 400 instead of wrong data. Also reject `offset % pageSize != 0`.

Option 1 is the better long-term contract (grids are offset-based, not page-based); option 2 is the smaller
change. Either touches the neutral request contract, so this should be a decision record.

**Related, same file.** `DataTablesAdapter` never validates `Start`: `start=-10&length=10` gives
`page = -1`, which `EfViewExecutor` silently rewrites to 0. `AgGridAdapter.cs:68` does validate its
equivalent, so the two adapters disagree. Throw `AdapterBindException` for `Start < 0`.

---

### BUG-03 — Write endpoints bind, read the key, and gate 428 before authorizing

| | |
|---|---|
| **Severity** | High |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.AspNetCore/Routing/VistaEndpointRouteBuilderExtensions.cs:481` (update), `:505` (delete) |

Authorization happens inside `ViewRequestExecutor.UpdateAsync`/`DeleteAsync`/`CreateAsync`, but the handler
does substantial work first:

```csharp
var model = VistaWriteBinding.BindModel(body, view.CrudType!);
var key = VistaWriteBinding.ReadKey(body);
var hasToken = ViewDeclaresConcurrencyToken(http, view.Name);
var ifMatch = ResolvePrecondition(http, hasToken);        // throws 428 — still pre-auth

var updated = await executor.UpdateAsync(...);            // authorization starts HERE
```

An unauthorized caller therefore receives `428 write-precondition-required` (or a `400` bind error) instead
of `403`. That response discloses three facts about a view the caller may not access: it exists, it is
writable, and it declares a concurrency token. It also lets an unauthenticated client force JSON parsing
and model binding work.

**Fix.** Authorize the facet before binding. Either split `AuthorizeAndShapeAsync` into a public pre-step
the mapper calls first, or move binding into the executor behind the authorization gate. The former keeps
the mapper dumb, consistent with the existing design intent.

**Related, same area.** `HandleAdapterAsync` (`:302`) has the same shape: `AdapterRequestFactory.CreateAsync`
(query + `ReadFormAsync` + full body read), `adapter.BindRequest`, and `adapter.ToQuery` all run before
`executor.ListForAdapterAsync` authorizes, so a bind failure returns `400 adapter-bind-failed` to an
unauthorized caller and distinguishes an existing view from a nonexistent one.

---

### BUG-04 — Optimistic concurrency is a non-atomic read-then-compare

| | |
|---|---|
| **Severity** | High |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.EntityFrameworkCore/Execution/EfViewExecutor.cs:1400` |

`EnforceConcurrencyToken` loads the row, formats its token, and compares it to `If-Match` in application
code:

```csharp
var storedToken = ReadConcurrencyToken(facet, entity);
if (!string.Equals(storedToken, expectedToken, StringComparison.Ordinal))
{
    throw new VistaConcurrencyConflictException();
}
```

Nothing here participates in the `UPDATE ... WHERE` predicate, so two concurrent requests can both pass the
check and both save — a lost update. The only atomic guard is the `DbUpdateConcurrencyException` handler in
`SaveWriteChangesAsync` (`:1094`), and EF Core raises that **only** when the property is configured
`IsRowVersion()` / `IsConcurrencyToken()` in the model.

`WithConcurrencyToken(e => e.Version)` is a Vista-level selector with no coupling to the EF model, and Vista
never verifies that the selected member is actually a model concurrency token. A view can therefore declare
a token, pass every Vista-level check, and have no database-level protection at all.

**Fix.** Two parts:

1. At startup, assert the selected member `IsConcurrencyToken()` in the `DbContext` model and fail closed
   when it is not — the same shape as the existing D105 key-derivation hook.
2. Set `entry.OriginalValues[token] = expected` so the token lands in the generated `UPDATE ... WHERE` and
   the check becomes atomic in the database.

---

### BUG-05 — Success `ETag` echoes the client's `If-Match` instead of the post-write token

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.AspNetCore/Routing/VistaEndpointRouteBuilderExtensions.cs:544` |

```csharp
private static IResult WriteOk(HttpContext http, bool hasToken, string? token)
{
    if (hasToken && token is not null)
    {
        http.Response.Headers.ETag = token;     // token == the request's If-Match value
    }

    return Results.Ok();
}
```

The `IViewExecutor` update/delete facet returns `bool`, so the endpoint has no post-write token to report
and round-trips the request value. Consequences:

- For a store-generated `rowversion`, the returned `ETag` is stale the instant it is sent, so the client's
  next update is **guaranteed** to 409.
- A delete emits an `ETag` for a row that no longer exists.

`EfViewExecutor.cs:1122` and `:1411` document a token read-back for exactly this round-trip, but
`ReadConcurrencyToken` is only ever called from `EnforceConcurrencyToken` — the read-back does not exist.

**Fix.** Surface the post-write token through the port (return a small result record instead of `bool`, or
add an `out`/`ref` token parameter), and have `WriteOk` emit that. The `WriteOk` remarks already identify
this as a known gap.

---

### BUG-06 — Raw `System.Text.Json` exception text leaks internal type names in write errors

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.AspNetCore/Execution/VistaWriteBinding.cs:178` |

```csharp
catch (JsonException ex)
{
    throw new VistaInvalidRequestException(
        $"The write 'model' payload is not valid: {ex.Message}", WriteErrorCode.MalformedBody);
}
```

STJ conversion messages embed the target CLR type and the member path — for example
`The JSON value could not be converted to System.Int32. Path: $.Model.Quantity`. That string becomes the
RFC 7807 `detail`, so a `400` response discloses internal type names and the write model's member layout.
Combined with `BUG-03`, this is reachable **before authorization**.

`VistaProblemResults` states in its own remarks that write messages are "leak-free by contract (no stack
traces, exception type names, SQL text, schema/object names, …)". This violates that contract. The same
pattern appears at `VistaWriteBinding.cs:72` and `VistaEndpointRouteBuilderExtensions.cs:398`
(`ReadBodyAsync`), where the leak is narrower (line/position only).

**Fix.** Emit a fixed, Vista-authored message plus the stable `code`, and log `ex` server-side only.

---

### BUG-07 — Style A projections run untracked-by-accident, and masking mutates rows in place

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Verified (code paths); **Unverified** (persistence consequence) |
| **Location** | `src/a2n.Vista.EntityFrameworkCore/Execution/EfViewExecutor.cs:274`, `src/a2n.Vista.Core/Metadata/MaskApplier.cs:259` |

There is no `AsNoTracking()` anywhere on the read path — a grep for `AsNoTracking` across
`src/a2n.Vista.EntityFrameworkCore` matches only test files. DTO projections track nothing, so this is
usually harmless. But an entity-bearing projection (`SplitViewExecutionPlan<T,T>` with `x => x`, or a
Style A `AddView(name, (db, sp) => db.Set<Entity>())`) yields **tracked** entities.

`MaskApplier`'s reflection accessor then writes the masked value back into that same instance:

```csharp
return new MaskAccessor(
    fieldName,
    property.GetValue,
    (row, value) =>
    {
        property.SetValue(row, value);
        return row;
    });
```

A subsequent `SaveChanges` on the same request-scoped context — the context the write path uses by design —
would persist the mask. I did not construct this scenario, so the persistence outcome is **unverified**; the
two contributing code paths are verified.

A second, independent defect in the same accessor: the guard at `MaskApplier.cs:244` throws
`MaskingException` when `property.SetMethod is null`. Anonymous types have get-only properties, so the
fallback advertised as "the RUC path for Style A" **cannot mask an anonymous row at all**. The method's own
summary claims it returns "a rebuilt instance for record rows", but no `with`-rebuild occurs on this path.

**Fix.** Add `.AsNoTracking()` in the execution plans (or in `MaterializeAsync`), and make the reflection
mask clone-and-return rather than mutate — or fail fast with a clear message when the row type cannot be
masked non-destructively. Correct the doc comment either way.

**Fixed (tranche 4, D147).** In the execution plans, not `MaterializeAsync`: the executor's `TRow` carries no
`class` constraint and the plan seam is type-erased, so there is no typed choke point downstream. With reads
untracked the in-place mask can no longer reach `SaveChanges`, so cloning was not needed for mutable rows —
what *was* needed is the get-only case, where the mask now rebuilds the row through its constructor. Note
`SplitViewExecutionPlan` could not actually hit the identity-projection case (Style B rejects `x => x`), so
the realistic vector was the Style A one the finding names.

---

### BUG-08 — OpenAPI component schemas are keyed by simple type name

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.OpenApi/Schema/DtoSchemaGenerator.cs:153` |

```csharp
var name = ComponentName(type);                                    // returns type.Name
if (_components.ContainsKey(name) || _inProgress.Contains(name))
{
    return name;                                                   // no type-identity check
}
```

Two views whose row types are `Sales.OrderRow` and `Purchasing.OrderRow` both resolve to
`#/components/schemas/OrderRow`. The second view's operations then document the **first** view's shape, and
a generated client mis-types its responses accordingly. The failure is silent — no notice, no collision
error.

**Fix.** Key `_components` by `Type`, and derive a unique component name, appending a namespace-derived
suffix on collision.

---

### BUG-09 — `Key(...)` does not satisfy the write facet's primary-key requirement

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.Core/Authoring/ViewBuilderOfTCrud.cs:74` |

`ValidateWriteFacet` receives the resolved key fields and ignores them:

```csharp
private protected override void ValidateWriteFacet(
    string viewName, bool hasPrimaryKey, IReadOnlyList<string> keyFields)   // keyFields unused
{
    // ...
    if (!hasPrimaryKey)
    {
        throw new InvalidOperationException(
            $"View '{viewName}' has a write facet and therefore requires a primary key; mark one " +
            "projected field with .PrimaryKey() (R4.4).");
    }
}
```

`hasPrimaryKey` is derived solely from `IFieldBuilderState.IsPrimaryKey` — that is, from a
`.Field(x => x.Id, f => f.PrimaryKey())` call. A writable Style B view that declares its key with the
view-level `Key(x => x.Id)` override — the documented path for join and union views under D104/D105 —
therefore **fails at startup** even though `ResolveKeyFields` already produced `["Id"]` and passed it in.

Same "two spellings, one broken" hazard as `SEC-05`.

**Fix.** Gate on `keyFields.Count != 0`, which already subsumes the `.PrimaryKey()` case because
`ResolveKeyFields` derives from it. The unused parameter is the direct evidence of the omission.

---

### BUG-10 — `ViewMetadata` record equality is broken by its mutable key state

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Verified (defect); **latent** (no current consumer) |
| **Location** | `src/a2n.Vista.Core/Metadata/ViewMetadata.cs:46` |

`ViewMetadata` is a `record`, and a record's synthesized `Equals`/`GetHashCode` compare **every instance
field**, including private ones. Three were added outside the primary constructor:

```csharp
private readonly object _keyFieldsGate = new();
private IReadOnlyList<string> _keyFields = [];
private bool _keyFieldsCompleted;
```

`_keyFieldsGate` is a fresh `object` per instance, so:

- two `ViewMetadata` with identical content are **never** equal;
- `GetHashCode` is an identity hash, unstable across runs;
- `CompleteKeyFields` mutates `_keyFields` after construction, changing the instance's hash code while it
  could be in a hash-based collection.

I verified that nothing currently keys a dictionary or set on `ViewMetadata`, so this is latent rather than
actively firing. It remains a trap on a type documented as an immutable snapshot.

**Fix.** Move the mutable key state into a separate non-record holder referenced by the record, or override
`Equals`/`GetHashCode` explicitly to compare only the logical content.

**Fixed (tranche 4, D148)** — the second option. The holder was rejected: a record's `with` uses a
compiler-generated copy constructor that copies fields, so a clone would **share** the holder and completing
one clone's key would silently complete the original's. A hand-written copy constructor avoids that but must
enumerate every property by hand, which is a maintenance trap on a positional record. Explicit
`Equals`/`GetHashCode` has neither problem, and it also fixed the reference-only `Fields` comparison the
synthesized version performed.

---

### BUG-11 — CSV export has no formula-injection defence

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.Core/Export/CsvViewExportWriter.cs:85` |

`Escape` implements RFC 4180 quoting only:

```csharp
private static readonly char[] QuoteTriggers = [',', '"', '\r', '\n'];
```

A cell value beginning with `=`, `+`, `-`, `@`, tab, or CR is emitted verbatim and is interpreted as a
formula when the file is opened in Excel or Google Sheets. Because views export arbitrary user-entered
column data, this is a stored-payload vector — notable on a library whose stated posture is
secure-by-default.

**Fix.** Prefix such values with `'` (or a leading tab) before `Escape`. This is the standard mitigation and
does not affect RFC 4180 conformance.

**Related.** `XlsxViewExportWriter.cs:139` (`AppendInlineString`) uses `SecurityElement.Escape`, which
handles `< > & ' "` but does **not** strip XML-illegal control characters (U+0000–U+0008, U+000B, U+000C,
U+000E–U+001F). A row value containing one produces a worksheet part that is not well-formed XML, so Excel
rejects the entire workbook. Filter or `_x####_`-escape them, as OpenXML does.

---

### BUG-12 — A negated empty QueryBuilder group inverts into "no filter"

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Verified |
| **Location** | `src/Adapters/a2n.Vista.Adapters.DataTablesNet/QueryBuilderParser.cs:62` |

```csharp
if (children.Count == 0)
{
    return null;              // returns before Not is applied
}

FilterNode group = /* ... */;
return node.Not ? new FilterNot(group) : group;
```

For `{"not":true,"condition":"AND","rules":[]}` the method returns `null`, so no filter is applied at all.
The compiler treats an empty `AND` as vacuously true (`FilterCompiler.Combine`, `seed: true`), so the
correct result is **zero rows** — but the client receives the entire unfiltered set.

**Fix.** Return `FilterAnd([])` and let the `Not` wrapper apply, or reject an empty `rules` array as an
`AdapterBindException`.

---

### BUG-13 — The net10 OpenAPI transformer branch drops `additionalProperties`

| | |
|---|---|
| **Severity** | Low |
| **Status** | Verified |
| **Location** | `src/a2n.Vista.OpenApi/AspNetCorePipeline/VistaOpenApiDocumentTransformer.cs:407` |

The net9 branch maps it (`:491`):

```csharp
if (schema.AdditionalProperties is not null)
{
    result.AdditionalPropertiesAllowed = schema.AdditionalProperties.Value;
}
```

The `NET10_0_OR_GREATER` branch has no equivalent. The merged `ProblemDetails` schema (authored with
`AdditionalProperties = true`) and every dictionary-shaped schema therefore lose their open-map semantics on
net10, so the same application emits a different document depending on its target framework — which
undercuts the determinism guarantee the emitter is built around.

**Fix.** Set the Microsoft.OpenApi 2.x equivalent in the net10 branch.

---

## 3. Dead code

Each item was cross-checked with a grep across all of `src/` — including `src/Tests`, `src/Examples`,
`src/Adapters`, and the source generators (whose emitted output references Core members **by name inside
string literals**, so generator `.cs` files were grepped for the member names too).

> ### Method correction (added 2026-07-31, during tranche 5)
>
> **This section's method establishes that a member is _unreferenced_, not that it is _dead_.** The two are
> not the same: an acceptance criterion can require a member to exist as an extension point without anything
> in-tree calling it, and a published API sketch can declare a member whose behaviour was never specified.
> The grep was never cross-checked against `.kiro/specs/*/requirements.md`, so this section mislabelled at
> least one required-but-unimplemented feature as dead code.
>
> Before acting on any `DEAD-*` item, classify it against the requirements:
>
> 1. **An acceptance criterion covers it** → it is an *implementation gap*, not dead code. Implement it, or
>    record the gap explicitly. Removing it silently drops a requirement.
> 2. **A design/spec surface declares it, no acceptance criterion defines its behaviour** → a deliberate
>    skeleton. Removal is a *scope decision* for the owner, and must reconcile the spec in the same change.
> 3. **Nothing in requirements, design, or tasks covers it** → genuine leftover. Safe to remove.
>
> The per-item reclassification is recorded on each finding below and summarised in the tranche 5 note.
> `DEAD-04` and `DEAD-05` were already fixed in tranche 1/2 and are unaffected — both were behavioural
> defects, not API-surface questions.

### DEAD-01 — `IViewRegistry.Register<TView>()` is a public member that always throws

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/a2n.Vista.Core/Ports/ViewRegistry.cs:40` + `src/a2n.Vista.Core/Ports/IViewRegistry.cs` |

```csharp
throw new NotSupportedException(
    "Register<TView>() requires the reflection authoring path, which is not implemented yet. " +
    "Register views by adding their built metadata via Add(ViewMetadata) until the authoring " +
    "builders (Tasks 6/7) or the source generator (Pilar 3) are available.");
```

The comment's premise is obsolete — the authoring builders and the source generator both landed. Grepping
`egistry.Register<` across `src/` returns no matches; the `Register<TView>()` used throughout the tests is
`IVistaBuilder.Register<TView>` on the DI builder, a different type.

**Fix.** Remove the member from `IViewRegistry` (technically breaking, but it can never have worked), or
implement it as a thin wrapper over the authoring path.

**Reclassified (tranche 5) → category 1, then superseded.** `pilar-1-core/tasks.md` 4.3 specifies
`IViewRegistry` as "(`Register<TView>`, hook template, `Get`, `All`); jalur reflection
`[RequiresUnreferencedCode]`" and is **ticked `[x]`** although the member only throws; requirement 1.2 also
names `Register<TView>` as a registration entry. So this is a task marked done that was never implemented —
not a leftover. What settles it is **D101/D103**: registration now owns route composition, and a Core-level
`Register<TView>` would produce a view whose `Route` is the bare name, i.e. a broken view. The correct
disposition is removal justified as *superseded by D101/D103*, with the tasks.md tick corrected — **not** as
dead code. Still pending an owner scope call.

---

### DEAD-02 — The `Format(...)` field-builder feature is entirely inert

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/a2n.Vista.Core/Authoring/IFieldBuilder.cs:50`, `FieldBuilder.cs:53`/`:78`, `IFieldBuilderState.cs:34` |

`Format(string)` validates its argument and stores `_format`; `FormatString` exposes it; **nothing reads
it**. `FieldMetadata` has no format member, and a repo-wide grep for `FormatString` finds only the
declarations plus one `<see cref>`. An author who writes `.Format("N2")` gets silent data loss — the call
compiles, validates, and has no effect anywhere in the metadata endpoint, the OpenAPI document, or the
exporters.

**Fix.** Either carry it onto `FieldMetadata` and through to the metadata/OpenAPI/TS-client surfaces, or
delete the three members. Silent no-op is the worst of the three options.

**Reclassified (tranche 5) → category 2. Fixed by carrying it (D149).** `docs/spec/01-view.md` §5.2 declares
`IFieldBuilder<TProp> Format(string formatString)` on the authored surface, so this is a designed member, not
an accident — and it is the successor of DynData's `DataFormatString`, which D98 says Style A preserves. No
acceptance criterion defines *who applies* the format, so that is the part that needed deciding: **the server
publishes it, the client applies it** (D149). `FieldMetadata.Format` now carries the hint, the metadata facet
publishes it, and the emitted OpenAPI schema declares it optional. Vista never interprets it, so filter, sort,
and export keep operating on raw values — a format hint cannot change what a query matches or what an export
contains. Purely additive, and the response omits the member when unset, so a view that sets no hint has a
byte-identical `/metadata` payload (verified: 1537 bytes before and after). The TypeScript client is
deliberately untouched — it types wire DTOs, not metadata.

---

### DEAD-03 — `CrudOn<TEntity>(projectionForRead)` discards its parameter

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/a2n.Vista.Core/Authoring/ViewBuilderOfTCrud.cs:30` + `IViewBuilder.cs:154` |

```csharp
public ICrudBuilder<TQuery, TCrud, TEntity> CrudOn<TEntity>(
    Expression<Func<TEntity, TQuery>>? projectionForRead = null)
    where TEntity : class
{
    var state = new CrudFacetState(typeof(TCrud), typeof(TEntity));   // parameter never used
    _crudState = state;
    return new CrudBuilder<TQuery, TCrud, TEntity>(state);
}
```

The parameter is documented as "an optional read-back projection … used after a write". A developer who
supplies one silently gets default behaviour.

**Fix.** Remove the parameter, or capture it on `CrudFacetState` and honour it in the write read-back.

**Reclassified (tranche 5) → category 2. Not removed; pending an owner scope call.** The parameter appears
verbatim in `docs/spec/01-view.md` §5.2 (the `CrudOn<TEntity>(Expression<Func<TEntity, TQuery>>? = null)`
signature), so it is a designed skeleton. But a grep for `read-back|projectionForRead` across the entire
`write-path` spec returns **nothing**: the signature was designed, the behaviour never specified. Removing it
is therefore a scope decision (is post-write read-back in this release?), and if taken it must reconcile
`01-view.md` §5.2 in the same change. A tranche-5 removal was drafted and **reverted** for this reason.

---

### DEAD-04 — `HardLimits.AbsoluteMaxExportRows` is never enforced

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/a2n.Vista.Core/Metadata/HardLimits.cs:24` |

```csharp
/// <summary>Absolute export cap that cannot be bypassed by per-view configuration (§11.2).</summary>
public const int AbsoluteMaxExportRows = 1_000_000;
```

A repo-wide grep finds no reference outside the declaration. Both builders only call
`ThrowIfNegativeOrZero`, so a view can set `MaxExportRows(int.MaxValue)` — and given `PERF-01` below (the
export path buffers everything in memory twice), the unenforced cap is a denial-of-service lever, not just
a documentation inaccuracy.

**Fix.** Clamp to `AbsoluteMaxExportRows` in both `MaxExportRows` overloads, or in the `HardLimits`
constructor.

---

### DEAD-05 — DataTables binds `searchable`, `orderable`, and `search[regex]`, then ignores them

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/Adapters/a2n.Vista.Adapters.DataTablesNet/DataTablesModels.cs:45`, `:57`, `:60`, `:96` |

`DtColumn.Searchable`, `DtColumn.Orderable`, `DtSearch.Regex`, and `DataTablesResponse<T>.Error` are bound
or declared and never read anywhere in `src/`. The consequences are behavioural, not merely tidiness:

- a column declared `searchable:false` still receives a `Contains` leaf (`DataTablesAdapter.cs:151`);
- a column declared `orderable:false` is still sorted (`:98`);
- `regex=true` is executed as a literal `Contains`.

Each is **silently wrong** rather than rejected — the adapter accepts a request whose stated semantics it
does not honour.

**Fix.** Honour the two flags, and reject `regex=true` with `AdapterBindException` (regex filtering is not
part of the neutral contract).

---

### DEAD-06 — `VistaBuilder.RegisterAssembly` produces permanently non-executable views

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/a2n.Vista.EntityFrameworkCore/DependencyInjection/VistaBuilder.cs:185` |

The method registers **metadata only**: no `GeneratedExecutionPlanStore` drain, no `RegisterMaskSpecs`, no
`RegisterWriteFacet`, no `_contextAccessor.Capture`. Scanned views therefore become route-bearing and
discoverable while remaining permanently non-executable — the executor throws "no generated execution plan"
at request time (`EfViewExecutor.cs:1512`).

It is never invoked anywhere in `src/` and has no test coverage.

**Fix.** Route it through the same body as `Register<TView>()`, or remove it until it can be completed.
Shipping a public API that yields discoverable-but-broken endpoints is worse than not shipping it.

**Reclassified (tranche 5) → category 1. Fixed by completing it.** `pilar-1-hardening` requirement **3.1**
lists `RegisterAssembly` as a peer of `RegisterTemplate`/`Register<TView>` under `RouteGroup`, and **3.7**
only adds the `[RequiresUnreferencedCode]` marking — **no requirement says "metadata-only"**. That phrase came
from `PROJECT-STATUS.md` §4's summary of D103, where a description of the half-finished implementation read
like a decision (now corrected there). So this was an under-implementation of R3.1, and the fix is the first
option: `Register<TView>()` and `RegisterAssembly` now share one private `RegisterSource` body, so a scanned
view gets metadata, mask specs, the write facet, and the generated execution plan on identical terms. It also
had **no test coverage**; it now has one, driven by a dedicated deterministic scan-target assembly
(`a2n.Vista.Examples.AssemblyScanTarget`) — the main test assembly cannot be its own scan target because it
holds fixtures that deliberately fail at metadata build time to exercise the startup guards.

---

### DEAD-07 — `VistaOpenApiOptions.IncludeAdapterEndpoints` does nothing

| | |
|---|---|
| **Severity** | Low |
| **Location** | `src/a2n.Vista.OpenApi/VistaOpenApiOptions.cs:56` |

The only reference outside its declaration is a test asserting it is `false`. A host can set it to `true`
and observe no change.

**Fix.** Wire it, or mark it `[Obsolete]`/remove until the adapter-documentation phase lands.

**Reclassified (tranche 5) → category 1. This finding was wrong; the member must not be removed.**
`openapi-emitter` requirement **12.2** states: "THE OpenApi_Emitter SHALL expose an extension point through
which adapter documentation MAY be contributed in a later phase, without requiring a change to the core
builder." A `bool` that nothing reads is not an extension point, so the real defect is that **R12.2 is
unimplemented** — the design doc reduced it to a flag ("extension hook") and the code kept the flag. Note also
that the test asserting `IncludeAdapterEndpoints == false` is not "the only reference": R12.1 is validated by
Property 10 (adapter endpoints absent from the v1 document), which passes. R12.1 is satisfied; R12.2 is not.
A tranche-5 removal was drafted and **reverted**. The correct fix is to implement a real contribution point
and let the flag either gain a reader or become unnecessary.

---

### DEAD-08 — TypeScript client: `--base-url` is parsed and ignored, plus a superseded CLI branch

| | |
|---|---|
| **Severity** | Low–Medium |
| **Location** | `src/a2n.Vista.Client.TypeScript/Pipeline/PipelineRunner.cs:38`, `Cli/CommandLine.cs:168`, `Cli/CliHost.cs:76` |

`CommandLine` sets `DefaultBaseUrl` and `GenerationConfig` documents it as "baked into the generated
client's default constructor argument", but `RunAsync` never reads it. The contradiction is explicit in the
test suite: `NoEmbeddedCredentialPropertyTests.cs:217` asserts the value must **not** appear in any
generated file. So the option, its documentation, and its test pull in three different directions.

Separately, `CliHost.cs:76` retains an unreachable branch:

```csharp
// The pipeline is wired in task 12.2. Until then, a valid configuration cannot be
// executed; fail cleanly with a nonzero exit rather than pretending to succeed.
stderr.WriteLine("The generation pipeline is not yet wired (pending task 12.2).");
```

`Program.cs:16` always passes a `PipelineRunner`, and every test passes a recording runner.

**Fix.** Remove the `--base-url` option and its documentation (or implement it), and make the `runner`
parameter non-nullable so the dead branch disappears.

**Reclassified (tranche 5) → category 3, plus a specification defect. Not removed; pending an owner scope
call.** `typescript-client` requirement **10** (CLI invocation and configuration) does not mention a base URL
at all — only the source location, the output directory, and the write-facet flag. The option appears only in
the requirements *glossary* ("transport/base-URL defaults") and in `design.md`, which describes it as "baked
into the generated client's default ctor arg". That description **contradicts** requirement 7.1 as encoded in
Property 20 (a supplied `DefaultBaseUrl` must never appear in any generated file) and requirement 6.3 (the
client accepts a base URL **at construction**). So the option cannot be implemented as designed without
breaking a tested security requirement. Removal is the right call, but it must reconcile the glossary and
`design.md` in the same change — which is why a tranche-5 removal was drafted and **reverted** pending that
decision. `DefaultTransportHint` (same file, "reserved; fetch-backed default is emitted regardless") is the
same category but was **not** part of this audit finding.

---

### DEAD-09 — Duplicated recognition and emission logic across the five source generators

| | |
|---|---|
| **Severity** | Low–Medium |
| **Location** | `src/a2n.Vista.SourceGenerators/` |

Duplicated helpers: 4× `FindViewBase` / `IsRecognizedViewDefinition`, 3× `IsNamedContractType`, 2× `Unwrap`
and `Literal`, and 5× hint-name builder. Four generators re-run identical view recognition; two re-run the
full DTO shape analysis.

This is already causing real divergence: **the accessor-map emitter has drifted between
`ViewAccessorGenerator` and `StyleAShapeGenerator` on key escaping.** That is the failure mode duplication
predicts, and it is present today.

Also unreferenced: `WriteMapperModel.HasCrudFacet` (tautologically true, never read),
`DtoMemberModel.ShapeKind` / `MemberShapeKind` (computed, never consumed), and `ShouldEmitMapper` — marked
internal-for-tests, but there is no `InternalsVisibleTo` for the generator assembly anywhere in the repo.

**Fix.** Extract the shared recognition and emission helpers into one internal static class per concern, and
re-verify the two accessor-map emitters produce byte-identical output (the standing parity guard should
cover this).

---

## 4. Performance findings

### PERF-01 — Export buffers the payload twice in memory, on top of a fully materialized row set

| | |
|---|---|
| **Severity** | High |
| **Location** | `src/a2n.Vista.AspNetCore/Routing/VistaEndpointRouteBuilderExtensions.cs:346` |

```csharp
var buffer = new MemoryStream();
await writer.WriteAsync(buffer, resolvedView, rows, http.RequestAborted).ConfigureAwait(false);
buffer.Position = 0;

return Results.File(buffer.ToArray(), /* ... */);
```

`ExportRowsAsync` has already buffered up to `view.Limits.MaxExportRows` rows (default 100,000) as
`IReadOnlyList<object?>`. The writer then fills a growing `MemoryStream`, and `ToArray()` copies the whole
payload **again**. Peak memory is roughly 2× the file size plus the boxed row list, per concurrent export —
a straightforward large-object-heap and OOM vector. `DEAD-04` (the unenforced absolute cap) removes the
intended ceiling.

**Fix.** Immediate win: `return Results.Stream(buffer, ...)` — `buffer.Position` is already 0, so this is a
one-line change that removes one full copy. Real fix: stream the writer directly to `http.Response.Body`
over a streamed row source.

---

### PERF-02 — The export reflection fallback does a member lookup per cell

| | |
|---|---|
| **Severity** | High |
| **Location** | `src/a2n.Vista.Core/Export/ExportColumns.cs:100` |

```csharp
public static object? Value(object? row, string name) =>
    row?.GetType().GetProperty(name)?.GetValue(row);
```

This runs **per cell**. For a Style A export of 100,000 rows × 10 columns that is one million
`GetProperty` lookups per request, uncached. Typed Style B views take the generated accessor and are
unaffected; Style A — the ergonomic authoring style the project deliberately preserves — takes the full cost.

**Fix.** Cache a `PropertyInfo` (better: a compiled getter delegate) per `(rowType, fieldName)` in a
`ConcurrentDictionary` on the fallback path.

**Fixed (tranche 3).** The `PropertyInfo` is memoized per `(row type, name)` — a `ConditionalWeakTable<Type,
ConcurrentDictionary<string, PropertyInfo?>>`, so a collectible row type is not rooted by the cache and a
name that does not exist on the type is looked up once rather than per row. The compiled-getter variant was
not taken: emitting a delegate over an anonymous (internal) row type runs into expression-compilation
visibility limits, and removing the per-cell name lookup is where the order of magnitude was.

---

### PERF-03 — The XLSX writer materializes the whole worksheet as a string, then copies it

| | |
|---|---|
| **Severity** | High |
| **Location** | `src/a2n.Vista.Core/Export/XlsxViewExportWriter.cs:72` |

`BuildSheetXml` accumulates all rows into a `StringBuilder` and returns `sb.ToString()`; `WriteEntryAsync`
then calls `Encoding.UTF8.GetBytes(content)`. At the default 100,000-row cap that is two large-object-heap
buffers holding the full document — the intermediate being UTF-16, so ~2× the byte size — on top of the
builder's own chunks.

**Fix.** Pass the `ZipArchiveEntry` stream into the builder and write through a `StreamWriter`, streaming
row by row.

**Related, same file.** `CellRef` (`:152`) allocates a `StringBuilder` **and** a `string` for every cell,
just to compute an A1 reference whose column part is constant within a row. Precompute the column letters
once into a `string[]` and append the row number directly to the output builder.

---

### PERF-04 — View authoring re-runs `Configure` four or more times per view

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/a2n.Vista.Core/Authoring/View.cs:145` (both `View<TQuery>` and `View<TQuery, TCrud>`) |

`_metadata` is cached only for the `Name` property. `IViewMetadataSource.BuildMetadata()`,
`GetMaskSpecs()`, `GetCrudFacetDefinition()`, and `GetSourceRowFilters<TSource>()` each construct a **fresh**
`ViewBuilder` and re-run `Configure` — four or more full builds per view, each including the projection walk
and field-metadata construction.

The correctness side-effect matters more than the cost: **the `ViewMetadata` published to the registry is a
different instance from the one `Name` reads.** With `BUG-10` (broken record equality) in the same area, this
is worth fixing for clarity alone.

**Fix.** Build once into a cached authoring result (metadata + masks + facet + row filters) and serve all
four members from it.

**Fixed (tranche 4).** The cached result is the configured `ViewBuilder` itself rather than a new record, so
the generic `GetSourceRowFilters<TSource>()` still resolves per `TSource` off the one authoring pass. The dead
`BuildMetadataCore` virtual went with it: its doc claimed an override by `View<TQuery, TCrud>`, which is
deliberately not a subclass (D26), so nothing could ever have overridden it.

---

### PERF-05 — `FilterCompiler` rebuilds the field lookup on every channel compile

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/a2n.Vista.Core/Filter/FilterCompiler.cs:172` |

`BuildFieldLookup` constructs a `Dictionary<string, FieldMetadata>` from `view.Fields` on every `Compile`
call, and `EfViewExecutor` compiles up to three channels per List request (scope, filter, search) — three
identical dictionary builds per request over data that is immutable after registration. The two grid
adapters each build a fourth.

**Fix.** Build the lookup once at registration and hang it off the execution plan, or memoize per
`ViewMetadata` via a `ConditionalWeakTable`.

**Fixed (tranche 3).** The `ConditionalWeakTable` option, exposed as `ViewFieldLookup.For(view)` in
`a2n.Vista.Core/Metadata`, and adopted by all four call sites — which also removes the duplicated builder
from both grid adapters. The result is a `FrozenDictionary`, so the shared lookup cannot be mutated through
a downcast.

---

### PERF-06 — Generators call `GetTypeByMetadataName` inside the per-node transform

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/a2n.Vista.SourceGenerators/ViewAccessorGenerator.cs:740`, `EmittableShapeAnalyzer.cs:180` |

The well-known type symbols are resolved per candidate node instead of once per compilation via
`CompilationProvider`. In an incremental generator this runs on every keystroke that touches a candidate
file, multiplied across the four class-based generators.

**Fix.** Resolve the symbols once from `CompilationProvider` and combine that into the transform.

The predicates themselves are clean — all five are cheap and syntax-only, with no semantic model access,
which is the more important property and is already right.

---

### PERF-07 — Metadata caching re-serializes and re-hashes on every request, including 304s

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/a2n.Vista.AspNetCore/Routing/VistaEndpointRouteBuilderExtensions.cs:255` |

```csharp
var json = JsonSerializer.Serialize(metadata, VistaJson.Options);
var etag = ComputeETag(json);                    // SHA-256 over the whole payload
```

`VistaMetadataResponse.From(view)` (LINQ `Where`/`Select`/`ToList` over all fields, one allocation per
field) also runs per request. Metadata changes only at registration time, so the "cache" currently saves the
client a download while costing the server the full serialization and hash — on the 304 path too.

**Fix.** Compute the JSON and ETag once per view into a `ConcurrentDictionary<string, (string Json, string
ETag)>`, reducing the 304 path to one string comparison.

**Fixed (tranche 3), in two layers.** `VistaMetadataResponse.From` memoizes the projection per
`ViewMetadata`, and the mapper memoizes the serialized payload + `ETag` keyed on **that response instance**
rather than on the view name. Instance keying matters: a name-keyed static cache would be shared by every
host in the process, so two test hosts registering the same view name could serve each other's bytes. The
shared response's field list is wrapped in a `ReadOnlyCollection` because it is no longer per-request. The
authorization pipeline is untouched — the facet is still authorized (and `ShapeQuery` still runs) on every
request, including the ones answered from cache.

---

### PERF-08 — Every AG Grid block fetch pays a discarded unfiltered `COUNT`

| | |
|---|---|
| **Severity** | Medium |
| **Location** | `src/Adapters/a2n.Vista.Adapters.AgGrid/AgGridAdapter.cs:139` |

The executor always issues two `LongCountAsync` calls (`EfViewExecutor.cs:409`, `:417`) plus the page query,
but `ToResponse` maps only `RecordsFiltered` and drops `RecordsTotal` by design (the server-side row model
has no slot for it). Three database round-trips where two suffice, on the hottest path in an
infinite-scroll grid.

**Fix.** Let the adapter signal that the unfiltered total is not needed (a flag on `ViewQueryRequest`, or a
distinct executor entry point) so the engine can skip the baseline count.

---

## 5. Verified sound

Recorded so the report is proportionate, and so these areas are not re-audited without cause.

| Area | Result |
|---|---|
| **Core static stores** | `ViewAccessorRegistry`, `ViewInvokerStore`, `GeneratedJsonContextStore`, `MaskSpecRegistry`, plus EF's `GeneratedWriteMapperStore` and `GeneratedExecutionPlanStore`, all back onto `ConcurrentDictionary<string, …>(StringComparer.Ordinal)` and register with `TryAdd`. The documented first-wins semantics genuinely hold, and concurrent registration/read is safe |
| **Incremental generator model hygiene** | No `ISymbol`, `Compilation`, `SyntaxNode`, `SemanticModel`, or `Location` captured on any pipeline model. `EquatableArray<T>` structural equality is correct. All five `WithTrackingName` values match `TrackingNames.cs`. Emission ordering is deterministic |
| **Generator diagnostics** | All 16 descriptor IDs and severities match `AnalyzerReleases.Unshipped.md`; no unused descriptors |
| **Generator predicates** | All five syntax predicates are cheap and syntax-only — no semantic model access in a predicate |
| **`FilterNodeJsonConverter` depth** | `JsonDocument.ParseValue(ref reader)` inherits `MaxDepth` from `VistaJson.Options` (default 64), so a deeply nested `FilterNode` is rejected before recursion can overflow the stack |
| **TypeScript client security posture** | Write facets are genuinely off without an explicit opt-in (`EmitAll(views, config.EmitWriteFacets)` → empty facet array; `EnvelopeCatalog` requires write envelopes only when the flag is set). No credential appears in any emitted file. Nothing logs a token |
| **TypeScript client HTTPS enforcement** | The emitted `runtime/url.ts` parses via the `URL` constructor and compares `hostname` against a fixed set after stripping IPv6 brackets — **not** a `Contains("localhost")` check, so `http://localhost.evil.com` is correctly rejected |
| **TypeScript client `$ref` handling** | Cyclic `$ref` cannot loop (`RefResolver.ValidateSchema` never follows a `$ref` edge); external and file refs are rejected as `Dangling`; parse recursion is bounded and surfaces as a typed `Malformed` error |
| **Acquire HTTP hygiene** | One `HttpClient` per run, disposed via `OwnedSource` (no socket exhaustion). .NET's redirect handler refuses HTTPS→HTTP downgrades by default. The 30-second budget is enforced by a linked `CancellationTokenSource` |
| **Sample hosts** | No hardcoded credentials (only `Data Source=../DB/northwind.db`), no CORS configuration at all, and `AllowAnonymousAccess()` used exactly as D94 intends with the rationale in a comment |

---

## 6. Explicitly not verified

Stated so these are treated as leads rather than conclusions:

- **`BUG-07` persistence outcome.** That an in-place mask on a tracked entity would be persisted by a
  later `SaveChanges` was not observed by executing code. Both contributing paths (no `AsNoTracking`;
  mutating mask accessor) are verified in source.
- **EF Core SQL parameterization.** `FilterCompiler` embeds values as `Expression.Constant`, which is
  expected to render as an inline SQL literal rather than a parameter — implying per-value query-cache
  churn and database plan-cache pollution driven by attacker-controlled input. This rests on the
  expression-construction code plus documented EF behaviour; **emitted SQL was not captured.** Worth a
  focused follow-up, because if confirmed it is a DoS lever.
- **`GeneratedJsonContextStore` drain ordering.** `VistaJson.cs:92` drains the store exactly once, inside
  the static `Options` initializer, while registration happens in per-assembly `[ModuleInitializer]`s. A
  view assembly first loaded *after* `Options` is initialized would never be chained, failing silently to
  the reflection fallback (or failing outright once `DisableReflectionFallback()` has been called). No
  late-loading view assembly exists in this repo, so **reachability in a shipped configuration is
  unverified**; the ordering hazard in the design is real.
- **`VistaOpenApiDocumentTransformer` activation lifetime.** That `AddDocumentTransformer<TTransformer>()`
  activates per document generation (making the instance-level `_vistaDocument` cache useless across
  requests) is per the framework's documented behaviour and was not verified by execution. The structural
  fact — the transformer bypasses `VistaOpenApiDocumentCache` and calls `_builder.Build()` directly — is
  verified.
- **`FilterCompiler` Scope-channel operator exemption.** The `Scope` channel checks `IsScopable` but not
  `AllowedOperators`, while the `Filter` channel checks both. Since `ExternalFilterParser` maps
  `externalFilter` to the Scope origin and emits `GreaterThan`/`LessThan`/`Contains`/`In`/`Between`, a
  `.Scopable()` field accepts operators its author restricted — and those leaves move `recordsTotal`.
  **Whether this asymmetry is deliberate under D111 was not determined** (the code was read, not the
  decision record). If unintentional, promote to a security finding.
- **`AgGridJsonContext` unmapped members.** `AgGridRowsRequest` omits `rowGroupCols`, `valueCols`,
  `groupKeys`, and `pivotMode`, and the context sets only `PropertyNameCaseInsensitive = true`, so STJ
  ignores unknown members by default. A grouped or pivoted request would therefore return flat rows rather
  than being rejected — in contrast to Advanced Filter, which is loudly rejected at
  `AgGridAdapter.cs:78`. Consider `UnmappedMemberHandling = Disallow`. **Not confirmed by execution.**
- **`EfViewExecutor`'s `protected FilterCompiler` property.** The usage grep was truncated, so it cannot be
  claimed unreferenced.
- **Export format matching and row clamping** in `a2n.Vista.AspNetCore` were outside the Core pass's scope
  and were not separately audited.
