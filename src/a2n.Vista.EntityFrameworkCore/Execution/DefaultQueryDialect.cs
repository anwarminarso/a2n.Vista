using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using a2n.Vista.Filter;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// The default <see cref="IQueryDialect"/> for relational providers that translate the SQL-standard
/// <c>LIKE</c> (SQL Server, SQLite, MySQL). Text operators emit
/// <see cref="DbFunctionsExtensions.Like(DbFunctions, string, string, string)"/> with an explicit
/// <c>ESCAPE</c> clause; the effective case-sensitivity follows the database/column collation
/// (Decision Log D17, D107, §8.2/§10). User wildcards <c>%</c>, <c>_</c>, and the escape character are
/// escaped (§10.4, anti wildcard-injection).
/// </summary>
/// <remarks>
/// This type folds the retired <c>ProviderAwareFilterCompiler</c> seam into a dialect. A
/// provider-specific dialect (for example <c>NpgsqlQueryDialect</c> emitting <c>ILIKE</c>, or a SQL
/// Server dialect additionally escaping <c>[</c>) subclasses this and overrides
/// <see cref="BuildTextMatch"/> / <see cref="EscapeLikePattern"/> without taking a provider dependency
/// here. The EF Core <b>InMemory</b> provider does not translate <c>EF.Functions.Like</c>; the
/// in-memory/test path uses the dialect-less <see cref="FilterCompiler"/> (ordinal) instead.
/// </remarks>
public class DefaultQueryDialect : IQueryDialect
{
    /// <summary>The sentinel provider name meaning "any relational provider via standard LIKE".</summary>
    public const string AnyRelationalProvider = "(default)";

    /// <summary>The escape character used with the <c>LIKE ... ESCAPE</c> clause.</summary>
    protected const string EscapeCharacter = "\\";

    private static readonly MethodInfo LikeWithEscapeMethod =
        typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like),
            new[] { typeof(DbFunctions), typeof(string), typeof(string), typeof(string) })!;

    /// <inheritdoc />
    public virtual string ProviderName => AnyRelationalProvider;

    /// <inheritdoc />
    public Expression BuildStringMatch(Expression member, string value, StringMatchKind kind)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(value);

        var escaped = EscapeLikePattern(value);
        var pattern = kind switch
        {
            StringMatchKind.Contains => $"%{escaped}%",
            StringMatchKind.StartsWith => $"{escaped}%",
            StringMatchKind.EndsWith => $"%{escaped}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown string match kind."),
        };

        return BuildTextMatch(member, pattern);
    }

    /// <summary>
    /// Builds the translatable match expression for a fully-formed (escaped, wildcard-wrapped)
    /// <paramref name="pattern"/>. The default emits <c>EF.Functions.Like(member, pattern, "\\")</c>; a
    /// provider subclass may override to emit a case-insensitive match such as
    /// <c>EF.Functions.ILike(member, pattern)</c>.
    /// </summary>
    /// <param name="member">The string member expression being tested.</param>
    /// <param name="pattern">The <c>LIKE</c> pattern, with literal wildcards already escaped.</param>
    /// <returns>A boolean expression EF Core can translate to SQL.</returns>
    protected virtual Expression BuildTextMatch(Expression member, string pattern)
    {
        var functions = Expression.Constant(EF.Functions, typeof(DbFunctions));
        var patternConstant = Expression.Constant(pattern, typeof(string));
        var escapeConstant = Expression.Constant(EscapeCharacter, typeof(string));
        return Expression.Call(LikeWithEscapeMethod, functions, member, patternConstant, escapeConstant);
    }

    /// <summary>
    /// Escapes the SQL-standard <c>LIKE</c> wildcards (<c>%</c>, <c>_</c>) and the escape character in
    /// <paramref name="value"/> so user text matches literally. Overridable so a SQL Server dialect can
    /// additionally escape <c>[</c> (a SQL Server wildcard PostgreSQL does not recognize).
    /// </summary>
    /// <param name="value">The raw, user-supplied search text.</param>
    /// <returns>The escaped text, safe to embed in a <c>LIKE</c> pattern using <see cref="EscapeCharacter"/>.</returns>
    protected virtual string EscapeLikePattern(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or '%' or '_')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
