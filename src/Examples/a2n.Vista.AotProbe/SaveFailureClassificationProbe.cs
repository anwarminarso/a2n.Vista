using System;
using System.Data.Common;
using a2n.Vista.EntityFrameworkCore.Execution;
using Microsoft.Data.Sqlite;

namespace a2n.Vista.AotProbe;

/// <summary>
/// Exercises <see cref="DbUpdateFailureClassification"/> on the AOT-analyzed surface (D158). Built-in
/// SQLSTATE checks and a direct type probe must not call <c>GetProperty</c>. A green probe build is the
/// trim/AOT proof.
/// </summary>
internal static class SaveFailureClassificationProbe
{
    public static void Run()
    {
        DbUpdateFailureClassification.AddConstraintProbe(static exception =>
            exception is NumberedException numbered ? numbered.Number == 2627 : null);

        if (!DbUpdateFailureClassification.IsConstraintViolation(new StateException("23505")))
        {
            throw new InvalidOperationException("SQLSTATE 23505 must classify as a constraint.");
        }

        if (DbUpdateFailureClassification.IsConstraintViolation(new StateException("42P01")))
        {
            throw new InvalidOperationException("SQLSTATE 42P01 must not classify as a constraint.");
        }

        if (!DbUpdateFailureClassification.IsConstraintViolation(new NumberedException(2627)))
        {
            throw new InvalidOperationException("The numeric probe must classify 2627 as a constraint.");
        }

        if (!DbUpdateFailureClassification.IsConstraintViolation(new SqliteException("SQLITE_CONSTRAINT", 19)))
        {
            throw new InvalidOperationException("SqliteErrorCode 19 must classify as a constraint.");
        }

        if (!DbUpdateFailureClassification.IsConstraintViolation(
                new SqliteException("CHECK constraint failed: Demo.Name", 1)))
        {
            throw new InvalidOperationException("The SQLite message fallback must classify a constraint.");
        }

        if (DbUpdateFailureClassification.IsConstraintViolation(new SqliteException("no such table: VistaMemos", 1)))
        {
            throw new InvalidOperationException("A missing table must not classify as a constraint.");
        }

        Console.WriteLine("AOT probe: save-failure classification exercised.");
    }

    private sealed class StateException : DbException
    {
        public StateException(string sqlState) => SqlState = sqlState;

        public override string? SqlState { get; }
    }

    private sealed class NumberedException : Exception
    {
        public NumberedException(int number) => Number = number;

        public int Number { get; }
    }
}
