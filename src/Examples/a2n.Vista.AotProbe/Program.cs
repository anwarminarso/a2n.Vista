// AOT verification probe — Source Generator Phase 1, Task 5.2 (R6.3, design "Property 1").
//
// Purpose: exercise the *covered* (generated-accessor) export path end to end so the trim/AOT
// analyzers (enabled via <IsAotCompatible>true</IsAotCompatible>) can prove it is free of IL2026
// (RequiresUnreferencedCode) and IL3050 (RequiresDynamicCode) warnings. With those codes promoted to
// errors in the .csproj, a green build IS the verification.
//
// The probe hand-writes the accessor map that the source generator would emit (a cast + property read
// per projected property), registers it into a2n.Vista.Metadata.ViewAccessorRegistry, builds a
// ViewMetadata, and runs CsvViewExportWriter.WriteAsync over a sample row — i.e. exactly what the
// export pipeline does for a typed Style B view. No EF, no AspNetCore, no reflection.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Contracts;
using a2n.Vista.Export;
using a2n.Vista.Metadata;

namespace a2n.Vista.AotProbe;

/// <summary>A sample projected row POCO, standing in for a typed Style B view's <c>TQuery</c>.</summary>
internal sealed class CustomerListItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Balance { get; init; }
}

internal static class Program
{
    private const string ViewName = "customers";

    private static async Task<int> Main()
    {

        // 1) Register the generated-style accessor map: cast + property read, exactly what the source
        //    generator emits. No reflection, no PropertyInfo — this is the AOT-clean shape.
        var accessors = new Dictionary<string, Func<object, object?>>(StringComparer.Ordinal)
        {
            ["Id"] = static row => ((CustomerListItem)row).Id,
            ["Name"] = static row => ((CustomerListItem)row).Name,
            ["Balance"] = static row => ((CustomerListItem)row).Balance,
        };
        ViewAccessorRegistry.Register(ViewName, accessors);

        // 2) Build the view metadata the export writer consumes.
        var view = BuildViewMetadata();

        // 3) Run the covered export path: CsvViewExportWriter -> ExportColumns.Value(view.Name, row, field)
        //    -> ViewAccessorRegistry hit -> generated accessor. This is the path Task 5.2 verifies.
        var rows = new object?[]
        {
            new CustomerListItem { Id = 1, Name = "Alfreds Futterkiste", Balance = 1234.50m },
            new CustomerListItem { Id = 2, Name = "Around the Horn, Ltd.", Balance = 0m },
        };

        var writer = new CsvViewExportWriter();
        using var buffer = new MemoryStream();
        await writer.WriteAsync(buffer, view, rows, CancellationToken.None).ConfigureAwait(false);

        var csv = Encoding.UTF8.GetString(buffer.ToArray());

        // 4) Also call the clean ExportColumns.Value overload directly, to keep that exact call on the
        //    analyzed surface even if the writer internals ever change.
        var probedName = ExportColumns.Value(view.Name, rows[0], "Name");

        Console.WriteLine("AOT probe: generated-accessor export path exercised.");
        Console.WriteLine($"Direct ExportColumns.Value(\"{ViewName}\", row, \"Name\") => {probedName}");
        Console.WriteLine("---- CSV ----");
        Console.Write(csv);

        // 5) Phase 2 (Task 11.1): exercise the generated Style B List and Detail compiled read path so
        //    the trim/AOT analyzer proves it is free of IL2026/IL3050 (R5.1/R5.4/R5.5/R1.7).
        await StyleBExecutableProbe.RunAsync().ConfigureAwait(false);

        // 6) Phase 3 (spec source-generator-write-mapper, Task 11.1, R10.1–R10.6): bind a payload →
        //    resolve a WriteMapper via WriteMapperResolver → apply it to an entity → persist, so the
        //    trim/AOT analyzer proves the generated write-mapper path is free of IL2026/IL3050.
        await GeneratedWriteMapperProbe.RunAsync().ConfigureAwait(false);

        // 7) Phase 5 (spec source-generator-json-typeinfo, Task 9.1, R8.1/R8.2/R8.3; D125/D126): dispatch
        //    List/Detail and a write through the generated IViewInvoker (from ViewInvokerStore), bind a
        //    write body and serialize responses through the Serialization_Seam using ONLY the shipped
        //    VistaStaticJsonContext + the source-generated per-view contexts (Generated_View_Context, drained
        //    from a2n.Vista.Core's GeneratedJsonContextStore) — NO developer App_Json_Context — with the
        //    reflection fallback removed, and demonstrate the Style A coexistence boundary. A green build
        //    proves the full typed Style B HTTP round-trip is free of IL2026/IL3050 with the developer
        //    context now optional.
        await HttpSurfaceProbe.RunAsync().ConfigureAwait(false);

        // 8) Phase 6 (spec openapi-emitter, Task 11.1, R13.2/R13.3/R13.4; D127): build an envelopes +
        //    FilterNode-only OpenAPI document from the reflection-free descriptors and the metadata-driven
        //    operation structure (no DTO reflection), and serialize it through the source-gen context. A
        //    green build proves the emitter's structure/descriptor path is free of IL2026/IL3050, and the
        //    RUC DtoSchemaGenerator is never reached on it.
        OpenApiDescriptorProbe.Run();

        // 9) M9 Style A coverage (spec style-a-coverage, Task 9.1, R9.1/R9.2/R9.3; D129/D130): drive the
        //    COVERED named-row Style A view (export via the generated accessor + read-DTO serialization
        //    through the seam using the generated per-view context) AOT-clean, and the D96 asymmetry case
        //    (writable anonymous-row view: bind + serialize the named TCrud through the generated context
        //    AOT-clean WHILE the anonymous read row stays on the RUC reflection path by design, isolated
        //    behind a narrowly-scoped suppression). A green build proves the covered Style A slice is free
        //    of IL2026/IL3050 while the anonymous read-row path is legitimately RUC and not required to be
        //    AOT-clean. Runs after the Phase 5 probe so the seam's reflection fallback is already removed.
        StyleACoverageProbe.Run();

        return 0;
    }

    private static ViewMetadata BuildViewMetadata()
    {
        var fields = new List<FieldMetadata>
        {
            FieldMetadata.Create("Id", typeof(int), allowedOperators: FilterOperator.None),
            FieldMetadata.Create("Name", typeof(string), allowedOperators: FilterOperator.None),
            FieldMetadata.Create("Balance", typeof(decimal), allowedOperators: FilterOperator.None),
        };

        return new ViewMetadata(
            Name: ViewName,
            Route: "/api/views/customers",
            QueryType: typeof(CustomerListItem),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: true);
    }
}
