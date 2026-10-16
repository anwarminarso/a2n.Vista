import {
  createTable,
  functionalUpdate,
  getCoreRowModel,
  type ColumnDef,
  type ColumnFiltersState,
  type PaginationState,
  type SortingState,
  type Table,
  type TableOptionsResolved,
  type Updater,
  type VisibilityState,
} from '@tanstack/table-core';
import { renderNav } from './nav.js';
import {
  fetchTanStackTablePage,
  tanStackEndpoint,
  type TanStackColumnFilter,
  type TanStackTableQueryState,
} from './vistaTanStackQuery.js';

/**
 * Northwind showcase page for TanStack Table (table-core, no React).
 *
 * The table is manual on every server-owned stage: pagination, sorting, and filtering are held as
 * TanStack state and posted to `POST /api/views/vProductCategory/tanstack`. Column visibility is the
 * same state slice — the table hides the column, and the request carries it so the adapter can record
 * it on `SelectFields`. Hiding a column does not remove the field from the response.
 */

interface ProductRow {
  productName?: string;
  unitPrice?: number | null;
  unitsInStock?: number | null;
  discontinued?: boolean;
  categoryName?: string;
  supplierName?: string;
}

const ENDPOINT = tanStackEndpoint('/api/views/vProductCategory');
const FILTER_DEBOUNCE_MS = 250;

const columns: ColumnDef<ProductRow>[] = [
  { id: 'ProductName', accessorKey: 'productName', header: 'Product' },
  { id: 'CategoryName', accessorKey: 'categoryName', header: 'Category' },
  { id: 'SupplierName', accessorKey: 'supplierName', header: 'Supplier' },
  { id: 'UnitPrice', accessorKey: 'unitPrice', header: 'Unit price' },
  { id: 'UnitsInStock', accessorKey: 'unitsInStock', header: 'In stock' },
  { id: 'Discontinued', accessorKey: 'discontinued', header: 'Discontinued' },
];

const priceFormat = new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

let pagination: PaginationState = { pageIndex: 0, pageSize: 10 };
let sorting: SortingState = [{ id: 'ProductName', desc: false }];
let columnFilters: ColumnFiltersState = [];
let globalFilter = '';
let columnVisibility: VisibilityState = {};
let rows: ProductRow[] = [];
let rowCount = 0;
let pageCount = 0;
let requestSeq = 0;

function getRequiredElement<T extends HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (element === null) {
    throw new Error(`Expected element #${id} to exist in tanstack.html.`);
  }
  return element as T;
}

function setError(statusEl: HTMLElement, message: string): void {
  statusEl.textContent = message;
  statusEl.hidden = message.length === 0;
}

function currentState(): TanStackTableQueryState {
  return {
    pagination,
    sorting,
    columnFilters: columnFilters as TanStackColumnFilter[],
    globalFilter,
    columnVisibility,
  };
}

function applyUpdater<T>(updater: Updater<T>, current: T): T {
  return functionalUpdate(updater, current);
}

const table: Table<ProductRow> = createTable<ProductRow>({
  data: [],
  columns,
  getCoreRowModel: getCoreRowModel(),
  state: {},
  onStateChange: () => {},
  renderFallbackValue: null,
  manualPagination: true,
  manualSorting: true,
  manualFiltering: true,
} as TableOptionsResolved<ProductRow>);

function commit(next: {
  pagination?: PaginationState;
  sorting?: SortingState;
  columnFilters?: ColumnFiltersState;
  globalFilter?: string;
  columnVisibility?: VisibilityState;
}): void {
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

function syncOptions(): void {
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

async function load(): Promise<void> {
  const seq = ++requestSeq;
  const statusEl = getRequiredElement<HTMLElement>('status-line');
  const errorEl = getRequiredElement<HTMLElement>('status-error');
  statusEl.textContent = 'Loading…';

  try {
    const page = await fetchTanStackTablePage<ProductRow>(ENDPOINT, currentState());
    if (seq !== requestSeq) {
      return;
    }
    rows = page.rows;
    rowCount = page.rowCount;
    pageCount = page.pageCount;
    setError(errorEl, '');
    render();
  } catch (error) {
    if (seq !== requestSeq) {
      return;
    }
    const message = error instanceof Error ? error.message : String(error);
    setError(errorEl, message);
    statusEl.textContent = rowCount === 0 ? 'No rows loaded.' : `Showing the previous page (${rowCount} matching rows).`;
  }
}

function headerLabel(header: string | unknown, id: string): string {
  return typeof header === 'string' ? header : id;
}

function cellText(columnId: string, value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }
  if (columnId === 'Discontinued') {
    return value === true ? 'Yes' : 'No';
  }
  if (columnId === 'UnitPrice' && typeof value === 'number') {
    return priceFormat.format(value);
  }
  return String(value);
}

function render(): void {
  syncOptions();

  const thead = getRequiredElement<HTMLTableSectionElement>('grid-head');
  const tbody = getRequiredElement<HTMLTableSectionElement>('grid-body');
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
  getRequiredElement<HTMLElement>('status-line').textContent =
    `Showing ${from}–${to} of ${rowCount} (page ${rowCount === 0 ? 0 : pagination.pageIndex + 1} of ${pageCount})`;

  getRequiredElement<HTMLButtonElement>('page-prev').disabled = !table.getCanPreviousPage();
  getRequiredElement<HTMLButtonElement>('page-next').disabled = !table.getCanNextPage();
  renderVisibility();
}

function renderVisibility(): void {
  const host = getRequiredElement<HTMLElement>('column-visibility');
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

function readFilters(): ColumnFiltersState {
  const filters: ColumnFiltersState = [];
  const product = getRequiredElement<HTMLInputElement>('product-filter').value.trim();
  if (product.length > 0) {
    filters.push({ id: 'ProductName', value: product });
  }

  const minText = getRequiredElement<HTMLInputElement>('price-min').value.trim();
  const maxText = getRequiredElement<HTMLInputElement>('price-max').value.trim();
  const min = minText.length > 0 ? Number(minText) : undefined;
  const max = maxText.length > 0 ? Number(maxText) : undefined;
  const range: { min?: number; max?: number } = {};
  if (min !== undefined && Number.isFinite(min)) {
    range.min = min;
  }
  if (max !== undefined && Number.isFinite(max)) {
    range.max = max;
  }
  if (range.min !== undefined || range.max !== undefined) {
    filters.push({ id: 'UnitPrice', value: range });
  }

  const discontinued = getRequiredElement<HTMLSelectElement>('discontinued-filter').value;
  if (discontinued === 'true' || discontinued === 'false') {
    filters.push({ id: 'Discontinued', value: { op: 'equals', value: discontinued === 'true' } });
  }

  return filters;
}

function bootstrap(): void {
  renderNav('tanstack', getRequiredElement<HTMLElement>('nav'));

  const globalInput = getRequiredElement<HTMLInputElement>('global-filter');
  const pageSize = getRequiredElement<HTMLSelectElement>('page-size');
  let filterTimer = 0;

  // One commit carries the global filter and the column filters together. Calling the table's
  // setGlobalFilter and setColumnFilters separately would post two requests.
  const pushFiltersOnce = (): void => {
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
  getRequiredElement<HTMLInputElement>('product-filter').addEventListener('input', pushFiltersOnce);
  getRequiredElement<HTMLInputElement>('price-min').addEventListener('input', pushFiltersOnce);
  getRequiredElement<HTMLInputElement>('price-max').addEventListener('input', pushFiltersOnce);
  getRequiredElement<HTMLSelectElement>('discontinued-filter').addEventListener('change', pushFiltersOnce);

  pageSize.addEventListener('change', () => {
    const size = Number(pageSize.value);
    commit({ pagination: { pageIndex: 0, pageSize: Number.isFinite(size) && size > 0 ? size : pagination.pageSize } });
  });

  getRequiredElement<HTMLButtonElement>('page-prev').addEventListener('click', () => {
    table.previousPage();
  });
  getRequiredElement<HTMLButtonElement>('page-next').addEventListener('click', () => {
    table.nextPage();
  });

  render();
  void load();
}

bootstrap();
