import type { IDatasource, IGetRowsParams } from 'ag-grid-community';

/**
 * Options for {@link createVistaAgGridDatasource}.
 *
 * The datasource is deliberately thin (D136): the M17 generated TypeScript client is OpenAPI-driven and
 * the `{route}/aggrid` adapter endpoint is not part of the OpenAPI document, so this datasource is
 * hand-written against the AG Grid row-model contract.
 *
 * It targets AG Grid's community **Infinite Row Model** (`IDatasource`). AG Grid's server-side row model
 * is an Enterprise-only feature at runtime, whereas the Infinite Row Model ships in `ag-grid-community`
 * and provides the same block-paging-per-request behaviour the Vista AG Grid adapter expects: each block
 * request carries `startRow`/`endRow` paging plus the grid's `sortModel`/`filterModel`, which the adapter
 * binds unchanged.
 */
export interface VistaAgGridDatasourceOptions {
  /**
   * The Vista AG Grid adapter endpoint URL, i.e. `{route}/aggrid`
   * (for example `/api/views/vProductCategory/aggrid`). May be absolute or relative.
   */
  endpoint: string;

  /**
   * Optional supplier of the current quick-filter (global search) text. When it returns a non-empty
   * value, the trimmed text is appended to the request URL as the `?q=` query-string parameter — the
   * out-of-band transport the adapter reads from `AdapterRequest.Values["q"]`.
   */
  getQuickFilter?: () => string;

  /**
   * Optional callback invoked with a human-readable message whenever a request fails (HTTP error or
   * network failure). Use it to surface a visible error indication in the UI.
   */
  onError?: (message: string) => void;
}

/**
 * The success payload returned by `POST {route}/aggrid`. Mirrors the AG Grid server-side row model
 * `LoadSuccessParams` shape: `rowData` are the rows for the requested block and `rowCount` is the total
 * number of matching rows before paging (used here as the Infinite Row Model's known last-row count).
 */
interface VistaAgGridResponse {
  rowData: unknown[];
  rowCount: number;
}

/**
 * Builds a thin AG Grid {@link IDatasource} (Infinite Row Model) that drives a Vista view through its AG
 * Grid adapter endpoint.
 *
 * On each block request it POSTs `{ startRow, endRow, sortModel, filterModel }` — assembled from the AG
 * Grid {@link IGetRowsParams} — as JSON to {@link VistaAgGridDatasourceOptions.endpoint}, appending the
 * current quick-filter text as `?q=` when present. This body is field-compatible with the AG Grid
 * `IServerSideGetRowsRequest` the Vista adapter binds, so the server side is identical regardless of which
 * AG Grid row model the front-end uses. A successful `{ rowData, rowCount }` response is forwarded to
 * `params.successCallback(rowData, rowCount)` (the second argument is the known total row count, so AG
 * Grid can detect the last block). On a non-success response (HTTP error) or a network failure it calls
 * `params.failCallback()` — which leaves the currently displayed rows unchanged — and surfaces the error
 * through {@link VistaAgGridDatasourceOptions.onError} (R7.7).
 */
export function createVistaAgGridDatasource(
  options: VistaAgGridDatasourceOptions,
): IDatasource {
  return {
    async getRows(params: IGetRowsParams): Promise<void> {
      const url = buildRequestUrl(options.endpoint, options.getQuickFilter?.());

      // Assemble the adapter request body from the Infinite Row Model params. The field names
      // (startRow/endRow/sortModel/filterModel) match the fields the Vista AG Grid adapter binds from the
      // JSON body, so the request is a faithful IServerSideGetRowsRequest subset.
      const body = JSON.stringify({
        startRow: params.startRow,
        endRow: params.endRow,
        sortModel: params.sortModel,
        filterModel: params.filterModel,
      });

      try {
        const response = await fetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body,
        });

        if (!response.ok) {
          // Non-success HTTP status (e.g. RFC 7807 400 from the adapter/engine). Fail the block and keep
          // the current rows; do not attempt to parse an error body as row data.
          reportFailure(
            params,
            options,
            `Request to ${url} failed with HTTP ${response.status} ${response.statusText}.`,
          );
          return;
        }

        const payload = (await response.json()) as VistaAgGridResponse;
        // The second argument is the known total row count, so AG Grid stops requesting further blocks
        // once the last matching row has been served (last-block detection).
        params.successCallback(payload.rowData, payload.rowCount);
      } catch (error) {
        // Network failure or malformed JSON. Fail the block, leave current rows unchanged, surface error.
        const detail = error instanceof Error ? error.message : String(error);
        reportFailure(params, options, `Could not load rows from ${url}: ${detail}`);
      }
    },
  };
}

/**
 * Appends the quick-filter text as a `?q=` query-string parameter when it is non-empty after trimming.
 * Preserves any existing query string on the endpoint by choosing the correct separator.
 */
function buildRequestUrl(endpoint: string, quickFilter: string | undefined): string {
  const trimmed = quickFilter?.trim();
  if (trimmed === undefined || trimmed.length === 0) {
    return endpoint;
  }

  const separator = endpoint.includes('?') ? '&' : '?';
  return `${endpoint}${separator}q=${encodeURIComponent(trimmed)}`;
}

/**
 * Fails the current block (leaving displayed rows unchanged) and reports a visible error, if a callback
 * was provided.
 */
function reportFailure(
  params: IGetRowsParams,
  options: VistaAgGridDatasourceOptions,
  message: string,
): void {
  params.failCallback();
  options.onError?.(message);
}
