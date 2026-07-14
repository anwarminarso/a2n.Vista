import { createGrid } from 'ag-grid-community';
import { createVistaAgGridDatasource } from './vistaAgGridDatasource.js';
/**
 * Entry point for the Vista AG Grid Northwind sample front-end.
 *
 * Wires an AG Grid **Infinite Row Model** over the read-only Northwind `vProductCategory` view through the
 * Vista AG Grid adapter endpoint `POST /api/views/vProductCategory/aggrid`. The Infinite Row Model is a
 * community feature (unlike the Enterprise-only server-side row model) and makes every grid interaction
 * observable as an HTTP request (R7.3/R7.4): each scroll fetches one block, and every sort, multi-sort,
 * filter, combined AND/OR condition, and quick-filter change issues a fresh POST followed by a
 * displayed-rows update. The request body the datasource sends
 * (`{ startRow, endRow, sortModel, filterModel }`) is field-compatible with the adapter's
 * `IServerSideGetRowsRequest`, so the server side is unchanged.
 *
 * Runtime note: AG Grid community modules (including the Infinite Row Model) auto-register on import from
 * the umbrella `ag-grid-community` package/CDN bundle (see the import map in `index.html`), so no explicit
 * `ModuleRegistry.registerModules(...)` call is required here.
 */
/** The Vista AG Grid adapter endpoint for the exposed Northwind view. */
const AGGRID_ENDPOINT = '/api/views/vProductCategory/aggrid';
/** How many rows AG Grid requests per block; each block is one POST to the adapter endpoint (R7.3). */
const CACHE_BLOCK_SIZE = 20;
/** Debounce window (ms) for quick-filter keystrokes, so typing does not fire a POST per character. */
const QUICK_FILTER_DEBOUNCE_MS = 250;
/**
 * Column definitions for the six visible `vProductCategory` fields (the hidden key columns ProductId /
 * CategoryId / SupplierId are intentionally not projected as columns). Text fields use
 * `agTextColumnFilter` and numeric fields use `agNumberColumnFilter`; both filter types support AG Grid
 * combined AND/OR conditions out of the box (R7.4).
 *
 * Each column separates `field` from `colId` on purpose. `field` is the row-data accessor and must match
 * the response JSON, which Vista serializes as **camelCase** (`productName`, `unitPrice`, …). `colId` is
 * the identifier AG Grid puts into `sortModel`/`filterModel`, and the adapter matches it against the
 * view's field names, which are **PascalCase** (`ProductName`, `UnitPrice`, …). Keeping both aligns the
 * displayed values with the sort/filter that the server understands.
 */
const columnDefs = [
    { colId: 'ProductName', field: 'productName', headerName: 'Product', filter: 'agTextColumnFilter', minWidth: 220, flex: 2 },
    { colId: 'UnitPrice', field: 'unitPrice', headerName: 'Unit price', filter: 'agNumberColumnFilter', minWidth: 130 },
    { colId: 'UnitsInStock', field: 'unitsInStock', headerName: 'In stock', filter: 'agNumberColumnFilter', minWidth: 120 },
    { colId: 'Discontinued', field: 'discontinued', headerName: 'Discontinued', filter: 'agTextColumnFilter', minWidth: 140 },
    { colId: 'CategoryName', field: 'categoryName', headerName: 'Category', filter: 'agTextColumnFilter', minWidth: 160, flex: 1 },
    { colId: 'SupplierName', field: 'supplierName', headerName: 'Supplier', filter: 'agTextColumnFilter', minWidth: 200, flex: 1 },
];
/** Looks up a required DOM element by id, throwing a clear error if the markup and script disagree. */
function getRequiredElement(id) {
    const element = document.getElementById(id);
    if (element === null) {
        throw new Error(`Expected element #${id} to exist in index.html.`);
    }
    return element;
}
/** Shows an error message in the status area (role="alert"); clearing it when the message is empty. */
function setError(statusEl, message) {
    statusEl.textContent = message;
    statusEl.hidden = message.length === 0;
}
function bootstrap() {
    const gridDiv = getRequiredElement('grid');
    const quickFilterInput = getRequiredElement('quick-filter');
    const errorEl = getRequiredElement('status-error');
    const statusEl = getRequiredElement('status-line');
    setError(errorEl, '');
    // The quick-filter text is sent out-of-band as the `?q=` query-string parameter (read by the adapter
    // from AdapterRequest.Values["q"]); the JSON body stays a faithful IServerSideGetRowsRequest subset.
    const datasource = createVistaAgGridDatasource({
        endpoint: AGGRID_ENDPOINT,
        getQuickFilter: () => quickFilterInput.value,
        onError: (message) => setError(errorEl, message),
    });
    const gridOptions = {
        columnDefs,
        // Every column is sortable and filterable so single-sort, Shift+click multi-sort across columns,
        // text/number filters, and AG Grid combined AND/OR conditions are all exercisable (R7.4).
        defaultColDef: {
            sortable: true,
            filter: true,
            floatingFilter: true,
            resizable: true,
        },
        // Infinite Row Model (community): paging, sorting, and filtering are delegated to the Vista adapter
        // endpoint. Scrolling requests the next block, one POST per block (R7.3).
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
    const api = createGrid(gridDiv, gridOptions);
    // Re-fetch from the server whenever the quick filter changes. With the Infinite Row Model the quick
    // filter is NOT applied client-side; purging the cache forces a fresh POST carrying the new `?q=` value
    // and a full displayed-rows update (R7.4).
    let debounceHandle;
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
}
else {
    bootstrap();
}
//# sourceMappingURL=main.js.map