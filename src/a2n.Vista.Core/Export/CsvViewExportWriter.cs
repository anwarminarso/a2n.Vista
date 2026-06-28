using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Metadata;

namespace a2n.Vista.Export;

/// <summary>
/// Built-in CSV export writer (Decision Log D115). Emits a header row of field labels followed by one row
/// per record, RFC 4180-quoted, CRLF-terminated, UTF-8 with a BOM (so Excel detects UTF-8). Columns are
/// the view's non-hidden fields in projection order (<see cref="ExportColumns"/>).
/// </summary>
public sealed class CsvViewExportWriter : IViewExportWriter
{
    /// <inheritdoc />
    public string Format => "csv";

    /// <inheritdoc />
    public string ContentType => "text/csv";

    /// <inheritdoc />
    public string FileExtension => "csv";

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

        // UTF-8 with BOM, leave the destination stream open for the caller.
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 4096, leaveOpen: true);

        var line = new StringBuilder();
        AppendRecord(line, columns.Count, i => columns[i].Label);
        await writer.WriteAsync(line.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            line.Clear();
            AppendRecord(line, columns.Count, i => Format1(ExportColumns.Value(row, columns[i].Name)));
            await writer.WriteAsync(line.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AppendRecord(StringBuilder line, int count, System.Func<int, string> cell)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                line.Append(',');
            }

            line.Append(Escape(cell(i)));
        }

        line.Append("\r\n");
    }

    private static string Format1(object? value) =>
        value switch
        {
            null => string.Empty,
            string s => s,
            System.IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    /// <summary>RFC 4180: quote a field containing a comma, quote, CR or LF; double internal quotes.</summary>
    private static string Escape(string value)
    {
        var mustQuote = value.IndexOfAny(QuoteTriggers) >= 0;
        if (!mustQuote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static readonly char[] QuoteTriggers = [',', '"', '\r', '\n'];
}
