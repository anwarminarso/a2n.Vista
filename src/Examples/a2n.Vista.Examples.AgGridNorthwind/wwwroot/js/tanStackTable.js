import { createTable, functionalUpdate, getCoreRowModel, } from '@tanstack/table-core';
import { renderNav } from './nav.js';
import { fetchTanStackTablePage, tanStackEndpoint, } from './vistaTanStackQuery.js';
const ENDPOINT = tanStackEndpoint('/api/views/vProductCategory');
const FILTER_DEBOUNCE_MS = 250;
const columns = [
    { id: 'ProductName', accessorKey: 'productName', header: 'Product' },
    { id: 'CategoryName', accessorKey: 'categoryName', header: 'Category' },
    { id: 'SupplierName', accessorKey: 'supplierName', header: 'Supplier' },
    { id: 'UnitPrice', accessorKey: 'unitPrice', header: 'Unit price' },
    { id: 'UnitsInStock', accessorKey: 'unitsInStock', header: 'In stock' },
    { id: 'Discontinued', accessorKey: 'discontinued', header: 'Discontinued' },
];
const priceFormat = new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
let pagination = { pageIndex: 0, pageSize: 10 };
let sorting = [{ id: 'ProductName', desc: false }];
let columnFilters = [];
let globalFilter = '';
let columnVisibility = {};
let rows = [];
let rowCount = 0;
let pageCount = 0;
let requestSeq = 0;
function getRequiredElement(id) {
    const element = document.getElementById(id);
    if (element === null) {
        throw new Error(`Expected element #${id} to exist in tanstack.html.`);
    }
    return element;
}
function setError(statusEl, message) {
    statusEl.textContent = message;
    statusEl.hidden = message.length === 0;
}
function currentState() {
    return {
        pagination,
        sorting,
        columnFilters: columnFilters,
        globalFilter,
        columnVisibility,
    };
}
function applyUpdater(updater, current) {
    return functionalUpdate(updater, current);
}
const table = createTable({
    data: [],
    columns,
    getCoreRowModel: getCoreRowModel(),
    state: {},
    onStateChange: () => { },
    renderFallbackValue: null,
    manualPagination: true,
    manualSorting: true,
    manualFiltering: true,
});
function commit(next) {
    if (next.pagination !== undefined) {
        pagination = next.pagination;
    }
    if (next.sorting !== undefined) {
        sorting = next.sorting;
    }
    if (next.columnFilters !== undefined) {
        columnFilters = next.columnFilters;
    }
    if (next.globalFilter !== undefined) {
        globalFilter = next.globalFilter;
    }
    if (next.columnVisibility !== undefined) {
        columnVisibility = next.columnVisibility;
    }
    void load();
}
function syncOptions() {
    table.setOptions((prev) => ({
        ...prev,
        data: rows,
        columns,
        rowCount,
        pageCount,
        manualPagination: true,
        manualSorting: true,
        manualFiltering: true,
        // Feature defaults (columnPinning, rowPinning, …) live on initialState. getHeaderGroups
        // reads columnPinning.left, so an empty state object throws before the first row renders.
        // Owned slices are applied last and stay the source of truth for the request.
        state: {
            ...table.initialState,
            ...prev.state,
            pagination,
            sorting,
            columnFilters,
            globalFilter,
            columnVisibility,
        },
        onPaginationChange: (updater) => {
            commit({ pagination: applyUpdater(updater, pagination) });
        },
        onSortingChange: (updater) => {
            commit({
                sorting: applyUpdater(updater, sorting),
                pagination: { ...pagination, pageIndex: 0 },
            });
        },
        onColumnFiltersChange: (updater) => {
            commit({
                columnFilters: applyUpdater(updater, columnFilters),
                pagination: { ...pagination, pageIndex: 0 },
            });
        },
        onColumnVisibilityChange: (updater) => {
            commit({ columnVisibility: applyUpdater(updater, columnVisibility) });
        },
    }));
}
async function load() {
    const seq = ++requestSeq;
    const statusEl = getRequiredElement('status-line');
    const errorEl = getRequiredElement('status-error');
    statusEl.textContent = 'Loading…';
    try {
        const page = await fetchTanStackTablePage(ENDPOINT, currentState());
        if (seq !== requestSeq) {
            return;
        }
        rows = page.rows;
        rowCount = page.rowCount;
        pageCount = page.pageCount;
        setError(errorEl, '');
        render();
    }
    catch (error) {
        if (seq !== requestSeq) {
            return;
        }
        const message = error instanceof Error ? error.message : String(error);
        setError(errorEl, message);
        statusEl.textContent = rowCount === 0 ? 'No rows loaded.' : `Showing the previous page (${rowCount} matching rows).`;
    }
}
function headerLabel(header, id) {
    return typeof header === 'string' ? header : id;
}
/**
 * Text for one body cell.
 *
 * Discontinued is a bool on the wire (`true` is Yes). Numeric `1` and the text `"1"` / `"true"` are
 * only a defensive fallback for a row that still carries the stored flag.
 */
export function cellText(columnId, value) {
    if (columnId === 'Discontinued') {
        return isDiscontinuedValue(value) ? 'Yes' : 'No';
    }
    if (value === null || value === undefined) {
        return '';
    }
    if (columnId === 'UnitPrice' && typeof value === 'number') {
        return priceFormat.format(value);
    }
    return String(value);
}
function isDiscontinuedValue(value) {
    if (value === true || value === 1) {
        return true;
    }
    if (typeof value !== 'string') {
        return false;
    }
    return value === '1' || value.toLowerCase() === 'true';
}
function render() {
    syncOptions();
    const thead = getRequiredElement('grid-head');
    const tbody = getRequiredElement('grid-body');
    thead.replaceChildren();
    tbody.replaceChildren();
    for (const headerGroup of table.getHeaderGroups()) {
        const tr = document.createElement('tr');
        for (const header of headerGroup.headers) {
            const th = document.createElement('th');
            th.scope = 'col';
            const sorted = header.column.getIsSorted();
            th.setAttribute('aria-sort', sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : 'none');
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'sort-button';
            const mark = sorted === 'asc' ? ' ▲' : sorted === 'desc' ? ' ▼' : '';
            button.textContent = `${headerLabel(header.column.columnDef.header, header.column.id)}${mark}`;
            const toggle = header.column.getToggleSortingHandler();
            if (toggle !== undefined) {
                button.addEventListener('click', (event) => {
                    toggle(event);
                });
            }
            th.appendChild(button);
            tr.appendChild(th);
        }
        thead.appendChild(tr);
    }
    for (const row of table.getRowModel().rows) {
        const tr = document.createElement('tr');
        for (const cell of row.getVisibleCells()) {
            const td = document.createElement('td');
            td.textContent = cellText(cell.column.id, cell.getValue());
            tr.appendChild(td);
        }
        tbody.appendChild(tr);
    }
    const from = rowCount === 0 ? 0 : pagination.pageIndex * pagination.pageSize + 1;
    const to = Math.min(rowCount, from + rows.length - (rowCount === 0 ? 0 : 1));
    getRequiredElement('status-line').textContent =
        `Showing ${from}–${to} of ${rowCount} (page ${rowCount === 0 ? 0 : pagination.pageIndex + 1} of ${pageCount})`;
    getRequiredElement('page-prev').disabled = !table.getCanPreviousPage();
    getRequiredElement('page-next').disabled = !table.getCanNextPage();
    renderVisibility();
}
function renderVisibility() {
    const host = getRequiredElement('column-visibility');
    host.replaceChildren();
    for (const column of table.getAllLeafColumns()) {
        const label = document.createElement('label');
        label.className = 'visibility-option';
        const box = document.createElement('input');
        box.type = 'checkbox';
        box.checked = column.getIsVisible();
        box.addEventListener('change', () => {
            column.toggleVisibility(box.checked);
        });
        label.append(box, document.createTextNode(headerLabel(column.columnDef.header, column.id)));
        host.appendChild(label);
    }
}
function readFilters() {
    const filters = [];
    const product = getRequiredElement('product-filter').value.trim();
    if (product.length > 0) {
        filters.push({ id: 'ProductName', value: product });
    }
    const minText = getRequiredElement('price-min').value.trim();
    const maxText = getRequiredElement('price-max').value.trim();
    const min = minText.length > 0 ? Number(minText) : undefined;
    const max = maxText.length > 0 ? Number(maxText) : undefined;
    const range = {};
    if (min !== undefined && Number.isFinite(min)) {
        range.min = min;
    }
    if (max !== undefined && Number.isFinite(max)) {
        range.max = max;
    }
    if (range.min !== undefined || range.max !== undefined) {
        filters.push({ id: 'UnitPrice', value: range });
    }
    const discontinued = getRequiredElement('discontinued-filter').value;
    if (discontinued === 'true' || discontinued === 'false') {
        filters.push({ id: 'Discontinued', value: { op: 'equals', value: discontinued === 'true' } });
    }
    return filters;
}
function bootstrap() {
    renderNav('tanstack', getRequiredElement('nav'));
    const globalInput = getRequiredElement('global-filter');
    const pageSize = getRequiredElement('page-size');
    let filterTimer = 0;
    // One commit carries the global filter and the column filters together. Calling the table's
    // setGlobalFilter and setColumnFilters separately would post two requests.
    const pushFiltersOnce = () => {
        window.clearTimeout(filterTimer);
        filterTimer = window.setTimeout(() => {
            commit({
                globalFilter: globalInput.value,
                columnFilters: readFilters(),
                pagination: { ...pagination, pageIndex: 0 },
            });
        }, FILTER_DEBOUNCE_MS);
    };
    globalInput.addEventListener('input', pushFiltersOnce);
    getRequiredElement('product-filter').addEventListener('input', pushFiltersOnce);
    getRequiredElement('price-min').addEventListener('input', pushFiltersOnce);
    getRequiredElement('price-max').addEventListener('input', pushFiltersOnce);
    getRequiredElement('discontinued-filter').addEventListener('change', pushFiltersOnce);
    pageSize.addEventListener('change', () => {
        const size = Number(pageSize.value);
        commit({ pagination: { pageIndex: 0, pageSize: Number.isFinite(size) && size > 0 ? size : pagination.pageSize } });
    });
    getRequiredElement('page-prev').addEventListener('click', () => {
        table.previousPage();
    });
    getRequiredElement('page-next').addEventListener('click', () => {
        table.nextPage();
    });
    render();
    void load();
}
// The page script boots the table. Unit tests import {@link cellText} from Node, where there is no document.
if (typeof document !== 'undefined') {
    bootstrap();
}
//# sourceMappingURL=tanStackTable.js.map