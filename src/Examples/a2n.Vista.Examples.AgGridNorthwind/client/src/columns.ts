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
 * The subset of a view's field metadata (`VistaMetadataResponse.fields[]`) the column mapping
 * consumes. Field names are PascalCase server field names used as the sort/filter key.
 */
export interface VistaFieldMetadata {
  /** PascalCase server field name; the sort/filter matching key. */
  name: string;
  /** Human-readable header text for the column. */
  label: string;
  /** CLR type name, e.g. "String" | "Int32" | "Decimal" | "DateTime" | "Boolean". */
  clrType: string;
  /** Whether the field may participate in the structured (advanced) filter. */
  isFilterable: boolean;
  /** Whether the field may be sorted server-side. */
  isSortable: boolean;
  /** Whether the field participates in the global-search channel. */
  isSearchable: boolean;
  /** Whether the field is part of the view's primary key. */
  isPrimaryKey: boolean;
  /** Whether the field is hidden and therefore produces no column. */
  isHidden: boolean;
}

/** The consumed shape of a view's metadata document (structural subset used here). */
export interface VistaMetadata {
  name: string;
  route: string;
  keyFields: string[];
  fields: VistaFieldMetadata[];
}

/** A grid column descriptor produced from a single non-hidden field. */
export interface ColumnDef {
  /** camelCase field name used for row rendering. */
  field: string;
  /** PascalCase field name used for server sort/filter matching. */
  colId: string;
  /** Column header text. */
  title: string;
  /** Sort affordance enabled iff the field is sortable. */
  sortable: boolean;
  /** Global-search affordance enabled iff the field is searchable. */
  searchable: boolean;
  /** Advanced-filter affordance enabled iff the field is filterable. */
  filterable: boolean;
}

/**
 * Lower-case the first character of a PascalCase name to derive the camelCase row-rendering key,
 * leaving the remainder untouched. Empty names pass through unchanged.
 */
function toCamelCase(pascalName: string): string {
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
export function buildColumnDefs(meta: VistaMetadata): ColumnDef[] {
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
