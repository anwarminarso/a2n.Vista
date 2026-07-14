/**
 * Minimal ambient type declarations for the third-party browser globals the View Browser page loads
 * from a CDN: jQuery, DataTables.NET, and jQuery-QueryBuilder.
 *
 * The showcase deliberately avoids pulling in the heavyweight `@types/jquery`, `@types/datatables.net`,
 * and QueryBuilder typing packages. Those CDN libraries are consumed only as loosely-typed browser
 * globals, so a small, hand-written surface keeps `tsc --noEmit` green without the maintenance cost and
 * version coupling of the full `@types` packages. Members are intentionally added here only as the
 * client modules come to depend on them.
 *
 * AG Grid is not declared here: its community package (`ag-grid-community`) ships first-party types and
 * is imported directly (type-only) by the AG Grid datasource module, while its runtime ESM is loaded via
 * a CDN import map.
 */

export {};

declare global {
  /**
   * The subset of the DataTables.NET API surface the View Browser orchestration uses. Returned by
   * initialising or re-accessing a DataTable on a matched table element.
   */
  interface DataTablesApi {
    /** Redraw the table, optionally resetting paging back to the first page. */
    draw(resetPaging?: boolean): DataTablesApi;
    /** Tear the instance down and restore the original element, so a view switch leaks no state. */
    destroy(remove?: boolean): DataTablesApi;
    /** Apply a global search term to the server-side request. */
    search(term: string): DataTablesApi;
    /** Attach a (optionally namespaced) DataTables event handler, e.g. `error.dt` / `xhr.dt`. */
    on(events: string, handler: (...args: unknown[]) => void): DataTablesApi;
    /** The server-side data controller; `reload()` re-issues the current request. */
    ajax: {
      /** Re-issue the server-side request, optionally without resetting paging. */
      reload(callback?: ((json: unknown) => void) | null, resetPaging?: boolean): DataTablesApi;
    };
  }

  /** A jQuery-wrapped set of elements, narrowed to only the members the showcase relies on. */
  interface JQuery {
    /** Initialise (or re-initialise) a DataTables.NET instance on the matched table element. */
    DataTable(options?: unknown): DataTablesApi;
    /** Initialise jQuery-QueryBuilder on the matched element. */
    queryBuilder(options?: unknown): JQuery;
    /** Invoke a jQuery-QueryBuilder method (e.g. `getRules`, `reset`, `destroy`). */
    queryBuilder(method: string, ...args: unknown[]): unknown;
    /** Attach a (optionally namespaced) event handler, e.g. `error.dt` / `xhr.dt`. */
    on(events: string, handler: (...args: unknown[]) => void): JQuery;
    /** Read the value of the matched form element (e.g. the search box). */
    val(): string | number | string[] | undefined;
    /** Write the value of the matched form element. */
    val(value: string): JQuery;
    /** Read the text content of the matched element. */
    text(): string;
    /** Write the text content of the matched element (e.g. an error/empty-state message). */
    text(value: string): JQuery;
    /** Show the matched elements. */
    show(): JQuery;
    /** Hide the matched elements. */
    hide(): JQuery;
    /** Number of matched elements. */
    length: number;
  }

  /**
   * The jQuery entry point (`$` / `jQuery`) as loaded from the CDN: callable as a selector or a
   * DOM-ready registration, and carrying the static helpers the showcase uses.
   */
  interface JQueryStatic {
    (selector: string | Element | Document): JQuery;
    (readyCallback: () => void): void;
    /** Perform an AJAX request (used indirectly by DataTables' server-side transport). */
    ajax(settings: unknown): unknown;
    /**
     * The jQuery plugin namespace. DataTables hangs its global extension surface here; the showcase
     * only needs `dataTable.ext.errMode` so it can opt out of DataTables' default alert() on error
     * and surface RFC 7807 problems in-page instead.
     */
    fn: {
      dataTable: {
        ext: {
          /** Global error-reporting mode; set to `'none'` to suppress the default alert. */
          errMode: string;
        };
      };
    };
  }

  const $: JQueryStatic;
  const jQuery: JQueryStatic;
}
