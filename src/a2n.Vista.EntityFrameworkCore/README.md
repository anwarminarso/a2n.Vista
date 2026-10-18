<p align="center">
  <img src="https://raw.githubusercontent.com/anwarminarso/a2n.Vista/main/assets/a2n-vista-wordmark.png" alt="a2n.Vista" width="420" />
</p>

# a2n.Vista.EntityFrameworkCore

Entity Framework Core execution for [a2n.Vista](https://github.com/anwarminarso/a2n.Vista) views.

Executes a Vista `View` over EF Core:

- **Read** — List + Detail-by-key, deterministic paging (key-field tiebreaker), filter/sort/search, composite keys.
- **Provider-aware** — an `IQueryDialect` port with a default `LIKE` dialect (add [`a2n.Vista.EntityFrameworkCore.Npgsql`](https://www.nuget.org/packages/a2n.Vista.EntityFrameworkCore.Npgsql) for PostgreSQL `ILIKE`).
- **Write facet** — Create/Update/Delete with a `MapWritable` default-deny whitelist, server-trusted scope, and a single `SaveChanges` per operation. A token-bearing write sends the quoted strong `ETag` as `If-Match` (428 when it is missing, 412 when it is stale); a constraint violation is 422, any other save failure is 500, a probe-recognized lock is 503 with `Retry-After`, and a successful delete or an update that leaves read scope is 204. See the [0.0.5 changelog](https://github.com/anwarminarso/a2n.Vista/blob/main/CHANGELOG.md#005---2026-10-18).
- **DI wiring** — `AddVista` / `Register<TView>` builds view metadata and adopts generated execution plans when present.

## Install

```sh
dotnet add package a2n.Vista.EntityFrameworkCore --version 0.0.5
```

Depends on [`a2n.Vista.Core`](https://www.nuget.org/packages/a2n.Vista.Core) (which bundles the Pillar 3 source generator).

## Documentation

See the [project README](https://github.com/anwarminarso/a2n.Vista#readme) and the [filter & query spec](https://github.com/anwarminarso/a2n.Vista/blob/main/docs/spec/02-filter-and-query.md).

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/anwarminarso/a2n.Vista/blob/main/LICENSE).
