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

        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(archive, "[Content_Types].xml", ContentTypesXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "_rels/.rels", RootRelsXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "xl/workbook.xml", WorkbookXml, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml, cancellationToken).ConfigureAwait(false);
            await WriteSheetEntryAsync(archive, view.Name, columns, rows, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes the worksheet part straight into its archive entry, one row at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The worksheet used to be accumulated into a single <see cref="StringBuilder"/>, returned as one
    /// <see cref="string"/>, and then converted with <c>Encoding.UTF8.GetBytes</c> — two large-object-heap
    /// buffers holding the whole document, the intermediate one in UTF-16 at roughly twice the byte size, on
    /// top of the builder's own chunks (audit finding <c>PERF-03</c>). At the default 100,000-row export cap
    /// that is the dominant allocation of the request. Peak memory is now one row's worth of characters plus
    /// the archive's own compression buffer, whatever the row count.
    /// </para>
    /// <para>
    /// Byte output is unchanged: the same markup in the same order, and the writer is UTF-8 <em>without</em> a
    /// preamble to match what <c>Encoding.UTF8.GetBytes</c> produced.
    /// </para>
    /// </remarks>
    private static async Task WriteSheetEntryAsync(
        ZipArchive archive,
        string viewName,
        IReadOnlyList<ExportColumns.Column> columns,
        IReadOnlyList<object?> rows,
        CancellationToken cancellationToken)
    {
        // The column part of an A1 reference is constant down a column, so resolve each one once instead of
        // rebuilding it per cell (the related allocation the same finding calls out).
        var columnNames = new string[columns.Count];
        for (var c = 0; c < columns.Count; c++)
        {
            columnNames[c] = ColumnName(c);
        }

        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, Utf8NoPreamble);

        await writer.WriteAsync(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>")
            .ConfigureAwait(false);

        // One reused builder: a row is composed in memory, flushed, then the buffer is cleared.
        var sb = new StringBuilder();

        // Header row (row 1): always inline strings.
        sb.Append("<row r=\"1\">");
        for (var c = 0; c < columns.Count; c++)
        {
            AppendInlineString(sb, columnNames[c], 1, columns[c].Label);
        }

        sb.Append("</row>");
        await writer.WriteAsync(sb, cancellationToken).ConfigureAwait(false);

        // Data rows start at row 2.
        var rowNo = 1;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNo++;

            sb.Clear();
            sb.Append("<row r=\"").Append(rowNo).Append("\">");
            for (var c = 0; c < columns.Count; c++)
            {
                var value = ExportColumns.Value(viewName, row, columns[c].Name);
                AppendCell(sb, columnNames[c], rowNo, value);
            }

            sb.Append("</row>");
            await writer.WriteAsync(sb, cancellationToken).ConfigureAwait(false);
        }

        await writer.WriteAsync("</sheetData></worksheet>").ConfigureAwait(false);
    }

    private static void AppendCell(StringBuilder sb, string columnName, int rowNumber, object? value)
    {
        if (value is null)
        {
            return; // omit empty cells
        }

        if (IsNumeric(value))
        {
            var number = ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            sb.Append("<c r=\"").Append(columnName).Append(rowNumber).Append("\" t=\"n\"><v>").Append(number).Append("</v></c>");
            return;
        }

        var text = value as string ?? (value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString());
        AppendInlineString(sb, columnName, rowNumber, text ?? string.Empty);
    }

    private static void AppendInlineString(StringBuilder sb, string columnName, int rowNumber, string text)
    {
        sb.Append("<c r=\"").Append(columnName).Append(rowNumber).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
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

    /// <summary>
    /// Builds the column part of an A1-style reference (<c>A</c>, <c>B</c>, …, <c>AA</c>) from a zero-based
    /// column index. Resolved once per column by the sheet writer, not once per cell.
    /// </summary>
    private static string ColumnName(int columnIndex)
    {
        var column = new StringBuilder();
        var n = columnIndex;
        do
        {
            column.Insert(0, (char)('A' + (n % 26)));
            n = (n / 26) - 1;
        }
        while (n >= 0);

        return column.ToString();
    }

    /// <summary>
    /// UTF-8 with no byte-order mark, matching what <c>Encoding.UTF8.GetBytes</c> emits for the fixed parts, so
    /// every part of the package is encoded identically.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoPreamble = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes one of the small fixed package parts, which are constants and need no streaming.</summary>
    private static async Task WriteEntryAsync(ZipArchive archive, string path, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
