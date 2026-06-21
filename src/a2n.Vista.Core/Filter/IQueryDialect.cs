using System.Linq.Expressions;

namespace a2n.Vista.Filter;

/// <summary>
/// The kind of string match a text filter operator requests. The client only sends intent; the server
/// (via <see cref="IQueryDialect"/>) decides the translation and case-sensitivity (Decision Log D17, §8.2).
/// </summary>
public enum StringMatchKind
{
    /// <summary>Substring match (<c>LIKE '%value%'</c>).</summary>
    Contains,

    /// <summary>Prefix match (<c>LIKE 'value%'</c>).</summary>
    StartsWith,

    /// <summary>Suffix match (<c>LIKE '%value'</c>).</summary>
    EndsWith,
}

/// <summary>
/// The per-provider string-match and wildcard-escaping strategy (Decision Log D107, §10). The port
/// lives in Core (free of EF) so the <see cref="FilterCompiler"/> can delegate text operators without
/// knowing the provider; concrete dialects that call provider functions (for example
/// <c>EF.Functions.Like</c>/<c>ILike</c>) live in the EF / Npgsql packages.
/// </summary>
/// <remarks>
/// A dialect owns both the match expression and the wildcard escaping for its provider, so the
/// case-sensitivity decision and the anti-wildcard-injection escaping stay together (§10.4).
/// </remarks>
public interface IQueryDialect
{
    /// <summary>
    /// The EF Core provider name this dialect targets (for example
    /// <c>"Npgsql.EntityFrameworkCore.PostgreSQL"</c>), used by the startup provider guard to verify the
    /// registered dialect matches the active provider. A provider-agnostic default dialect returns a
    /// sentinel that matches any relational provider.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Builds a translatable boolean expression that tests whether <paramref name="member"/> matches the
    /// raw user-supplied <paramref name="value"/> under the given <paramref name="kind"/>. The dialect
    /// is responsible for escaping wildcards in <paramref name="value"/> before embedding it in a
    /// pattern (§10.4).
    /// </summary>
    /// <param name="member">The string member expression being tested.</param>
    /// <param name="value">The raw, user-supplied search text (un-escaped).</param>
    /// <param name="kind">Whether to match as Contains/StartsWith/EndsWith.</param>
    /// <returns>A boolean expression the query provider can translate.</returns>
    Expression BuildStringMatch(Expression member, string value, StringMatchKind kind);
}
