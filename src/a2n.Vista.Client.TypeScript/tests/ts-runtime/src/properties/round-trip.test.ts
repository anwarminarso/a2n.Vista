// Feature: typescript-client, Property 9: Generated-type round-trip
//
// Validates: Requirements 11.2, 2.2, 2.3
//
// This suite exercises the *committed generated client* under `tests/ts-runtime/generated/`
// (see ../harness/generated.ts for how that fixture was produced). It builds fast-check
// arbitraries that PRODUCE well-typed values of the generated interfaces — the recursive,
// presence-discriminated `FilterNode` family (`FilterLeaf`/`FilterAnd`/`FilterOr`/`FilterNot`),
// plus the representative object types `VistaListRequestBody` (with nested `FilterNode` in
// `filter`/`scope` and a `VistaSortBody[]` in `sort`) and `CustomerRow` — and asserts two things:
//
//   1. Round-trip (R11.2): for every generated value `v`, `JSON.parse(JSON.stringify(v))` deeply
//      equals `v`. Serialize -> parse loses nothing: identical properties, scalar values, enum
//      members, and array element order all survive.
//
//   2. Presence narrowing (R2.2/2.3): the bare, discriminator-less `FilterNode` union narrows to
//      exactly ONE member by which key is present (`and` -> FilterAnd, `or` -> FilterOr,
//      `not` -> FilterNot, else FilterLeaf). Every node in every generated tree matches exactly one
//      variant, and the recursive edges (`FilterNode[]` for and/or, `FilterNode` for not) are walked
//      to arbitrary depth.
//
// Optional-vs-undefined approach (so JSON round-trips cleanly): the arbitraries NEVER set a property
// to `undefined`. An *absent* optional field means the key is simply not present on the object
// (fast-check's `requiredKeys` omits the key entirely rather than assigning `undefined`), and a
// *present* nullable field carries an explicit `null` (`fc.option(..., { nil: null })`). Because no
// property ever holds `undefined`, `JSON.stringify` drops nothing and the parsed object is deeply
// equal to the original. Leaf `value` payloads are drawn from a JSON-safe scalar/array arbitrary
// (no `undefined`, `NaN`, `Infinity`, or `-0`) so they too survive the round-trip verbatim.
//
// Minimum 100 runs (fast-check `numRuns: 100`).

import { describe, expect, it } from "vitest";
import fc from "fast-check";

import type {
  CustomerRow,
  FilterLeaf,
  FilterNode,
  FilterOperator,
  VistaListRequestBody,
  VistaSortBody,
} from "../harness/generated.js";

// --- Arbitraries producing well-typed generated values ------------------------------------------

// The 12 FilterOperator literals, in the order the generator emits them.
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

const filterOperatorArb: fc.Arbitrary<FilterOperator> = fc.constantFrom(...OPERATORS);

// A JSON-safe payload for `FilterLeaf.value` (which is typed `unknown | null`). Deliberately
// excludes `undefined`, `NaN`, `Infinity`, and `-0` so serialize -> parse is the identity. `null`
// is included because `value` is explicitly nullable.
const jsonSafeScalarArb: fc.Arbitrary<unknown> = fc.oneof(
  fc.constant(null),
  fc.boolean(),
  fc.integer({ min: -1_000_000, max: 1_000_000 }),
  fc.string(),
);

const jsonSafeValueArb: fc.Arbitrary<unknown> = fc.oneof(
  jsonSafeScalarArb,
  fc.array(jsonSafeScalarArb, { maxLength: 4 }),
);

// The recursive, presence-discriminated FilterNode. `fc.letrec` ties the four variants together;
// `fc.oneof` with a bounded `maxDepth`/`depthIdentifier` keeps the trees finite while still
// exercising the recursive `FilterNode[]` (and/or) and `FilterNode` (not) edges. The leaf variant is
// listed first so it is used as the base case once the depth budget is exhausted. and/or arrays may
// be empty or non-empty.
type FilterNodeArbs = {
  filterNode: FilterNode;
  leaf: FilterLeaf;
  and: FilterNode;
  or: FilterNode;
  not: FilterNode;
};

const { filterNode: filterNodeArb } = fc.letrec<FilterNodeArbs>((tie) => ({
  filterNode: fc.oneof(
    { maxDepth: 4, depthIdentifier: "filterNode" },
    tie("leaf"),
    tie("and"),
    tie("or"),
    tie("not"),
  ),
  leaf: fc.record(
    { field: fc.string(), op: filterOperatorArb, value: jsonSafeValueArb },
    { requiredKeys: ["field", "op"] },
  ),
  and: fc.record({ and: fc.array(tie("filterNode"), { maxLength: 3 }) }),
  or: fc.record({ or: fc.array(tie("filterNode"), { maxLength: 3 }) }),
  not: fc.record({ not: tie("filterNode") }),
}));

// VistaSortBody: both members optional; absent means the key is omitted.
const sortBodyArb: fc.Arbitrary<VistaSortBody> = fc.record(
  { desc: fc.boolean(), field: fc.string() },
  { requiredKeys: [] },
);

// VistaListRequestBody: every member optional. Nested FilterNode in filter/scope, a VistaSortBody[]
// in sort, and the nullable fields present-null (explicit null) or absent (key omitted).
const listRequestArb: fc.Arbitrary<VistaListRequestBody> = fc.record(
  {
    filter: filterNodeArb,
    format: fc.option(fc.string(), { nil: null }),
    page: fc.integer({ min: 0, max: 100_000 }),
    pageSize: fc.integer({ min: 0, max: 100_000 }),
    scope: filterNodeArb,
    search: fc.option(fc.string(), { nil: null }),
    sort: fc.option(fc.array(sortBodyArb, { maxLength: 4 }), { nil: null }),
  },
  { requiredKeys: [] },
);

// CustomerRow: required scalars plus the two nullable-optional strings (present-null or absent).
const customerRowArb: fc.Arbitrary<CustomerRow> = fc.record(
  {
    companyName: fc.string(),
    contactName: fc.option(fc.string(), { nil: null }),
    country: fc.option(fc.string(), { nil: null }),
    customerId: fc.string(),
    isActive: fc.boolean(),
  },
  { requiredKeys: ["companyName", "customerId", "isActive"] },
);

// --- Helpers ------------------------------------------------------------------------------------

/** Serialize -> parse must be the identity for a well-typed generated value (Requirement 11.2). */
function assertRoundTrips<T>(value: T): void {
  const roundTripped = JSON.parse(JSON.stringify(value)) as unknown;
  expect(roundTripped).toEqual(value);
}

type FilterVariant = "leaf" | "and" | "or" | "not";

/**
 * Narrows a FilterNode by which member is present — the presence-discriminated union with no
 * discriminator property (Requirement 2.2). Returns the single matched variant.
 */
function narrowByPresence(node: FilterNode): FilterVariant {
  if ("and" in node) {
    return "and";
  }
  if ("or" in node) {
    return "or";
  }
  if ("not" in node) {
    return "not";
  }
  return "leaf";
}

/**
 * Asserts a node matches EXACTLY one FilterNode member by presence, then recurses into the
 * recursive edges (Requirement 2.3). Returns the tree's depth so callers can confirm recursion
 * reaches beyond a single level.
 */
function assertExactlyOneMember(node: FilterNode): number {
  const memberFlags = [
    "field" in node && "op" in node && !("and" in node) && !("or" in node) && !("not" in node),
    "and" in node && !("or" in node) && !("not" in node),
    "or" in node && !("and" in node) && !("not" in node),
    "not" in node && !("and" in node) && !("or" in node),
  ];
  expect(memberFlags.filter(Boolean).length).toBe(1);

  // The presence discriminator agrees with the structural check and gives a total narrowing.
  const variant = narrowByPresence(node);

  if ("and" in node) {
    expect(variant).toBe("and");
    const childDepths = node.and.map(assertExactlyOneMember);
    return 1 + Math.max(0, ...childDepths);
  }
  if ("or" in node) {
    expect(variant).toBe("or");
    const childDepths = node.or.map(assertExactlyOneMember);
    return 1 + Math.max(0, ...childDepths);
  }
  if ("not" in node) {
    expect(variant).toBe("not");
    return 1 + assertExactlyOneMember(node.not);
  }
  // FilterLeaf: field + op present, no variant keys.
  expect(variant).toBe("leaf");
  expect(typeof node.field).toBe("string");
  expect(OPERATORS).toContain(node.op);
  return 1;
}

// --- Properties ---------------------------------------------------------------------------------

describe("Property 9 — generated-type round-trip (recursive FilterNode + presence narrowing)", () => {
  it("round-trips every recursive FilterNode through JSON without loss (R11.2)", () => {
    fc.assert(
      fc.property(filterNodeArb, (node) => {
        assertRoundTrips(node);
      }),
      { numRuns: 100 },
    );
  });

  it("narrows every FilterNode to exactly one member by presence, recursively (R2.2, R2.3)", () => {
    let maxDepthSeen = 0;
    const variantsSeen = new Set<FilterVariant>();

    fc.assert(
      fc.property(filterNodeArb, (node) => {
        variantsSeen.add(narrowByPresence(node));
        const depth = assertExactlyOneMember(node);
        maxDepthSeen = Math.max(maxDepthSeen, depth);
      }),
      { numRuns: 100 },
    );

    // The recursive edges are genuinely walked: trees nest beyond a single level (R2.3).
    expect(maxDepthSeen).toBeGreaterThanOrEqual(2);
  });

  it("generates all four FilterNode variants and each still round-trips (R2.2, R11.2)", () => {
    // A larger sample so every one of the four presence-discriminated members is produced.
    const samples = fc.sample(filterNodeArb, { numRuns: 1000 });
    const variants = new Set<FilterVariant>();
    let maxDepth = 0;

    for (const node of samples) {
      variants.add(narrowByPresence(node));
      maxDepth = Math.max(maxDepth, assertExactlyOneMember(node));
      assertRoundTrips(node);
    }

    expect(variants).toEqual(new Set<FilterVariant>(["leaf", "and", "or", "not"]));
    // Recursion reaches arbitrary depth (the FilterNode[]/FilterNode edges nest several levels).
    expect(maxDepth).toBeGreaterThanOrEqual(3);
  });

  it("round-trips VistaListRequestBody (nested FilterNode + VistaSortBody[]) through JSON (R11.2)", () => {
    fc.assert(
      fc.property(listRequestArb, (body) => {
        assertRoundTrips(body);
        // When present, filter/scope are themselves valid presence-discriminated FilterNodes.
        if (body.filter !== undefined) {
          assertExactlyOneMember(body.filter);
        }
        if (body.scope !== undefined) {
          assertExactlyOneMember(body.scope);
        }
      }),
      { numRuns: 100 },
    );
  });

  it("round-trips CustomerRow through JSON without loss (R11.2)", () => {
    fc.assert(
      fc.property(customerRowArb, (row) => {
        assertRoundTrips(row);
      }),
      { numRuns: 100 },
    );
  });
});
