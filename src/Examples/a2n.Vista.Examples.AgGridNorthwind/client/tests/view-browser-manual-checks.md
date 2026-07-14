# View Browser — manual verification checks (Task 7.3)

These are precise, reproducible manual steps for the three View Browser edge behaviors that cannot be
exercised under the Node/Vitest harness. The subject module,
[`client/src/viewBrowser.ts`](../src/viewBrowser.ts), is the composition root for the page: it owns the
DOM and the network side effects and drives the CDN-loaded jQuery / DataTables.NET / jQuery-QueryBuilder
runtime. It exports only `ELEMENT_IDS`, `MIN_SEARCH_LENGTH`, and `initViewBrowser()` — there is no pure,
side-effect-free seam for these three behaviors, and each one requires the full grid + filter runtime plus
a live DOM. The pure logic the page relies on is unit/property-tested separately
(`search.property.test.ts` covers the R3.3 min-length gate; `columns.property.test.ts` covers the
metadata→columns mapping), and the server round-trip is covered by the `dotnet run -- selftest`
view-browser check. What remains — and is verified here by hand — are the three DOM/runtime edge
behaviors:

- **R2.3** — switching views disposes the previous grid + filter instance before building the new one, so
  no state leaks across views.
- **R2.5** — selecting the empty/placeholder option shows no grid and issues **no** DataTables request.
- **R3.9** — an RFC 7807 error from the DataTables endpoint surfaces a visible error and leaves the
  currently displayed rows unchanged.

Building a full jsdom + jQuery + DataTables + QueryBuilder mock to automate these would be brittle and
would have to re-implement the very runtime under test, so a scripted browser walkthrough is the
authoritative check. Automating these later (for example with Playwright against the running host) would
be the natural upgrade path.

## Traceability

| Behavior | Requirement | Element ids (`ELEMENT_IDS`) | Endpoints |
|----------|-------------|-----------------------------|-----------|
| Dispose-then-rebuild, no state leak | R2.3 | `viewSelector`, `grid`, `builder`, `globalSearch`, `gridPanel`, `status` | `GET {route}/metadata`, `GET {route}/querybuilder`, `POST {route}/datatable` |
| Placeholder ⇒ no grid, no request | R2.5 | `viewSelector`, `grid`, `gridPanel`, `status` | `POST {route}/datatable` (must **not** fire) |
| RFC 7807 error ⇒ visible error, rows unchanged | R3.9 | `viewSelector`, `grid`, `globalSearch`, `applyFilter`, `builder`, `status` | `POST {route}/datatable` (400 Problem Details) |

Endpoint shapes: catalog is `GET /api/showcase/views`; per selected view the sub-routes resolve from that
view's route, e.g. for `vProductCategory` the routes are `GET /api/views/vProductCategory/metadata`,
`GET /api/views/vProductCategory/querybuilder`, and `POST /api/views/vProductCategory/datatable`.

## Preconditions (once per session)

1. **Extract the database.** Ensure `src/Examples/DB/northwind.db` exists (extract
   `src/Examples/DB/Northwind SQLite.zip` and rename to `northwind.db` if needed). The host prints the
   same guidance and exits non-zero if it is missing.
2. **Build the client.** From `src/Examples/Northwind/client`:
   ```sh
   npm install
   npm run build      # tsc -> emits ES modules into ../wwwroot/js
   ```
3. **Run the host.** From the repository root:
   ```sh
   dotnet run --project src/Examples/Northwind
   ```
4. Open the printed URL and navigate to **View Browser** (`/view-browser.html`) via the shared nav.
5. Open the browser **DevTools**; keep the **Network** tab (filter to `Fetch/XHR`) and the **Console** tab
   visible. Clear the Network log before each check so the request/no-request assertions are unambiguous.

The catalog registers three read-only views (`vProductCategory`, `vOrderDetail`, `vOrder`), which is
enough to cover the view-switch check.

---

## Check A — View switch: dispose then rebuild, no state leak (R2.3)

**Goal:** switching from one view to another tears the previous DataTable + QueryBuilder down and rebuilds
from the new view's metadata, with no leftover columns, filter rules, or search term.

**Steps**

1. In the **View Selector** (`#viewSelector`), select **vProductCategory**.
2. Wait for the grid (`#grid`) to render. Confirm in Network that `GET .../vProductCategory/metadata`,
   `GET .../vProductCategory/querybuilder`, and `POST .../vProductCategory/datatable` all fired and
   succeeded (200).
3. Establish state to check for leaks:
   - Type at least 3 characters into the global search box (`#globalSearch`), e.g. `cha`, and confirm a
     `POST .../vProductCategory/datatable` fires (the R3.3 gate lets it through at 3+ chars).
   - Add a rule in the QueryBuilder panel (`#builder`) and click **Apply filters** (`#applyFilter`);
     confirm a reload `POST` fires carrying a `jsonQB` field in the request body.
   - Click a column header to change the sort.
4. Now switch the selector to **vOrder**.

**Expected observations**

- The previous grid is disposed before the new one builds: the old `<table id="grid">` header/rows are
  gone and rebuilt (the module empties `#grid.innerHTML` and calls DataTables `destroy()`), and the
  QueryBuilder is destroyed and re-created (`#builder` is emptied, then re-initialized from the new view's
  `GET .../vOrder/querybuilder`).
- The new grid's **columns match `vOrder`'s metadata**, not `vProductCategory`'s — no stale columns.
- **No state leaks:** the global search box no longer filters the new grid by the old term, and the
  previously-applied advanced filter is gone (the module resets `currentQbJson` to `null` on dispose, so
  the first `vOrder` datatable request carries **no** `jsonQB`). Confirm the first
  `POST .../vOrder/datatable` body has no `jsonQB` field.
- Network shows a fresh trio for `vOrder` (`metadata`, `querybuilder`, `datatable`) and continues to
  target only `vOrder` sub-routes afterward — no further requests to `vProductCategory`.
- The status line (`#status`) shows no error.

**Fail signals:** old columns persist; the new grid's first request carries a stale `jsonQB` or search
term; a request still targets the previous view's route; a JS error appears in Console.

---

## Check B — Placeholder selection: no grid, no request (R2.5)

**Goal:** the empty/placeholder option renders no grid and issues no DataTables request.

**Steps**

1. First put a real grid on screen: select **vProductCategory** and let it load (as in Check A, step 2).
2. **Clear the Network log.**
3. In the **View Selector**, choose the first option — the placeholder **"Select a view…"** (its value is
   the empty string).

**Expected observations**

- The grid panel (`#gridPanel`) is hidden and the previous grid is disposed (no visible table).
- The status line (`#status`) is cleared (no error).
- **No `POST .../datatable` request is issued** for the placeholder selection — the Network log stays
  empty after step 2. This is the key assertion: selecting the placeholder must not trigger any
  server-side request.

**Also (empty-catalog variant, R4.5 — informational):** if the catalog (`GET /api/showcase/views`) were
to return `[]`, the page shows the explicit empty-state ("No browsable views are registered.") and issues
no datatable request. This variant is not reproducible against the shipped host because three views are
always registered; it is noted here only for completeness and is exercised at the unit level by
`catalog.ts`'s empty-state signal.

**Fail signals:** a `datatable` request appears after selecting the placeholder; the old grid stays
visible; the panel does not hide.

---

## Check C — RFC 7807 error: visible error, rows unchanged (R3.9)

**Goal:** when the DataTables endpoint returns an RFC 7807 Problem Details error, the page surfaces a
visible error and leaves the currently displayed rows unchanged (DataTables `errMode` is `'none'`, so no
default `alert()`).

Provoke a per-channel rejection (the engine returns RFC 7807 `400` for a disallowed filter/search leaf).
Use whichever of the two methods is easier in your build:

**Method 1 — disallowed advanced-filter leaf (preferred, exercises the real 400 path)**

1. Select a view and let a page of rows render (note the visible rows — e.g. the first few primary-key
   values).
2. In the QueryBuilder panel, build a rule that the server will reject as a disallowed leaf (for example a
   field/operator combination that is not permitted for that view's filterable set). If the panel only
   offers server-allowed fields (it is populated from `GET {route}/querybuilder`, so it should), use
   Method 2 instead.
3. Click **Apply filters** (`#applyFilter`).

**Method 2 — forced 400 via DevTools request interception (deterministic)**

1. Select a view and let a page of rows render; note the visible rows.
2. In DevTools, add a network override / request-block rule (or use the "Block request URL" →
   then a local override returning `400`) for `POST .../datatable`, returning an
   `application/problem+json` body such as:
   ```json
   { "type": "about:blank", "title": "Bad Request", "status": 400, "detail": "Disallowed filter leaf" }
   ```
3. Trigger a reload: page to the next page, or click **Apply filters** (`#applyFilter`), or type 3+
   characters into `#globalSearch`.

**Expected observations (both methods)**

- The status line (`#status`) shows a **visible error** with the error styling (the element gets
  `class="err"`), e.g. `Request failed: … See the server console for the Problem Details response.`
- The **currently displayed rows in `#grid` are unchanged** — DataTables does not clear the table on the
  failed request because `$.fn.dataTable.ext.errMode` is `'none'` and the module only writes the status
  line in the `error.dt` handler. Confirm the same primary-key values you noted in step 1 are still shown.
- No `alert()` dialog appears (that is the whole point of `errMode = 'none'`).
- Console may log the DataTables warning, but the page must not throw.

Then confirm recovery: remove the override / clear the bad rule and trigger a successful reload — the
status line clears (the `xhr.dt` handler clears it on a 2xx) and the grid updates normally.

**Fail signals:** a native `alert()` pops; the grid empties or shows "No data" after the error; no visible
status message appears; the page throws in Console.

---

## Notes on automation

- No automated test is added for Task 7.3: the three behaviors are DOM + jQuery/DataTables/QueryBuilder
  runtime behaviors with no pure exported seam, and the Vitest environment is `node` (no DOM). Because no
  automated test was added, `npm test` and `tsc --noEmit` are unaffected by this task (this file is
  Markdown under `tests/` and is not compiled or run by Vitest — the Vitest `include` is
  `tests/**/*.test.ts`).
- The complementary automated coverage that already exists: `search.property.test.ts` (R3.3 gate),
  `columns.property.test.ts` (metadata→columns), `vistaAgGridDatasource.test.ts` (datasource request
  shaping + error handling), and the server-side `dotnet run -- selftest` view-browser round-trip.
- Upgrade path: these three checks map cleanly onto a Playwright end-to-end suite against the running host
  (drive `#viewSelector`, assert Network activity, and use route interception for the R3.9 forced-400
  case). That is out of scope for this sample's current tooling.
