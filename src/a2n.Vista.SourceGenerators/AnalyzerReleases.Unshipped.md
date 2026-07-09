; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VISTA0001 | a2n.Vista.SourceGenerators | Error | Style B view must be declared 'partial'; the view is skipped. See https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/VISTA0001.md
VISTA0002 | a2n.Vista.SourceGenerators | Info | Style B view has no public parameterless constructor; accessors cannot be registered at module load and the view is skipped. See https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/VISTA0002.md
VISTA0003 | a2n.Vista.SourceGenerators | Warning | Style B view projection cannot be reproduced statically; no execution plan is generated and the view remains metadata-only. See https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/VISTA0003.md
VISTA0020 | a2n.Vista.SourceGenerators | Error | Style B executable view declares no key and projects from more than one source entity, so no key can be derived. See https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/VISTA0020.md
VISTA0030 | a2n.Vista.SourceGenerators | Error | Writable view's CRUD facet declares zero MapWritable mappings; no write mapper is generated and the compilation fails. See https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/VISTA0030.md
VISTA0031 | a2n.Vista.SourceGenerators | Error | MapWritable target is a navigation rather than a scalar member; no write mapper is generated and the compilation fails. See https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/VISTA0031.md
VISTA0032 | a2n.Vista.SourceGenerators | Error | MapWritable targets a declared key or the concurrency token; no write mapper is generated and the compilation fails. See https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/VISTA0032.md
VISTA0033 | a2n.Vista.SourceGenerators | Warning | Writable view's MapWritable chain cannot be analyzed statically; no write mapper is generated and the view falls back to reflection. See https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/VISTA0033.md
