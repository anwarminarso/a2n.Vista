using System.Globalization;
using System.Reflection;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// Distinguishes a database constraint violation from every other <c>DbUpdateException</c> without
/// taking a compile-time dependency on a database provider (Decision Log D154).
/// </summary>
/// <remarks>
/// <para>
/// <c>DbUpdateConcurrencyException</c> is a subclass of <c>DbUpdateException</c> and is classified by
/// the caller before this helper runs. What remains is either a constraint breach (unique key, foreign
/// key, NOT NULL, CHECK) or some other store failure (missing relation, I/O, lock, corruption). Only
/// the former is a client-visible constraint violation (HTTP 422); the latter is an unexpected
/// persistence failure (HTTP 500). Neither is a concurrency conflict.
/// </para>
/// <para>
/// Provider exceptions are recognized by type name and a stable public error code, read reflectively,
/// so this assembly stays free of Sqlite, SqlClient, Npgsql, and MySQL references. An unrecognized
/// provider is not guessed into a constraint: the caller then reports a persistence failure.
/// </para>
/// </remarks>
internal static class DbUpdateFailureClassifier
{
    /// <summary>SQLite primary result code <c>SQLITE_CONSTRAINT</c> (extended constraint codes share it).</summary>
    private const int SqliteConstraint = 19;

    /// <summary>
    /// SQL Server numbers that mean an integrity-constraint breach: unique index (2601),
    /// primary-key/unique constraint (2627), foreign-key or CHECK (547), and NOT NULL (515).
    /// </summary>
    private static readonly HashSet<int> SqlServerConstraintNumbers = [2601, 2627, 547, 515];

    /// <summary>
    /// MySQL / MariaDB server errors for duplicate keys, foreign keys, NOT NULL, and CHECK.
    /// </summary>
    private static readonly HashSet<int> MySqlConstraintNumbers = [1048, 1062, 1364, 1451, 1452, 1586, 3819, 4025];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/>, or one of its inner
    /// exceptions, is a provider integrity-constraint failure.
    /// </summary>
    /// <param name="exception">The provider failure raised by <c>SaveChanges</c>.</param>
    /// <returns><see langword="true"/> for a unique, foreign-key, NOT NULL, or CHECK breach.</returns>
    public static bool IsConstraintViolation(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (IsProviderConstraint(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProviderConstraint(Exception exception)
    {
        if (IsType(exception, "Microsoft.Data.Sqlite.SqliteException"))
        {
            var code = ReadInt32(exception, "SqliteErrorCode");
            if (code is int sqlite)
            {
                return sqlite == SqliteConstraint;
            }

            // SQLite's own English text ("UNIQUE constraint failed", "NOT NULL constraint failed",
            // "FOREIGN KEY constraint failed", "CHECK constraint failed"). "no such table" does not match.
            return exception.Message.Contains("constraint failed", StringComparison.OrdinalIgnoreCase);
        }

        if (IsType(exception, "Microsoft.Data.SqlClient.SqlException")
            || IsType(exception, "System.Data.SqlClient.SqlException"))
        {
            var number = ReadInt32(exception, "Number");
            return number is int sql && SqlServerConstraintNumbers.Contains(sql);
        }

        if (IsType(exception, "Npgsql.PostgresException"))
        {
            // SQLSTATE class 23 is Integrity Constraint Violation (23505 unique, 23503 foreign key,
            // 23502 not null, 23514 check, 23P01 exclusion).
            var state = ReadString(exception, "SqlState");
            return state is not null
                && state.Length >= 2
                && state.StartsWith("23", StringComparison.Ordinal);
        }

        if (IsType(exception, "MySqlConnector.MySqlException")
            || IsType(exception, "MySql.Data.MySqlClient.MySqlException"))
        {
            var number = ReadInt32(exception, "Number");
            return number is int mysql && MySqlConstraintNumbers.Contains(mysql);
        }

        return false;
    }

    private static bool IsType(Exception exception, string fullName)
    {
        for (var type = exception.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            if (string.Equals(type.FullName, fullName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int? ReadInt32(Exception exception, string propertyName)
    {
        try
        {
            var value = exception.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(exception);
            return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            // A provider shape we do not understand is not a constraint. The caller reports 500.
            return null;
        }
    }

    private static string? ReadString(Exception exception, string propertyName)
    {
        try
        {
            return exception.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(exception) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
