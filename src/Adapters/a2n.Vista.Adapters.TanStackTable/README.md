<p align="center">
  <img src="https://raw.githubusercontent.com/anwarminarso/a2n.Vista/main/assets/a2n-vista-wordmark.png" alt="a2n.Vista" width="420" />
</p>

# a2n.Vista.Adapters.TanStackTable

A [a2n.Vista](https://github.com/anwarminarso/a2n.Vista) grid adapter for **TanStack Table** (React, Vue, Solid, and vanilla `table-core`).

TanStack Table does not define an HTTP protocol. A manual table already holds the state this adapter binds, and those slices are the same in v8 and v9:

- **`POST {route}/tanstack`** — JSON body `{ pagination, sorting, columnFilters, globalFilter, columnVisibility }`.
- **Pagination** — `pageIndex` / `pageSize` become an absolute row offset (`pageIndex * pageSize`) and a page size. Omitted pagination is TanStack's initial state `{ pageIndex: 0, pageSize: 10 }`. A non-positive `pageSize` is passed through so the engine rejects it.
- **Sorting** — every `{ id, desc }` entry becomes a `SortSpec`, in order. An unknown `id` is not dropped; the engine returns `400`.
- **Column filters** — the `Filter` channel. A string is `contains`, a number or boolean is `equals`, `null` is `isNull`, an array is `in` (a faceted selection; an empty array is no filter). `{ min, max }` is a range (`between`, or one bound when the other is absent). `{ op, value }` names the operator (`gte`, `includesString`, `inNumberRange`, …).
- **Global filter** — the `Search` channel: `contains` over the view's searchable string fields, same as the other grids. A non-string `globalFilter` is a bind failure.
- **Column visibility** — presentation only. `false` on a view field name is recorded on `SelectFields`, and the table hides that column. **This is not an access-control mechanism.** Hidden fields are still sent to the client in `rows`. Keys that are not view fields (an actions column) are ignored.
- **Response** — `{ rows, rowCount, pageCount }`. `rows` is the page (pass it as `data`). `rowCount` is the filtered total. `pageCount` is `ceil(rowCount / pageSize)`.

Core-only (no EF/ASP.NET reference); reuses the same host glue as the other adapters. Register with `AddVistaAdapter<TanStackTableAdapter>()`.

## Column visibility is not access control

`columnVisibility` only changes which columns the table draws. A field mapped to `false` is still present on every row in the response. `SelectFields` records the caller's display choice; the list executor does not apply it, and applying it later would still be a projection hint, not authorization. To keep a value off the client, mask the field or leave it out of the view.

## Column `id` is the field name, not the JSON name

| Payload | Naming | Example |
|---|---|---|
| `rows` (response) | **camelCase** — the serialized row | `productName`, `unitPrice` |
| `sorting[].id`, `columnFilters[].id`, `columnVisibility` keys | the view's **field name**, PascalCase | `ProductName`, `UnitPrice` |

Matching is **ordinal**. `productName` does not sort or filter `ProductName` — that request is `400` `filter-unknown-field`. A mis-cased *visibility* key does not 400: visibility is not a request to order or filter, so the field simply stays in the projection.

```ts
const columns = [
  // id = PascalCase view field (sort/filter/visibility). accessorKey = camelCase row field.
  { id: 'ProductName', accessorKey: 'productName', header: 'Product' },
  { id: 'UnitPrice', accessorKey: 'unitPrice', header: 'Unit price' },
  { id: 'actions', header: 'Actions', enableSorting: false },
]
```

`GET {route}/metadata` publishes the field names.

## Manual table

```ts
const table = useReactTable({
  data: page.rows,
  columns,
  rowCount: page.rowCount,
  state: { pagination, sorting, columnFilters, globalFilter, columnVisibility },
  onPaginationChange: setPagination,
  onSortingChange: setSorting,
  onColumnFiltersChange: setColumnFilters,
  onGlobalFilterChange: setGlobalFilter,
  onColumnVisibilityChange: setColumnVisibility,
  manualPagination: true,
  manualSorting: true,
  manualFiltering: true,
  getCoreRowModel: getCoreRowModel(),
})

// POST {route}/tanstack
body: JSON.stringify({ pagination, sorting, columnFilters, globalFilter, columnVisibility })
```

A two-element array is always `in`, never a range — a pair of ids would otherwise be ambiguous. Send a range as `{ min, max }` or `{ op: "between", value: [lo, hi] }`.

## Install

```sh
dotnet add package a2n.Vista.Adapters.TanStackTable
```

Depends on [`a2n.Vista.Core`](https://www.nuget.org/packages/a2n.Vista.Core).

## Documentation

See the [project README](https://github.com/anwarminarso/a2n.Vista#readme) and the [adapter contract spec](https://github.com/anwarminarso/a2n.Vista/blob/main/docs/spec/04-adapter-contract.md).

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/anwarminarso/a2n.Vista/blob/main/LICENSE).
