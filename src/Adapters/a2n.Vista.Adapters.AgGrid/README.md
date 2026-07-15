<p align="center">
  <img src="https://raw.githubusercontent.com/anwarminarso/a2n.Vista/main/assets/a2n-vista-wordmark.png" alt="a2n.Vista" width="420" />
</p>

# a2n.Vista.Adapters.AgGrid

A [a2n.Vista](https://github.com/anwarminarso/a2n.Vista) grid adapter for **AG Grid** (server-side row model).

Translates AG Grid's request/response shape into the neutral Vista contract:

- **`POST {route}/aggrid`** — block paging (`startRow`/`endRow`), `sortModel`, and `filterModel` (text/number/date/`set`, combined AND/OR) mapped to Vista's `FilterNode`.
- **Quick filter** — via `?q=`, folded into the search channel.
- **Response** — `{ rowData, rowCount }` for AG Grid last-block detection.

Core-only (no EF/ASP.NET reference); reuses the same host glue as the other adapters. Advanced Filter is deferred for v1 (an Advanced-Filter payload is rejected loudly rather than silently dropped).

## Install

```sh
dotnet add package a2n.Vista.Adapters.AgGrid
```

Depends on [`a2n.Vista.Core`](https://www.nuget.org/packages/a2n.Vista.Core).

## Documentation

See the [project README](https://github.com/anwarminarso/a2n.Vista#readme) and the [adapter contract spec](https://github.com/anwarminarso/a2n.Vista/blob/main/docs/spec/04-adapter-contract.md).

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/anwarminarso/a2n.Vista/blob/main/LICENSE).
