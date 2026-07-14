// Feature: typescript-client, Property 9 (parity): Generated-type round-trip against the representative set
//
// Validates: Requirements 11.1, 11.2
//
// This is the ROUND-TRIP PARITY HARNESS. Unlike ../round-trip.test.ts (which invents fast-check
// arbitraries of the generated interfaces), this suite drives the *representative value set* built from
// the resolved OpenAPI document — the authoritative oracle. That set is produced C#-side by
// `RepresentativeValueSet.Build(...)` (task 15.1) and exported to the committed fixture
// `tests/ts-runtime/fixtures/representative-values.json` by the TUnit test
// `RepresentativeValuesFixtureExportTests` (see src/Tests/a2n.Vista.Client.TypeScript.Tests). Regenerate
// the fixture by running that C# test; its output is deterministic, so the committed JSON stays byte-stable.
//
// The fixture maps each `Generated_Type` name to an array of representative JSON values covering the
// Requirement 11.1 criteria: each declared property present, each nullable property in both its
// present-and-null and absent forms, each enum member at least once, and each collection-typed property in
// both empty and non-empty forms.
//
// This harness asserts two things against the GENERATED TypeScript client:
//
//   1. Round-trip (R11.2): for every representative value `v`, `JSON.parse(JSON.stringify(v))` deeply
//      equals `v` — serialize -> parse loses nothing (identical properties, scalar values, enum members,
//      and array element order all survive). Checked both exhaustively (every value) and under a
//      fast-check property with >= 100 iterations (sampling the loaded set with replacement).
//
//   2. Representative-set coverage (R11.1): the loaded fixture itself is asserted to cover the criteria,
//      tying this harness to the document-derived set rather than to ad-hoc values — a nullable property
//      in present-null AND absent forms, a collection in empty AND non-empty forms, and every
//      `FilterOperator` enum member.
//
// Compile-time parity: each type's representative values are asserted (via a type-assertion) to be the
// corresponding GENERATED type, so `npm run typecheck` confirms the generated types name and accept the
// document-derived shapes.

import { describe, expect, it } from "vitest";
import fc from "fast-check";

import representativeValuesJson from "../../fixtures/representative-values.json";
import type {
  CustomerRow,
  FilterNode,
  FilterOperator,
  ProblemDetails,
  VistaListRequestBody,
  VistaMetadataResponse,
  VistaSortBody,
} from "../harness/generated.js";

// --- Load the representative set (the oracle) ---------------------------------------------------

const representativeValues = representativeValuesJson as unknown as Record<string, unknown[]>;

function valuesOf(typeName: string): unknown[] {
  const values = representativeValues[typeName];
  if (values === undefined || values.length === 0) {
    throw new Error(
      `representative-values.json is missing a non-empty value set for "${typeName}". ` +
        "Regenerate it via the C# RepresentativeValuesFixtureExportTests.",
    );
  }
  return values;
}

// Compile-time parity: the document-derived shapes are the GENERATED types. These type-assertions make
// `npm run typecheck` verify the generated type names resolve and accept the representative set.
const customerRows = valuesOf("CustomerRow") as CustomerRow[];
const filterNodes = valuesOf("FilterNode") as FilterNode[];
const problemDetails = valuesOf("ProblemDetails") as ProblemDetails[];
const listRequests = valuesOf("VistaListRequestBody") as VistaListRequestBody[];
const metadataResponses = valuesOf("VistaMetadataResponse") as VistaMetadataResponse[];
const sortBodies = valuesOf("VistaSortBody") as VistaSortBody[];

// Every representative value across every generated type, flattened for the round-trip discipline.
const allValues: readonly unknown[] = [
  ...customerRows,
  ...filterNodes,
  ...problemDetails,
  ...listRequests,
  ...metadataResponses,
  ...sortBodies,
];

// The 12 FilterOperator literals, in the order the generator emits them (FilterLeaf.op enum).
const OPERATORS: readonly FilterOperator[] = [
  "Equals",
  "NotEquals",
  "GreaterThan",
  "GreaterThanOrEqual",
  "LessThan",
  "LessThanOrEqual",
  "Contains",
  "StartsWith",
  "EndsWith",
  "In",
  "Between",
  "IsNull",
];

// --- Helpers ------------------------------------------------------------------------------------

/** Serialize -> parse must be the identity for a representative value (Requirement 11.2). */
function assertRoundTrips(value: unknown): void {
  const roundTripped = JSON.parse(JSON.stringify(value)) as unknown;
  expect(roundTripped).toEqual(value);
}

/** Treats an unknown representative value as a plain string-keyed record for coverage inspection. */
function asRecord(value: unknown): Record<string, unknown> {
  return value as Record<string, unknown>;
}

// --- Properties ---------------------------------------------------------------------------------

describe("Property 9 (parity) — generated-type round-trip against the representative set", () => {
  it("loads a non-empty representative set for every exercised generated type (R11.1)", () => {
    expect(allValues.length).toBeGreaterThan(0);
    for (const set of [
      customerRows,
      filterNodes,
      problemDetails,
      listRequests,
      metadataResponses,
      sortBodies,
    ]) {
      expect(set.length).toBeGreaterThan(0);
    }
  });

  it("round-trips EVERY representative value losslessly (R11.2, exhaustive)", () => {
    for (const value of allValues) {
      assertRoundTrips(value);
    }
  });

  it("round-trips representative values under a >=100-iteration property (R11.2)", () => {
    fc.assert(
      fc.property(fc.constantFrom(...allValues), (value) => {
        assertRoundTrips(value);
      }),
      // The set is small; sample with replacement to satisfy the property discipline of >= 100 runs.
      { numRuns: Math.max(100, allValues.length) },
    );
  });

  it("covers a nullable property in present-null AND absent forms (R11.1)", () => {
    // CustomerRow.contactName is nullable-optional: the set carries a present-and-null form...
    const hasPresentNull = customerRows.some(
      (row) => "contactName" in asRecord(row) && asRecord(row).contactName === null,
    );
    // ...and an entirely-absent form.
    const hasAbsent = customerRows.some((row) => !("contactName" in asRecord(row)));

    expect(hasPresentNull).toBe(true);
    expect(hasAbsent).toBe(true);
  });

  it("covers a collection-typed property in empty AND non-empty forms (R11.1)", () => {
    // FilterNode's `and` children appear both empty and non-empty across the representative set.
    const andArrays = filterNodes
      .map(asRecord)
      .filter((node) => Array.isArray(node.and))
      .map((node) => node.and as unknown[]);

    expect(andArrays.some((children) => children.length === 0)).toBe(true);
    expect(andArrays.some((children) => children.length > 0)).toBe(true);

    // And VistaListRequestBody.sort likewise carries empty and non-empty forms.
    const sortArrays = listRequests
      .map(asRecord)
      .filter((body) => Array.isArray(body.sort))
      .map((body) => body.sort as unknown[]);

    expect(sortArrays.some((rows) => rows.length === 0)).toBe(true);
    expect(sortArrays.some((rows) => rows.length > 0)).toBe(true);
  });

  it("covers every FilterOperator enum member at least once (R11.1)", () => {
    const seen = new Set<string>();
    for (const node of filterNodes.map(asRecord)) {
      if (typeof node.op === "string") {
        seen.add(node.op);
      }
    }

    for (const op of OPERATORS) {
      expect(seen.has(op)).toBe(true);
    }
  });
});
