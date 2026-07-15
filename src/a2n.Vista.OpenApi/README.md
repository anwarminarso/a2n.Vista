<p align="center">
  <img src="https://raw.githubusercontent.com/anwarminarso/a2n.Vista/main/assets/a2n-vista-wordmark.png" alt="a2n.Vista" width="420" />
</p>

# a2n.Vista.OpenApi

An opt-in **OpenAPI v3.x** emitter for [a2n.Vista](https://github.com/anwarminarso/a2n.Vista) views.

- Emits a deterministic OpenAPI document for every mapped view — the fixed operation set (`list`/`detail`/`metadata`/`export`, plus `create`/`update`/`delete` when writable), security requirements, RFC 7807 error responses, and the polymorphic `FilterNode` schema.
- **Off by default, additive-only** — `AddVistaOpenApi()` + `MapVistaOpenApi()` serve `GET /openapi/v1.json` inside your existing auth pipeline. It changes no route, envelope, or error shape.
- On net9.0/net10.0 it can merge into the built-in `Microsoft.AspNetCore.OpenApi` pipeline.

## Install

```sh
dotnet add package a2n.Vista.OpenApi
```

Depends on [`a2n.Vista.AspNetCore`](https://www.nuget.org/packages/a2n.Vista.AspNetCore). Feed the emitted document to [`a2n.Vista.Client.TypeScript`](https://www.nuget.org/packages/a2n.Vista.Client.TypeScript) to generate a typed client.

## Documentation

See the [project README](https://github.com/anwarminarso/a2n.Vista#readme).

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/anwarminarso/a2n.Vista/blob/main/LICENSE).
