# Spec 05 — ASP.NET Core Mapping (HTTP composition: read, write, auth, error)

> Status: **PARTIALLY IMPLEMENTED (Pillar 1)** — reconciled with the code
> Date: 2026-06-20 (rev: synchronized to the `pilar-1-core` implementation)
> Scope: the `a2n.Vista.AspNetCore` package. Bridges HTTP to the neutral pipeline: endpoint mapping (`MapView<TView>()`/`MapVistaViews()`), binding `HttpContext` → `AdapterRequest` (Spec 04 §5.1), auth composition → `IViewScope` → `IViewExecutor` (Spec 02 §5), the **write/CRUD path** (create/update/delete), concurrency (`If-Match`/`ETag`), **bulk ops**, Detail by-key, export endpoint, metadata endpoint, the HTTP error model (concrete RFC 7807), and OpenAPI registration. **Not** included: View authoring (Spec 01), the read engine (Spec 02), the source generator (Spec 03), grid filter mapping (Spec 04), the EF implementation (concrete `IViewExecutor`) — that is `a2n.Vista.EntityFrameworkCore`.
>
> **Reconciliation note (2026-06-27 — action surface + adapter landed; code is authoritative).** The HTTP
> layer is implemented; the bullets below from the 2026-06-20 note are superseded where they conflict
> (`docs/PROJECT-STATUS.md` §2.5/§2.7):
> - **Action-style surface (D110, supersedes DR3).** Reads are `POST {route}/list`, `POST {route}/detail`,
>   `GET {route}/metadata`, `POST {route}/export`; writes `POST {route}/{create|update|delete}`. The query
>   and key travel in the **JSON body** (`VistaListRequestBody`, polymorphic `FilterNodeJsonConverter`),
>   not the query string.
> - **Multi-channel body (D111).** `VistaListRequestBody` carries `Filter` + `Scope` sub-trees and a global
>   `Search` string; `VistaSearchMerge` routes the global search to the `Search` slot (not folded into
>   `Filter`).
> - **Adapter endpoint (D112).** `POST {route}/{adapter.RouteSuffix}` (DataTables → `/datatable`), wired via
>   `AddVistaAdapter<TAdapter>()` + `AdapterRequestFactory`.
> - **Metadata caching (opt-in).** `GET {route}/metadata` emits `ETag`/`Cache-Control` + honors
>   `If-None-Match` only when `AddVistaEndpoints(e => e.EnableMetadataCaching())` is set (off by default).
> - Writes still return **501** (writable) / **404** (read-only); EF write wiring follows (DR7).
>
> **Earlier reconciliation note (2026-06-20).** Routing, auth (`IViewAuthorizer`), and the RFC 7807 error model
> are already implemented in Pillar 1. Differences from the code (the code that applies, see Spec 01 §13.1):
> - **List = `GET {root}/{viewName}`** (paging/sort via query string), Detail = `GET .../{key}`,
>   Create = `POST .../`, Update = `PUT .../{key}`, Delete = `DELETE .../{key}` (DR3). **(Superseded by
>   D110 above.)**
> - **No `IViewWriter`** (DR8): writes live in `IViewExecutor` (generic). In Pillar 1 the write endpoints
>   return **501** (writable) / **404** (read-only); EF write wiring follows (DR7).
> - Auth DI via `AddVistaEndpoints(v => v.UseAuthorizer<T>())`; view registration via EF `AddVista(...)`.
> - `app.MapView(string viewName)` (by name) — `MapView<TView>()` deferred (requires source-gen, DR10).
> - Bulk ops, export, the metadata endpoint, `If-Match` concurrency, OpenAPI = **forward-looking**.

---

## 1. Purpose

This spec is Vista's **composition root** on the HTTP side. This is where the three deliberately separated ports (Spec 01 D48) meet via DI — `IViewExecutor` (Core/EF, Spec 02), `IViewAdapter` (Core, Spec 04), `IViewExporter` (Core, Spec 01 §11) — without making Core/EF aware of anything about HTTP.

`a2n.Vista.AspNetCore` must be:

1. **Thin & declarative** — an endpoint is the result of registering `ViewMetadata` (Spec 01 §5.4), not a hand-written controller. One code path for all views.
2. **Host-only deps** — the only Vista package allowed to touch `HttpContext`. **No** EF reference (Spec 01 D48); CRUD is accessed through the `IViewWriter`/`IViewExecutor` ports.
3. **Secure-by-default at the gate** — every request passes through `IViewAuthorizer` (§6) before touching data. With no authorizer registered → default allow + startup warning (Spec 01 D43), not silently.
4. **One error source** — all failures (engine validation Spec 02 §15, adapter binding Spec 04 §10, auth, concurrency) are mapped to a single RFC 7807 shape (§9).
5. **AOT-clean** — endpoints are registered from the source-gen registry (Spec 03 §7), without reflection scan; no MVC controller discovery on the hot path (§11).

## 2. Position in the Architecture

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
       a2n.Vista.EntityFrameworkCore        (resolved at the composition root)
```

| Document | Relationship |
|---|---|
| `01-view.md` | **Input.** `ViewMetadata` (route, facet, limits, auth), endpoint table §12.3, error classification §14.1, concurrency §14.2, `IViewAuthorizer`/`ViewAuthContext`/`ViewFacet` (types defined **here**, §6, location D48). |
| `02-filter-and-query.md` | **Consumed.** Calls `IViewExecutor.QueryAsync`/`GetByKeyAsync` after building `ViewQueryExecution` (Spec 02 §6.3). Maps engine errors §15 → HTTP. |
| `03-source-generator.md` | **Consumed.** Auto-registration (§7), the OpenAPI document model (§10), `CompiledView.ApplyWritable` (write), `KeySelector` (Detail by-key). |
| `04-adapter-contract.md` | **Consumed.** Builds `AdapterRequest`, selects the adapter, calls `BindRequest`/`ToQuery`/`ToResponse`, maps `AdapterBindException` → 400. |
| `dyndata-datatables-observed.md` | Endpoint parity (`/datatable`, `/create`, `/update`, `/delete`) for migration. |

Package split (Spec 01 D48): `IViewWriter` (the write port) lives in **Core** like `IViewExecutor`; the EF implementation (CRUD + bulk via `ExecuteUpdate/DeleteAsync`) lives in **`a2n.Vista.EntityFrameworkCore`**. `IViewAuthorizer` (HTTP-bound, carrying `HttpContext`) lives in **AspNetCore**.

## 3. Terminology

| Term | Meaning |
|---|---|
| **Endpoint group** | The set of routes for a single View, mapped from one `ViewMetadata`: query, detail, write, export, metadata. |
| **Composition root** | The DI point in the app host where `IViewExecutor`/`IViewWriter` (EF) + `IViewAuthorizer` (app) + adapters are resolved. |
| **Negotiation** | The selection of a response adapter for the List facet (route suffix vs `Accept` vs `?format=`). §5. |
| **Write facet** | Create/update/delete operations (Spec 01 §4.6); only for Views with a `CrudType` (typed). |
| **Trusted scope** | An `IViewScope` populated by `IViewAuthorizer.ShapeQuery` (server-trusted, `Trusted` channel, Spec 02 §7). |
| **ETag** | The string representation of a concurrency token (Spec 01 §14.2), carried in the `ETag`/`If-Match` header. |

## 4. Non-Goals

- Concrete `IViewExecutor`/`IViewWriter` implementation (EF, provider detection, `ExecuteUpdate/Delete`) → `a2n.Vista.EntityFrameworkCore` (Spec 09 candidate).
- Read engine semantics (validation, coercion, paging) → Spec 02.
- Specific grid filter mapping (DataTables/QueryBuilder) → Spec 04.
- How the generator **produces** the registry/OpenAPI model → Spec 03; here only its **consumption**.
- TypeScript client → Spec 06. Export format details → Spec 07.
- Identity/authentication (who the user is) — Vista delegates to ASP.NET Core auth; Vista only **authorizes** (view, facet, user) via `IViewAuthorizer`.

## 5. Endpoint Mapping & Routing

### 5.1 Registration & mapping

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDb>(/* ... */);
builder.Services.AddVista(v =>
{
    v.RouteRoot("/api/views");                 // global route (Spec 01 §5.6)
    v.UseAuthorizer<AppViewAuthorizer>();      // single auth gate (§6)
    v.RegisterTemplate<NorthwindViews>();      // Style A
    v.Register<CustomerListView>();            // Style B (also added by source-gen, Spec 03 §7)
    v.AddAdapter<DataTablesAdapter>();         // Spec 04 §5.3
});
builder.Services.AddVistaEntityFrameworkCore<AppDb>();  // register IViewExecutor/IViewWriter (EF)

var app = builder.Build();
app.MapVistaViews();                 // map ALL registered views
// or explicitly (codegen-friendly):
app.MapView<CustomerListView>();
app.Run();
```

- `MapVistaViews()` iterates `IViewRegistry.All` (populated by the source-gen module initializer, Spec 03 §7) and maps one endpoint group per `ViewMetadata`. AOT-clean: no controller discovery.
- `MapView<TView>()` maps a single explicit view (idempotent with `MapVistaViews`; dedup by view name).
- Returns `IEndpointConventionBuilder` so consumers can attach standard ASP.NET conventions (rate limit, CORS, output cache) — **but not** `RequireAuthorization` per-view (auth goes through `IViewAuthorizer`, §6).

### 5.2 Route conventions — action-style surface (D110, supersedes DR3)

**Routing model (D101/D103, model R).** A view's **full route** (`{route}`) is composed at
**registration** time — the EF layer's default root (`/api/views`) or a `RouteGroup` prefix — and
recorded verbatim in `ViewMetadata.Route`. `a2n.Vista.AspNetCore` is a **dumb mapper**: it reads
`IViewRegistry` and mounts the **fixed facet sub-paths** under each view's own `{route}`. There is no
per-view `Route()` on the HTTP side, and no route reconstruction here. This keeps the invariant **one
view = one route prefix** (D103): internal vs external groups (different prefixes) work with no
AspNetCore-side configuration, because the prefix already lives in `ViewMetadata.Route`.

`{route}` = the view's full `ViewMetadata.Route` (e.g. `/api/views/orders`). The query and key travel in
the **JSON request body** (D110), so composite keys and rich filter/search/sort/paging payloads need no
URL-encoding gymnastics; reads never depend on the query string.

| Facet | Method + Route | Requirement | Spec |
|---|---|---|---|
| List | `POST {route}/list` | always | 02 |
| Detail by-key | `POST {route}/detail` | always (key in body: scalar or name→value map) | 02 §6.3, D109 |
| Metadata | `GET {route}/metadata` | always (cacheable; immutable after startup) | §8 |
| Export | `POST {route}/export` | always (query in body; bounded by `MaxExportRows`) | §7.5, Spec 07 |
| List (grid adapter) | `POST {route}/{suffix}` (e.g. `/datatable`) | an `IViewAdapter` with a `RouteSuffix` (D112) | 04 §5.1 |
| Metadata (schema adapter) | `GET {route}/{suffix}` (e.g. `/querybuilder`) | an `IViewMetadataAdapter` with a `RouteSuffix` (D116) | 04 §5.2 |
| Create | `POST {route}/create` | writable view (`CrudType != null`) | §7.2 |
| Update | `POST {route}/update` | writable view (`CrudType != null`) | §7.3 |
| Delete | `POST {route}/delete` | writable view (`CrudType != null`) | §7.4 |

Design notes:

- **Uniform action surface.** Every facet is `POST {route}/{action}` except the cacheable
  `GET {route}/metadata`. This is the DynData-heritage surface (D98 migration ergonomics): predictable,
  client-codegen-friendly, no verb/resource ambiguity.
- **A read-only view maps only the read actions** (`list`/`detail`/`metadata`/`export`); the write
  actions are **not mapped** for it (D38). A write action on a writable view returns **501** while DR7
  stands (EF write wiring follows).
- **`list`/`detail`/`export` use POST** because the `FilterNode` tree (Spec 01 §8) and composite keys are
  too complex for a query string and could exceed the URL limit. `VistaQueryStringParser` is **retired**.
- **Detail/Update/Delete carry the key in the body** as a scalar or a `{ field: value }` name→value map,
  normalized against `ViewMetadata.KeyFields` by the engine (D109); no serializer type crosses into Core
  (R2.6). Composite PKs need no path encoding.

### 5.3 Binding `HttpContext` → `AdapterRequest`

The host builds the neutral `AdapterRequest` (Spec 04 §5.1) then hands it to the adapter. `Values` merge rules:

1. **`POST .../query` form-urlencoded** (classic DataTables): read `request.Form` → `Values` (bracket keys `columns[0][data]` as-is, Spec 04 §7.2). The query string is merged (does the query win for the same key? **no** — the form wins; the query only adds keys not present in the form — D83).
2. **`POST .../query` `application/json`**: the entire body goes into `AdapterRequest.JsonBody`; `Values` = query string only.
3. **Source selection** is based on `Content-Type`; a mix (form + JSON body) is not supported → 415.
4. **Body size limit** is capped (`MaxRequestBodyBytes`, default 1 MiB for query/write; export requests are small) → 413 when exceeded. Anti-DoS, complementing the engine tree complexity guard (Spec 02 §8.3).

### 5.4 Adapter selection (negotiation) — D84

Only relevant for the **List** facet (Detail/Write always use the neutral Vista shape). Resolution order (first match wins):

1. **Route suffix** — a request to `{root}/{v}/{suffix}` selects the adapter with `RouteSuffix == suffix` (e.g. `/datatable` → `DataTablesAdapter`). DynData parity; explicit, deterministic.
2. **`Accept` header** — `{root}/{v}/query` with `Accept: application/vnd.vista.datatables+json` selects the adapter by its registered media type.
3. **`?format=`** — `{root}/{v}/query?format=datatables` (escape hatch for clients that cannot set a header).
4. **Default** — without any of the three → the neutral `PagedResult<T>` shape (Spec 01 §10), without an adapter.

Exactly one adapter is selected per request. An unknown `?format=` / unregistered suffix → 404 (route does not exist) or 406 (`Accept` does not match) — D84.

## 6. Authorization (`IViewAuthorizer`) — type definition

This type is **defined in `a2n.Vista.AspNetCore`** (HTTP-bound, Spec 01 D48). Spec 01 §5.6 declares its contract; here is the final shape + runtime semantics.

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
    // Allow/deny gate per (view, facet, user). Called on every request BEFORE data is touched.
    ValueTask<bool> IsAllowedAsync(ViewAuthContext ctx);

    // Inject a server-trusted row filter (tenant, ownership) — centralized, cannot be bypassed by the client.
    // Added leaves go into the Trusted channel (not validated, Spec 02 §7).
    void ShapeQuery(ViewAuthContext ctx, IViewScope scope);

    // The awaited door for a scope that must be LOADED (Spec 01 D151). Default implementation
    // forwards to ShapeQuery, so an authorizer needing no I/O implements only the sync member.
    ValueTask ShapeQueryAsync(ViewAuthContext ctx, IViewScope scope, CancellationToken ct);
}
```

### 6.1 Position in the pipeline (read & write)

Every endpoint group runs, **before** touching `IViewExecutor`/`IViewWriter`:

```text
1. resolve ViewMetadata (registry)
2. ctx = ViewAuthContext(User, viewName, facet, HttpContext, Services)
3. if (!await authorizer.IsAllowedAsync(ctx)) → 403 forbidden        (Spec 01 §14.1)
4. scope = new ViewScope();  await authorizer.ShapeQueryAsync(ctx, scope, RequestAborted)  (read & write, D151)
5a. READ : exec = new ViewQueryExecution(viewName, request, scope, sp)
           → IViewExecutor.QueryAsync(exec)                          (Spec 02 §6.3)
5b. WRITE: IViewWriter.Create/Update/Delete(..., scope, ...)          (§7) — scope limits the rows that may be touched
```

- **`ShapeQuery` also applies to writes** (D85): update/delete by-key is `AND`-ed with the trusted filter, so a user cannot modify rows outside their tenant/ownership even if they know the PK. Without this, writes would leak across tenants.
- **Step 4 is outside the fail-closed catch of step 3** (D151). A throw from `IsAllowedAsync` is a deny (403); a throw from shaping is a scope-loading fault and propagates as a 500. No rows are served either way, but the cause is reported honestly — which is why scope data must not be loaded from `IsAllowedAsync`.
- **Granular `Facet`** (`Create`/`Update`/`Delete` separated, not a single `Write`) so the authorizer can, e.g., allow `Update` but deny `Delete`.

### 6.2 Default & warning

| Condition | Behavior |
|---|---|
| `UseAuthorizer<T>` registered | `T` is the sole gate. Any facet whose `IsAllowedAsync` is `false` → 403. |
| `UseAuthorizer` not called | **Default allow** (DynData parity, Spec 01 D43). Startup warning: `"no IViewAuthorizer registered — all views are publicly accessible"`. `ShapeQuery` is a no-op (no trusted filter). |

Deliberately **not** fail-closed (Spec 01 D43). Production documentation mandates `UseAuthorizer`. `IsAllowedAsync` is called **once per request** (not per-row); the result is not cached across requests (user claims can change).

### 6.3 Relationship to ASP.NET Core auth

- **Authentication** (who the user is) remains the standard ASP.NET pipeline (`UseAuthentication`); `ctx.User` is `HttpContext.User`.
- `IViewAuthorizer` is purely **authorization** (allowed/not). The implementor is free to use `IAuthorizationService`, a policy, or claims directly inside `IsAllowedAsync`.
- The user is not yet authenticated but `IsAllowedAsync` needs identity → the implementor returns `false`; the host maps this to **401** (not 403) when `ctx.User.Identity?.IsAuthenticated == false`, otherwise **403** (D86).

## 7. Write / CRUD Path

Write is **only** for Views with a `CrudType` (typed DTO, Spec 01 §4.5 invariant). Anonymous-only views have no write routes (not mapped). Execution uses `CompiledView.ApplyWritable` (Spec 03 §8) — compile-time `TCrud → TEntity` assignment, **without** reflection / mass-assignment.

### 7.1 The `IViewWriter` port (Core) — ⚠️ NOT ADOPTED (see reconciliation)

> **Reconciliation (2026-06-20).** `IViewWriter` was **not created** (DR8). Writes are merged into
> `IViewExecutor` (generic): `CreateAsync<TCrud>`, `UpdateAsync<TCrud>` (with `string? concurrencyToken`),
> `DeleteAsync` (see Spec 02 §6.3). In Pillar 1 the EF write path is not yet implemented → the endpoint
> returns **501** (writable) / **404** (read-only, R3.3). `WriteResult`/`WriteStatus`/`ViewWriteExecution`
> below do not exist yet. The block below is kept as the write path design target.

A non-generic write port, in line with `IViewExecutor` (Spec 02 §6.3). `TCrud`/`TEntity` are erased to `object` at the boundary; typed materialization via a source-gen delegate.

```csharp
namespace a2n.Vista;

public interface IViewWriter
{
    // Create: bind crud (deserialized object) → a new entity via ApplyWritable, SaveChanges.
    // Return the new entity's key (for the Location header) + ETag (if there is a concurrency token).
    Task<WriteResult> CreateAsync(ViewWriteExecution exec, CancellationToken ct = default);

    // Update by-key: load the entity (AND-ed with scope), apply the concurrency check, ApplyWritable, SaveChanges.
    Task<WriteResult> UpdateAsync(ViewWriteExecution exec, object key, string? ifMatch, CancellationToken ct = default);

    // Delete by-key: load the entity (AND-ed with scope), concurrency check, remove, SaveChanges.
    Task<WriteResult> DeleteAsync(string viewName, object key, IViewScope scope, string? ifMatch, IServiceProvider sp, CancellationToken ct = default);
}

public sealed record ViewWriteExecution(
    string ViewName,
    object Crud,            // deserialized TCrud (object at the boundary)
    IViewScope Scope,       // trusted filter from ShapeQuery (§6.1)
    IServiceProvider Services);

public sealed record WriteResult(
    WriteStatus Status,     // Ok, NotFound, ConcurrencyConflict, ValidationFailed
    object? Key,            // entity key (Create → new key; Update/Delete → echo)
    string? ETag,           // encoded concurrency token (§7.8), null if the view has no token
    IReadOnlyList<ValidationError>? Errors); // when ValidationFailed

public enum WriteStatus { Ok, NotFound, ConcurrencyConflict, ValidationFailed }
```

The `object` boundary is consistent with Spec 02 §6.3 and keeps Core/AspNetCore free of generic monomorphization via reflection.

### 7.2 Create — `POST {root}/{v}`

```text
1. auth: IsAllowedAsync(facet=Create) → 403 ; ShapeQuery → scope
2. deserialize body → TCrud via JsonTypeInfo source-gen (Spec 03 §9)   [415 if not JSON; 400 if malformed]
3. validate TCrud (IViewCrudValidator, Spec 01 §5.2) → 400 .../validation if it fails
4. IViewWriter.CreateAsync(exec) → ApplyWritable(crud, newEntity); SaveChanges
5. 201 Created
   - Location: {root}/{v}/{newKey}
   - ETag: "<token>"   (if WithConcurrencyToken)
   - body: the Detail representation (GET-by-key shape) of the new entity
```

- **Only `MapWritable` fields are set** (Spec 01 D25). Other `TCrud` fields are ignored (default) or rejected (strict mode, Spec 03 VISTA0011). Entity fields outside the whitelist are **never** touched by the client — this is the core of anti mass-assignment.
- Create does **not** use the `key` from the client; the PK is DB-generated (identity) or server-set.

### 7.3 Update — `PUT {root}/{v}/{key}`

```text
1. auth: IsAllowedAsync(facet=Update) → 403 ; ShapeQuery → scope
2. deserialize body → TCrud ; validate
3. concurrency: the If-Match header is REQUIRED if the view has a token (§7.8) → 412 if missing
4. IViewWriter.UpdateAsync(exec, key, ifMatch):
     entity = source.Where(scope).FirstOrDefault(e => KeySelector(e) == key)   // scope AND-ed
       → null → 404 not-found
     if there is a token: compare ifMatch vs the entity token → mismatch → 412 precondition-failed
     ApplyWritable(crud, entity); SaveChanges
       → DbUpdateConcurrencyException → 409 concurrency-conflict
5. 200 OK + new ETag + Detail-shape body
```

- **Update by-key is `AND`-ed with the trusted scope** (D85): a row outside the tenant → appears as `404` (not 403), not revealing the existence of a cross-tenant row.
- PUT is a **full update** over the whitelist fields; whitelist fields absent from the `TCrud` payload follow DTO semantics (null/default) — partial update (PATCH per-field) is **out of v1.0** (§12 OQ).

### 7.4 Delete — `DELETE {root}/{v}/{key}`

```text
1. auth: IsAllowedAsync(facet=Delete) → 403 ; ShapeQuery → scope
2. concurrency: If-Match REQUIRED if the view has a token → 412 if missing
3. IViewWriter.DeleteAsync(viewName, key, scope, ifMatch):
     entity = source.Where(scope).FirstOrDefault(KeySelector == key) → null → 404
     token mismatch → 412 ; SaveChanges → DbUpdateConcurrencyException → 409
4. 204 No Content
```

Soft-delete is not the endpoint's concern: if a view models soft-delete, it is expressed as `WithRowFilter<TSource>(_ => e => !e.IsDeleted)` (Spec 01 §5.2) + a `WithCrud` that sets the flag, not a physical `DELETE`. The `DELETE` endpoint always = remove the row (or fail to 404/409/412).

### 7.5 Export — `POST {root}/{v}/export?format=csv|xlsx`

```text
1. auth: IsAllowedAsync(facet=Export) → 403 ; ShapeQuery → scope
2. bind ViewQueryRequest (same path as read; filter/sort from the body) — WITHOUT paging (export = the full filtered set)
3. enforce MaxExportRows: Take(maxRows + 1); > maxRows → 413 payload-too-large (Spec 01 §11.2)
4. resolve IViewExporter by format (Spec 01 §11.1) → 415 if the format is not registered
5. stream: Response.ContentType = exporter.MimeType
           Content-Disposition: attachment; filename="{v}.{ext}"
           exporter.ExportAsync(rows: IAsyncEnumerable<object>, fields, accessors, Response.Body, options, ct)
```

Format/streaming/`LiteXlsxViewExporter` details → Spec 07. Here only the **HTTP glue**: content-type, disposition, streaming to `Response.Body`, limit enforcement. `length=-1` is not relevant (export does not page); the absolute hard cap of 1,000,000 rows still applies (Spec 01 D19).

### 7.6 Bulk operations — D87

Bulk only when the View has `CrudType != null` **and** `AllowBulk(true)` (Spec 01 §5.2). Uses EF 7+ `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (set-based, without loading entities into memory) — implemented in the EF layer.

| Endpoint | Body | Execution | Notes |
|---|---|---|---|
| `PATCH {root}/{v}/bulk` | `{ filter: FilterNode, set: { field: value, ... } }` | `Where(scope ∧ filter).ExecuteUpdateAsync(set)` | `set` only allows `MapWritable` fields (whitelist); otherwise → 400 |
| `POST {root}/{v}/bulk-delete` | `{ filter: FilterNode }` | `Where(scope ∧ filter).ExecuteDeleteAsync()` | the filter is validated the same as read (Spec 02 §7) |

Bulk security rules:

1. **`filter` must be non-empty** (D87) — bulk without a filter = update/delete the entire table; rejected with 400 `bulk-requires-filter`. Prevents an accidental "DELETE all".
2. **The trusted scope is still `AND`-ed** — bulk cannot break through the tenant boundary.
3. **`set` fields are whitelisted** via the same `MapWritable` as single-write; the concurrency token is **not** checked per-row (bulk = set-based) — documented as a trade-off (D87).
4. **No per-row hook/validator/interceptor** in bulk v1.0 (set-based bypasses the change tracker). If per-row audit is needed, use single-write. Response: `{ affected: <long> }`.

### 7.7 Key encoding (`{key}`)

- **Single PK**: encoded in the path. `int`/`long`/`Guid` as-is; a `string` PK is URL-encoded. Coercion to the PK `ClrType` uses the Spec 02 §8 rules → on failure → 400 `invalid-key`.
- **Composite PK** (Spec 03 §17 #3): in v1.0 encoded as comma-separated segments `{key1},{key2}` in PK declaration order; segment count ≠ PK count → 400. (Candidate alternatives tuple/base64 → §12 OQ.)

### 7.8 Concurrency (`ETag`/`If-Match`) — details (Spec 01 §14.2)

- A View with `WithConcurrencyToken(field)` exposes the token as the **`ETag` header** on the Detail response (`GET .../{key}`) and on Create/Update items. Header by default; option to expose it to a DTO field via `WithConcurrencyToken(..., exposeAs: "RowVersion")` (Spec 01 §15 #5).
- Token encoding → ETag string: `byte[] RowVersion` → **base64url**; `DateTime LastModifiedAt` → **ISO-8601**; `xmin` (PostgreSQL) → a numeric string. Always a **strong ETag** (not weak `W/`).
- `PUT`/`DELETE` on a token-bearing view **must** supply `If-Match: "<token>"`:
  - missing header → **412** `precondition-failed` (do not silently perform last-write-wins).
  - token mismatch on load / `SaveChanges` throwing `DbUpdateConcurrencyException` → **409** `concurrency-conflict`.
- A View without a token: `If-Match` is ignored (no optimistic protection). A token is recommended for all multi-user writes.

## 8. Metadata Endpoint

`GET {root}/{v}/metadata` → `ViewMetadata` (Spec 01 §5.4) as JSON, for dynamic client consumption & the TS client (Spec 06). Sensitive fields do not leak (metadata = field shape + flags, not data). An `IsHidden` field still appears in the metadata (the client needs to know the PK for Detail routing) but is marked `IsHidden=true`.

`GET {root}/{v}/metadata/{adapterId}` → the output of `IViewMetadataAdapter<TSchema>.ToSchema` (Spec 04 §5.2), e.g. `/metadata/querybuilder` → the jQuery-QueryBuilder schema (`metadataQB` in DynData). 404 if `adapterId` is not registered.

Metadata is cached (immutable per build); `ETag`/`Cache-Control` may be attached (candidate, §12 OQ).

## 9. HTTP Error Model (concrete RFC 7807)

Satisfies Spec 01 §14.1 & Spec 02 §15: a single `application/problem+json` shape, `type` under `https://a2n.dev/vista/errors/`. AspNetCore is the **only place** where domain errors (engine/adapter/writer) become HTTP — via Vista's `IExceptionHandler`/middleware.

### 9.1 JSON shape

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

- Standard RFC 7807 properties: `type`, `title`, `status`, `detail`, `instance`.
- **Machine-readable `extensions`** (Spec 02 §15): `viewName`, `field`, `operator`, `allowed`, `expectedType`, etc. — flat at the root object (the ASP.NET `ProblemDetails.Extensions` convention).
- `traceId` is always included (W3C trace context) for log correlation. `detail` must not leak internals (stack trace, SQL, other rows' values).

### 9.2 Exception/status → HTTP mapping

| Source | Condition | HTTP | `type` |
|---|---|---|---|
| Engine (Spec 02 §15) | filter/sort/scope/search/operator/value/paging/complexity | 400/413 | per the Spec 02 §15 table |
| Adapter (Spec 04 §10) | `AdapterBindException` (malformed JSON, invalid column index) | 400 | `.../adapter-bind-failed` |
| Auth (§6) | `IsAllowedAsync == false`, user authenticated | 403 | `.../forbidden` |
| Auth (§6) | `IsAllowedAsync == false`, anonymous user | 401 | `.../unauthorized` |
| Write (§7) | `WriteStatus.ValidationFailed` | 400 | `.../validation` (per-field in `errors[]`) |
| Write (§7) | `WriteStatus.NotFound` (key missing / outside scope) | 404 | `.../not-found` |
| Write (§7) | `If-Match` missing on a token-bearing view | 412 | `.../precondition-failed` |
| Write (§7) | token mismatch / `DbUpdateConcurrencyException` | 409 | `.../concurrency-conflict` |
| Bulk (§7.6) | empty `filter` | 400 | `.../bulk-requires-filter` |
| Binding (§5.3) | mixed / unsupported content-type | 415 | `.../unsupported-media-type` |
| Binding (§5.3) | body exceeds `MaxRequestBodyBytes` | 413 | `.../payload-too-large` |
| Negotiation (§5.4) | `?format=`/Accept does not match | 406 | `.../adapter-not-acceptable` |
| Key (§7.7) | key cannot be coerced / wrong segment | 400 | `.../invalid-key` |
| Limits (Spec 01 §11.2) | export rows / page size | 413 | `.../payload-too-large` |
| Unexpected | unhandled | 500 | `.../unexpected` (without internal detail) |

### 9.3 DataTables error shape (optional, parity)

For DataTables-native clients (Spec 04 §7.1 `DataTablesResponse.Error`), the host **may** wrap Problem Details into `{ "draw": n, "error": "<title>" }` when the request arrives via the DataTables adapter **and** `Accept` indicates a grid client. The default remains `application/problem+json` (D88); the wrapping is a negotiable opt-in so it does not break the global error contract.

## 10. OpenAPI Integration

`a2n.Vista.AspNetCore` consumes the neutral **OpenAPI document model** generated by source-gen from `ViewMetadata` (Spec 03 §10) and registers it into the ASP.NET OpenAPI pipeline (`Microsoft.AspNetCore.OpenApi`) at **compile-time** — without a runtime scan.

- Per facet (§5.2) → one OpenAPI operation: path, method, request schema (`TCrud` for write, `ViewQueryRequest`/`DataTablesQuery` for query), response schema (`TQuery`/`PagedResult<TQuery>`/Detail), error responses (§9.2).
- Typed `TQuery`/`TCrud` schemas → `#/components/schemas/...` from `JsonTypeInfo` source-gen.
- **Anonymous view** (Style A): schema from the anonymous shape, component name derived from the view name (e.g. `vProductCategoryRow`, Spec 03 §17 #5). Operations still appear; this is a build-time artifact (RUC not relevant).
- Security scheme: if an `IViewAuthorizer` is registered, operations are marked `security` (bearer/cookie per app configuration) — informational; enforcement remains in `IsAllowedAsync`.

## 11. AOT Constraints

In line with Spec 01 §9, Spec 03 §14:

1. **Endpoint mapping from the source-gen registry** (Spec 03 §7) — `MapVistaViews()` iterates `IViewRegistry.All`, populated by the module initializer. **No** `Assembly.GetTypes()`/controller discovery. `RegisterAssembly` (Spec 01 §5.3) remains `[RequiresUnreferencedCode]`.
2. **`TCrud` deserialization** via `JsonTypeInfo` source-gen (Spec 03 §9) — no non-typed `JsonSerializer.Deserialize(stream, Type)` on the typed write path.
3. **Response serialization** typed via `JsonTypeInfo`; anonymous projection (Style A) falls to the `[RequiresUnreferencedCode]` path (Spec 01 §4.5) — consistent: what is non-AOT is *anonymous serialization*, not HTTP mapping.
4. **Key/concurrency** use the `KeySelector`/accessor source-gen (Spec 03 §8), not `PropertyInfo`.
5. **Minimal API** (`MapXxx` delegate) is more AOT-clean than an MVC controller (which needs reflection action discovery) → Vista uses the Minimal API endpoint path as the primary. An MVC controller adapter (if parity is needed) is the secondary non-AOT path (candidate, §12 OQ).

## 12. Decision Log (continued from Spec 03 D81)

| # | Decision | Status | Notes |
|---|---|---|---|
| D82 | `IViewWriter` is a **port in Core** (in line with `IViewExecutor`); the CRUD + bulk implementation is in `a2n.Vista.EntityFrameworkCore`. `TCrud`/`TEntity` are erased to `object` at the boundary; the `ApplyWritable` source-gen performs the typed assignment. | **Decided** | §7.1. Spec 01 D48, Spec 03 §8. |
| D83 | `POST .../query` binding: form-urlencoded → `Values`; **form wins** over query string for the same key (the query only adds new keys). `application/json` → `JsonBody`. Mixed → 415. | **Decided** | §5.3. |
| D84 | List adapter selection: priority route suffix → `Accept` media type → `?format=` → default `PagedResult`. Exactly one adapter/request; no match → 404/406. | **Decided** | §5.4. Closes Spec 04 §12 #1. |
| D85 | `ShapeQuery` (trusted scope) is `AND`-ed into **write** by-key & bulk, not only read. Rows outside scope → 404 (not 403) on update/delete. | **Decided** | §6.1, §7.3/§7.4. Prevents cross-tenant leakage on write. |
| D86 | Auth denied: anonymous user → **401**; authenticated but unauthorized user → **403**. | **Decided** | §6.3, §9.2. |
| D87 | Bulk (`PATCH .../bulk`, `POST .../bulk-delete`) requires `AllowBulk` + a **non-empty filter** (400 if empty); the trusted scope is still `AND`-ed; `set` is whitelisted via `MapWritable`; per-row concurrency/hooks do **not** apply (set-based via `ExecuteUpdate/DeleteAsync`). | **Decided** | §7.6. ROADMAP "Bulk operations" requirement. |
| D88 | The default error is always `application/problem+json` (RFC 7807). The DataTables `{draw,error}` shape is only a negotiable opt-in for grid-native clients. | **Decided** | §9.1, §9.3. One error source. |
| D89 | List-query = `POST {root}/{v}/query` (body); Create = `POST {root}/{v}`. Detail/Update/Delete by-key single path; composite PK = comma segments (v1.0). | **Decided** | §5.2, §7.7. Spec 01 D32. |
| D90 | `query` & `export` use **POST** (filter tree in the body), not GET. `MaxRequestBodyBytes` (default 1 MiB) → 413. | **Decided** | §5.2, §5.3. |
| D91 | Endpoints are mapped via **Minimal API** (delegate) from the source-gen registry as the primary AOT-clean path; no MVC controller discovery on the hot path. | **Decided** | §11. |
| D92 | `WithConcurrencyToken`: **strong** ETag, base64url encoding (`byte[]`) / ISO-8601 (`DateTime`); `If-Match` required on a token-bearing write view (412 if missing), mismatch → 409. | **Decided** | §7.8. Spec 01 §14.2/D30. |
| D93 | PUT = full update over the whitelist fields; partial/JSON-Merge-Patch deferred. Soft-delete is modeled via `WithRowFilter` + `WithCrud`, not the `DELETE` verb. | **Decided** | §7.3, §7.4. |

## 13. Open Questions

1. **Partial update (PATCH per-field)** — in v1.0 PUT = full whitelist update. PATCH JSON-Merge / JSON-Patch per-field needs to track which field is "present" in the payload (`TCrud` cannot distinguish explicit-null vs absent). Candidates: a source-gen `Optional<T>` wrapper or raw `JsonElement`. Deferred to v1.x.
2. **Composite PK encoding** — comma segments (§7.7) are fragile when a value contains a comma. Alternatives: base64url(JSON array) or a composite key in the body for Detail-by-key (POST). In line with Spec 03 §17 #3.
3. **MVC controller adapter** — some consumers need controllers (the MVC filter pipeline, custom model binding). Provide `a2n.Vista.AspNetCore.Mvc` (non-AOT) or Minimal API only? Candidate: Minimal API v1.0, MVC if there is demand.
4. **Metadata caching** — `ETag`/`Cache-Control` for `GET .../metadata` (immutable per build). Safe, but needs a build-version invalidation strategy.
5. **Per-view rate limiting & request quotas** — currently delegated to ASP.NET conventions (`IEndpointConventionBuilder`). Does Vista need a built-in per-view hook (e.g. limiting export concurrency)? Candidate v1.x.
6. **`Accept` media type negotiation registry** — the final media type string per adapter (`application/vnd.vista.{id}+json`?) needs to be standardized for cross-adapter consistency.

## 14. Next / Forward References

- `06-typescript-client.md` — the TS client consumes `GET .../metadata` (§8) & OpenAPI (§10) for DTO + filter API codegen; calls the endpoints in §5.2.
- `07-export.md` — `IViewExporter` details, streaming, `LiteXlsxViewExporter`; called from §7.5.
- `08-migration-from-dyndata.md` — mapping DynData's `/datatable`,`/create`,`/update`,`/delete` endpoints → the §5.2 routes; error shape parity §9.3.
- `09-efcore-integration.md` (candidate) — the `IViewExecutor`/`IViewWriter` implementation (CRUD, `ExecuteUpdate/DeleteAsync`), provider detection, authoring a DbContext-bound `ViewTemplate<TDbContext>`.
