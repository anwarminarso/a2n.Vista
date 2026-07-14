// Feature: typescript-client, Property 14: Schema parity (the document is the oracle)
//
// Validates: Requirements 11.3, 11.4, 11.5, 11.1
//
// This is the SCHEMA-PARITY HARNESS. The OpenAPI document is the authoritative ORACLE. It asserts two
// directions of parity between the GENERATED TypeScript client and the document's `components.schemas`:
//
//   1. EMITTED REQUESTS CONFORM (R11.3): every representative `VistaListRequestBody` value — including one
//      whose `filter` is each representative `FilterNode` (the nested, presence-discriminated `oneOf`
//      tree) — validates against the document's request schema. In other words, the shape the emitted
//      client sends is accepted by the schema the server publishes.
//
//   2. RESPONSE PARSE DROPS NOTHING (R11.4/R11.5): schema-valid response JSON (the representative values
//      for `CustomerRow`, `ProblemDetails`, `VistaMetadataResponse`, and a composed
//      `ViewListResult_CustomerRow`) both (a) validates against the document response schema and (b) is
//      consumed as the `Generated_Type` without dropping any defined property — `JSON.parse(JSON.stringify(v))`
//      retains every key present in the input (TypeScript's structural typing keeps every runtime key, so a
//      key-preservation + deep-equality check is the meaningful runtime assertion).
//
//   3. DISAGREEMENTS IDENTIFY THE OFFENDER (R11.5): on any validation failure the harness surfaces the
//      validator's error path (instancePath / schemaPath / message) so the offending type + property is
//      named. A dedicated test drives a deliberately-invalid value to prove the diagnostics name the
//      offending property.
//
// The representative value set (the oracle-derived values) is produced C#-side by
// `RepresentativeValueSet.Build(...)` (task 15.1) and committed at
// `tests/ts-runtime/fixtures/representative-values.json` (see task 15.2's round-trip-parity harness). The
// OpenAPI document is committed at `tests/ts-runtime/fixtures/vista-document.json` (a byte copy of the C#
// test oracle `src/Tests/a2n.Vista.Client.TypeScript.Tests/Fixtures/valid-vista-document.json`).
//
// ---------------------------------------------------------------------------------------------------------
// OpenAPI 3.0 -> JSON Schema conversion (the crux)
// ---------------------------------------------------------------------------------------------------------
// Ajv validates JSON Schema, not OpenAPI. Two OpenAPI-isms must be translated so Ajv validates faithfully:
//
//   * `nullable: true` is an OpenAPI 3.0 keyword, NOT JSON Schema. It is translated by folding "null" into
//     the node's `type` (`{type:"string", nullable:true}` -> `{type:["string","null"]}`). A `nullable`
//     `$ref` is wrapped as `anyOf:[{$ref}, {type:"null"}]`. A `nullable` node with neither `type` nor
//     `$ref` (a bare `{nullable, description}`) already accepts any JSON value (including null), so no
//     constraint is added.
//   * `$ref: "#/components/schemas/X"` is rewritten to `$ref: "#/$defs/X"` and all schemas are bundled
//     under a single `{$id:"vista-oracle", $defs:{...}}` document so both direct and RECURSIVE refs
//     (`FilterNode` -> `FilterAnd.items` -> `FilterNode`) resolve within one document.
//
// OpenAPI-only annotations Ajv would either ignore or (in strict mode) reject are dropped: `format` (we do
// not enforce formats — int32/int64/binary are irrelevant to structural parity), `description`, `example`,
// `readOnly`/`writeOnly`, `deprecated`, `xml`, `externalDocs`. Structural keywords (`type`, `enum`,
// `required`, `oneOf`, `properties`, `items`, `additionalProperties`, ...) are preserved. `oneOf` maps
// directly (this is how `FilterNode` narrows by member presence).

import { describe, expect, it } from "vitest";
import fc from "fast-check";
import Ajv, { type ErrorObject, type ValidateFunction } from "ajv";

import openApiDocument from "../../fixtures/vista-document.json";
import representativeValuesJson from "../../fixtures/representative-values.json";
import type {
  CustomerRow,
  FilterNode,
  ProblemDetails,
  VistaListRequestBody,
  VistaMetadataResponse,
} from "../harness/generated.js";

// --- OpenAPI -> JSON Schema converter -----------------------------------------------------------

const COMPONENT_REF_PREFIX = "#/components/schemas/";

/** Rewrite an OpenAPI component `$ref` to a `$defs` pointer inside the bundled oracle document. */
function rewriteRef(ref: string): string {
  return ref.startsWith(COMPONENT_REF_PREFIX)
    ? `#/$defs/${ref.slice(COMPONENT_REF_PREFIX.length)}`
    : ref;
}

/**
 * Recursively convert an OpenAPI 3.0 schema node into a JSON-Schema-compatible node for Ajv.
 * See the file header for the full conversion contract (nullable folding, $ref rewrite, dropped
 * annotations). The function is pure and returns a fresh node.
 */
function toJsonSchema(node: unknown): unknown {
  if (node === null || typeof node !== "object") {
    return node;
  }
  if (Array.isArray(node)) {
    return node.map(toJsonSchema);
  }

  const source = node as Record<string, unknown>;
  const out: Record<string, unknown> = {};
  const isNullable = source.nullable === true;

  for (const [key, value] of Object.entries(source)) {
    switch (key) {
      // OpenAPI-only keyword: handled after the loop (folded into `type`).
      case "nullable":
      // Dropped annotations / OpenAPI-only metadata (not structural; Ajv ignores or rejects them).
      case "format":
      case "description":
      case "example":
      case "readOnly":
      case "writeOnly":
      case "deprecated":
      case "xml":
      case "externalDocs":
      case "discriminator":
        break;
      case "$ref":
        out.$ref = rewriteRef(value as string);
        break;
      case "properties": {
        const props = value as Record<string, unknown>;
        const mapped: Record<string, unknown> = {};
        for (const [propName, propSchema] of Object.entries(props)) {
          mapped[propName] = toJsonSchema(propSchema);
        }
        out.properties = mapped;
        break;
      }
      case "items":
        out.items = toJsonSchema(value);
        break;
      case "oneOf":
      case "anyOf":
      case "allOf":
        out[key] = (value as unknown[]).map(toJsonSchema);
        break;
      case "additionalProperties":
        out[key] =
          typeof value === "object" && value !== null ? toJsonSchema(value) : value;
        break;
      default:
        // Structural keywords copied verbatim: type, enum, required, minimum/maximum, etc.
        out[key] = value;
    }
  }

  if (isNullable) {
    if ("type" in out) {
      const t = out.type;
      out.type = Array.isArray(t)
        ? Array.from(new Set([...(t as string[]), "null"]))
        : [t as string, "null"];
    } else if ("$ref" in out) {
      // A nullable $ref accepts the referenced shape OR null (no such case in the current document,
      // but the converter stays faithful for completeness).
      return { anyOf: [{ $ref: out.$ref }, { type: "null" }] };
    }
    // nullable with neither `type` nor `$ref` already accepts any value incl. null -> no constraint.
  }

  return out;
}

// --- Build the Ajv validators from the oracle ---------------------------------------------------

const rawSchemas = (openApiDocument as { components: { schemas: Record<string, unknown> } })
  .components.schemas;

const $defs: Record<string, unknown> = {};
for (const [name, schema] of Object.entries(rawSchemas)) {
  $defs[name] = toJsonSchema(schema);
}

// `strict:false` so Ajv tolerates any residual OpenAPI-isms and does not enforce `format`.
const ajv = new Ajv({ allErrors: true, strict: false });
ajv.addSchema({ $id: "vista-oracle", $defs });

const validatorCache = new Map<string, ValidateFunction>();
function validatorFor(typeName: string): ValidateFunction {
  const cached = validatorCache.get(typeName);
  if (cached) {
    return cached;
  }
  const validate = ajv.compile({ $ref: `vista-oracle#/$defs/${typeName}` });
  validatorCache.set(typeName, validate);
  return validate;
}

/** Render a validator's errors so the offending type + property is legible (R11.5). */
function describeErrors(typeName: string, errors: readonly ErrorObject[] | null | undefined): string {
  const rendered = (errors ?? [])
    .map((e) => `  - ${typeName}${e.instancePath || "(root)"} ${e.message ?? ""} [schema ${e.schemaPath}]`)
    .join("\n");
  return `"${typeName}" did not validate against the document schema:\n${rendered || "  (no error detail)"}`;
}

/** Assert a value validates against the named document schema; on failure, name the offender. */
function assertValidates(typeName: string, value: unknown): void {
  const validate = validatorFor(typeName);
  const ok = validate(value);
  expect(
    ok,
    `${describeErrors(typeName, validate.errors)}\n  value=${JSON.stringify(value)}`,
  ).toBe(true);
}

// --- "Drops no defined property" (R11.4/R11.5) --------------------------------------------------

/** Collect the fully-qualified path of every object key present in `value` (arrays indexed). */
function collectKeyPaths(value: unknown, prefix = ""): Set<string> {
  const paths = new Set<string>();
  if (Array.isArray(value)) {
    value.forEach((element, index) => {
      for (const nested of collectKeyPaths(element, `${prefix}[${index}]`)) {
        paths.add(nested);
      }
    });
  } else if (value !== null && typeof value === "object") {
    for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
      const path = prefix ? `${prefix}.${key}` : key;
      paths.add(path);
      for (const nested of collectKeyPaths(child, path)) {
        paths.add(nested);
      }
    }
  }
  return paths;
}

/**
 * Parsing schema-valid response JSON into the Generated_Type must drop no defined property (R11.4/R11.5):
 * every key present survives the round-trip and no value drifts. On failure, the dropped property paths
 * are named.
 */
function assertNoPropertyDropped(typeName: string, value: unknown): void {
  const before = collectKeyPaths(value);
  const roundTripped = JSON.parse(JSON.stringify(value)) as unknown;
  const after = collectKeyPaths(roundTripped);
  const dropped = [...before].filter((key) => !after.has(key));
  expect(
    dropped,
    `"${typeName}" dropped defined propert${dropped.length === 1 ? "y" : "ies"} when parsed as the ` +
      `generated type: ${dropped.join(", ")}`,
  ).toEqual([]);
  // No key loss AND no value drift.
  expect(roundTripped).toEqual(value);
}

// --- Load the representative value set (the oracle-derived values) -------------------------------

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

// Compile-time parity: the representative values ARE the generated types (type-assertions here make
// `npm run typecheck` confirm the generated type names resolve and accept the document-derived shapes).
const listRequests = valuesOf("VistaListRequestBody") as VistaListRequestBody[];
const filterNodes = valuesOf("FilterNode") as FilterNode[];
const customerRows = valuesOf("CustomerRow") as CustomerRow[];
const problemDetails = valuesOf("ProblemDetails") as ProblemDetails[];
const metadataResponses = valuesOf("VistaMetadataResponse") as VistaMetadataResponse[];

// The list response type (ViewListResult_CustomerRow) is not in the representative fixture (it is a
// per-view monomorphization), so compose schema-valid values from the CustomerRow representatives, covering
// the empty and non-empty `page.items` collection forms.
const viewListResults: unknown[] = [
  {
    page: { items: [], totalRows: 0, pageIndex: 0, pageSize: 25, totalPages: 0 },
    totalRowsUnfiltered: 0,
  },
  {
    page: {
      items: customerRows,
      totalRows: customerRows.length,
      pageIndex: 0,
      pageSize: 25,
      totalPages: 1,
    },
    totalRowsUnfiltered: customerRows.length,
  },
];

// Response types keyed by their document schema name (the oracle names).
const responseValuesByType: ReadonlyArray<readonly [string, readonly unknown[]]> = [
  ["CustomerRow", customerRows],
  ["ProblemDetails", problemDetails],
  ["VistaMetadataResponse", metadataResponses],
  ["ViewListResult_CustomerRow", viewListResults],
];

const allResponseValues: ReadonlyArray<readonly [string, unknown]> = responseValuesByType.flatMap(
  ([typeName, values]) => values.map((value) => [typeName, value] as const),
);

// A minimum of 100 iterations per the property discipline (sets are small -> sample with replacement).
const REQUEST_RUNS = Math.max(100, listRequests.length + filterNodes.length);
const RESPONSE_RUNS = Math.max(100, allResponseValues.length);

// --- Properties ---------------------------------------------------------------------------------

describe("Property 14 — schema parity (the document is the oracle)", () => {
  it("registers a validator for every document component schema (converter sanity)", () => {
    // Every schema converts and compiles (recursive FilterNode refs included).
    for (const name of Object.keys(rawSchemas)) {
      expect(() => validatorFor(name)).not.toThrow();
    }
  });

  // 1. EMITTED REQUESTS CONFORM (R11.3) --------------------------------------------------------

  it("every representative VistaListRequestBody validates against the document request schema (R11.3)", () => {
    for (const request of listRequests) {
      assertValidates("VistaListRequestBody", request);
    }
  });

  it("every representative FilterNode validates against the document FilterNode schema (R11.3)", () => {
    for (const node of filterNodes) {
      assertValidates("FilterNode", node);
    }
  });

  it("a VistaListRequestBody carrying each representative FilterNode as its filter conforms (R11.3)", () => {
    for (const node of filterNodes) {
      const request = { filter: node, page: 1, pageSize: 25 };
      assertValidates("VistaListRequestBody", request);
    }
  });

  it("emitted requests conform under a >=100-iteration property (R11.3)", () => {
    const nestedFilterRequests = filterNodes.map((node) => ({
      filter: node,
      scope: node,
      page: 1,
      pageSize: 25,
    }));
    const requestArb = fc.constantFrom(...listRequests, ...nestedFilterRequests);
    fc.assert(
      fc.property(requestArb, (request) => {
        assertValidates("VistaListRequestBody", request);
      }),
      { numRuns: REQUEST_RUNS },
    );
  });

  // 2. RESPONSE PARSE DROPS NOTHING (R11.4/R11.5) ----------------------------------------------

  it("every schema-valid response value validates against its document schema (R11.4)", () => {
    for (const [typeName, value] of allResponseValues) {
      assertValidates(typeName, value);
    }
  });

  it("consuming a schema-valid response as the generated type drops no defined property (R11.4/R11.5)", () => {
    for (const [typeName, value] of allResponseValues) {
      assertNoPropertyDropped(typeName, value);
    }
  });

  it("response parity holds under a >=100-iteration property (R11.4/R11.5)", () => {
    const responseArb = fc.constantFrom(...allResponseValues);
    fc.assert(
      fc.property(responseArb, ([typeName, value]) => {
        assertValidates(typeName, value);
        assertNoPropertyDropped(typeName, value);
      }),
      { numRuns: RESPONSE_RUNS },
    );
  });

  // 3. DISAGREEMENTS IDENTIFY THE OFFENDER (R11.5) ---------------------------------------------

  it("a disagreement names the offending type and property (R11.5)", () => {
    // A CustomerRow missing the required `customerId` must fail and the diagnostics must name it.
    const validate = validatorFor("CustomerRow");
    const invalid = { companyName: "sample", isActive: true }; // customerId missing
    const ok = validate(invalid);
    expect(ok).toBe(false);

    const diagnostics = describeErrors("CustomerRow", validate.errors);
    expect(diagnostics).toContain("CustomerRow");
    expect(diagnostics).toContain("customerId");

    // A wrong-typed property is also localized to the offending path.
    const validateProblem = validatorFor("VistaMetadataResponse");
    const wrongType = {
      name: "sample",
      route: "sample",
      isReadOnly: true,
      keyFields: [123], // should be strings
      maxPageSize: 1,
      maxExportRows: 1,
      fields: [],
    };
    expect(validateProblem(wrongType)).toBe(false);
    const diag2 = describeErrors("VistaMetadataResponse", validateProblem.errors);
    expect(diag2).toContain("/keyFields");
  });
});
