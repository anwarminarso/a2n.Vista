<p align="center">
  <img src="https://raw.githubusercontent.com/anwarminarso/a2n.Vista/main/assets/a2n-vista-wordmark.png" alt="a2n.Vista" width="420" />
</p>

# a2n.Vista.EntityFrameworkCore.Npgsql

The PostgreSQL query dialect for [a2n.Vista](https://github.com/anwarminarso/a2n.Vista).

Provides an `IQueryDialect` implementation that maps Vista search to PostgreSQL `ILIKE` (case-insensitive) instead of the default `LIKE`, so text search behaves correctly on Npgsql-backed contexts.

## Install

```sh
dotnet add package a2n.Vista.EntityFrameworkCore.Npgsql
```

Depends on [`a2n.Vista.EntityFrameworkCore`](https://www.nuget.org/packages/a2n.Vista.EntityFrameworkCore). Register the Npgsql dialect alongside `AddVista` when your `DbContext` targets PostgreSQL.

## Documentation

See the [project README](https://github.com/anwarminarso/a2n.Vista#readme) and the [filter & query spec](https://github.com/anwarminarso/a2n.Vista/blob/main/docs/spec/02-filter-and-query.md).

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/anwarminarso/a2n.Vista/blob/main/LICENSE).
