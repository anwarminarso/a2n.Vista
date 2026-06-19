using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using a2n.Vista.Contracts;
using a2n.Vista.Filter;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// An EF Core-aware <see cref="FilterCompiler"/> whose text operators
/// (<see cref="FilterOperator.Contains"/>, <see cref="FilterOperator.StartsWith"/>,
/// <see cref="FilterOperator.EndsWith"/>) translate to SQL rather than to in-memory string
/// comparisons (Task 9.3, Requirement R9.3, Decision Log D17, §8.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a subclass.</b> The base <see cref="FilterCompiler"/> emits
/// <c>member.Contains(value, StringComparison.OrdinalIgnoreCase)</c>. That overload is correct for
/// LINQ-to-objects (the in-memory/test path) but <b>does not translate</b> in EF Core — the relational
/// providers only translate the single-argument <c>string.Contains</c>/<c>StartsWith</c>/<c>EndsWith</c>
/// and the <c>EF.Functions.Like</c> family. This subclass therefore emits
/// <see cref="DbFunctionsExtensions.Like(DbFunctions, string, string, string)"/> calls, which the
/// relational providers translate to SQL <c>LIKE</c>.
/// </para>
/// <para>
/// <b>Case-sensitivity is server-decided (D17, §8.2).</b> The client only sends intent
/// (<c>Contains</c>/<c>StartsWith</c>/<c>EndsWith</c>); it never sends a case-sensitivity flag. By
/// emitting <c>LIKE</c>, the effective case-sensitivity is decided by the database/column collation:
/// SQL Server and MySQL are case-insensitive under their common default collations, SQLite's
/// <c>LIKE</c> is ASCII case-insensitive, and PostgreSQL is case-sensitive (where a provider-specific
/// adapter can opt into <c>ILIKE</c> — see the seam below). This keeps the decision on the server, per
/// the spec.
/// </para>
/// <para>
/// <b>Npgsql/ILIKE seam (no Npgsql dependency here).</b> <c>EF.Functions.ILike</c> is defined by the
/// Npgsql provider package, which this core EF package intentionally does <b>not</b> reference. The
/// case-insensitive match is funnelled through the single overridable
/// <see cref="BuildTextMatch(Expression, string, FilterLeaf)"/> seam, so a provider-specific adapter
/// (for example an <c>a2n.Vista.EntityFrameworkCore.Npgsql</c> package) can subclass this type and
/// override that one method to emit <c>EF.Functions.ILike</c> without this package taking a dependency
/// on Npgsql. Likewise, <see cref="EscapeLikePattern(string)"/> is overridable so a SQL Server adapter
/// can additionally escape the <c>[</c> wildcard (see below).
/// </para>
/// <para>
/// <b>Wildcard escaping.</b> User-supplied text is escaped for the SQL-standard <c>LIKE</c> wildcards
/// <c>%</c> and <c>_</c> (and the escape character itself), and the match is emitted with an explicit
/// <c>ESCAPE</c> clause via the three-argument <see cref="DbFunctionsExtensions.Like(DbFunctions, string, string, string)"/>
/// overload. SQL Server additionally treats <c>[</c> as a wildcard (character-set start), but escaping
/// <c>[</c> universally would break PostgreSQL, which rejects an escape character that is not followed
/// by <c>%</c>, <c>_</c>, or the escape character. <c>[</c> escaping is therefore left to a SQL
/// Server-specific override of <see cref="EscapeLikePattern(string)"/>.
/// </para>
/// <para>
/// <b>Null handling.</b> No <c>member != null</c> guard is emitted (unlike the in-memory base): in SQL,
/// <c>LIKE</c> against a <see langword="null"/> column yields <c>NULL</c>, so the row is correctly
/// excluded. Omitting the guard keeps the generated SQL clean and fully translatable.
/// </para>
/// <para>
/// <b>Provider caveat for tests (Task 12).</b> The EF Core <b>InMemory</b> provider does <b>not</b>
/// translate <c>EF.Functions.Like</c> (it throws at query time). Tests that exercise text operators
/// against InMemory must use the base <see cref="FilterCompiler"/> (ordinal, in-memory) instead;
/// tests that want to validate the <c>LIKE</c> translation should use the <b>SQLite</b> provider, which
/// translates it. The constructors of <see cref="EfViewExecutor"/> that accept a
/// <see cref="FilterCompiler"/> exist precisely so a test can inject the base compiler for InMemory.
/// </para>
/// </remarks>
public class ProviderAwareFilterCompiler : FilterCompiler
{
    /// <summary>
    /// The escape character used with the <c>LIKE ... ESCAPE</c> clause. A backslash is a safe choice
    /// across the relational providers Vista targets.
    /// </summary>
    protected const string EscapeCharacter = "\\";

    private static readonly MethodInfo LikeWithEscapeMethod =
        typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like),
            new[] { typeof(DbFunctions), typeof(string), typeof(string), typeof(string) })!;

    /// <summary>
    /// Emits a SQL <c>LIKE '%value%'</c> match (substring), with wildcards in <c>value</c> escaped.
    /// </summary>
    protected override Expression BuildContains(Expression member, Expression value, FilterLeaf leaf)
    {
        var escaped = EscapeLikePattern(ReadLiteral(value, leaf));
        return BuildTextMatch(member, $"%{escaped}%", leaf);
    }

    /// <summary>
    /// Emits a SQL <c>LIKE 'value%'</c> match (prefix), with wildcards in <c>value</c> escaped.
    /// </summary>
    protected override Expression BuildStartsWith(Expression member, Expression value, FilterLeaf leaf)
    {
        var escaped = EscapeLikePattern(ReadLiteral(value, leaf));
        return BuildTextMatch(member, $"{escaped}%", leaf);
    }

    /// <summary>
    /// Emits a SQL <c>LIKE '%value'</c> match (suffix), with wildcards in <c>value</c> escaped.
    /// </summary>
    protected override Expression BuildEndsWith(Expression member, Expression value, FilterLeaf leaf)
    {
        var escaped = EscapeLikePattern(ReadLiteral(value, leaf));
        return BuildTextMatch(member, $"%{escaped}", leaf);
    }

    /// <summary>
    /// Builds the actual translatable match expression for a fully-formed (already escaped, already
    /// wildcard-wrapped) <paramref name="pattern"/>. <b>This is the provider seam (D17, §8.2).</b>
    /// </summary>
    /// <param name="member">The string member expression being tested.</param>
    /// <param name="pattern">The <c>LIKE</c> pattern, with literal wildcards already escaped.</param>
    /// <param name="leaf">The originating leaf, for diagnostics.</param>
    /// <returns>A boolean expression that EF Core can translate to SQL.</returns>
    /// <remarks>
    /// The default emits <c>EF.Functions.Like(member, pattern, "\\")</c>, whose case-sensitivity is
    /// decided by the database/column collation. A provider-specific subclass (for example for Npgsql)
    /// may override this to emit a case-insensitive match such as <c>EF.Functions.ILike(member, pattern)</c>
    /// without this package depending on the Npgsql provider.
    /// </remarks>
    protected virtual Expression BuildTextMatch(Expression member, string pattern, FilterLeaf leaf)
    {
        var functions = Expression.Constant(EF.Functions, typeof(DbFunctions));
        var patternConstant = Expression.Constant(pattern, typeof(string));
        var escapeConstant = Expression.Constant(EscapeCharacter, typeof(string));
        return Expression.Call(LikeWithEscapeMethod, functions, member, patternConstant, escapeConstant);
    }

    /// <summary>
    /// Escapes the SQL-standard <c>LIKE</c> wildcards (<c>%</c> and <c>_</c>) and the escape character
    /// itself in <paramref name="value"/>, so user text matches literally. Overridable so a SQL
    /// Server-specific adapter can additionally escape <c>[</c> (a SQL Server wildcard that PostgreSQL
    /// does not recognize).
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

    /// <summary>
    /// Reads the literal string the text operator searches for, from the value expression the base
    /// compiler supplies (a <see cref="ConstantExpression"/> over a string) or, failing that, from the
    /// leaf itself. The base validates the value is a string before calling the text builders, so this
    /// only guards against an unexpected expression shape.
    /// </summary>
    private static string ReadLiteral(Expression value, FilterLeaf leaf)
    {
        if (value is ConstantExpression { Value: string fromExpression })
        {
            return fromExpression;
        }

        if (leaf.Value is string fromLeaf)
        {
            return fromLeaf;
        }

        throw new FilterValidationException(
            FilterErrorCode.InvalidValue,
            $"Operator '{leaf.Op}' on field '{leaf.Field}' requires a string value.",
            leaf.Field,
            leaf.Op);
    }
}
