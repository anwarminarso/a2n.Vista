# AG Grid e2e flake — 2026-10-16

`AgGrid_Simple_Wiring_Renders_Rows_And_Sort_Flips` failed because it read the product cell before AG Grid painted the row. Tracked as [issue #18](https://github.com/anwarminarso/a2n.Vista/issues/18). No library code was changed.

## Failure

Commit `002fb6a` on PR #12. Playwright e2e (net8.0), [run 38882214341](https://github.com/anwarminarso/a2n.Vista/actions/runs/38882214341), 2026-10-16. The job failed at `tests/e2e/ShowcaseUiTests.cs:36` in 1.9s:

```text
Expected to be greater than 0
but received -12
at Assert.That(string.Compare(descending, "Alice Mutton", StringComparison.OrdinalIgnoreCase)).IsGreaterThan(0)
```

`string.Compare("", "Alice Mutton", OrdinalIgnoreCase)` is `-12`, the length of `"Alice Mutton"`. The previous line, `ToHaveTextAsync("Alice Mutton")` after the ascending sort, had passed. `IsNotEqualTo("Alice Mutton")` also passed, because the cell text was empty rather than still `Alice Mutton`. Suite on that run: 34 total, 1 failed, 32 passed, 1 skipped.

The test waited until the sort POST completed, then called `InnerTextAsync`. The cell element exists before its text is committed, so that read does not wait for the row.

## Fix

Same shape as the TanStack showcase checks, with one change: a non-2xx data response fails immediately instead of being ignored until `WaitForResponseAsync` times out (`response.Ok` in the predicate does that).

1. Wait for the POST whose body matches the action (`"sort":"desc"`, `"pageIndex":1`, the DataTables post, and so on). Register the wait before the click or navigation that sends it.
2. If that response is not 2xx, throw with the status and a short body.
3. Assert with `Expect(locator).ToHaveTextAsync(...)` (or `ToHaveCountAsync` / `ToContainTextAsync`). Do not snapshot the cell with `InnerTextAsync` or `AllInnerTextsAsync`.

Descending `ProductName` is asserted as `Zaanse koeken`. View Browser and TanStack page 2 are asserted as `Chocolade`. Both are the server order for this database, and the retrying assertion waits until the cell shows them.

The same direct read was in the AG Grid filter and custom-renderer checks, the View Browser and Northwind DataTables page checks, and the TanStack paging, sort, filter, and column-hide checks. Those now use the response wait plus a retrying assertion.

## Re-run

Branch `qa/vista-aggrid-e2e-flake`, parent `e8563d9` (`develop`). Full `dotnet test tests/e2e/a2n.Vista.E2E.csproj` after the hosts were built:

```text
Test run summary: Passed!
  total: 34
  failed: 0
  succeeded: 33
  skipped: 1
  duration: 5s 598ms
```

The skipped test is the known `vWritableMemo` round trip. The six `ShowcaseUiTests` and five `TanStackShowcaseTests` passed in the filtered runs immediately before this (5.5s and 4.6s).
