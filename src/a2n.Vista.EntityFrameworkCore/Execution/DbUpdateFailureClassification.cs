using System.Data.Common;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// Distinguishes a database constraint violation from every other <c>DbUpdateException</c> without
/// reflecting over provider exception types (Decision Log D158).
/// </summary>
/// <remarks>
/// <para>
/// <c>DbUpdateConcurrencyException</c> is a subclass of <c>DbUpdateException</c> and is classified by
/// the caller before this helper runs. What remains is either a constraint breach (unique key, foreign
/// key, NOT NULL, CHECK) or some other store failure (missing relation, I/O, lock, corruption). Only
/// the former is HTTP 422 <c>write-constraint-violation</c>; the latter is HTTP 500
/// <c>write-persistence-failed</c>. Neither is a concurrency conflict.
/// </para>
/// <para>
/// Built-in rules are trim-safe. They read <see cref="DbException.SqlState"/> (SQLSTATE class
/// <c>23</c> is an integrity-constraint violation: PostgreSQL, and drivers that surface SQLSTATE) and
/// SQLite's stable message on <c>Microsoft.Data.Sqlite.SqliteException</c> ("UNIQUE/FOREIGN KEY/NOT
/// NULL/CHECK constraint failed"). They do not call <c>GetProperty</c>, so a trimmer cannot strip a
/// provider error-code property and silently turn a constraint into a 500.
/// </para>
/// <para>
/// A provider whose constraint signal is only a numeric code (SQL Server 2601/2627/547/515, MySQL
/// 1062/1451/1452, and so on) and that does not set <see cref="DbException.SqlState"/> is classified
/// by a host probe registered with <see cref="AddConstraintProbe"/>. The probe pattern-matches the
/// provider's own exception type, which keeps the property access visible to the trimmer in the
/// assembly that references that provider. Return <see langword="null"/> for an exception the probe
/// does not recognize.
/// </para>
/// </remarks>
public static class DbUpdateFailureClassification
{
    private static readonly object Gate = new();
    private static Func<Exception, bool?>[] _probes = [];

    /// <summary>
    /// Registers a trim-safe constraint probe. Probes run before the built-in rules, walking the
    /// exception and its inner exceptions.
    /// </summary>
    /// <param name="probe">
    /// Returns <see langword="true"/> for a constraint, <see langword="false"/> for a definite
    /// non-constraint, or <see langword="null"/> to defer. A <see langword="false"/> stops the walk.
    /// </param>
    public static void AddConstraintProbe(Func<Exception, bool?> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        lock (Gate)
        {
            var next = new Func<Exception, bool?>[_probes.Length + 1];
            Array.Copy(_probes, next, _probes.Length);
            next[^1] = probe;
            _probes = next;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/>, or one of its inner
    /// exceptions, is a provider integrity-constraint failure.
    /// </summary>
    /// <param name="exception">The provider failure raised by <c>SaveChanges</c>.</param>
    /// <returns><see langword="true"/> for a unique, foreign-key, NOT NULL, or CHECK breach.</returns>
    public static bool IsConstraintViolation(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var probes = _probes;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            foreach (var probe in probes)
            {
                var decision = probe(current);
                if (decision is bool decided)
                {
                    return decided;
                }
            }

            if (IsBuiltInConstraint(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBuiltInConstraint(Exception exception)
    {
        if (exception is DbException db && IsIntegritySqlState(db.SqlState))
        {
            return true;
        }

        // SQLite does not publish SQLSTATE. The provider's English text is stable and does not
        // require reading SqliteErrorCode, which a trimmer can remove.
        var fullName = exception.GetType().FullName;
        return fullName is not null
            && string.Equals(fullName, "Microsoft.Data.Sqlite.SqliteException", StringComparison.Ordinal)
            && exception.Message.Contains("constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIntegritySqlState(string? sqlState) =>
        sqlState is not null
        && sqlState.Length >= 2
        && sqlState.StartsWith("23", StringComparison.Ordinal);
}
