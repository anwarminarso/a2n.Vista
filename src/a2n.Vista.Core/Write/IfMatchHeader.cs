namespace a2n.Vista.Write;

/// <summary>
/// Parses an HTTP <c>If-Match</c> header into the strong opaque tags a token-bearing write can place
/// in the database concurrency predicate (RFC 9110 §13.1.1).
/// </summary>
/// <remarks>
/// <para>
/// A header that is exactly <c>*</c> is recognized and reported as a wildcard. A token-bearing write
/// rejects it: a wildcard cannot be the original value of <c>WHERE token = @original</c>, and treating
/// it as "match anything" would drop the atomic check.
/// </para>
/// <para>
/// A single unquoted token (no comma, no quotes) is accepted as a legacy form. Quoted strong tags,
/// including a comma-separated list, are the RFC form. Weak tags (<c>W/"…"</c>) never satisfy
/// <c>If-Match</c> under strong comparison and are ignored. A list that also contains <c>*</c>, a
/// trailing comma, or an unclosed quote is unusable.
/// </para>
/// </remarks>
public static class IfMatchHeader
{
    /// <summary>The result of parsing one <c>If-Match</c> header value.</summary>
    /// <param name="IsMissing">The header was absent, empty, or whitespace.</param>
    /// <param name="IsWildcard">The header was exactly <c>*</c>.</param>
    /// <param name="IsLegacyUnquoted">The header was one unquoted token, accepted as a legacy form.</param>
    /// <param name="StrongTags">Quoted strong tags, or the one legacy token. Empty when none are usable.</param>
    public readonly record struct Result(
        bool IsMissing,
        bool IsWildcard,
        bool IsLegacyUnquoted,
        IReadOnlyList<string> StrongTags)
    {
        /// <summary>An absent header.</summary>
        public static Result Missing { get; } = new(true, false, false, Array.Empty<string>());

        /// <summary>True when at least one strong tag was parsed.</summary>
        public bool HasStrongTag => StrongTags.Count > 0;
    }

    /// <summary>
    /// Parses <paramref name="header"/>. Does not throw: an unusable header yields an empty tag list
    /// so the caller can respond 412.
    /// </summary>
    /// <param name="header">The raw <c>If-Match</c> value, or <see langword="null"/> when the header is absent.</param>
    /// <returns>The parsed result.</returns>
    public static Result Parse(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return Result.Missing;
        }

        var text = header.Trim();
        if (text == "*")
        {
            return new Result(false, true, false, Array.Empty<string>());
        }

        // Legacy: one opaque token, not an entity-tag and not a list. Cheap to accept, and Guid / integer
        // tokens produced before quoting have this shape.
        if (!text.Contains('"', StringComparison.Ordinal))
        {
            if (text.Contains(',', StringComparison.Ordinal))
            {
                return Unusable();
            }

            return new Result(false, false, true, new[] { text });
        }

        return ParseQuotedList(text);
    }

    /// <summary>
    /// Formats <paramref name="opaque"/> as a strong entity-tag (<c>"opaque"</c>, RFC 9110). The caller
    /// passes the bare concurrency token; the quotes are only the header encoding.
    /// </summary>
    /// <param name="opaque">The bare token. Must be a legal <c>etagc</c> sequence.</param>
    /// <returns>The quoted strong entity-tag.</returns>
    /// <exception cref="ArgumentException"><paramref name="opaque"/> is empty or contains a character that cannot appear in an entity-tag.</exception>
    public static string ToStrongEntityTag(string opaque)
    {
        ArgumentException.ThrowIfNullOrEmpty(opaque);
        for (var i = 0; i < opaque.Length; i++)
        {
            if (!IsEtagc(opaque[i]))
            {
                throw new ArgumentException(
                    "A concurrency token cannot be represented as a strong entity-tag.",
                    nameof(opaque));
            }
        }

        return "\"" + opaque + "\"";
    }

    private static Result ParseQuotedList(string text)
    {
        var tags = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            i = SkipOws(text, i);
            if (i >= text.Length)
            {
                break;
            }

            // `*` is only valid as the entire header. Inside a list it makes the header unusable.
            if (text[i] == '*')
            {
                return Unusable();
            }

            var weak = false;
            if (i + 2 < text.Length && text[i] == 'W' && text[i + 1] == '/' && text[i + 2] == '"')
            {
                weak = true;
                i += 2;
            }

            if (i >= text.Length || text[i] != '"')
            {
                return Unusable();
            }

            i++;
            var start = i;
            while (i < text.Length && text[i] != '"')
            {
                if (!IsEtagc(text[i]))
                {
                    return Unusable();
                }

                i++;
            }

            if (i >= text.Length || text[i] != '"')
            {
                return Unusable();
            }

            if (!weak)
            {
                tags.Add(text[start..i]);
            }

            i++;
            i = SkipOws(text, i);
            if (i >= text.Length)
            {
                break;
            }

            if (text[i] != ',')
            {
                return Unusable();
            }

            i++;
            var next = SkipOws(text, i);
            if (next >= text.Length)
            {
                // Trailing comma.
                return Unusable();
            }
        }

        return new Result(false, false, false, tags);
    }

    private static int SkipOws(string text, int index)
    {
        while (index < text.Length && text[index] is ' ' or '\t')
        {
            index++;
        }

        return index;
    }

    /// <summary>RFC 9110 <c>etagc</c>: <c>0x21</c>, <c>0x23–0x7E</c>, or obs-text.</summary>
    private static bool IsEtagc(char c) => c == 0x21 || (c >= 0x23 && c <= 0x7E) || c >= 0x80;

    private static Result Unusable() => new(false, false, false, Array.Empty<string>());
}
