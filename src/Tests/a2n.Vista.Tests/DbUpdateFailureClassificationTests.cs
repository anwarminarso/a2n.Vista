// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Data.Common;
using System.Threading.Tasks;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Examples;
using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// D158: core classification is SQLSTATE class <c>23</c> plus host probes. A message that contains
/// <c>constraint failed</c> is not a constraint unless a probe that has already matched the provider
/// type says so. The Northwind probe is that check for <see cref="SqliteException"/>.
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
    public async Task A_Non_Sqlite_DbException_Whose_Message_Says_Constraint_Failed_Is_Not_A_Constraint()
    {
        SqliteConstraintProbe.Register();

        var exception = new SqlStateException(null, "UNIQUE constraint failed: Demo.Name");
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(exception)).IsFalse();
    }

    [Test]
    public async Task Northwind_Sqlite_Probe_Uses_Error_Code_19_Then_The_Message()
    {
        SqliteConstraintProbe.Register();

        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(
                new SqliteException("SQLITE_CONSTRAINT", 19)))
            .IsTrue();
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(
                new SqliteException("CHECK constraint failed: Demo.Name", 1)))
            .IsTrue();
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(
                new SqliteException("no such table: VistaMemos", 1)))
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
        public SqlStateException(string? sqlState, string? message = null)
            : base(message ?? string.Empty) =>
            SqlState = sqlState;

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
