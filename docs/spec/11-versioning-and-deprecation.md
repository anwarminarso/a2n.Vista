# Spec 11 — Versioning & Deprecation (cross-cutting)

> Status: **DRAFT (design intent, cross-pillar)**
> Date: 2026-06-20
> Scope: Vista's evolution policy — public contract surface, definition of "breaking", versioning scheme
> (package vs wire), deprecation mechanisms & lifecycle, and known evolution scenarios.
> Vendor-neutral and organization-neutral.
>
> Decision Log source: Spec 01 §13.2 (D96 permanent dual-style, D97 cross-assembly, D98 no compat,
> D99 wire versioning) and §13.1 (DR-series).

---

## 1. Purpose

Evolution must be designed **before v1.0**, not when v2 becomes urgent. For OSS, the moment another party
uses Vista, every change is a promise either kept or broken. This document makes those promises explicit.

## 2. Public contract surface

You cannot deprecate what is not defined publicly. Vista has **many** surfaces, each with a different
audience, upgrade cadence, and definition of "breaking":

| # | Surface | Audience | Status |
|---|---|---|---|
| S1 | C# authoring API (`View<>`, builder, `IFieldBuilder`, `ViewTemplate`) | developer (recompile) | public |
| S2 | Bootstrap/DI API (`AddVista`, `AddVistaEndpoints`, `MapVistaViews`, `UseAuthorizer`, `AllowAnonymousAccess`) | composition root | public |
| S3 | HTTP wire contract (route, `ViewQueryRequest`, `PagedResult`/`ViewListResult`, RFC 7807 error) | separately deployed client (no recompile) | public, **URL-versioned** (§4) |
| S4 | `ViewMetadata` schema (used by TS codegen) | generated client | public, follows URL-versioning |
| S5 | `FilterNode` / `FilterOperator` / `SortSpec` | client & adapter | public, follows URL-versioning |
| S6 | Observability contract (`ActivitySource`/`Meter`/attribute/health names) | operator/dashboard | public (Spec 10 §10) |
| S7 | Source generator diagnostics & output (`VISTA####`) | build pipeline | public (Pillar 3) |
| S8 | Defaults & behavior (default values, auth posture) | everyone | public (behavioral) |
| — | `internal` types, concrete EF execution plan, helpers | — | **internal**, may change |

## 3. Definition of "breaking" per surface

"Breaking" does not have a single meaning:

- **C# (S1/S2):** distinguish *source-breaking* (user code does not compile) vs *binary-breaking* (old assembly
  does not link). Adding an optional parameter = source-compatible but potentially binary-breaking.
- **Wire (S3/S4/S5):** *additive* (new optional field, new route) = safe. *Breaking* = removing/renaming
  a field, changing the **semantics** of a field (e.g. the meaning of `TotalRows`), changing the meaning of an operator, renaming a route, changing the
  error `type` URI.
- **Behavioral (S8):** changing a default value (auth, masked-filterable, page-size cap) — passes type-check
  but changes the runtime result. **The most insidious.**
- **Observability (S6):** renaming a span/metric/attribute → operator dashboards go dark. Not detectable by any test.

## 4. Versioning scheme — package vs wire (D99)

Two separate version axes:

### 4.1 NuGet package → SemVer

`MAJOR.MINOR.PATCH`. MAJOR = breaking C# (S1/S2) or behavioral (S8). Multi-target `net8/9/10`:
dropping a TFM follows the .NET EOL schedule (predictable).

### 4.2 Wire (S3/S4/S5) → contract envelope version via URL

The package version is **not enough** to protect wire clients — clients (browser/mobile/other services) upgrade on
a different cadence and do not recompile when the library is bumped. So the wire is versioned via the **URL**:

| Form | Meaning | Usage |
|---|---|---|
| `/api/views` | **"latest" alias** | **Dev/exploration only.** "Latest" moves → a production client pointing here can break silently when wire v2 ships. |
| `/api/v{n}/views` | **pinned** to contract envelope version `n` | **Required for production clients.** Stable throughout that version's support window. |

Rules:

1. **Version = contract envelope** (wire shape + `ViewMetadata` + `FilterNode`), **not per-view**. The
   `customers` view appears at `/api/v1/views/customers` and `/api/views/customers` — same data, different format
   version. `/metadata` & `FilterNode` follow the same URL (one mechanism).
2. **Coexistence is allowed by design**: a single deployment may serve `/api/v1` and `/api/v2`
   side by side during a transition. But coexistence is expensive (two serialization/adapter paths) → **v1.0 just ships
   v1 + alias**; the URL form is reserved now so that adding `v2` later is additive.
3. The TS client generated from `/api/v1/.../metadata` automatically targets the v1 endpoint.

> `RouteRoot` consequence (D101): the root must be able to express the version and emit both the alias and
> pinned root.

## 5. Deprecation mechanisms (toolbox per surface)

- **C# (S1/S2):** `[Obsolete("use X; remove in vN", error)]` with escalation: warning (one MINOR) →
  error (one MAJOR) → removal. Install a **public API analyzer** (`Microsoft.CodeAnalysis.PublicApiAnalyzers`
  + `PublicAPI.Shipped/Unshipped.txt`, or `Microsoft.DotNet.ApiCompat`) so that an accidental API change
  **fails in CI**, rather than being discovered after release.
- **Authoring-time (S7):** `VISTA####` diagnostics with graduated severity + help-link; guide migration
  while typing.
- **Wire (S3):** *additive-first* + *tolerant reader* (ignore unknown fields). Deprecating an
  endpoint/field: keep the old one **side by side** for ≥1 MAJOR; send the **`Deprecation` + `Sunset`** headers
  (RFC 8594) in the old response; the `vista.deprecated.hit` metric (Spec 10 §6) monitors who still uses it.
- **Default/behavior (S8):** when changing a default, provide an **opt-back flag** to the old behavior for one
  MAJOR + a startup warning, then remove it.
- **Observability (S6):** published names = frozen; change them only by emitting both old and new during the transition.

## 6. Lifecycle & communication

- **Explicit overlap:** a deprecated feature survives for **at least 1 MAJOR**; write the number, do not say "later".
- **Communication:** `CHANGELOG.md` + a **migration guide per MAJOR** + release notes.
- **Deploy-time detectable (operator-neutral):** breaking/deprecation must not live only in a changelog the
  operator does not read. **Validate the configuration at startup** (Spec 10 §9) to detect a removed/renamed
  setting and fail with an actionable message; the `vista:config` health reports it.

## 7. Known Vista evolution scenarios

The contract shape must be prepared **now** so that later additions are additive (non-breaking):

1. **Reflection → source generator (Pillar 3).** When source-gen lands, the `[RUC]`-marked reflection path
   becomes legacy; both live side by side, source-gen by default, reflection deprecated gradually.
   The `IConfiguredView`/`IViewRegistry.Add` contract is already designed for this (DR1).
2. **Write path (currently 501, DR7).** Its later implementation = additive **provided** the route &
   `TCrud` request are forward-compatible now. Do not lock write to a shape that will later have to be broken.
3. **Style B metadata-only → executable (DR5).** When the builder feeds the source/projection to EF,
   `Register<TView>(plan)` becomes optional → additive.
4. **Style A & B permanent (D96).** **No deprecation of Style A.** A forever dual-path commitment, with a
   **permanent AOT asymmetry**: Style A serialization stays `[RUC]` (anonymous), its filter/sort/paging is
   AOT-clean. Stated explicitly, not a temporary shortcoming.
5. **Cross-assembly view discovery (D97).** Style B in a sub-project needs cross-assembly registration
   (module initializer + resilience to trimming). Promoted from an Open Question (Spec 03 §17 #4)
   to a **mandatory Pillar 3 requirement**.
6. **Manual DynData migration, no compat layer (D98).** No `/dyndata/*` wire shim. DynData ergonomics
   are preserved through Style A; `08-migration-from-dyndata.md` becomes the primary migration tool
   (before/after `QueryTemplate` → `AddView`, `externalFilter`/`jsonQB` → `FilterNode`).
7. **Result shape convergence.** `PagedResult` + `ViewListResult` are final (DR6); the proposed
   `ViewQueryResult` is dropped before v1.0 — do not ship then immediately deprecate a new type.
8. **Reserved stubs.** `distinct` endpoint (D35), `WithInterceptor`/`WithValidator` hooks (DR4):
   reserve the name/route now → later implementation is additive.
9. **Observability names (S6).** Frozen per MAJOR (Spec 10 §10).

## 8. The "evolve" side — growing without breaking

- **Additive-first & opt-in:** new features default off/safe; new defaults only in a MAJOR.
- **Capability negotiation:** the client asks about support via `ViewMetadata`/wire version, rather than
  assuming.
- **Source generator as a migration engine:** it sees the entire authoring code at compile time → it can
  surface precise migration diagnostics that guide the user through a MAJOR.

## 9. Decision Log

| # | Decision | Status | Notes |
|---|-----------|--------|---------|
| D96 | Style A & B permanent; AOT asymmetry permanent & explicit; no deprecation of Style A. | **Decided** | §7.4. Spec 01 §4.5. |
| D97 | Cross-assembly view discovery = mandatory Pillar 3 requirement (not an open question). | **Decided** | §7.5. |
| D98 | No DynData compat layer; manual migration; the guide becomes the primary tool. | **Decided** | §7.6. Spec 01 §12.5. |
| D99 | Wire versioning via URL: `/api/views` (latest alias, dev-only) + `/api/v{n}/views` (pinned, prod); version = contract envelope; coexist by design, v1.0 ships v1 + alias. | **Decided** | §4. Closes Open Question Spec 01 §15 #1. |
| D101 | Single source `RouteRoot` (currently duplicated across EF/AspNetCore) — implementation to follow. | **Decided (impl to follow)** | §4.2. Spec 01 §13.2. |

## 10. Next / Forward References

- `10-operations-and-observability.md` — observability contract & startup validation (deploy-time detectable).
- `08-migration-from-dyndata.md` — primary migration tool (D98).
- `03-source-generator.md` — cross-assembly discovery (D97), reflection→source-gen (§7.1).
