# a2n.Vista — Project Status & Session Handoff

> Status: **LIVING DOCUMENT** — update as work proceeds.
> Last updated: 2026-06-21
> Purpose: a single, authoritative snapshot of *where the project is*, *what was decided*, and *what
> is next*, so a new chat/work session can continue without re-litigating settled decisions ("no
> dispute"). When this document and the code disagree, **the code is the source of truth**; reconcile
> the docs to the code, not the other way around.

---

## 0. How to use this document

- Read §1–§3 for the current state and what is implemented.
- Read §4 (**Settled decisions — do not re-litigate**) before proposing design changes. Each entry
  cites where the full rationale lives.
- §5 is the **decision-number map** (which spec owns which `D###`/`DR##`, what was superseded). Use the
  next free number when adding decisions.
- §6 is the **next-work plan** (Spec 02 gap analysis → `query-engine-hardening`).
- §7 is the **backlog / known gaps**.
- §8 has **build/test commands** (important: TUnit specifics).
- §9 has **key code locations**.

---

## 1. Project shape

`a2n.Vista` is a .NET library that exposes EF Core data as declarative, secure-by-default **Views**
(read + optional write) over HTTP, replacing/evolving the older `a2n.DynData` (a read-only reference
repo at `d:\GitHub\DynData`, **do not modify**; see `.kiro/steering/readonly-external-roots.md`).

Three pillars (see `ROADMAP.md`):
- **Pillar 1 — Core View engine** (View concept, neutral query/filter contract, EF execution, ASP.NET
  endpoints). **Implemented.**
- **Pillar 2 — Adapters + neutral query engine** (server half = query engine, partially built; client
  half = grid adapters, **not built**).
- **Pillar 3 — Source generator** (AOT-clean codegen). **Not built**; Pillar 1 uses a reflection path
  marked `[RequiresUnreferencedCode]`.

Multi-target: `net8.0;net9.0;net10.0`. Nullable enabled. Central Package Management. Test framework:
**TUnit**.

Packages / layering (Decision D48, enforced):
- `a2n.Vista.Core` — EF-free & HTTP-free. Contracts, metadata, authoring builders, ports
  (`IViewExecutor`, `IViewScope`, `IViewRegistry`), `FilterCompiler`.
- `a2n.Vista.EntityFrameworkCore` — implements `IViewExecutor` (`EfViewExecutor`), registration
  (`AddVista`/`IVistaBuilder`), provider-aware filter.
- `a2n.Vista.AspNetCore` — HTTP: endpoint mapping, `IViewAuthorizer`, error model. **No EF reference.**
- EF and AspNetCore do **not** reference each other; they meet at Core ports.

---

## 2. Implemented & verified

### 2.1 `pilar-1-core` (`.kiro/specs/pilar-1-core`, Tasks 1–13 complete)
Core contracts (`FilterNode`/`FilterLeaf`/`FilterAnd/Or/Not`, `FilterOperator`, `SortSpec`,
`ViewQueryRequest`, `FilterOrigin`), `PagedResult<T>`, `ViewListResult<TRow>`, `FieldMetadata`,
`ViewMetadata`, `HardLimits`; ports `IViewScope`/`IViewExecutor`/`IViewRegistry`; authoring Style A
(`ViewTemplate`) + Style B (`View<TQuery>`/`View<TQuery,TCrud>`); `FilterCompiler` with tri-whitelist;
`EfViewExecutor` (List/Detail end-to-end, provider-aware, two counts); AspNetCore endpoints + one-door
auth; Northwind example.

### 2.2 `pilar-1-hardening` (`.kiro/specs/pilar-1-hardening`, Tasks 1–6 complete)
- **D94** auth fail-safe posture (see §4).
- **D95** masked field defaults non-filterable/non-searchable.
- **D101 + D103** route groups + single source (model R, see §4).
- **D99** wire-version seam (deferred).

### 2.3 Verification status (as of last update)
- Full solution build green on **net8.0 / net9.0 / net10.0**.
- Test suite: **53 tests, 0 failed** on all three TFMs.
- Northwind example **selftest PASS** (List paging, filter+search, Detail by-key). Example targets
  **net8.0 only**.

---

## 3. Documentation map (authoritative)

Under `docs/spec/` (all **English** after the 2026-06-20 migration; see §4 language policy):
- `01-view.md` — **foundation**; View concept, public contract, full Decision Log (D1–D50, §13.1
  DR1–DR10, §13.2 D94–D103). Status: IMPLEMENTED, reconciled with code.
- `02-filter-and-query.md` — query engine (Pillar 2 server half). Status: PARTIALLY IMPLEMENTED;
  reconciliation banners present. See §6 for remaining gaps.
- `03-source-generator.md` — Pillar 3. Status: **DESIGN INTENT (frozen; not a contract until built)**.
- `04-adapter-contract.md` — Pillar 2 adapters. Status: **DESIGN INTENT (frozen)**.
- `05-aspnetcore-mapping.md` — HTTP composition. Status: PARTIALLY IMPLEMENTED; reconciled.
- `10-operations-and-observability.md` — vendor-neutral observability + health + startup validation.
  Status: DESIGN INTENT (not built).
- `11-versioning-and-deprecation.md` — public surfaces, versioning scheme, deprecation policy.
  Status: DESIGN INTENT.

Kiro specs under `.kiro/specs/`: `pilar-1-core` (done), `pilar-1-hardening` (done). These are
**Indonesian** (legacy); see language note in §4.

> Important reconciliation history: `02`/`05` were authored after Pillar 1 was implemented and had
> drifted; they now carry "reconciliation" banners and the **code-accurate** contracts. `03`/`04` are
> forward-looking design intent and intentionally not pinned to code yet.

---

## 4. Settled decisions — DO NOT re-litigate

Each is final for this release; full rationale at the cited location. Reopen only with explicit owner
approval.

### Security & defaults
- **Auth posture (D94, revises D43).** Two-level model: *switch* (authorizer present/absent) + *policy*
  (handler). Without an authorizer: **Development** → allow-all + startup warning; **non-Development**
  (Production/Staging/UAT/unset env) → **fail-closed startup throw** unless `AllowAnonymousAccess()` is
  called explicitly. Rationale: operator ≠ author and may lack source; a security omission must
  fail-safe; "open" must be a deliberate, reviewed choice. Impl: `VistaStartupValidator` (injects
  `IHostEnvironment`), `VistaEndpointOptions.AllowAnonymous`, `IVistaEndpointBuilder.AllowAnonymousAccess()`.
  (`01-view.md` §5.6, §13.2 D94.)
- **Masked ⇒ non-probeable (D95).** A `MaskField`'d field defaults to `IsFilterable=false` and (string)
  `IsSearchable=false`, unless an explicit `Filterable(true)`/`Searchable(true)`/`Operators(...)` opt-in
  is given. Closes binary-search probing of masked values. Impl: `FieldBuilder` tracks explicit-set;
  `ViewBuilder.Build` applies the masked default. (`01-view.md` §5.2/§7, §13.2 D95.)
  **Note:** the masking *transform* itself is not yet applied at runtime (see §7 backlog).
- **Filter/Sort/Search default-allow (D42, supersedes D3/D14).** All projection fields are
  filter/sort/searchable by default (search = string only); opt out per field. Security boundary = the
  curated projection.

### Authoring & routing
- **Two authoring styles are permanent (D96).** Style A (central template, anonymous) **and** Style B
  (class-per-view, typed) are first-class forever — no deprecation of Style A. **AOT asymmetry is
  permanent & explicit:** Style A serialization stays `[RequiresUnreferencedCode]` (anonymous);
  filter/sort/paging stays AOT-clean. Use-case guidance: monolith→A, modular monolith→B,
  microservices→either. (`01-view.md` §4.5, §13.2 D96.)
- **Route model = registration owns the route (model R) (D101, done).** A view's full route is composed
  at registration (default root `/api/views`, or a `RouteGroup` prefix) and baked into
  `ViewMetadata.Route`; the AspNetCore mapper is a **dumb mapper** that maps each view at its
  `ViewMetadata.Route`. The AspNetCore-owned `RouteRoot` setter was **removed**. (`01-view.md` §13.2 D101.)
- **Route groups + one view = one endpoint (D103, done).** `IVistaBuilder.RouteGroup(prefix, g => {...})`
  scoping (nested groups combine), `RegisterAssembly(...)` (`[RequiresUnreferencedCode]`, metadata-only),
  default root for ungrouped. View names are **globally unique**; a view maps to **exactly one** endpoint
  (registering the same view in two groups fails fast). (`01-view.md` §13.2 D103.)
- **Cross-assembly view discovery (D97)** is a **committed Pillar 3 requirement** (needed by the
  modular-monolith Style B use case), not an open question.

### Migration & versioning
- **No DynData compatibility layer (D98, revises D20).** Migration is manual. DynData ergonomics are
  preserved via Style A (its spiritual successor), and `08-migration-from-dyndata.md` is the primary
  migration tool. No `/dyndata/*` aliases, no wire shim.
- **Wire versioning deferred (D99).** No `/api/v{n}/` routes this release; unversioned = latest; a
  `VistaEndpointOptions.CurrentWireVersion` seam exists for additive future versioning; **route groups
  are the intended versioning vehicle**. Avoids premature abstraction. (`11-versioning-and-deprecation.md`.)

### Operations & observability (designed, NOT built)
- **Vendor-neutral observability (D100).** Instrument via OpenTelemetry-native `ActivitySource`/`Meter`/
  `ILogger` (no APM dependency — Instana/Datadog/Jaeger/Prometheus/Serilog all consume OTel). Enrich
  auto-instrumented HTTP/EF spans with View semantics; expose operational status (e.g. authorizer) via
  standard health checks; opt-in & zero-cost when unused. Observability names = an operational contract,
  subject to the deprecation policy. (`10-operations-and-observability.md`.)
- **Operating-model assumption (org-neutral).** The operator is often not the code author and may lack
  source access; therefore defaults fail safe, failures are operator-actionable via standard signals
  (health/logs/telemetry), and breaking changes must be deploy-time-detectable (startup config
  validation). This generalizes the original enterprise Dev/Ops-separation context — it is **not**
  company-specific.

### Pillar 1 implementation reconciliation (DR1–DR10, `01-view.md` §13.1)
These record where the code intentionally differs from the early spec sketches. **Code wins.**
- **DR1** `IViewRegistry`: `Add(ViewMetadata)`, `Register<TView>()` (RUC), `Get` **nullable** (miss→404),
  `All`. No `Register(Type)`/`RegisterAssembly` on the Core registry.
- **DR2** DI is **two doors**: `AddVista` (EF — registration) + `AddVistaEndpoints` (AspNetCore — auth).
- **DR3** Pillar 1 List = **`GET {root}/{viewName}`** (query string). `POST .../query` is the Pillar 2
  adapter form (layers on top).
- **DR4** `WithValidator`/`WithInterceptor` deferred (not in code).
- **DR5** Style B `Register<TView>()` is **metadata-only** (not executable without an `IViewExecutionPlan`
  via `Register<TView>(plan)` or source-gen).
- **DR6** List result = **`ViewListResult<TRow>`** (`PagedResult<TRow> Page` + `long TotalRowsUnfiltered`).
  **Supersedes** the proposed `ViewQueryResult<T>` (Spec 02 §6.2, D51).
- **DR7** Write endpoints mapped but EF write **not implemented** → **501** (writable) / **404** (read-only).
- **DR8** Write is **merged into `IViewExecutor`** (`CreateAsync<TCrud>`/`UpdateAsync<TCrud>`/`DeleteAsync`),
  **not** a separate `IViewWriter` (contra Spec 05 §7.1 D82). `IViewExecutor` is **generic**
  (`ListAsync<TRow>`/`DetailAsync<TRow>`), not erased-to-`object` `QueryAsync(ViewQueryExecution)`.
- **DR9** `FilterOrigin` is a **public 3-value enum** (`Filter`/`Search`/`Scope`) passed as a **parameter**
  to `FilterCompiler.Compile(node, origin, view)`. **Not** a field on `FilterLeaf`; no `Trusted` value
  (trusted scope flows via `IViewScope`, unvalidated). **Supersedes** Spec 02 §6.1 refinement (D52) and
  Spec 02 D53 (erased port). Consequence: the executor receives one **merged** tree; per-channel
  Search/Scope separation must be done by the **adapter** (Spec 04) — see §6 coupling.
- **DR10** `app.MapView(string viewName)` + `app.MapVistaViews()`. `MapView<TView>()` deferred (needs
  source-gen type→name resolution).

### Language policy
- **Repository artifacts are English** (code, comments, docs, specs, commits, PRs) — per
  `.kiro/steering/persona-and-language.md`. Chat/conversation may be Bahasa Indonesia.
- `docs/spec/*.md` were migrated to English on 2026-06-20. **New `docs/` artifacts must be English.**
- The two `.kiro/specs/{pilar-1-core,pilar-1-hardening}` documents are still Indonesian (legacy). New
  Kiro specs should be English. (Migrating the legacy two is optional cleanup; not done.)

---

## 5. Decision-number map (avoid collisions)

| Range | Owner | Notes |
|---|---|---|
| D1–D50 | `01-view.md` §13 | Foundation. Superseded: D3/D14→D42; D4→D43→D94; D9→D9-revised; D20→D98; D36→D42/D45. |
| D51–D62 | `02-filter-and-query.md` §16 | Engine. **Overridden by reconciliation:** D51→DR6, D52→DR9, D53→DR8, D58→(two counts via `ViewListResult`). Others (coercion/dialect/paging/guards) stand as targets. |
| D63–D70 | `04-adapter-contract.md` §11 | Adapters (design intent). |
| D71–D81 | `03-source-generator.md` §15 | Source gen (design intent). |
| D82–D93 | `05-aspnetcore-mapping.md` §12 | HTTP. **D82 (`IViewWriter`) overridden by DR8.** |
| D94–D103 | `01-view.md` §13.2 (+ docs 10/11) | Cross-cutting (this session). |
| DR1–DR10 | `01-view.md` §13.1 | Pillar 1 code reconciliation. |
| **D104+** | **next free** | Use for new decisions. |

Observability-doc-local: `10-operations-and-observability.md` also lists D100/D102 (D102 = observability
names are an operational contract).

---

## 6. Next work: Spec 02 (Filter & Query Engine) — gap analysis & plan

The engine **core is built and works** (proven by the Northwind selftest). Remaining Spec 02 work is
**hardening/correctness**, verified by reading `FilterCompiler.cs` and `EfViewExecutor.cs`.

### 6.1 Real gaps (code-verified)
| Gap | Spec ref | Risk | Detail |
|---|---|---|---|
| Non-deterministic paging | §11 / D56 | **High (correctness)** | `EfViewExecutor.ApplySort` adds **no PK tiebreaker** and **no default PK order** when sort is empty → `Skip/Take` can duplicate/skip rows across pages. |
| PK not surfaced into metadata | §11 | **Prereq** | `PrimaryKey()` is used only for build-time validation; it is **not** on `FieldMetadata`/`ViewMetadata`. `EfViewExecutor.ResolveKeyField` flags this and falls back to a name convention. Surfacing the PK is a **prerequisite** for the tiebreaker above and robust Detail-by-key. |
| No `In` cap | §8.2 | DoS | `MaxInValues` (1000) not enforced in `FilterCompiler.BuildIn`. |
| No complexity guards | §8.3 / D61 | DoS | `MaxFilterDepth`(16)/`MaxFilterLeaves`(128)/`MaxFilterStringLength`(4096) not enforced. |
| Masking not applied at runtime | §13 | security/correctness | `MaskField` is captured by the builder but `EfViewExecutor` never applies the masker on materialization. Practically dormant today (Style B not executable per DR5; Style A has no `MaskField`). |
| `IQueryDialect` port vs code | §10.1/§10.3 | architecture | Spec designs an `IQueryDialect` port + a separate Npgsql package; code uses a `ProviderAwareFilterCompiler` subclass. Doc-vs-code divergence. |
| ILIKE wildcard escaping | §10.4 | injection-adjacent | Verify `ProviderAwareFilterCompiler` escapes `%`/`_`/`\` on the raw ILIKE pattern path. |
| Per-channel Search/Scope enforcement | §7 | coupling | `EfViewExecutor` compiles the whole tree as `FilterOrigin.Filter`. Search/Scope channels exist in `FilterCompiler` but are not exercised end-to-end. |

### 6.2 Couplings / sequencing
1. **PK-in-metadata is a prerequisite** for deterministic paging (and improves Detail-by-key). Do first.
   Shape: add `FieldMetadata.IsPrimaryKey` (or `ViewMetadata.KeyField`); authoring + plan populate it.
2. **Per-channel enforcement is coupled to adapters (Spec 04)** because of **DR9** (no per-leaf
   `Origin`): the executor gets one merged tree. Channel separation must be done by the adapter
   (compile sub-trees with distinct origins, then AND), or via a contract change. **Defer to Spec 04.**
3. **Masking runtime** is coupled to "Style B executable" (DR5) / adding `MaskField` to Style A. **Defer.**

### 6.3 Proposed plan: a new Kiro spec `query-engine-hardening` (engine-only)
Priority (risk-first):
- **P1** PK-in-metadata + deterministic paging (PK tiebreaker, default order by PK).
- **P1** DoS guards: `MaxFilterDepth`/`MaxFilterLeaves`/`MaxFilterStringLength` + `MaxInValues`.
- **P2** Verify/fix ILIKE wildcard escaping.
- **P2** Reconcile `IQueryDialect`: **recommended** to update Spec 02 §10 to match
  `ProviderAwareFilterCompiler` (cheaper); refactor to the port only if real multi-provider need arises.

Deferred from this spec: per-channel Search/Scope enforcement (→ Spec 04 adapters), masking runtime
(→ Style B executable / Style A `MaskField`).

> Open questions to confirm at kickoff: (1) `FieldMetadata.IsPrimaryKey` vs `ViewMetadata.KeyField`;
> (2) `IQueryDialect` doc-update vs port refactor; (3) confirm per-channel + masking are deferred.

---

## 7. Backlog / known gaps (tech debt)

- **Write path (DR7)** — Create/Update/Delete return 501. Needs `TCrud → entity` mapping (reflection
  now, source-gen later), concurrency, SaveChanges, bulk. Its own spec.
- **Style B executable (DR5)** — `Register<TView>()` is metadata-only; needs the builder to surface
  source/projection to EF, or a source-generated `IViewExecutionPlan`.
- **Masking runtime** — apply `MaskField` transforms on materialization (see §6).
- **Per-channel enforcement** — bind to Spec 04 adapters.
- **Source generator (Pillar 3)** — removes the reflection (`[RequiresUnreferencedCode]`) paths; the
  AOT-clean route. Also: cross-assembly discovery (D97), `MapView<TView>()` (DR10).
- **Observability (D100) & versioning (D99)** — designed, not built.
- **Adapters (Spec 04, Pillar 2 client half)** — DataTables/QueryBuilder reference adapters; needed to
  exercise Search/Scope channels and the `POST .../query` form.
- **Legacy Kiro specs in Indonesian** — optional migration to English.
- **`RouteRoot` global default override** — model R uses a fixed default `/api/views` for ungrouped
  views; to change it globally, wrap registrations in a `RouteGroup`. Add an ergonomic override only if
  demanded.

---

## 8. Build & test commands (Windows, from repo root `d:\GitHub\a2n.Vista`)

```powershell
# Build all (net8/9/10)
dotnet build src\a2n.Vista.slnx -c Debug

# Run tests — TUnit uses Microsoft.Testing.Platform: use `dotnet run`, NOT `dotnet test`
# (`dotnet test` mis-parses args and reports "Zero tests ran").
dotnet run --project src\Tests\a2n.Vista.Tests\a2n.Vista.Tests.csproj -c Debug --framework net9.0
# Repeat with --framework net8.0 / net10.0 to cover all TFMs.

# Northwind example end-to-end self-test (example targets net8.0 ONLY):
dotnet run --project src\Examples\Northwind --framework net8.0 -c Debug -- selftest
```

- Tests live in `src\Tests\a2n.Vista.Tests` (TUnit). Tests go through **public API** where possible
  (no `InternalsVisibleTo` for the test project); `InternalsVisibleTo` is granted only to
  `a2n.Vista.EntityFrameworkCore`.
- Reflection-path tests carry `[UnconditionalSuppressMessage("Trimming", "IL2026...", ...)]`.
- Cross-assembly `View<TQuery>` override must be `protected override` (not `protected internal`).

---

## 9. Key code locations

- Contracts: `src/a2n.Vista.Core/Contracts/` (`ViewQueryRequest`, `FilterNode`, `FilterOperator`,
  `FilterOrigin`, `SortSpec`).
- Metadata: `src/a2n.Vista.Core/Metadata/` (`ViewMetadata`, `FieldMetadata`, `HardLimits`).
- Authoring: `src/a2n.Vista.Core/Authoring/` (`View<>`, `ViewTemplate<>`, `IViewBuilder*`,
  `IFieldBuilder`/`FieldBuilder`/`IFieldBuilderState`, `ViewBuilder`).
- Filter engine: `src/a2n.Vista.Core/Filter/FilterCompiler.cs`.
- Ports: `src/a2n.Vista.Core/Ports/` (`IViewExecutor`, `IViewScope`, `IViewRegistry`, `ViewListResult`).
- EF execution + registration: `src/a2n.Vista.EntityFrameworkCore/` (`Execution/EfViewExecutor.cs`,
  `Execution/ProviderAwareFilterCompiler.cs`, `DependencyInjection/IVistaBuilder.cs` + `VistaBuilder.cs`).
- AspNetCore: `src/a2n.Vista.AspNetCore/` (`Routing/VistaEndpointRouteBuilderExtensions.cs`,
  `Authorization/IViewAuthorizer.cs`, `Configuration/VistaEndpoint*`, `Hosting/VistaStartupValidator.cs`).
- Example: `src/Examples/Northwind/` (`Program.cs`, `Views/NorthwindViews.cs`, `SelfTest.cs`).
- Tests: `src/Tests/a2n.Vista.Tests/` (`AuthorizationTests`, `MaskingTests`, `RouteGroupTests`,
  `WireVersionTests`, `EnforcementTests`, `DefaultAllowTests`, `PagingTests`, `TypingInvariantTests`,
  `WidgetTestFixtures`).
