// Feature: northwind-sample-showcase, Property 3: The global-search minimum-length gate
//
// Validates: Requirements 3.3
//
// The View Browser reproduces DynData's `minGlobalSearchCharLength` behavior: a global-search
// request is only issued once the entered term reaches the configured minimum length, measured
// against the *trimmed* term (leading/trailing whitespace never counts, whitespace-only terms are
// treated as empty). The subject under test is the pure function
// `shouldIssueSearch(term, minLen)` in ../src/search.ts, which returns `term.trim().length >= minLen`.
//
// This suite asserts, over a wide input space (minimum 100 runs) of arbitrary strings — including
// whitespace-only and Unicode terms — and arbitrary `minLen` values, that:
//
//   1. The decision always equals an independent oracle computed from the trimmed length.
//   2. Below-min (trimmed length < minLen)  ⇒ NO request is issued (false).
//   3. At/above-min (trimmed length >= minLen) ⇒ a request IS issued (true).
//
// The gate is measured against the trimmed length, so padding a term with surrounding whitespace
// never changes the outcome.

import { describe, expect, it } from "vitest";
import fc from "fast-check";

import { shouldIssueSearch } from "../src/search.js";

// --- Generators --------------------------------------------------------------------------------

// Whitespace runs used to pad terms and to build whitespace-only terms. Covers the common ASCII
// whitespace the `String.prototype.trim` contract strips.
const whitespaceArb: fc.Arbitrary<string> = fc
  .array(fc.constantFrom(" ", "\t", "\n", "\r", "\f", "\v", "\u00a0"), {
    maxLength: 6,
  })
  .map((parts) => parts.join(""));

// A broad string generator including Unicode (fc.string exercises the full code-point space via
// its default unit), plus explicit Unicode-heavy and emoji samples to stress code-point counting.
const unicodeTermArb: fc.Arbitrary<string> = fc.oneof(
  fc.string(),
  fc.string({ unit: "grapheme" }),
  fc.constantFrom(
    "café",
    "naïve",
    "Ω≈ç√",
    "日本語",
    "Ελληνικά",
    "😀🎉",
    "a\u0301", // combining acute accent
    "  leading",
    "trailing  ",
    "  both  ",
  ),
);

// Whitespace-only terms (including the empty string) — must trim to length 0.
const whitespaceOnlyTermArb: fc.Arbitrary<string> = whitespaceArb;

// A term optionally padded with surrounding whitespace, so the trimmed length is what matters.
const paddedTermArb: fc.Arbitrary<string> = fc
  .tuple(whitespaceArb, unicodeTermArb, whitespaceArb)
  .map(([lead, core, trail]) => `${lead}${core}${trail}`);

// The union covering every bucket: raw Unicode terms, padded terms, and whitespace-only terms.
const anyTermArb: fc.Arbitrary<string> = fc.oneof(
  unicodeTermArb,
  paddedTermArb,
  whitespaceOnlyTermArb,
);

// `minLen` values around the interesting boundary (DynData used 3), including 0 and larger gates.
const minLenArb: fc.Arbitrary<number> = fc.integer({ min: 0, max: 12 });

// --- Tests -------------------------------------------------------------------------------------

describe("Property 3 — global-search minimum-length gate", () => {
  it("decides exactly by the trimmed length against minLen (R3.3)", () => {
    fc.assert(
      fc.property(anyTermArb, minLenArb, (term, minLen) => {
        const trimmedLength = term.trim().length;
        const expected = trimmedLength >= minLen;

        expect(shouldIssueSearch(term, minLen)).toBe(expected);

        // Restate the directional guarantees explicitly.
        if (trimmedLength < minLen) {
          // Below-min ⇒ no request is issued.
          expect(shouldIssueSearch(term, minLen)).toBe(false);
        } else {
          // At/above-min ⇒ a request is issued.
          expect(shouldIssueSearch(term, minLen)).toBe(true);
        }
      }),
      { numRuns: 100 },
    );
  });

  it("ignores surrounding whitespace: padding a term never changes the decision (R3.3)", () => {
    fc.assert(
      fc.property(unicodeTermArb, whitespaceArb, whitespaceArb, minLenArb, (core, lead, trail, minLen) => {
        const padded = `${lead}${core}${trail}`;
        // The trimmed content is identical, so the gate outcome must be identical.
        expect(shouldIssueSearch(padded, minLen)).toBe(shouldIssueSearch(core.trim(), minLen));
      }),
      { numRuns: 100 },
    );
  });

  it("treats whitespace-only terms as empty: no request unless minLen is 0 (R3.3)", () => {
    fc.assert(
      fc.property(whitespaceOnlyTermArb, minLenArb, (term, minLen) => {
        // Trimmed length is 0, so a request is issued only when the gate is 0.
        expect(shouldIssueSearch(term, minLen)).toBe(minLen === 0);
      }),
      { numRuns: 100 },
    );
  });
});
