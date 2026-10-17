using System.Globalization;
using System.Linq.Expressions;

namespace a2n.Vista.Write;

/// <summary>
/// Formats an optimistic-concurrency token into the stable wire string used for <c>If-Match</c> and
/// <c>ETag</c> (Decision Log D146, D158).
/// </summary>
public static class ConcurrencyTokenFormat
{
    /// <summary>
    /// Formats <paramref name="value"/> for the wire: a <c>byte[]</c> rowversion becomes Base64, any
    /// other formattable value uses invariant culture, and <see langword="null"/> stays
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="value">The token value read from the entity or the projected row.</param>
    /// <returns>The wire string, or <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.</returns>
    public static string? ToWire(object? value) => value switch
    {
        null => null,
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
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
