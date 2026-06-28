# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the version is `0.x`, anything may change between releases.

## [Unreleased]

### Added
- **SourceGenerators** — Pillar 3, Phase 1 (Decision Log D117): an incremental
  generator (`ViewAccessorGenerator`) that recognizes typed Style B views by
  fully-qualified name and emits shape-driven field accessors plus a
  `[ModuleInitializer]` that registers them into a Core `ViewAccessorRegistry`.
  The export pipeline prefers generated accessors and falls back to reflection
  (coexistence), removing the `[RequiresUnreferencedCode]` value-read on the
  export path for covered views. Diagnostics `VISTA0001` (non-partial Style B
  view) and `VISTA0002` (Style B view without a public parameterless
  constructor); help docs under `docs/diagnostics/`.
- **Core** — `ViewAccessorRegistry` (static, thread-safe accessor store) and an
  AOT-clean `ExportColumns.Value(viewName, row, fieldName)` overload consumed by
  the CSV/XLSX export writers.
- **Core** — View authoring (`View`/`ViewBuilder`/`ViewTemplate`), `ViewMetadata`,
  the filter contract, and the `IViewExecutor`/`IViewScope` ports.
- **EntityFrameworkCore** — View execution over EF Core (List + Detail-by-key,
  paging, filter/sort/search, provider-aware) and DbContext-bound authoring.
- **AspNetCore** — generic endpoint mapping (`MapVistaViews`), RFC 7807 error
  mapping, and an optional fail-open `IViewAuthorizer` with a startup warning.
- **Examples** — a Northwind sample app exposing the read-only `vProductCategory`
  View over the real Microsoft Northwind SQLite database, with an end-to-end
  self-test (`dotnet run -- selftest`).
- Specs `01`–`05`, `10`, and `11` under `docs/spec/`.
- Project docs: `CONTRIBUTING`, `CODE_OF_CONDUCT`, `SECURITY`, `SUPPORT`,
  `NOTICES`, `COPYING`, and this changelog.

### Changed
- The global route root is owned solely by the AspNetCore layer (Decision Log
  D101); `ViewMetadata.Route` now carries only the view-name segment.

[Unreleased]: https://github.com/anwarminarso/a2n.Vista/commits
