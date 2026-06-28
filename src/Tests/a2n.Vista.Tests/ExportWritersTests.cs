using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Export;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for the built-in export writers (Decision Log D115): CSV (RFC 4180) and XLSX (minimal valid
/// OpenXML package). Columns/labels come from the view's non-hidden fields.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Export reads row values via reflection (Style A) by design.")]
public sealed class ExportWritersTests
{
    private static IReadOnlyList<object?> Rows() => new object?[]
    {
        new WidgetRow { Id = 1, Name = "A,B", Price = 10m },
        new WidgetRow { Id = 2, Name = "plain", Price = 20m },
    };

    [Test]
    public async Task Csv_Writes_Header_And_Quotes_Special_Values()
    {
        var writer = new CsvViewExportWriter();
        using var stream = new MemoryStream();

        await writer.WriteAsync(stream, WidgetTestHarness.BuildView(), Rows(), CancellationToken.None);

        var text = Encoding.UTF8.GetString(stream.ToArray());
        await Assert.That(text).Contains("Id,Name,Price");
        await Assert.That(text).Contains("\"A,B\"");      // comma-containing value is quoted
        await Assert.That(text).Contains("2,plain,20");
        await Assert.That(writer.ContentType).IsEqualTo("text/csv");
    }

    [Test]
    public async Task Xlsx_Produces_Valid_Package_With_Header_And_Numeric_Cell()
    {
        var writer = new XlsxViewExportWriter();
        using var stream = new MemoryStream();

        await writer.WriteAsync(stream, WidgetTestHarness.BuildView(), Rows(), CancellationToken.None);

        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        await Assert.That(zip.GetEntry("[Content_Types].xml")).IsNotNull();
        await Assert.That(zip.GetEntry("xl/workbook.xml")).IsNotNull();

        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml");
        await Assert.That(sheet).IsNotNull();
        using var reader = new StreamReader(sheet!.Open());
        var xml = await reader.ReadToEndAsync();

        await Assert.That(xml).Contains("Id");          // header label
        await Assert.That(xml).Contains("<v>1</v>");    // numeric cell for Id=1
        await Assert.That(writer.FileExtension).IsEqualTo("xlsx");
    }
}
