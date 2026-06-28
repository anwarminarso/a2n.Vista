using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Export;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Export-pipeline behavioral-parity test for the source generator coexistence seam (Decision Log D117,
/// design <c>Property 2</c>). Proves the export writers produce IDENTICAL output whether a row value is
/// read through a registered <em>generated</em> accessor map (the AOT-clean path the generator emits) or
/// through the reflection fallback — satisfying R6.2 (and exercising R4.2, R2.3).
/// </summary>
/// <remarks>
/// The <see cref="ViewAccessorRegistry"/> is process-wide, static and idempotent (first registration
/// wins), so a single view name cannot be registered then "cleared". The test therefore isolates the two
/// value-read paths using TWO DIFFERENT view names over the SAME <see cref="WidgetRow"/> shape and field
/// set:
/// <list type="bullet">
/// <item>a "generated" view whose name IS registered with a hand-written accessor map mimicking the
///   generator's emitted output (<c>cast + property read</c>), and</item>
/// <item>a "reflection" view whose name is NEVER registered, so <see cref="ExportColumns.Value(string, object?, string)"/>
///   falls back to reflection.</item>
/// </list>
/// Because the field set and labels are identical and the view <see cref="ViewMetadata.Name"/> is not
/// written into the CSV/XLSX payload, any difference in output would come solely from the value-read path.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The reflection branch is exercised on purpose to prove parity with the generated path.")]
public sealed class ExportParityTests
{
    private static IReadOnlyList<object?> Rows() => new object?[]
    {
        new WidgetRow { Id = 1, Name = "A,B", Price = 10m },   // comma forces RFC 4180 quoting
        new WidgetRow { Id = 2, Name = "plain", Price = 20m },
        new WidgetRow { Id = 3, Name = "Wei\u00DF", Price = 30m }, // non-ASCII to exercise UTF-8 encoding
    };

    /// <summary>
    /// Builds the accessor map exactly as the source generator emits for <see cref="WidgetRow"/>: one
    /// <c>Func&lt;object, object?&gt;</c> per public readable property, each a cast + property read (no
    /// reflection).
    /// </summary>
    private static Dictionary<string, Func<object, object?>> GeneratedWidgetAccessors() =>
        new(StringComparer.Ordinal)
        {
            [nameof(WidgetRow.Id)] = static row => ((WidgetRow)row).Id,
            [nameof(WidgetRow.Name)] = static row => ((WidgetRow)row).Name,
            [nameof(WidgetRow.Price)] = static row => ((WidgetRow)row).Price,
        };

    [Test]
    public async Task Csv_Output_Is_Identical_Via_Generated_Accessor_And_Reflection()
    {
        // Two distinct, unique view names over the SAME WidgetRow shape + field set.
        var generatedViewName = $"Widgets_GenAccessor_{Guid.NewGuid():N}";
        var reflectionViewName = $"Widgets_Reflection_{Guid.NewGuid():N}";

        // Register a generated-style accessor map for ONLY the "generated" view name.
        ViewAccessorRegistry.Register(generatedViewName, GeneratedWidgetAccessors());

        // Confirm the test genuinely exercises BOTH branches: the generated view resolves an accessor,
        // the reflection view does not (so its export must take the reflection fallback).
        await Assert.That(ViewAccessorRegistry.TryGetAccessor(generatedViewName, nameof(WidgetRow.Id), out _))
            .IsTrue();
        await Assert.That(ViewAccessorRegistry.TryGetAccessor(reflectionViewName, nameof(WidgetRow.Id), out _))
            .IsFalse();

        var generatedView = WidgetTestHarness.BuildView(generatedViewName);
        var reflectionView = WidgetTestHarness.BuildView(reflectionViewName);

        var generatedCsv = await WriteCsvAsync(generatedView, Rows());
        var reflectionCsv = await WriteCsvAsync(reflectionView, Rows());

        // Byte-for-byte identical: only the value-read path differs between the two exports.
        await Assert.That(generatedCsv.SequenceEqual(reflectionCsv)).IsTrue();

        // And the produced text is the expected content (header from labels, quoted comma value, UTF-8).
        var text = Encoding.UTF8.GetString(generatedCsv);
        await Assert.That(text).Contains("Id,Name,Price");
        await Assert.That(text).Contains("\"A,B\"");
        await Assert.That(text).Contains("Wei\u00DF");

        // The differing view Name must NOT leak into the payload.
        await Assert.That(text).DoesNotContain(generatedViewName);
        await Assert.That(text).DoesNotContain(reflectionViewName);
    }

    [Test]
    public async Task Xlsx_Sheet_Values_Are_Identical_Via_Generated_Accessor_And_Reflection()
    {
        var generatedViewName = $"Widgets_GenAccessor_{Guid.NewGuid():N}";
        var reflectionViewName = $"Widgets_Reflection_{Guid.NewGuid():N}";

        ViewAccessorRegistry.Register(generatedViewName, GeneratedWidgetAccessors());

        var generatedView = WidgetTestHarness.BuildView(generatedViewName);
        var reflectionView = WidgetTestHarness.BuildView(reflectionViewName);

        var generatedSheet = await WriteXlsxSheetAsync(generatedView, Rows());
        var reflectionSheet = await WriteXlsxSheetAsync(reflectionView, Rows());

        // The worksheet XML (header labels + value cells) is produced solely from labels and row values,
        // so it must be identical regardless of which value-read path served the rows.
        await Assert.That(generatedSheet).IsEqualTo(reflectionSheet);
    }

    private static async Task<byte[]> WriteCsvAsync(ViewMetadata view, IReadOnlyList<object?> rows)
    {
        var writer = new CsvViewExportWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, view, rows, CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<string> WriteXlsxSheetAsync(ViewMetadata view, IReadOnlyList<object?> rows)
    {
        var writer = new XlsxViewExportWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, view, rows, CancellationToken.None);

        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml");
        await Assert.That(sheet).IsNotNull();
        using var reader = new StreamReader(sheet!.Open());
        return await reader.ReadToEndAsync();
    }
}
