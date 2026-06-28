// Licensed to the a2n.Vista project. Published artifact — English only.
//
// An equatable surrogate for Microsoft.CodeAnalysis.Location.
//
// WHY THIS EXISTS (R1.3 caching):
//   Microsoft.CodeAnalysis.Location does NOT have value-based equality in the sense the incremental
//   pipeline needs — storing a raw Location on the equatable ViewModel would defeat Roslyn's caching
//   and regenerate every view on unrelated edits (Spec 03 §12). The standard generator pattern is to
//   capture a lightweight, fully equatable surrogate (file path + text span + line span) on the model
//   and reconstruct a real Location with Location.Create(...) only at report time in RegisterSourceOutput.

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// A value-equal snapshot of a source <see cref="Location"/>: the syntax-tree file path, the raw
    /// character <see cref="TextSpan"/>, and the <see cref="LinePositionSpan"/>. Being a record over
    /// value types/strings keeps the owning model genuinely equatable (R1.3); reconstruct a real
    /// <see cref="Location"/> via <see cref="ToLocation"/> at diagnostic-report time.
    /// </summary>
    internal sealed record LocationInfo
    {
        public LocationInfo(string filePath, TextSpan textSpan, LinePositionSpan lineSpan)
        {
            FilePath = filePath;
            TextSpan = textSpan;
            LineSpan = lineSpan;
        }

        /// <summary>Path of the syntax tree the location came from.</summary>
        public string FilePath { get; }

        /// <summary>Raw character span within the source text.</summary>
        public TextSpan TextSpan { get; }

        /// <summary>Line/column span (used by <see cref="Location.Create(string, TextSpan, LinePositionSpan)"/>).</summary>
        public LinePositionSpan LineSpan { get; }

        /// <summary>
        /// Captures the surrogate from a syntax node, or <c>null</c> when <paramref name="node"/> is
        /// <c>null</c>. <see cref="TextSpan"/> and <see cref="LinePositionSpan"/> are both value types
        /// with structural equality, so the result participates cleanly in the model's value equality.
        /// </summary>
        public static LocationInfo From(Microsoft.CodeAnalysis.SyntaxNode node)
            => node is null ? null : From(node.GetLocation());

        /// <summary>
        /// Captures the surrogate from a token (e.g. a class identifier), or <c>null</c> when the token
        /// carries no source location.
        /// </summary>
        public static LocationInfo From(Microsoft.CodeAnalysis.SyntaxToken token)
            => From(token.GetLocation());

        /// <summary>
        /// Captures the surrogate from a <see cref="Location"/>, or <c>null</c> when it is <c>null</c>.
        /// </summary>
        public static LocationInfo From(Location location)
        {
            if (location is null)
            {
                return null;
            }

            return new LocationInfo(
                location.SourceTree?.FilePath ?? string.Empty,
                location.SourceSpan,
                location.GetLineSpan().Span);
        }

        /// <summary>
        /// Reconstructs a reportable <see cref="Location"/> from this surrogate. Returns
        /// <see cref="Location.None"/> when no file path was captured.
        /// </summary>
        public Location ToLocation()
            => string.IsNullOrEmpty(FilePath)
                ? Location.None
                : Location.Create(FilePath, TextSpan, LineSpan);
    }
}
