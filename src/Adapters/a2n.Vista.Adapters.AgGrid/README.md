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

## Column names: `colId` is the field name, not the JSON name

This is the one thing to get right when wiring a grid or calling the endpoint programmatically.

| Payload | Naming | Example |
|---|---|---|
| `rowData` (response) | **camelCase** — the serialized row | `productName`, `unitPrice` |
| `sortModel[].colId` (request) | the view's **field name**, PascalCase | `ProductName`, `UnitPrice` |
| `filterModel` keys (request) | the view's **field name**, PascalCase | `ProductName`, `UnitPrice` |

Matching is **ordinal** (case-sensitive) by design — a client cannot reach a field through a differently cased
spelling. So `productName` is not a mis-spelling that "nearly works": it is an unknown field, and both channels
reject it with `400` `filter-unknown-field` naming the offending field. Neither channel silently ignores a name
it does not recognize, so a wrong name can never come back as a `200` with an unchanged row order.

In AG Grid, keep `colId` and `field` separate on each column:

```ts
const columnDefs: ColDef[] = [
  // field  = camelCase row accessor (response)   colId = PascalCase view field (sort/filter)
  { colId: 'ProductName', field: 'productName', headerName: 'Product' },
  { colId: 'UnitPrice',   field: 'unitPrice',   headerName: 'Unit price' },

  // A column with no server field behind it must opt out of sorting, or the grid will ask the
  // server to order by a field that does not exist (→ 400).
  { colId: 'actions', cellRenderer: ActionsRenderer, sortable: false, filter: false },
];
```

`GET {route}/metadata` publishes the field names, so a generated grid can derive both spellings instead of
hard-coding them.

## Install

```sh
dotnet add package a2n.Vista.Adapters.AgGrid
```

Depends on [`a2n.Vista.Core`](https://www.nuget.org/packages/a2n.Vista.Core).

## Documentation

See the [project README](https://github.com/anwarminarso/a2n.Vista#readme) and the [adapter contract spec](https://github.com/anwarminarso/a2n.Vista/blob/main/docs/spec/04-adapter-contract.md).

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/anwarminarso/a2n.Vista/blob/main/LICENSE).
