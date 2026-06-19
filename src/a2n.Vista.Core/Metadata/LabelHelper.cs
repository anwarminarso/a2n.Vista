using System.Text;

namespace a2n.Vista.Metadata;

/// <summary>
/// Derives human-friendly display labels from code identifiers.
/// Used to auto-populate <see cref="FieldMetadata.Label"/> from a field's
/// <see cref="FieldMetadata.Name"/> (for example <c>"ProductName"</c> → <c>"Product Name"</c>),
/// matching the behaviour described in docs/spec/01-view.md §5.4.
/// </summary>
public static class LabelHelper
{
    /// <summary>
    /// Converts a PascalCase or camelCase identifier into a "Title Case" label by inserting
    /// spaces at word boundaries and capitalizing the first letter of each word.
    /// </summary>
    /// <param name="identifier">The source identifier (typically a property name).</param>
    /// <returns>
    /// A spaced, title-cased label. Examples: <c>"ProductId"</c> → <c>"Product Id"</c>,
    /// <c>"UnitPrice"</c> → <c>"Unit Price"</c>, <c>"customerID"</c> → <c>"Customer ID"</c>,
    /// <c>"XMLParser"</c> → <c>"XML Parser"</c>, <c>"Address1"</c> → <c>"Address 1"</c>.
    /// Returns the input unchanged when it is <see langword="null"/> or empty.
    /// </returns>
    /// <remarks>
    /// Word boundaries are detected at transitions between lower/upper case, at the end of an
    /// acronym that is followed by a normal word (for example <c>"XMLParser"</c>), at
    /// letter↔digit transitions, and at separator characters (<c>'_'</c>, <c>'-'</c>, whitespace).
    /// Existing capitalization within a word is preserved so acronyms such as <c>"ID"</c> survive.
    /// </remarks>
    public static string ToTitleCase(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return identifier ?? string.Empty;
        }

        var builder = new StringBuilder(identifier.Length + 8);
        var wordStart = true;

        for (var i = 0; i < identifier.Length; i++)
        {
            var current = identifier[i];

            if (IsSeparator(current))
            {
                // Collapse separators into a single space, but never lead with one.
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                wordStart = true;
                continue;
            }

            if (builder.Length > 0 && builder[^1] != ' ' && ShouldBreakBefore(identifier, i))
            {
                builder.Append(' ');
                wordStart = true;
            }

            builder.Append(wordStart ? char.ToUpperInvariant(current) : current);
            wordStart = false;
        }

        return builder.ToString();
    }

    private static bool IsSeparator(char c) => c is '_' or '-' || char.IsWhiteSpace(c);

    /// <summary>
    /// Decides whether a word boundary occurs immediately before position <paramref name="index"/>.
    /// </summary>
    private static bool ShouldBreakBefore(string s, int index)
    {
        var previous = s[index - 1];
        var current = s[index];

        // lower/digit → Upper : "productName" → "product Name".
        if (char.IsUpper(current) && !char.IsUpper(previous))
        {
            return true;
        }

        // Acronym end followed by a new word: "XMLParser" → "XML Parser".
        if (char.IsUpper(previous) && char.IsUpper(current)
            && index + 1 < s.Length && char.IsLower(s[index + 1]))
        {
            return true;
        }

        // letter ↔ digit transitions: "Address1" → "Address 1", "3D" → "3 D".
        if (char.IsDigit(current) != char.IsDigit(previous)
            && (char.IsLetter(current) || char.IsLetter(previous)))
        {
            return true;
        }

        return false;
    }
}
