using System.Globalization;
using System.Linq.Expressions;

namespace a2n.Vista.Write;

/// <summary>
/// Formats an optimistic-concurrency token into the stable wire string used for <c>If-Match</c> and
/// <c>ETag</c> (Decision Log D146, D159).
/// </summary>
public static class ConcurrencyTokenFormat
{
    /// <summary>
    /// Formats <paramref name="value"/> for the wire. A <c>byte[]</c> rowversion becomes standard
    /// Base64. <see cref="DateTime"/> and <see cref="DateTimeOffset"/> use the round-trip <c>O</c>
    /// format in invariant culture. A <see cref="DateTime"/> whose <see cref="DateTime.Kind"/> is
    /// <see cref="DateTimeKind.Unspecified"/> is labeled UTC with <see cref="DateTime.SpecifyKind"/>
    /// and is not shifted. <see cref="DateTimeKind.Local"/> is converted with
    /// <see cref="DateTime.ToUniversalTime"/>. A <see cref="DateTimeOffset"/> keeps its offset.
    /// Any other formattable value uses invariant culture. <see langword="null"/> stays
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="value">The token value read from the entity or the projected row.</param>
    /// <returns>The wire string, or <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.</returns>
    public static string? ToWire(object? value) => value switch
    {
        null => null,
        byte[] bytes => Convert.ToBase64String(bytes),
        DateTime dateTime => NormalizeDateTime(dateTime).ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>
    /// Labels an unspecified <see cref="DateTime"/> as UTC without moving the clock, and converts a
    /// local value to UTC. EF Core SQLite reads a <see cref="DateTime"/> back as
    /// <see cref="DateTimeKind.Unspecified"/>; labeling it UTC makes the detail <c>ETag</c> match the
    /// create/update <c>ETag</c> minted from <see cref="DateTime.UtcNow"/>.
    /// </summary>
    private static DateTime NormalizeDateTime(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>
    /// Converts a bare wire token back to <paramref name="clrType"/> so it can be bound as the original
    /// value of an EF concurrency predicate. The inverse of <see cref="ToWire"/> for the token types
    /// Vista writes (Guid, integers, <c>byte[]</c>, string, and other invariant-formattable values).
    /// </summary>
    /// <param name="wire">The bare token (quotes already removed).</param>
    /// <param name="clrType">The concurrency property's CLR type.</param>
    /// <param name="value">The converted value when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="wire"/> is a value of <paramref name="clrType"/>.</returns>
    public static bool TryFromWire(string wire, Type clrType, out object? value)
    {
        ArgumentNullException.ThrowIfNull(wire);
        ArgumentNullException.ThrowIfNull(clrType);

        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type == typeof(string))
        {
            value = wire;
            return true;
        }

        if (type == typeof(Guid))
        {
            if (Guid.TryParse(wire, out var guid))
            {
                value = guid;
                return true;
            }

            value = null;
            return false;
        }

        if (type == typeof(byte[]))
        {
            var buffer = new byte[wire.Length];
            if (Convert.TryFromBase64String(wire, buffer, out var written))
            {
                value = buffer.AsSpan(0, written).ToArray();
                return true;
            }

            value = null;
            return false;
        }

        if (type == typeof(DateTime))
        {
            if (DateTime.TryParse(wire, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
            {
                value = NormalizeDateTime(dateTime);
                return true;
            }

            value = null;
            return false;
        }

        if (type == typeof(DateTimeOffset))
        {
            // Exact "O" first so a non-zero offset round-trips. Do not convert to UTC: EF SQLite
            // stores the offset, and a shifted value would miss the WHERE predicate.
            if (DateTimeOffset.TryParseExact(wire, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exact)
                || DateTimeOffset.TryParse(wire, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out exact))
            {
                value = exact;
                return true;
            }

            value = null;
            return false;
        }

        if (type.IsEnum)
        {
            if (Enum.TryParse(type, wire, ignoreCase: false, out var enumValue))
            {
                value = enumValue;
                return true;
            }

            value = null;
            return false;
        }

        try
        {
            value = Convert.ChangeType(wire, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            value = null;
            return false;
        }
        catch (InvalidCastException)
        {
            value = null;
            return false;
        }
        catch (OverflowException)
        {
            value = null;
            return false;
        }
    }
}

/// <summary>
/// Reads the member name from a simple concurrency-token selector (<c>e =&gt; e.Member</c>).
/// </summary>
public static class ConcurrencyTokenMember
{
    /// <summary>
    /// Returns the member name selected by a simple <c>e =&gt; e.Member</c> lambda, unwrapping a
    /// compiler-inserted conversion, or <see langword="null"/> for any other shape.
    /// </summary>
    /// <param name="selector">The token selector captured at authoring time.</param>
    /// <returns>The member name, or <see langword="null"/> when <paramref name="selector"/> is not a simple member access.</returns>
    public static string? TryGetName(LambdaExpression? selector)
    {
        if (selector is null)
        {
            return null;
        }

        var body = selector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        return body is MemberExpression { Expression: ParameterExpression } member ? member.Member.Name : null;
    }
}
