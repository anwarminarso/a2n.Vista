using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Metadata;

namespace a2n.Vista.Export;

/// <summary>
/// A pluggable export formatter for one file format (Decision Log D115). Given a view's metadata and the
/// already-materialized, scope-applied, <c>MaxExportRows</c>-bounded rows, it writes the formatted file to
/// a destination stream. Built-in writers (CSV, XLSX) ship with Vista; an application can register a
/// custom writer (for example a ClosedXML-based XLSX writer) via <c>AddVistaExportWriter&lt;T&gt;()</c>,
/// overriding a built-in by sharing its <see cref="Format"/>.
/// </summary>
/// <remarks>
/// Writers are neutral: they reference <c>a2n.Vista.Core</c> only and use the BCL — no EF, no HTTP. The
/// column set, order, and labels come from the view's non-hidden <see cref="FieldMetadata"/>
/// (see <see cref="ExportColumns"/>).
/// </remarks>
public interface IViewExportWriter
{
    /// <summary>The format identifier this writer handles (for example <c>"csv"</c>, <c>"xlsx"</c>); matched case-insensitively.</summary>
    string Format { get; }

    /// <summary>The MIME content type for the produced file (for example <c>"text/csv"</c>).</summary>
    string ContentType { get; }

    /// <summary>The file extension (without the dot) for the produced file (for example <c>"csv"</c>).</summary>
    string FileExtension { get; }

    /// <summary>Writes the export to <paramref name="destination"/>.</summary>
    /// <param name="destination">The stream to write the formatted file to.</param>
    /// <param name="view">The view metadata supplying the exportable columns (non-hidden fields).</param>
    /// <param name="rows">The materialized rows (each a projected row object) to export.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task WriteAsync(Stream destination, ViewMetadata view, IReadOnlyList<object?> rows, CancellationToken cancellationToken);
}
