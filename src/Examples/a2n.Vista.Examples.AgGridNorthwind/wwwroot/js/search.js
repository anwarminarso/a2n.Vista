/**
 * Global-search minimum-length gate for the View Browser page.
 *
 * DynData's "Table Browser" used `minGlobalSearchCharLength: 3`, meaning a
 * global-search request is only issued once the entered term reaches the
 * configured minimum length. This module reproduces that behavior as a single
 * pure, deterministic function so it can be property-tested (design Property 3).
 */
/**
 * Decides whether a global-search request should be issued for the given term.
 *
 * The term is trimmed before its length is measured, so leading/trailing
 * whitespace never counts toward the minimum (whitespace-only terms are treated
 * as empty). Returns `false` when the trimmed length is strictly below
 * `minLen`, and `true` otherwise.
 *
 * @param term   The raw search term entered by the user.
 * @param minLen The configured minimum global-search length (e.g. 3).
 * @returns `true` when a server-side search should be issued, `false` otherwise.
 */
export function shouldIssueSearch(term, minLen) {
    return term.trim().length >= minLen;
}
//# sourceMappingURL=search.js.map