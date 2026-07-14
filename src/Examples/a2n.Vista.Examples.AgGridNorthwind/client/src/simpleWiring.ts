import { createGrid, type ColDef, type GridApi, type GridOptions } from 'ag-grid-community';
import { renderNav } from './nav.js';
import { createVistaAgGridDatasource } from './vistaAgGridDatasource.js';

/**
 * Entry point for the Northwind showcase **Simple Wiring** page (Requirements 1.1–1.4).
 *
 * This is the minimal, "it just works" wiring: an AG Grid **Infinite Row Model** (a community feature,
 * unlike the Enterprise-only server-side row model) driving the read-only Northwind `vProductCategory`
 * view through the landed Vista AG Grid adapter endpoint `POST /api/views/vProductCategory/aggrid`. It
 * preserves the behavior of the standalone `a2n.Vista.Examples.AgGridNorthwind` grid (1.3) and requires
 * no change to the adapter endpoint or any Core/EF/AspNetCore contract; it only adds the shared showcase
 * navigation so the page is reachable as one of the three showcase pages (1.4).
 *
 * Every grid interaction is an observable HTTP request: each scroll fetches one block, and every sort,
 * multi-sort, column filter, combined AND/OR condition, and quick-filter change issues a fresh POST
 * followed by a displayed-rows update (1.1). The quick-filter text is sent out-of-band as the `?q=`
 * query-string parameter the adapter reads from `AdapterRequest.Values["q"]` (1.2); the JSON body
 * (`{ startRow, endRow, sortModel, filterModel }`) stays a faithful `IServerSideGetRowsRequest` subset,
 * so the server side is identical regardless of the front-end row model.
 *
 * Runtime note: AG Grid community modules (including the Infinite Row Model) auto-register on import from
 * the umbrella `ag-grid-community` package/CDN bundle (see the import map in `index.html`), so no explicit
 * `ModuleRegistry.registerModules(...)` call is required here.
 */

/** The Vista AG Grid adapter endpoint for the exposed Northwind view. */
const AGGRID_ENDPOINT = '/api/views/vProductCategory/aggrid';

/** How many rows AG Grid requests per block; each block is one POST to the adapter endpoint (1.1). */
const CACHE_BLOCK_SIZE = 20;

/** Debounce window (ms) for quick-filter keystrokes, so typing does not fire a POST per character. */
const QUICK_FILTER_DEBOUNCE_MS = 250;

/**
 * Column definitions for the six visible `vProductCategory` fields (the hidden key columns ProductId /
 * CategoryId / SupplierId are intentionally not projected as columns). Text fields use
 * `agTextColumnFilter` and numeric fields use `agNumberColumnFilter`; both filter types support AG Grid
 * combined AND/OR conditions out of the box.
 *
 * Each column separates `field` from `colId` on purpose. `field` is the row-data accessor and must match
 * the response JSON, which Vista serializes as **camelCase** (`productName`, `unitPrice`, …). `colId` is
 * the identifier AG Grid puts into `sortModel`/`filterModel`, and the adapter matches it against the
 * view's field names, which are **PascalCase** (`ProductName`, `UnitPrice`, …). Keeping both aligns the
 * displayed values with the sort/filter that the server understands.
 */
const columnDefs: ColDef[] = [
  { colId: 'ProductName', field: 'productName', headerName: 'Product', filter: 'agTextColumnFilter', minWidth: 220, flex: 2 },
  { colId: 'UnitPrice', field: 'unitPrice', headerName: 'Unit price', filter: 'agNumberColumnFilter', minWidth: 130 },
  { colId: 'UnitsInStock', field: 'unitsInStock', headerName: 'In stock', filter: 'agNumberColumnFilter', minWidth: 120 },
  { colId: 'Discontinued', field: 'discontinued', headerName: 'Discontinued', filter: 'agTextColumnFilter', minWidth: 140 },
  { colId: 'CategoryName', field: 'categoryName', headerName: 'Category', filter: 'agTextColumnFilter', minWidth: 160, flex: 1 },
  { colId: 'SupplierName', field: 'supplierName', headerName: 'Supplier', filter: 'agTextColumnFilter', minWidth: 200, flex: 1 },
];

/** Looks up a required DOM element by id, throwing a clear error if the markup and script disagree. */
function getRequiredElement<T extends HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (element === null) {
    throw new Error(`Expected element #${id} to exist in index.html.`);
  }
  return element as T;
}

/** Shows an error message in the status area (role="alert"); clearing it when the message is empty. */
function setError(statusEl: HTMLElement, message: string): void {
  statusEl.textContent = message;
  statusEl.hidden = message.length === 0;
}

function bootstrap(): void {
  // Render the shared showcase navigation, marking this page active (1.4).
  renderNav('simple-wiring', getRequiredElement<HTMLElement>('nav'));

  const gridDiv = getRequiredElement<HTMLDivElement>('grid');
  const quickFilterInput = getRequiredElement<HTMLInputElement>('quick-filter');
  const errorEl = getRequiredElement<HTMLElement>('status-error');
  const statusEl = getRequiredElement<HTMLElement>('status-line');

  setError(errorEl, '');

  // The quick-filter text is sent out-of-band as the `?q=` query-string parameter (read by the adapter
  // from AdapterRequest.Values["q"]); the JSON body stays a faithful IServerSideGetRowsRequest subset.
  const datasource = createVistaAgGridDatasource({
    endpoint: AGGRID_ENDPOINT,
    getQuickFilter: () => quickFilterInput.value,
    onError: (message) => setError(errorEl, message),
  });

  const gridOptions: GridOptions = {
    columnDefs,
    // Every column is sortable and filterable so single-sort, Shift+click multi-sort across columns,
    // text/number filters, and AG Grid combined AND/OR conditions are all exercisable (1.1).
    defaultColDef: {
      sortable: true,
      filter: true,
      floatingFilter: true,
      resizable: true,
    },
    // Infinite Row Model (community): paging, sorting, and filtering are delegated to the Vista adapter
    // endpoint. Scrolling requests the next block, one POST per block (1.1).
    rowModelType: 'infinite',
    datasource,
    cacheBlockSize: CACHE_BLOCK_SIZE,
    // Keep memory bounded and issue one request at a time so each block is a discrete, observable POST.
    maxConcurrentDatasourceRequests: 1,
    animateRows: true,
    // Surface a per-request status and clear any stale error once a block resolves.
    onModelUpdated: () => {
      statusEl.textContent = 'Rows loaded.';
    },
  };

  const api: GridApi = createGrid(gridDiv, gridOptions);

  // Re-fetch from the server whenever the quick filter changes. With the Infinite Row Model the quick
  // filter is NOT applied client-side; purging the cache forces a fresh POST carrying the new `?q=` value
  // and a full displayed-rows update (1.2).
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
