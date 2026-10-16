/**
 * Framework-agnostic client for `POST {route}/tanstack` (D158).
 *
 * TanStack Table does not define an HTTP protocol. A manual table already holds the state slices the
 * adapter binds — `pagination`, `sorting`, `columnFilters`, `globalFilter`, `columnVisibility` — and
 * this module posts that state and reads `{ rows, rowCount, pageCount }` back. React, Vue, and Solid
 * callers pass the same object their table state already is; the Northwind page uses it with
 * `@tanstack/table-core` directly.
 */

/** TanStack `PaginationState`. */
export interface TanStackPaginationState {
  pageIndex: number;
  pageSize: number;
}

/** TanStack `ColumnSort`. `id` is the view field name. */
export interface TanStackColumnSort {
  id: string;
  desc: boolean;
}

/** A `{ min, max }` range. An omitted side is unbounded. */
export interface TanStackRangeFilter {
  min?: number | string | null;
  max?: number | string | null;
}

/** An explicit operator. `op` is one of the names the adapter documents (`contains`, `gte`, …). */
export interface TanStackOperatorFilter {
  op: string;
  value?: unknown;
}

/**
 * A column-filter value. A string is `contains`, a number or boolean is `equals`, an array is `in`,
 * null is `isNull`. Ranges and named operators use the object forms.
 */
export type TanStackColumnFilterValue =
  | string
  | number
  | boolean
  | null
  | readonly (string | number | boolean | null)[]
  | TanStackRangeFilter
  | TanStackOperatorFilter;

/** TanStack `ColumnFilter`. */
export interface TanStackColumnFilter {
  id: string;
  value: TanStackColumnFilterValue;
}

/** The slice of table state the adapter reads. */
export interface TanStackTableQueryState {
  pagination: TanStackPaginationState;
  sorting: readonly TanStackColumnSort[];
  columnFilters: readonly TanStackColumnFilter[];
  globalFilter: string;
  columnVisibility: Readonly<Record<string, boolean>>;
}

/** The adapter response. `rows` is the page (`data` on the table); `rowCount` is the filtered total. */
export interface TanStackTablePage<T> {
  rows: T[];
  rowCount: number;
  pageCount: number;
}

/** Thrown when the endpoint responds with a non-2xx status. The current page of rows is left untouched. */
export class TanStackTableRequestError extends Error {
  readonly status: number;
  readonly statusText: string;
  readonly endpoint: string;

  constructor(status: number, statusText: string, endpoint: string) {
    super(`Request to ${endpoint} failed with HTTP ${status} ${statusText}.`);
    this.name = 'TanStackTableRequestError';
    this.status = status;
    this.statusText = statusText;
    this.endpoint = endpoint;
  }
}

/** The route suffix the adapter mounts (`POST {route}/tanstack`). */
export const TANSTACK_ROUTE_SUFFIX = 'tanstack';

/** Appends the adapter suffix to a view route, trimming one trailing slash. */
export function tanStackEndpoint(viewRoute: string): string {
  const base = viewRoute.endsWith('/') ? viewRoute.slice(0, -1) : viewRoute;
  return `${base}/${TANSTACK_ROUTE_SUFFIX}`;
}

/**
 * Copies the table state into the JSON body the adapter binds. The copy is the wire shape: no query
 * string, no renamed fields. Column ids pass through unchanged.
 */
export function buildTanStackTableRequest(state: TanStackTableQueryState): TanStackTableQueryState {
  return {
    pagination: {
      pageIndex: state.pagination.pageIndex,
      pageSize: state.pagination.pageSize,
    },
    sorting: state.sorting.map((entry) => ({ id: entry.id, desc: entry.desc })),
    columnFilters: state.columnFilters.map((entry) => ({ id: entry.id, value: entry.value })),
    globalFilter: state.globalFilter,
    columnVisibility: { ...state.columnVisibility },
  };
}

/**
 * POSTs {@link buildTanStackTableRequest} to `endpoint` and returns the page.
 * `fetchImpl` defaults to the global `fetch` so tests can pass a stub.
 */
export async function fetchTanStackTablePage<T>(
  endpoint: string,
  state: TanStackTableQueryState,
  fetchImpl: typeof fetch = fetch,
): Promise<TanStackTablePage<T>> {
  const response = await fetchImpl(endpoint, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    },
    body: JSON.stringify(buildTanStackTableRequest(state)),
  });

  if (!response.ok) {
    throw new TanStackTableRequestError(response.status, response.statusText, endpoint);
  }

  return (await response.json()) as TanStackTablePage<T>;
}
