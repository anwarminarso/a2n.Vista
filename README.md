# a2n.Vista

> *Define a view, get an API. Type-safe, AOT-friendly, grid-agnostic projections for ASP.NET Core.*

`a2n.Vista` adalah library .NET untuk membangun back-office dengan **View** berbasis projection LINQ kompleks, integrasi grid agnostic, secure-by-default, dan AOT-clean.

Vista adalah penerus desain dari [`a2n.DynData`](https://github.com/anwarminarso/a2n.DynData) — bukan rewrite mekanis, melainkan redesign yang fokus pada diferensiasi unik. Lihat [`ROADMAP.md`](./ROADMAP.md) untuk konteks lengkap.

## Status

**Pre-alpha.** Skeleton solusi sudah berdiri; implementasi belum dimulai. Lihat [`docs/spec/`](./docs/spec/) untuk spec yang sedang dimatangkan.

## Tiga Pilar

1. **View sebagai Citizen Utama** — projection LINQ deklaratif sebagai unit inti, secure-by-default, strongly typed.
2. **Integrasi UI Grid-Agnostic** — core netral, adapter terpisah per ekosistem grid (DataTables, AG Grid, MudBlazor, Telerik, Syncfusion, TanStack, PrimeNG, OData, GraphQL).
3. **AOT-First** — source generator untuk metadata & endpoint, tanpa runtime reflection di hot path.

## Struktur Solusi

```
src/
  a2n.Vista.Core                 ← engine: view, query, expression, metadata
  a2n.Vista.AspNetCore           ← endpoint mapping (MVC + Minimal API)
  a2n.Vista.EntityFrameworkCore  ← integrasi EF Core
  a2n.Vista.SourceGenerators     ← compile-time codegen, AOT
  a2n.Vista.Newtonsoft           ← optional, untuk legacy
  Adapters/
    a2n.Vista.Adapters.*         ← adapter UI per ekosistem
  a2n.Vista.Client.TypeScript    ← TS codegen tool
```

## Dokumentasi

- [ROADMAP](./ROADMAP.md) — visi, posisi vs kompetitor, strategi rilis
- [Spec Pilar 1 — View](./docs/spec/01-view.md) — definisi konsep View, API surface

## Lisensi

TBD.
