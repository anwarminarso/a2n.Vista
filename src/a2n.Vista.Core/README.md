<p align="center">
  <img src="https://raw.githubusercontent.com/anwarminarso/a2n.Vista/main/assets/a2n-vista-wordmark.png" alt="a2n.Vista" width="420" />
</p>

# a2n.Vista.Core

The engine of [a2n.Vista](https://github.com/anwarminarso/a2n.Vista) — *define a view, get an API*. Type-safe, AOT-friendly, grid-agnostic projections for ASP.NET Core.

This package provides the core building blocks:

- **View authoring** — `View`/`ViewBuilder`/`ViewTemplate` for declaring LINQ projections as first-class, secure-by-default units.
- **Metadata** — `ViewMetadata`, field/key metadata, and the neutral filter contract.
- **Ports** — the `IViewExecutor`/`IViewScope` seams plus the write seam (`WriteMapper`/`IWriteFacetRegistry`).
- **Pillar 3 codegen, bundled** — the Vista source generator ships **inside this package** (`analyzers/dotnet/cs`). Referencing `a2n.Vista.Core` gives you AOT-clean metadata/execution/serialization codegen transitively — no extra package reference, no manual analyzer wiring.

## Install

```sh
dotnet add package a2n.Vista.Core
```

Core is the base dependency for the rest of the stack: add [`a2n.Vista.EntityFrameworkCore`](https://www.nuget.org/packages/a2n.Vista.EntityFrameworkCore) to execute views and [`a2n.Vista.AspNetCore`](https://www.nuget.org/packages/a2n.Vista.AspNetCore) to expose them over HTTP.

## Documentation

See the [project README](https://github.com/anwarminarso/a2n.Vista#readme) and the [specs](https://github.com/anwarminarso/a2n.Vista/tree/main/docs/spec).

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/anwarminarso/a2n.Vista/blob/main/LICENSE).
