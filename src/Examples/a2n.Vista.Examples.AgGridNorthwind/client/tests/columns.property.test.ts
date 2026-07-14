// Feature: northwind-sample-showcase, Property 1: Metadata drives columns and column affordances
//
// Validates: Requirements 2.2, 2.4
//
// The View Browser never hard-codes columns; it derives them from a view's metadata
// (`GET {route}/metadata`) via the pure `buildColumnDefs` transform. This suite pins that
// transform's contract over a wide, bucketed input space (minimum 100 runs):
//
//   * Column/field bijection over NON-HIDDEN fields (R2.2): `buildColumnDefs` emits exactly one
//     column per non-hidden field, in field order, and zero columns for hidden fields. Generators
//     cover the boundary buckets explicitly — all-hidden, none-hidden, and mixed.
//
//   * Per-field affordance equality (R2.4): each produced column's `colId` equals the field's
//     PascalCase `name`, its `field` is the camelCase of that name, its `title` equals the field's
//     `label`, and its `sortable` / `searchable` / `filterable` flags equal the field's
//     `isSortable` / `isSearchable` / `isFilterable` flags respectively.
//
// The assertions are cross-checked against an independent oracle written without reusing the code
// under test, so the property does not tautologically restate the implementation.

import { describe, expect, it } from "vitest";
import fc from "fast-check";

import { buildColumnDefs } from "../src/columns";
import type { VistaFieldMetadata, VistaMetadata } from "../src/columns";

// --- Independent oracle ------------------------------------------------------------------------

// Independent camelCase derivation (mirrors the spec: lower-case the first char, keep the rest;
// empty passes through). Written separately from the implementation's private `toCamelCase`.
function oracleCamelCase(pascalName: string): string {
  if (pascalName.length === 0) {
    return pascalName;
  }
  return pascalName.charAt(0).toLowerCase() + pascalName.slice(1);
}

// --- Generators --------------------------------------------------------------------------------

// A spread of representative CLR type names — the transform must not depend on the CLR type, so we
// vary it freely to prove that independence.
const clrTypeArb: fc.Arbitrary<string> = fc.constantFrom(
  "String",
  "Int32",
  "Int64",
  "Decimal",
  "Double",
  "DateTime",
  "DateTimeOffset",
  "Boolean",
  "Guid",
  "Byte[]",
);

// PascalCase-ish server field names plus the empty-name boundary (exercises camelCase passthrough).
const fieldNameArb: fc.Arbitrary<string> = fc.oneof(
  fc.stringMatching(/^[A-Za-z][A-Za-z0-9]{0,15}$/),
  fc.constant(""),
);

// A single field whose `isHidden` is drawn from a supplied strategy so callers can pin the
// all-hidden / none-hidden / mixed buckets. All other flags are independent booleans.
function makeFieldArb(
  hiddenArb: fc.Arbitrary<boolean>,
): fc.Arbitrary<VistaFieldMetadata> {
  return fc.record<VistaFieldMetadata>({
    name: fieldNameArb,
    label: fc.string(),
    clrType: clrTypeArb,
    isFilterable: fc.boolean(),
    isSortable: fc.boolean(),
    isSearchable: fc.boolean(),
    isPrimaryKey: fc.boolean(),
    isHidden: hiddenArb,
  });
}

// A metadata document whose fields all share the given hidden-strategy. Includes the empty-field
// (zero column) boundary via `minLength: 0`.
function makeMetaArb(hiddenArb: fc.Arbitrary<boolean>): fc.Arbitrary<VistaMetadata> {
  return fc
    .array(makeFieldArb(hiddenArb), { minLength: 0, maxLength: 20 })
    .map((fields) => ({
      name: "vSample",
      route: "/api/views/sample",
      keyFields: fields.filter((f) => f.isPrimaryKey).map((f) => f.name),
      fields,
    }));
}

// The union spreads each run across the boundary buckets: fully mixed flags, all-hidden (no
// columns), and none-hidden (every field becomes a column).
const metaArb: fc.Arbitrary<VistaMetadata> = fc.oneof(
  makeMetaArb(fc.boolean()),
  makeMetaArb(fc.constant(true)),
  makeMetaArb(fc.constant(false)),
);

// --- Test --------------------------------------------------------------------------------------

describe("Property 1 — metadata drives columns and column affordances (R2.2, R2.4)", () => {
  it("emits one column per non-hidden field, in order, with affordances equal to the field flags", () => {
    fc.assert(
      fc.property(metaArb, (meta) => {
        const columns = buildColumnDefs(meta);

        // Oracle: the non-hidden fields, in field order, are the exact source of columns.
        const expected = meta.fields.filter((field) => !field.isHidden);

        // Bijection over non-hidden fields (R2.2): equal counts, hidden fields contribute none.
        expect(columns.length).toBe(expected.length);

        // Positional one-to-one mapping preserving field order, with per-field affordance equality.
        for (let i = 0; i < expected.length; i++) {
          const field = expected[i]!;
          const column = columns[i]!;

          // colId is the PascalCase field name — the server sort/filter matching key.
          expect(column.colId).toBe(field.name);
          // field is the camelCase row-rendering key derived from the name.
          expect(column.field).toBe(oracleCamelCase(field.name));
          // title is the field label verbatim.
          expect(column.title).toBe(field.label);

          // Affordances (R2.4) equal the field's own flags.
          expect(column.sortable).toBe(field.isSortable);
          expect(column.searchable).toBe(field.isSearchable);
          expect(column.filterable).toBe(field.isFilterable);
        }

        // No hidden field ever produces a column: every emitted colId maps back to a non-hidden
        // field at the same ordinal (guards against accidental inclusion/reordering).
        const hiddenNames = new Set(
          meta.fields.filter((field) => field.isHidden).map((field) => field.name),
        );
        for (let i = 0; i < columns.length; i++) {
          if (!expected[i]!.isHidden) {
            continue;
          }
          // Unreachable by construction; asserted for clarity if the oracle ever changes.
          expect(hiddenNames.has(columns[i]!.colId)).toBe(false);
        }
      }),
      { numRuns: 100 },
    );
  });
});
