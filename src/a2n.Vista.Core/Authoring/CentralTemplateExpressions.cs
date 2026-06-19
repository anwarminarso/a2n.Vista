using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// Internal helpers for the Gaya A (central template) authoring builders. Deliberately Gaya-A-scoped
/// (and <see langword="internal"/>) so it never clashes with the class-per-view builders, which keep
/// their own equivalent logic.
/// </summary>
internal static class CentralTemplateExpressions
{
    /// <summary>
    /// Extracts the member name from a simple member-access selector such as <c>x =&gt; x.Name</c>,
    /// transparently unwrapping the <see cref="ExpressionType.Convert"/> the compiler inserts when the
    /// selected member is a value type surfaced through an <see cref="object"/>-typed lambda.
    /// </summary>
    /// <param name="selector">The selector lambda to inspect.</param>
    /// <returns>The selected member's name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="selector"/> is not a simple member access (for example it is a method call or a
    /// composite expression).
    /// </exception>
    public static string GetMemberName(LambdaExpression selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        if (body is MemberExpression member)
        {
            return member.Member.Name;
        }

        throw new ArgumentException(
            $"The selector '{selector}' must be a simple member access expression (for example 'x => x.PropertyName').",
            nameof(selector));
    }
}
