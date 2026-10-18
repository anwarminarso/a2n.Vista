using System;
using System.Data.Common;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Examples;
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

        if (DbUpdateFailureClassification.IsConstraintViolation(
                new StateException(null, "UNIQUE constraint failed: Demo.Name")))
        {
            throw new InvalidOperationException("Core must not treat a non-SQLite message as a constraint.");
        }

        SqliteConstraintProbe.Register();

        if (!DbUpdateFailureClassification.IsConstraintViolation(new SqliteException("SQLITE_CONSTRAINT", 19)))
        {
            throw new InvalidOperationException("The host probe must classify SqliteErrorCode 19 as a constraint.");
        }

        if (!DbUpdateFailureClassification.IsConstraintViolation(
                new SqliteException("CHECK constraint failed: Demo.Name", 1)))
        {
            throw new InvalidOperationException("The host probe message fallback must classify a SqliteException.");
        }

        if (DbUpdateFailureClassification.IsConstraintViolation(new SqliteException("no such table: VistaMemos", 1)))
        {
            throw new InvalidOperationException("A missing table must not classify as a constraint.");
        }

        Console.WriteLine("AOT probe: save-failure classification exercised.");
    }

    private sealed class StateException : DbException
    {
        public StateException(string? sqlState, string? message = null)
            : base(message ?? string.Empty) =>
            SqlState = sqlState;

        public override string? SqlState { get; }
    }

    private sealed class NumberedException : Exception
    {
        public NumberedException(int number) => Number = number;

        public int Number { get; }
    }
}
