; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VISTA0001 | a2n.Vista.SourceGenerators | Error | Style B view must be declared 'partial'; the view is skipped. See https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/VISTA0001.md
VISTA0002 | a2n.Vista.SourceGenerators | Info | Style B view has no public parameterless constructor; accessors cannot be registered at module load and the view is skipped. See https://github.com/anwarminarso/a2n.Vista/blob/main/docs/diagnostics/VISTA0002.md
