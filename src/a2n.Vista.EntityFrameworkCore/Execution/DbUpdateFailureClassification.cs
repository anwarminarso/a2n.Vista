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
/// the former is HTTP 422 <c>write-constraint-violation</c>; a transient lock
/// (<see cref="IsTransientLock"/>) is HTTP 503 <c>write-busy</c>; the rest is HTTP 500
/// <c>write-persistence-failed</c>. None of those is a concurrency conflict.
/// </para>
/// <para>
/// The only built-in rule is trim-safe and reads <see cref="DbException.SqlState"/> (SQLSTATE class
/// <c>23</c> is an integrity-constraint violation: PostgreSQL, and drivers that surface SQLSTATE).
/// There is no provider package reference, no exception-type name match, and no message-text match.
/// A message that happens to contain <c>constraint failed</c> is not a constraint. Anything this
/// helper does not recognize is not a constraint, so the caller maps it to HTTP 500
/// <c>write-persistence-failed</c>.
/// </para>
/// <para>
/// A provider that does not set <see cref="DbException.SqlState"/> — SQLite's
/// <c>SqliteErrorCode</c> 19, SQL Server 2601/2627/547/515, MySQL 1062/1451/1452 — is classified by a
/// host probe registered with <see cref="AddConstraintProbe"/>. The probe pattern-matches the
/// provider's own exception type in the assembly that references that provider, so the property access
/// stays visible to the trimmer. The Northwind hosts register the SQLite probe there. Return
/// <see langword="null"/> for an exception the probe does not recognize.
/// </para>
/// </remarks>
public static class DbUpdateFailureClassification
{
    private static readonly object Gate = new();
    private static Func<Exception, bool?>[] _probes = [];
    private static Func<Exception, bool?>[] _transientLockProbes = [];

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
    /// Registers a trim-safe probe for a transient store lock (Decision Log D160). The probe lives in
    /// the assembly that references the provider. Return <see langword="true"/> for a lock the caller
    /// should map to HTTP 503, or <see langword="null"/> to defer. A <see langword="false"/> is ignored
    /// so a constraint probe cannot hide a lock.
    /// </summary>
    /// <param name="probe">The provider-specific lock classifier.</param>
    public static void AddTransientLockProbe(Func<Exception, bool?> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        lock (Gate)
        {
            var next = new Func<Exception, bool?>[_transientLockProbes.Length + 1];
            Array.Copy(_transientLockProbes, next, _transientLockProbes.Length);
            next[^1] = probe;
            _transientLockProbes = next;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/>, or one of its inner
    /// exceptions, is a transient store lock: <see cref="DbException.IsTransient"/> or a probe
    /// registered with <see cref="AddTransientLockProbe"/>.
    /// </summary>
    /// <param name="exception">The provider failure raised by <c>SaveChanges</c>.</param>
    /// <returns><see langword="true"/> when the write should be retried (HTTP 503).</returns>
    public static bool IsTransientLock(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var probes = _transientLockProbes;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { IsTransient: true })
            {
                return true;
            }

            foreach (var probe in probes)
            {
                if (probe(current) is true)
                {
                    return true;
                }
            }
        }

        return false;
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

        return false;
    }

    private static bool IsIntegritySqlState(string? sqlState) =>
        sqlState is not null
        && sqlState.Length >= 2
        && sqlState.StartsWith("23", StringComparison.Ordinal);
}
