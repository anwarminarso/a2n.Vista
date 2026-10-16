// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Data.Common;
using System.Threading.Tasks;
using a2n.Vista.EntityFrameworkCore.Execution;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// D158: constraint classification is trim-safe. Built-in rules read <see cref="DbException.SqlState"/>
/// and the SQLite exception's type name plus message. They do not reflect over a provider error-code
/// property. A host probe pattern-matches its own exception type.
/// </summary>
public sealed class DbUpdateFailureClassificationTests
{
    [Test]
    public async Task SqlState_Class_23_Is_A_Constraint_And_Other_Classes_Are_Not()
    {
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(new SqlStateException("23505")))
            .IsTrue();
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(new SqlStateException("23000")))
            .IsTrue();
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(new SqlStateException("42P01")))
            .IsFalse();
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(new SqlStateException(null)))
            .IsFalse();
    }

    [Test]
    public async Task A_Lookalike_Error_Code_Property_Is_Not_Read()
    {
        // The message says "constraint failed" and a property is named SqliteErrorCode, but the type is
        // not Microsoft.Data.Sqlite.SqliteException and SQLSTATE is unset. Reflection would have treated
        // this as a constraint; the trim-safe classifier must not.
        var lookalike = new LookalikeSqliteException(19, "UNIQUE constraint failed: Demo.Name");
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(lookalike)).IsFalse();
    }

    [Test]
    public async Task Host_Probe_Classifies_A_Numeric_Provider_Code_Without_Reflection()
    {
        DbUpdateFailureClassification.AddConstraintProbe(static exception =>
            exception is NumberedException numbered
                ? numbered.Number is 2601 or 2627 or 547 or 515 ? true : null
                : null);

        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(new NumberedException(2627)))
            .IsTrue();
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(new NumberedException(1205)))
            .IsFalse();
    }

    private sealed class SqlStateException : DbException
    {
        public SqlStateException(string? sqlState) => SqlState = sqlState;

        public override string? SqlState { get; }
    }

    private sealed class LookalikeSqliteException : Exception
    {
        public LookalikeSqliteException(int sqliteErrorCode, string message)
            : base(message) =>
            SqliteErrorCode = sqliteErrorCode;

        public int SqliteErrorCode { get; }
    }

    private sealed class NumberedException : Exception
    {
        public NumberedException(int number) => Number = number;

        public int Number { get; }
    }
}
