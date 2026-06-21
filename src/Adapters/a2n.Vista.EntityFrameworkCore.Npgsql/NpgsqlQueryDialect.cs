using System.Linq.Expressions;
using System.Reflection;
using a2n.Vista.EntityFrameworkCore.Execution;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.EntityFrameworkCore.Npgsql;

/// <summary>
/// PostgreSQL (Npgsql) query dialect (Decision Log D107, §10.3). PostgreSQL's <c>LIKE</c> is
/// case-sensitive, so for case-insensitive search parity with the other providers this dialect emits
/// <c>EF.Functions.ILike(member, pattern)</c>. Wildcard escaping is inherited from
/// <see cref="DefaultQueryDialect"/> (PostgreSQL's default <c>LIKE</c>/<c>ILIKE</c> escape character is
/// the backslash, matching the inherited escaping).
/// </summary>
public sealed class NpgsqlQueryDialect : DefaultQueryDialect
{
    /// <summary>The EF Core provider name this dialect targets.</summary>
    public const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private static readonly MethodInfo ILikeMethod =
        typeof(NpgsqlDbFunctionsExtensions).GetMethod(
            nameof(NpgsqlDbFunctionsExtensions.ILike),
            new[] { typeof(DbFunctions), typeof(string), typeof(string) })!;

    /// <inheritdoc />
    public override string ProviderName => NpgsqlProviderName;

    /// <inheritdoc />
    protected override Expression BuildTextMatch(Expression member, string pattern)
    {
        var functions = Expression.Constant(EF.Functions, typeof(DbFunctions));
        var patternConstant = Expression.Constant(pattern, typeof(string));
        return Expression.Call(ILikeMethod, functions, member, patternConstant);
    }
}
