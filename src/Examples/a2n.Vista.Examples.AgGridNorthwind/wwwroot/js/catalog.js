/**
 * View-catalog fetch for the Northwind showcase View Browser page.
 *
 * The View Browser populates its View Selector from the app-level catalog endpoint
 * `GET /api/showcase/views` (D138), which projects the host's `IViewRegistry.All` to one entry per
 * explicitly-registered view (secure-by-default). This module owns the browser-side fetch and the three
 * outcomes the page must distinguish:
 *
 *  1. a non-empty catalog          → populate the selector;
 *  2. an empty catalog (`[]`)      → show an explicit empty-state and issue no datatable request (R4.5);
 *  3. a network / non-2xx failure  → show a visible error; leave the selector empty; build no grid.
 *
 * `fetchCatalog()` is the low-level primitive: it resolves the parsed entries (possibly empty) and
 * throws on any network or HTTP failure. `loadCatalog()` is the page-facing helper that maps those into
 * a discriminated {@link CatalogSignal}, so callers never have to `try/catch` to tell "empty" from
 * "broken".
 *
 * This is purely additive at the sample layer: it consumes an app-level endpoint and changes no Vista
 * package contract, route, envelope, or error shape.
 */
/** The catalog endpoint served by the showcase host (app-level, inside the D94 auth pipeline). */
export const CATALOG_ENDPOINT = '/api/showcase/views';
/** Narrow untrusted parsed JSON to a well-formed {@link ViewCatalogEntry}. */
function isViewCatalogEntry(value) {
    if (typeof value !== 'object' || value === null) {
        return false;
    }
    const entry = value;
    return (typeof entry['name'] === 'string' &&
        typeof entry['route'] === 'string' &&
        typeof entry['title'] === 'string');
}
/**
 * Fetch and parse the view catalog from {@link CATALOG_ENDPOINT}.
 *
 * Resolves the projected entries (an empty array when the registry has no browsable views). Rejects with
 * an `Error` on a network failure, a non-2xx HTTP status, or a response body that is not a JSON array of
 * well-formed catalog entries — callers that need the empty-vs-failure distinction should prefer
 * {@link loadCatalog}.
 */
export async function fetchCatalog() {
    let response;
    try {
        response = await fetch(CATALOG_ENDPOINT, {
            method: 'GET',
            headers: { Accept: 'application/json' },
        });
    }
    catch (cause) {
        const detail = cause instanceof Error ? `: ${cause.message}` : '';
        throw new Error(`Failed to reach the view catalog at ${CATALOG_ENDPOINT}${detail}.`);
    }
    if (!response.ok) {
        throw new Error(`The view catalog request failed with HTTP ${response.status} ${response.statusText}.`);
    }
    let payload;
    try {
        payload = await response.json();
    }
    catch {
        throw new Error('The view catalog response was not valid JSON.');
    }
    if (!Array.isArray(payload) || !payload.every(isViewCatalogEntry)) {
        throw new Error('The view catalog response was not a list of view entries.');
    }
    return payload;
}
/**
 * Load the catalog into a {@link CatalogSignal} the View Browser page can act on directly.
 *
 * Never throws: a successful non-empty fetch yields `catalog`, a successful empty fetch yields `empty`
 * (R4.5), and any network / HTTP / parse failure yields `error` carrying a human-readable message for a
 * visible error indication.
 */
export async function loadCatalog() {
    try {
        const entries = await fetchCatalog();
        if (entries.length === 0) {
            return { kind: 'empty' };
        }
        return { kind: 'catalog', entries };
    }
    catch (error) {
        const message = error instanceof Error ? error.message : 'Failed to load the view catalog.';
        return { kind: 'error', message };
    }
}
//# sourceMappingURL=catalog.js.map