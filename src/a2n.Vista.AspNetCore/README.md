<p align="center">
  <img src="https://raw.githubusercontent.com/anwarminarso/a2n.Vista/main/assets/a2n-vista-wordmark.png" alt="a2n.Vista" width="420" />
</p>

# a2n.Vista.AspNetCore

HTTP composition for [a2n.Vista](https://github.com/anwarminarso/a2n.Vista) — map a view, get an endpoint.

- **Action-style mapping** — `POST list/detail/export/create/update/delete` + `GET metadata`, with the full route composed at registration (`/api/views` by default, or a `RouteGroup` prefix). One view = one endpoint.
- **Secure-by-default authorization** — a single-door `IViewAuthorizer`; allow-all with a warning in Development, but **fail-closed at startup** in non-Development unless `AllowAnonymousAccess()` is called.
- **Write errors** — `ProblemDetails` bodies. A token-bearing write repeats the quoted strong `ETag` as `If-Match` (428 `write-precondition-required` when it is missing, 412 `write-concurrency-conflict` when it is stale); a constraint violation is 422 `write-constraint-violation`, any other save failure is 500 `write-persistence-failed`, and a probe-recognized lock is 503 `write-busy` with `Retry-After`. A successful delete, and an update that leaves the row outside read scope, are 204 with no body and no `ETag`. See the [0.0.5 changelog](https://github.com/anwarminarso/a2n.Vista/blob/main/CHANGELOG.md#005---2026-10-18).
- **AOT-clean serialization seam** — the typed Style B `request → authorize → execute → serialize` path is trim/AOT-clean.

## Install

```sh
dotnet add package a2n.Vista.AspNetCore --version 0.0.5
```

Depends on [`a2n.Vista.Core`](https://www.nuget.org/packages/a2n.Vista.Core). Pair with [`a2n.Vista.EntityFrameworkCore`](https://www.nuget.org/packages/a2n.Vista.EntityFrameworkCore) to execute views.

## Documentation

See the [project README](https://github.com/anwarminarso/a2n.Vista#readme) and the [ASP.NET Core mapping spec](https://github.com/anwarminarso/a2n.Vista/blob/main/docs/spec/05-aspnetcore-mapping.md).

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/anwarminarso/a2n.Vista/blob/main/LICENSE).
