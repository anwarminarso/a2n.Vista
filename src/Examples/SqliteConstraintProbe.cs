using a2n.Vista.EntityFrameworkCore.Execution;
using Microsoft.Data.Sqlite;

namespace a2n.Vista.Examples;

/// <summary>
/// Host probe for SQLite constraint failures (Decision Log D158). Compiled into the Northwind sample
/// hosts, which are the assemblies that reference <c>Microsoft.Data.Sqlite</c>. The EF library does not.
/// </summary>
internal static class SqliteConstraintProbe
{
    /// <summary>
    /// Registers <see cref="Classify"/> with <see cref="DbUpdateFailureClassification.AddConstraintProbe"/>.
    /// Safe to call more than once; each registration agrees.
    /// </summary>
    public static void Register()
    {
        DbUpdateFailureClassification.AddConstraintProbe(Classify);
        DbUpdateFailureClassification.AddTransientLockProbe(ClassifyLock);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> is a
    /// <see cref="SqliteException"/> whose <see cref="SqliteException.SqliteErrorCode"/> is 19
    /// (<c>SQLITE_CONSTRAINT</c>), or, only after that type check, whose message contains
    /// <c>constraint failed</c>. Returns <see langword="false"/> for any other
    /// <see cref="SqliteException"/> and <see langword="null"/> for every other exception so core
    /// SQLSTATE rules still run.
    /// </summary>
    /// <param name="exception">One exception in the provider failure chain.</param>
    /// <returns>A constraint decision, or <see langword="null"/> when this probe does not apply.</returns>
    public static bool? Classify(Exception exception)
    {
        if (exception is not SqliteException sqlite)
        {
            return null;
        }

        if (sqlite.SqliteErrorCode == 19)
        {
            return true;
        }

        return sqlite.Message.Contains("constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <see langword="true"/> for SQLite error 5 (<c>SQLITE_BUSY</c>) and 6
    /// (<c>SQLITE_LOCKED</c>). Returns <see langword="null"/> for every other exception so a
    /// constraint failure is not classified as a lock (Decision Log D160).
    /// </summary>
    /// <param name="exception">One exception in the provider failure chain.</param>
    /// <returns>A lock decision, or <see langword="null"/> when this probe does not apply.</returns>
    public static bool? ClassifyLock(Exception exception)
    {
        if (exception is not SqliteException sqlite)
        {
            return null;
        }

        return sqlite.SqliteErrorCode is 5 or 6 ? true : null;
    }
}
