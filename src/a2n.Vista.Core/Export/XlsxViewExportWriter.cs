using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Metadata;

namespace a2n.Vista.Export;

/// <summary>
/// Built-in XLSX export writer (Decision Log D115): a clean, zero-dependency re-implementation of the
/// idea behind DynData's <c>LiteExcelWriter</c>. It writes a minimal but valid OpenXML workbook with a
/// single worksheet (header row of field labels + one row per record) using only
/// <see cref="System.IO.Compression"/> and string building — no external package, no styles. Numeric
/// values become number cells; everything else is an inline string (XML-escaped). Columns are the view's
/// non-hidden fields in projection order (<see cref="ExportColumns"/>).
/// </summary>
public sealed class XlsxViewExportWriter : IViewExportWriter
{
    /// <inheritdoc />
    public string Format => "xlsx";

    /// <inheritdoc />
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <inheritdoc />
    public string FileExtension => "xlsx";

    private const string ContentTypesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
        + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
        + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
        + "</Types>";

    private const string RootRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
        + "</Relationships>";

    private const string WorkbookXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
        + "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
        + "</workbook>";

    private const string WorkbookRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
        + "</Relationships>";

    /// <inheritdoc />
    [RequiresUnreferencedCode("Reads projected row values by reflection (Style A); use the source generator path for AOT.")]
    public async Task WriteAsync(
        Stream destination,
        ViewMetadata view,
        IReadOnlyList<object?> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(rows);

        var columns = ExportColumns.For(view);
        var sheet = BuildSheetXml(columns, rows, cancellationToken);

        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(archive, "[Content_Types].xml", ContentTypesXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "_rels/.rels", RootRelsXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "xl/workbook.xml", WorkbookXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "xl/worksheets/sheet1.xml", sheet, cancellationToken).ConfigureAwait(false);
        }
    }

    [RequiresUnreferencedCode("Reads projected row values by reflection (Style A); use the source generator path for AOT.")]
    private static string BuildSheetXml(
        IReadOnlyList<ExportColumns.Column> columns,
        IReadOnlyList<object?> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        // Header row (row 1): always inline strings.
        sb.Append("<row r=\"1\">");
        for (var c = 0; c < columns.Count; c++)
        {
            AppendInlineString(sb, CellRef(c, 1), columns[c].Label);
        }

        sb.Append("</row>");

        // Data rows start at row 2.
        var rowNo = 1;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNo++;
            sb.Append("<row r=\"").Append(rowNo).Append("\">");
            for (var c = 0; c < columns.Count; c++)
            {
                var value = ExportColumns.Value(row, columns[c].Name);
                AppendCell(sb, CellRef(c, rowNo), value);
            }

            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendCell(StringBuilder sb, string cellRef, object? value)
    {
        if (value is null)
        {
            return; // omit empty cells
        }

        if (IsNumeric(value))
        {
            var number = ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            sb.Append("<c r=\"").Append(cellRef).Append("\" t=\"n\"><v>").Append(number).Append("</v></c>");
            return;
        }

        var text = value as string ?? (value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString());
        AppendInlineString(sb, cellRef, text ?? string.Empty);
    }

    private static void AppendInlineString(StringBuilder sb, string cellRef, string text)
    {
        sb.Append("<c r=\"").Append(cellRef).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
            .Append(SecurityElement.Escape(text))
            .Append("</t></is></c>");
    }

    private static bool IsNumeric(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    /// <summary>Builds an A1-style cell reference from a zero-based column index and a 1-based row number.</summary>
    private static string CellRef(int columnIndex, int rowNumber)
    {
        var column = new StringBuilder();
        var n = columnIndex;
        do
        {
            column.Insert(0, (char)('A' + (n % 26)));
            n = (n / 26) - 1;
        }
        while (n >= 0);

        return column.Append(rowNumber).ToString();
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
