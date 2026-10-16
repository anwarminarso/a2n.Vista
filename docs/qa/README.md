# a2n.Vista QA notes

This folder records verification that was actually run: commands, counts, and gaps. The library and the samples are the source of truth.

| Note | What it is |
| --- | --- |
| [e2e-playwright.md](./e2e-playwright.md) | How to run the Playwright suite, what it covers, and the skipped `vWritableMemo` round trip |
| [vista-e2e-2026-10-16.md](./vista-e2e-2026-10-16.md) | Counts from the first green run of that suite, plus the library `dotnet test` baseline taken on the same tree |

## Commands

Library baseline (no browser). `global.json` selects the Microsoft.Testing.Platform runner:

```bash
dotnet test src/a2n.Vista.slnx
```

End-to-end (real Kestrel, real `northwind.db`, headless Chromium). The script installs the browser:

```bash
bash tests/e2e/run.sh
```

`tests/e2e` is not part of `src/a2n.Vista.slnx`, so the baseline command does not download a browser or start the samples.

Do not commit `northwind.db` (it is gitignored) or anything under `bin/`, `obj/`, or `TestResults/`.
