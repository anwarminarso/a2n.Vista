/**
 * View Browser page orchestration (DynData "Table Browser" parity).
 *
 * This module is the composition root for the View Browser page. It wires together the already-built
 * pure building blocks — {@link loadCatalog} (the view catalog), {@link buildColumnDefs} (metadata ->
 * columns), and {@link shouldIssueSearch} (the min-length global-search gate) — with the landed Vista
 * adapter endpoints, driving everything server-side:
 *
 *  - `GET /api/showcase/views`      populates the View Selector (catalog).
 *  - `GET {route}/metadata`         discovers the selected view's columns (never hard-coded).
 *  - `GET {route}/querybuilder`     populates the jQuery-QueryBuilder advanced-filter panel.
 *  - `POST {route}/datatable`       serves every page, combining paging + global search + single/multi
 *                                   sort (`order[]`) + the structured `jsonQB` advanced filter in one
 *                                   request, each in its own D111 channel.
 *
 * Behavioral contract (requirements in parentheses):
 *  - Selecting a view rebuilds the grid from that view's metadata (2.2) and reflects each field's
 *    sort/search affordances per its metadata flags (2.4); the advanced-filter panel only ever shows
 *    server-allowed filterable fields because the QueryBuilder schema is server-emitted (2.4, 3.6).
 *  - Switching views disposes the prior DataTable + QueryBuilder instance before building the new one,
 *    so no state leaks across views (2.3).
 *  - The empty/placeholder selection renders no grid and issues no datatable request (2.5); an empty
 *    catalog shows an explicit empty-state and issues no request (4.5).
 *  - A global-search term shorter than the configured minimum never triggers a request (3.3); paging
 *    (3.1), single/multi sort (3.4, 3.5), search (3.2) and the advanced filter (3.7) are applied
 *    together in one server-side request (3.8).
 *  - DataTables `errMode` is `'none'`; an RFC 7807 error from the datatable endpoint surfaces a visible
 *    message and leaves the currently displayed rows unchanged (3.9).
 *
 * This module owns DOM and network side effects (it is the page glue); the deterministic logic it relies
 * on lives in the pure, separately-tested modules it imports. It is purely additive at the sample layer
 * and changes no Vista package contract, route, envelope, or error shape.
 */

import { loadCatalog, type ViewCatalogEntry } from './catalog.js';
import { buildColumnDefs, type ColumnDef, type VistaMetadata } from './columns.js';
import { renderNav } from './nav.js';
import { shouldIssueSearch } from './search.js';

/**
 * The DOM element ids the View Browser page (task 7.2) must provide. Exported so the page HTML and this
 * orchestration stay in lock-step from a single source of truth.
 */
export const ELEMENT_IDS = {
  /** Header slot the shared navigation is rendered into. */
  nav: 'nav',
  /** `<select>` listing the catalog; its empty first option is the placeholder. */
  viewSelector: 'viewSelector',
  /** `<input type="search">` for the gated global-search term. */
  globalSearch: 'globalSearch',
  /** `<div>` the jQuery-QueryBuilder panel is initialised on. */
  builder: 'builder',
  /** Button that applies the QueryBuilder advanced filter. */
  applyFilter: 'applyFilter',
  /** Button that clears the advanced filter and the global search. */
  resetFilter: 'resetFilter',
  /** `<table>` DataTables initialises on. */
  grid: 'grid',
  /** Visible status/error line (RFC 7807 surfacing). */
  status: 'status',
  /** Explicit empty-state message (empty catalog / placeholder selection). */
  emptyState: 'emptyState',
  /** Wrapper around the grid + filter controls, hidden while no view is selected. */
  gridPanel: 'gridPanel',
} as const;

/**
 * Minimum global-search term length before a request is issued, matching DynData's
 * `minGlobalSearchCharLength: 3`.
 */
export const MIN_SEARCH_LENGTH = 3;

/** The empty-value option of the View Selector: the placeholder that renders no grid (R2.5). */
const PLACEHOLDER_VALUE = '';

// ---------------------------------------------------------------------------------------------------
// Module state — the single live grid + filter instance, disposed and rebuilt on every view switch.
// ---------------------------------------------------------------------------------------------------

/** The live DataTables instance for the currently-selected view, or `null` when none is shown. */
let currentTable: DataTablesApi | null = null;
/** Whether a jQuery-QueryBuilder instance is currently initialised on the builder element. */
let queryBuilderReady = false;
/** The applied advanced-filter payload (`jsonQB`), refreshed on "Apply filters"; `null` when none. */
let currentQbJson: string | null = null;

// ---------------------------------------------------------------------------------------------------
// Small typed DOM helpers.
// ---------------------------------------------------------------------------------------------------

function byId<T extends HTMLElement>(id: string): T | null {
  return document.getElementById(id) as T | null;
}

/** Show a visible status line; `isError` toggles the error styling used by the page stylesheet. */
function showStatus(message: string, isError: boolean): void {
  const el = byId(ELEMENT_IDS.status);
  if (!el) {
    return;
  }
  el.textContent = message;
  el.className = isError ? 'err' : 'ok';
}

/** Clear the visible status line. */
function clearStatus(): void {
  const el = byId(ELEMENT_IDS.status);
  if (!el) {
    return;
  }
  el.textContent = '';
  el.className = '';
}

/** Toggle the explicit empty-state message (empty catalog or placeholder selection). */
function setEmptyState(message: string | null): void {
  const el = byId(ELEMENT_IDS.emptyState);
  if (!el) {
    return;
  }
  if (message === null) {
    el.textContent = '';
    el.style.display = 'none';
  } else {
    el.textContent = message;
    el.style.display = 'block';
  }
}

/** Show or hide the grid + filter controls wrapper. */
function setGridPanelVisible(visible: boolean): void {
  const el = byId(ELEMENT_IDS.gridPanel);
  if (el) {
    el.style.display = visible ? 'block' : 'none';
  }
}

// ---------------------------------------------------------------------------------------------------
// Endpoint fetches (metadata + querybuilder). The datatable endpoint is driven by DataTables itself.
// ---------------------------------------------------------------------------------------------------

/** Fetch and shape a view's field metadata (`GET {route}/metadata`) for {@link buildColumnDefs}. */
async function fetchMetadata(route: string): Promise<VistaMetadata> {
  const response = await fetch(`${route}/metadata`, { headers: { Accept: 'application/json' } });
  if (!response.ok) {
    throw new Error(`metadata request failed (HTTP ${response.status} ${response.statusText})`);
  }
  const payload = (await response.json()) as VistaMetadata;
  if (!payload || !Array.isArray(payload.fields)) {
    throw new Error('metadata response did not contain a fields array');
  }
  return payload;
}

/** A single jQuery-QueryBuilder filter descriptor (loosely typed; server-emitted). */
interface QueryBuilderFilter {
  type?: string;
  input?: string;
  values?: Record<string, string>;
}

/** Fetch the QueryBuilder schema (`GET {route}/querybuilder`); returns the filter descriptors. */
async function fetchQueryBuilderFilters(route: string): Promise<QueryBuilderFilter[]> {
  const response = await fetch(`${route}/querybuilder`, { headers: { Accept: 'application/json' } });
  if (!response.ok) {
    throw new Error(`querybuilder schema request failed (HTTP ${response.status} ${response.statusText})`);
  }
  const schema = (await response.json()) as { queryBuilderOptions?: { filters?: QueryBuilderFilter[] } };
  return schema.queryBuilderOptions?.filters ?? [];
}

// ---------------------------------------------------------------------------------------------------
// Grid + filter lifecycle.
// ---------------------------------------------------------------------------------------------------

/**
 * Dispose the current DataTable and QueryBuilder instances and reset filter state, leaving the grid and
 * builder elements empty and ready for a fresh view. Safe to call when nothing is currently shown, so a
 * view switch never leaks state from the previous view (R2.3).
 */
function disposeCurrentView(): void {
  if (currentTable) {
    currentTable.destroy();
    currentTable = null;
  }
  const grid = byId(ELEMENT_IDS.grid);
  if (grid) {
    grid.innerHTML = '';
  }

  if (queryBuilderReady) {
    $(`#${ELEMENT_IDS.builder}`).queryBuilder('destroy');
    queryBuilderReady = false;
  }
  const builder = byId(ELEMENT_IDS.builder);
  if (builder) {
    builder.innerHTML = '';
  }

  currentQbJson = null;
}

/** Rebuild the table's `<thead>` from the column defs so DataTables has a matching header row. */
function buildTableHeader(grid: HTMLTableElement, columns: ColumnDef[]): void {
  const thead = document.createElement('thead');
  const row = document.createElement('tr');
  for (const column of columns) {
    const th = document.createElement('th');
    th.textContent = column.title;
    row.appendChild(th);
  }
  thead.appendChild(row);
  grid.appendChild(thead);
}

/**
 * Initialise jQuery-QueryBuilder from the server-emitted schema for `route`. Boolean fields are given
 * explicit radio values so the standalone build can render them (mirrors the existing single-view demo).
 * When the schema exposes no filterable fields, the panel is left empty and no builder is created — the
 * grid still works without an advanced filter.
 */
async function initQueryBuilder(route: string): Promise<void> {
  const filters = await fetchQueryBuilderFilters(route);
  for (const filter of filters) {
    if (filter.type === 'boolean') {
      filter.input = 'radio';
      filter.values = { true: 'Yes', false: 'No' };
    }
  }
  if (filters.length === 0) {
    return;
  }
  $(`#${ELEMENT_IDS.builder}`).queryBuilder({ filters });
  queryBuilderReady = true;
}

/** Read the applied QueryBuilder rules as a `jsonQB` string, or `null` when the builder is empty/absent. */
function readQbJson(): string | null {
  if (!queryBuilderReady) {
    return null;
  }
  const rules = $(`#${ELEMENT_IDS.builder}`).queryBuilder('getRules', { skip_empty: true }) as
    | { rules?: unknown[] }
    | null
    | undefined;
  if (!rules || !Array.isArray(rules.rules) || rules.rules.length === 0) {
    return null;
  }
  return JSON.stringify(rules);
}

/** Index of the first sortable column, or -1 when none is sortable (used for the initial order). */
function firstSortableIndex(columns: ColumnDef[]): number {
  return columns.findIndex((column) => column.sortable);
}

/**
 * Build the DataTables instance for `entry` from its `columns`, wiring the server-side request to
 * `POST {route}/datatable` and combining paging + global search + `order[]` + `jsonQB` in one request
 * (R3.1, R3.2, R3.4, R3.5, R3.7, R3.8). RFC 7807 errors surface a visible message and leave the current
 * rows unchanged (R3.9).
 */
function buildTable(entry: ViewCatalogEntry, columns: ColumnDef[]): DataTablesApi {
  const grid = byId<HTMLTableElement>(ELEMENT_IDS.grid);
  if (!grid) {
    throw new Error(`grid element #${ELEMENT_IDS.grid} not found`);
  }
  buildTableHeader(grid, columns);

  const sortableIndex = firstSortableIndex(columns);
  const initialOrder = sortableIndex >= 0 ? [[sortableIndex, 'asc']] : [];

  const table = $(`#${ELEMENT_IDS.grid}`).DataTable({
    serverSide: true,
    processing: true,
    // DataTables' own global search box is disabled; the page's gated #globalSearch drives the
    // Search channel via table.search(...) so the min-length gate (R3.3) is honored.
    searching: true,
    orderMulti: true,
    order: initialOrder,
    pageLength: 10,
    lengthMenu: [10, 25, 50],
    columns: columns.map((column) => ({
      // camelCase `data` renders the row cell; PascalCase `name` is the server sort/filter field. The
      // ajax.data hook below rewrites columns[i][data] to the PascalCase name for the server.
      data: column.field,
      name: column.colId,
      title: column.title,
      orderable: column.sortable,
      searchable: column.searchable,
      defaultContent: '',
    })),
    ajax: {
      url: `${entry.route}/datatable`,
      type: 'POST',
      data: (payload: { columns: Array<{ name?: string; data?: string }> }) => {
        // Match the server on PascalCase field names for sort + per-column search.
        payload.columns.forEach((column) => {
          if (column.name) {
            column.data = column.name;
          }
        });
        // Attach the structured advanced-filter channel when a filter is applied.
        if (currentQbJson) {
          (payload as Record<string, unknown>)['jsonQB'] = currentQbJson;
        }
      },
    },
  });

  // Clear the status line whenever a request succeeds.
  table.on('xhr.dt', (...args: unknown[]) => {
    const xhr = args[3] as { status?: number } | undefined;
    if (xhr && typeof xhr.status === 'number' && xhr.status >= 200 && xhr.status < 300) {
      clearStatus();
    }
  });

  // Surface RFC 7807 errors visibly; DataTables (errMode 'none') keeps the current rows unchanged.
  table.on('error.dt', (...args: unknown[]) => {
    const message = typeof args[3] === 'string' ? args[3] : 'unknown error';
    showStatus(
      `Request failed: ${message}. See the server console for the Problem Details response.`,
      true,
    );
  });

  return table;
}

/**
 * Load and render the selected view: dispose the prior instance, fetch metadata -> columns, populate the
 * QueryBuilder panel, and build the server-side grid. Surfaces a visible error and shows no grid on
 * failure (leaving no half-built state behind).
 */
async function selectView(entry: ViewCatalogEntry): Promise<void> {
  disposeCurrentView();
  clearStatus();
  setEmptyState(null);
  setGridPanelVisible(true);

  try {
    const metadata = await fetchMetadata(entry.route);
    const columns = buildColumnDefs(metadata);
    await initQueryBuilder(entry.route);
    currentTable = buildTable(entry, columns);
  } catch (error) {
    const message = error instanceof Error ? error.message : 'failed to load the selected view';
    disposeCurrentView();
    setGridPanelVisible(false);
    showStatus(`Could not load the view: ${message}.`, true);
  }
}

// ---------------------------------------------------------------------------------------------------
// Control wiring.
// ---------------------------------------------------------------------------------------------------

/** Populate the View Selector `<select>` with a placeholder followed by one option per catalog entry. */
function populateSelector(select: HTMLSelectElement, entries: readonly ViewCatalogEntry[]): void {
  select.innerHTML = '';

  const placeholder = document.createElement('option');
  placeholder.value = PLACEHOLDER_VALUE;
  placeholder.textContent = 'Select a view\u2026';
  select.appendChild(placeholder);

  for (const entry of entries) {
    const option = document.createElement('option');
    option.value = entry.name;
    option.textContent = entry.title;
    select.appendChild(option);
  }
  select.value = PLACEHOLDER_VALUE;
}

/** Wire the gated global-search box: apply the term server-side only once it meets the minimum length. */
function wireGlobalSearch(input: HTMLInputElement): void {
  input.addEventListener('input', () => {
    if (!currentTable) {
      return;
    }
    const term = input.value;
    if (shouldIssueSearch(term, MIN_SEARCH_LENGTH)) {
      currentTable.search(term).draw();
    } else if (term.trim().length === 0) {
      // Cleared box: reset to the unfiltered result (still one server-side request, no term).
      currentTable.search('').draw();
    }
    // 1..(min-1) chars: intentionally issue no request (R3.3).
  });
}

/** Wire the advanced-filter Apply/Reset buttons to the structured `jsonQB` channel. */
function wireFilterButtons(): void {
  const applyBtn = byId<HTMLButtonElement>(ELEMENT_IDS.applyFilter);
  applyBtn?.addEventListener('click', () => {
    if (!currentTable) {
      return;
    }
    currentQbJson = readQbJson();
    currentTable.ajax.reload();
  });

  const resetBtn = byId<HTMLButtonElement>(ELEMENT_IDS.resetFilter);
  resetBtn?.addEventListener('click', () => {
    if (!currentTable) {
      return;
    }
    currentQbJson = null;
    if (queryBuilderReady) {
      $(`#${ELEMENT_IDS.builder}`).queryBuilder('reset');
    }
    const searchInput = byId<HTMLInputElement>(ELEMENT_IDS.globalSearch);
    if (searchInput) {
      searchInput.value = '';
    }
    currentTable.search('').ajax.reload();
  });
}

/** Wire the View Selector: placeholder disposes the grid and issues no request (R2.5). */
function wireSelector(select: HTMLSelectElement, entries: readonly ViewCatalogEntry[]): void {
  const byName = new Map(entries.map((entry) => [entry.name, entry]));
  select.addEventListener('change', () => {
    const value = select.value;
    if (value === PLACEHOLDER_VALUE) {
      disposeCurrentView();
      setGridPanelVisible(false);
      clearStatus();
      return;
    }
    const entry = byName.get(value);
    if (entry) {
      void selectView(entry);
    }
  });
}

/**
 * Initialise the View Browser page: render the shared nav, opt DataTables out of its default alert error
 * mode, load the catalog, and wire the controls. On an empty catalog or a catalog failure the page shows
 * the appropriate message and issues no datatable request (R4.5).
 */
export async function initViewBrowser(): Promise<void> {
  const nav = byId(ELEMENT_IDS.nav);
  if (nav) {
    renderNav('view-browser', nav);
  }

  // Opt out of DataTables' default alert() so RFC 7807 problems surface in-page (R3.9).
  $.fn.dataTable.ext.errMode = 'none';

  // Nothing is shown until a real view is selected.
  setGridPanelVisible(false);

  const select = byId<HTMLSelectElement>(ELEMENT_IDS.viewSelector);
  wireGlobalSearch(byId<HTMLInputElement>(ELEMENT_IDS.globalSearch) ?? document.createElement('input'));
  wireFilterButtons();

  const signal = await loadCatalog();
  switch (signal.kind) {
    case 'empty':
      setEmptyState('No browsable views are registered.');
      return;
    case 'error':
      showStatus(`Could not load the view catalog: ${signal.message}`, true);
      return;
    case 'catalog':
      setEmptyState(null);
      if (select) {
        populateSelector(select, signal.entries);
        wireSelector(select, signal.entries);
      }
      return;
  }
}

// Auto-initialise when loaded as a page module (`<script type="module" src="js/viewBrowser.js">`).
if (typeof document !== 'undefined') {
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
      void initViewBrowser();
    });
  } else {
    void initViewBrowser();
  }
}
