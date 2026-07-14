import {
  createGrid,
  type ColDef,
  type GridApi,
  type GridOptions,
  type ICellRendererParams,
} from 'ag-grid-community';
import { createVistaAgGridDatasource } from './vistaAgGridDatasource.js';
import { renderNav } from './nav.js';

/**
 * Entry point for the Custom Renderer page of the Northwind showcase (Requirements R5.1–R5.5).
 *
 * This page proves that a consumer can brand and format a grid **entirely on the client**, using only AG
 * Grid *community* features, while every data operation — paging, sorting, and filtering — still happens
 * **server-side** through the Vista AG Grid adapter (`POST /api/views/vProductCategory/aggrid`). It reuses
 * the same thin community Infinite-Row-Model datasource as the Simple Wiring page
 * ({@link createVistaAgGridDatasource}); the only difference here is presentation.
 *
 * Three consumer-owned `cellRenderer`s are demonstrated (R5.2), all pure presentation with no data
 * shaping (R5.3):
 *   1. {@link priceCellRenderer}        — formats the numeric `unitPrice` as a localized currency string.
 *   2. {@link discontinuedBadgeRenderer} — turns the boolean `discontinued` flag into a colored badge.
 *   3. {@link productLinkRenderer}       — renders `productName` as an external lookup link.
 *
 * Runtime note: AG Grid community modules (including the Infinite Row Model and cell renderers) auto-
 * register on import from the umbrella `ag-grid-community` package/CDN bundle (see the import map in
 * `custom-renderer.html`), so no explicit `ModuleRegistry.registerModules(...)` call is required, and no
 * Enterprise feature is referenced anywhere (R5.4).
 */

/** The Vista AG Grid adapter endpoint for the server-side-driven Northwind view. */
const AGGRID_ENDPOINT = '/api/views/vProductCategory/aggrid';

/** How many rows AG Grid requests per block; each block is one server-side POST to the adapter (R5.1). */
const CACHE_BLOCK_SIZE = 20;

/** Debounce window (ms) for quick-filter keystrokes, so typing does not fire a POST per character. */
const QUICK_FILTER_DEBOUNCE_MS = 250;

/**
 * Currency formatter for the price column. Prices in the Northwind sample are USD; a fixed locale/currency
 * keeps the formatting deterministic regardless of the visitor's browser locale.
 */
const priceFormatter = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
});

/**
 * Consumer-owned renderer #1 — formatted price (R5.2).
 *
 * Formats the raw numeric `unitPrice` as a localized currency string (for example `18` → `$18.00`). This
 * is presentation only: the underlying value the server sorts and filters on is unchanged (R5.3). Empty
 * or non-numeric values fall back to a blank / passthrough cell.
 */
function priceCellRenderer(params: ICellRendererParams): string {
  const { value } = params;
  if (value === null || value === undefined || value === '') {
    return '';
  }

  const numeric = typeof value === 'number' ? value : Number(value);
  return Number.isNaN(numeric) ? String(value) : priceFormatter.format(numeric);
}

/**
 * Consumer-owned renderer #2 — Discontinued badge (R5.2).
 *
 * Turns the boolean `discontinued` flag into a small colored badge ("Discontinued" vs "Active"). Returns a
 * DOM element (never an HTML string) so the value is never interpolated into markup — safe by construction.
 */
function discontinuedBadgeRenderer(params: ICellRendererParams): HTMLElement {
  const badge = document.createElement('span');
  const isDiscontinued =
    params.value === true || params.value === 1 || params.value === 'true' || params.value === '1';

  badge.className = isDiscontinued ? 'badge badge--discontinued' : 'badge badge--active';
  badge.textContent = isDiscontinued ? 'Discontinued' : 'Active';
  return badge;
}

/**
 * Consumer-owned renderer #3 — product link (R5.2).
 *
 * Renders `productName` as an external lookup link, built purely client-side from the row value. Uses a
 * DOM element with `rel="noopener noreferrer"` and encodes the query so the product name is never treated
 * as markup. This is a branding/navigation affordance only; it shapes no data (R5.3).
 */
function productLinkRenderer(params: ICellRendererParams): HTMLElement {
  const name = params.value === null || params.value === undefined ? '' : String(params.value);

  const link = document.createElement('a');
  link.className = 'product-link';
  link.textContent = name.length > 0 ? name : '(unnamed)';
  link.href = `https://www.google.com/search?q=${encodeURIComponent(name)}`;
  link.target = '_blank';
  link.rel = 'noopener noreferrer';
  return link;
}

/**
 * Column definitions for the six visible `vProductCategory` fields, wired to the three consumer-owned
 * renderers above. As on the other AG Grid pages, `field` (camelCase) is the row-data accessor matching
 * Vista's serialized JSON, while `colId` (PascalCase) is what AG Grid places into `sortModel`/`filterModel`
 * for the adapter to match against the view's field names — so the server understands every sort/filter.
 */
const columnDefs: ColDef[] = [
  {
    colId: 'ProductName',
    field: 'productName',
    headerName: 'Product',
    filter: 'agTextColumnFilter',
    cellRenderer: productLinkRenderer,
    minWidth: 220,
    flex: 2,
  },
  {
    colId: 'UnitPrice',
    field: 'unitPrice',
    headerName: 'Unit price',
    filter: 'agNumberColumnFilter',
    cellRenderer: priceCellRenderer,
    type: 'rightAligned',
    minWidth: 130,
  },
  {
    colId: 'UnitsInStock',
    field: 'unitsInStock',
    headerName: 'In stock',
    filter: 'agNumberColumnFilter',
    type: 'rightAligned',
    minWidth: 120,
  },
  {
    colId: 'Discontinued',
    field: 'discontinued',
    headerName: 'Discontinued',
    filter: 'agTextColumnFilter',
    cellRenderer: discontinuedBadgeRenderer,
    minWidth: 150,
  },
  {
    colId: 'CategoryName',
    field: 'categoryName',
    headerName: 'Category',
    filter: 'agTextColumnFilter',
    minWidth: 160,
    flex: 1,
  },
  {
    colId: 'SupplierName',
    field: 'supplierName',
    headerName: 'Supplier',
    filter: 'agTextColumnFilter',
    minWidth: 200,
    flex: 1,
  },
];

/** Looks up a required DOM element by id, throwing a clear error if the markup and script disagree. */
function getRequiredElement<T extends HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (element === null) {
    throw new Error(`Expected element #${id} to exist in custom-renderer.html.`);
  }
  return element as T;
}

/** Shows an error message in the status area (role="alert"), clearing it when the message is empty. */
function setError(statusEl: HTMLElement, message: string): void {
  statusEl.textContent = message;
  statusEl.hidden = message.length === 0;
}

function bootstrap(): void {
  // Render the shared cross-page navigation, marking this page active (R7.1).
  renderNav('custom-renderer', getRequiredElement<HTMLElement>('nav'));

  const gridDiv = getRequiredElement<HTMLDivElement>('grid');
  const quickFilterInput = getRequiredElement<HTMLInputElement>('quick-filter');
  const errorEl = getRequiredElement<HTMLElement>('status-error');
  const statusEl = getRequiredElement<HTMLElement>('status-line');

  setError(errorEl, '');

  // Same thin community datasource as the Simple Wiring page: the quick-filter text is sent out-of-band
  // as `?q=` (read by the adapter from AdapterRequest.Values["q"]); paging/sort/filter stay server-side.
  const datasource = createVistaAgGridDatasource({
    endpoint: AGGRID_ENDPOINT,
    getQuickFilter: () => quickFilterInput.value,
    onError: (message) => setError(errorEl, message),
  });

  const gridOptions: GridOptions = {
    columnDefs,
    // Sorting and filtering are delegated to the server via the datasource; the client only renders.
    defaultColDef: {
      sortable: true,
      filter: true,
      floatingFilter: true,
      resizable: true,
    },
    // Infinite Row Model (community): one POST per block; no client-side data shaping (R5.3, R5.4).
    rowModelType: 'infinite',
    datasource,
    cacheBlockSize: CACHE_BLOCK_SIZE,
    maxConcurrentDatasourceRequests: 1,
    animateRows: true,
    onModelUpdated: () => {
      statusEl.textContent = 'Rows loaded.';
    },
  };

  const api: GridApi = createGrid(gridDiv, gridOptions);

  // Re-fetch from the server whenever the quick filter changes. As on the Simple Wiring page, the quick
  // filter is NOT applied client-side; purging the cache forces a fresh POST carrying the new `?q=` value.
  let debounceHandle: number | undefined;
  quickFilterInput.addEventListener('input', () => {
    if (debounceHandle !== undefined) {
      window.clearTimeout(debounceHandle);
    }
    debounceHandle = window.setTimeout(() => {
      statusEl.textContent = 'Applying quick filter…';
      api.purgeInfiniteCache();
    }, QUICK_FILTER_DEBOUNCE_MS);
  });
}

// Defer until the DOM is parsed so the grid container and controls exist.
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', bootstrap);
} else {
  bootstrap();
}
