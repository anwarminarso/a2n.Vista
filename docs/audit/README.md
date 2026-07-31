# Code audits

Point-in-time audit reports for the `a2n.Vista` codebase. Each report is a **dated snapshot**: it records
what the code looked like on that date, not what it looks like now. Reports are never rewritten after the
fact — when a finding is addressed, note the resolution in the report's status table and let the prose
stand as history.

## Reports

| Date | Report | Scope | Findings |
|---|---|---|---|
| 2026-07-31 | [Full code audit](2026-07-31-full-code-audit.md) | All 7 shipped libraries + 2 grid adapters + samples (~35k lines of production code) | 6 security, 13 correctness, 9 dead code, 8 performance |

## How findings are identified

Every finding carries a stable ID so it can be cited from commits, issues, and `docs/PROJECT-STATUS.md`:

| Prefix | Meaning |
|---|---|
| `SEC-nn` | Security: a gap in the secure-by-default posture, an authorization hole, or information disclosure |
| `BUG-nn` | Correctness: wrong results, wrong status codes, or broken contracts |
| `DEAD-nn` | Dead code: unreachable, unreferenced, or accepted-then-ignored |
| `PERF-nn` | Performance: measurable waste on a hot or bounded-resource path |

IDs are scoped to their report (`2026-07-31/SEC-01`). They are not reused across reports.

## Verification status

Findings are labelled with how strongly they are evidenced:

- **Verified** — the auditor read the code and traced the failing path end to end.
- **Unverified** — the structural fact was read in source, but the runtime consequence was not observed
  by executing code. Treat as a lead, not a conclusion.

No audit in this folder was produced by running the application. Every claim rests on source reading plus
the greps quoted in the report.
