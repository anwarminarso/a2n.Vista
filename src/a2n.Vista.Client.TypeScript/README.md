<p align="center">
  <img src="https://raw.githubusercontent.com/anwarminarso/a2n.Vista/main/assets/a2n-vista-wordmark.png" alt="a2n.Vista" width="420" />
</p>

# a2n.Vista.Client.TypeScript

A standalone **.NET CLI tool** that generates a framework-agnostic, strongly-typed **TypeScript client** from a [a2n.Vista](https://github.com/anwarminarso/a2n.Vista) OpenAPI document.

Reads an OpenAPI 3.x document (from a file or an HTTPS URL) and emits:

- Per-view `TRow`/`TCrud` DTO types, the fixed Vista request/response envelopes, the presence-discriminated `FilterNode` union, and the RFC 7807 `ProblemDetails` type.
- A per-view typed client over an injectable HTTP transport and auth provider.

**Secure-by-default** — read facets by default, write facets gated behind an explicit opt-in; never embeds a credential; defaults transport to HTTPS. Deterministic, atomic, UTF-8 (no BOM) output. References **no** Vista package — a pure downstream consumer.

## Install

```sh
dotnet add package a2n.Vista.Client.TypeScript
```

Then run the generator against a document emitted by [`a2n.Vista.OpenApi`](https://www.nuget.org/packages/a2n.Vista.OpenApi). See the [project README](https://github.com/anwarminarso/a2n.Vista#readme) for the exact invocation.

## Documentation

See the [project README](https://github.com/anwarminarso/a2n.Vista#readme).

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/anwarminarso/a2n.Vista/blob/main/LICENSE).
