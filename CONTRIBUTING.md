# Contributing to a2n.Vista

Thanks for your interest in contributing. Vista is in **pre-alpha**, so the
public API and internals still change frequently — please open an issue to
discuss anything substantial before investing time in a large change.

## Code of Conduct

This project follows the [Contributor Covenant](./CODE_OF_CONDUCT.md). By
participating you agree to uphold it.

## Prerequisites

- .NET SDK capable of building the target frameworks. The solution multi-targets
  **.NET 8, 9, and 10** (see `Directory.Build.props`); install the SDKs you intend
  to build against.
- Git.

No `sqlite3` CLI is required — the Northwind example ships a SQLite database in
`src/Examples/DB` (extract `Northwind SQLite.zip` to `northwind.db` before running
the example).

## Build & test

```sh
# Restore + build the whole solution
dotnet build src/a2n.Vista.slnx

# Run the test suite (TUnit on Microsoft.Testing.Platform)
dotnet test src/a2n.Vista.slnx

# Run the end-to-end example self-test
dotnet run --project src/Examples/Northwind -- selftest
```

Before opening a pull request, make sure:

- the solution builds with **no new warnings**, and
- existing tests pass (add tests for new behavior and bug fixes).

## Project layout

See [README](./README.md#solution-layout). In short: `Core` is EF- and HTTP-free;
`EntityFrameworkCore` and `AspNetCore` both depend only on `Core` and never on each
other. Respect these boundaries — they are the basis of the AOT and grid-agnostic
design (ROADMAP "Dependency rules", D48).

## Coding guidelines

- Match the surrounding style; `Nullable` is enabled solution-wide, so keep the
  nullability annotations honest.
- Public types and members should carry XML documentation.
- Keep changes focused. Unrelated refactors belong in their own PR.
- All code, comments, identifiers, and documentation are written in **English**.

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <short summary>

<body explaining what and why, not how>
```

Common types: `feat`, `fix`, `refactor`, `docs`, `test`, `build`, `chore`.
Example scope: `core`, `ef`, `aspnetcore`, `examples/northwind`, `spec`.

## Pull requests

1. Fork and create a topic branch (never commit directly to `main`/`master`).
2. Make your change with tests and docs.
3. Ensure `dotnet build` and `dotnet test` are clean.
4. Open a PR with a clear description: what changed, why, and how you tested it.
   Link any related issue.

A maintainer will review. Expect iteration — pre-alpha APIs are still settling.

## Licensing of contributions

This project is licensed under the **LGPL-3.0-or-later** (see [LICENSE](./LICENSE)).
By submitting a contribution you agree that it is licensed under the same terms and
that you have the right to submit it.
