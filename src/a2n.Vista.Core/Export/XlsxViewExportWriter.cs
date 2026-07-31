using System;
using System.Collections.Generic;
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
        var sheet = BuildSheetXml(view.Name, columns, rows, cancellationToken);

        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(archive, "[Content_Types].xml", ContentTypesXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "_rels/.rels", RootRelsXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "xl/workbook.xml", WorkbookXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "xl/worksheets/sheet1.xml", sheet, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildSheetXml(
        string viewName,
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
                var value = ExportColumns.Value(viewName, row, columns[c].Name);
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
            .Append(SecurityElement.Escape(StripXmlIllegalCharacters(text)))
            .Append("</t></is></c>");
    }

    /// <summary>
    /// Removes characters that are not legal in XML 1.0 content. <see cref="SecurityElement.Escape"/> handles
    /// <c>&lt; &gt; &amp; ' "</c> but passes control characters through, and a single one of them makes the
    /// worksheet part malformed XML — at which point Excel rejects the entire workbook rather than one cell.
    /// </summary>
    /// <remarks>
    /// Illegal in XML 1.0: U+0000–U+0008, U+000B, U+000C, U+000E–U+001F, U+FFFE and U+FFFF. Tab (U+0009),
    /// LF (U+000A) and CR (U+000D) are legal and preserved. A well-formed surrogate <b>pair</b> is preserved
    /// (astral characters such as emoji stay intact); a lone surrogate is dropped, since it cannot be encoded.
    /// </remarks>
    private static string StripXmlIllegalCharacters(string text)
    {
        if (!NeedsStripping(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    builder.Append(c).Append(text[i + 1]);
                    i++;
                }

                continue; // a lone high surrogate is dropped
            }

            if (char.IsLowSurrogate(c))
            {
                continue; // a lone low surrogate is dropped
            }

            if (IsLegalXmlChar(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool NeedsStripping(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    continue;
                }

                return true; // lone surrogate
            }

            if (char.IsLowSurrogate(c) || !IsLegalXmlChar(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLegalXmlChar(char c) =>
        c is '\t' or '\n' or '\r' || (c >= ' ' && c <= '\uFFFD');

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
