/**
 * Pure metadata -> column-definition mapping for the View Browser page.
 *
 * The View Browser never hard-codes columns: it discovers them from a view's metadata
 * (`GET {route}/metadata`). `buildColumnDefs` is the deterministic, side-effect-free transform
 * that turns that metadata into the column descriptors the grid renders and the affordances
 * (sort / search / filter) it enables per column.
 *
 * This module owns no I/O and touches no DOM so it can be exercised by property-based tests
 * (design Property 1). Keep it pure and deterministic.
 */
/**
 * Lower-case the first character of a PascalCase name to derive the camelCase row-rendering key,
 * leaving the remainder untouched. Empty names pass through unchanged.
 */
function toCamelCase(pascalName) {
    if (pascalName.length === 0) {
        return pascalName;
    }
    return pascalName.charAt(0).toLowerCase() + pascalName.slice(1);
}
/**
 * Build the grid column definitions for a view from its metadata.
 *
 * Produces exactly one column per non-hidden field (and none for hidden fields), preserving the
 * order of `meta.fields`. Each column's `colId` is the field's PascalCase name and its
 * `sortable` / `searchable` / `filterable` flags equal the field's `isSortable` / `isSearchable` /
 * `isFilterable` flags respectively.
 *
 * @param meta The view metadata whose fields drive the columns.
 * @returns The column definitions, in field order, for the non-hidden fields.
 */
export function buildColumnDefs(meta) {
    return meta.fields
        .filter((field) => !field.isHidden)
        .map((field) => ({
        field: toCamelCase(field.name),
        colId: field.name,
        title: field.label,
        sortable: field.isSortable,
        searchable: field.isSearchable,
        filterable: field.isFilterable,
    }));
}
//# sourceMappingURL=columns.js.map