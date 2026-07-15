<p align="center">
  <img src="https://raw.githubusercontent.com/anwarminarso/a2n.Vista/main/assets/a2n-vista-wordmark.png" alt="a2n.Vista" width="420" />
</p>

# a2n.Vista.Adapters.DataTablesNet

A [a2n.Vista](https://github.com/anwarminarso/a2n.Vista) grid adapter for **jQuery DataTables** and **jQuery-QueryBuilder**.

Translates the DataTables native request/response shape into the neutral Vista contract:

- **`POST {route}/datatable`** — server-side paging, sorting, and min-length global search, plus `jsonQB`/`externalFilter` advanced-filter parsing.
- **`GET {route}/querybuilder`** — a per-view jQuery-QueryBuilder metadata schema.
- **Export** — a pluggable pipeline with built-in, zero-dependency CSV and XLSX writers.

Core-only (no EF/ASP.NET reference); exposed through the standard Vista host glue.

## Install

```sh
dotnet add package a2n.Vista.Adapters.DataTablesNet
```

Depends on [`a2n.Vista.Core`](https://www.nuget.org/packages/a2n.Vista.Core).

## Documentation

See the [project README](https://github.com/anwarminarso/a2n.Vista#readme) and the [adapter contract spec](https://github.com/anwarminarso/a2n.Vista/blob/main/docs/spec/04-adapter-contract.md).

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/anwarminarso/a2n.Vista/blob/main/LICENSE).
