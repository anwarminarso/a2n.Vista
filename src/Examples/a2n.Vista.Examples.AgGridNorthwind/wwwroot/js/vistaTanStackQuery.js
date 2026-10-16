/**
 * Framework-agnostic client for `POST {route}/tanstack` (D158).
 *
 * TanStack Table does not define an HTTP protocol. A manual table already holds the state slices the
 * adapter binds — `pagination`, `sorting`, `columnFilters`, `globalFilter`, `columnVisibility` — and
 * this module posts that state and reads `{ rows, rowCount, pageCount }` back. React, Vue, and Solid
 * callers pass the same object their table state already is; the Northwind page uses it with
 * `@tanstack/table-core` directly.
 */
/** Thrown when the endpoint responds with a non-2xx status. The current page of rows is left untouched. */
export class TanStackTableRequestError extends Error {
    constructor(status, statusText, endpoint) {
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
export function tanStackEndpoint(viewRoute) {
    const base = viewRoute.endsWith('/') ? viewRoute.slice(0, -1) : viewRoute;
    return `${base}/${TANSTACK_ROUTE_SUFFIX}`;
}
/**
 * Copies the table state into the JSON body the adapter binds. The copy is the wire shape: no query
 * string, no renamed fields. Column ids pass through unchanged.
 */
export function buildTanStackTableRequest(state) {
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
export async function fetchTanStackTablePage(endpoint, state, fetchImpl = fetch) {
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
    return (await response.json());
}
//# sourceMappingURL=vistaTanStackQuery.js.map