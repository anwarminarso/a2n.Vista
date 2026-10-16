using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Northwind.DataAccess;

/// <summary>
/// Startup helper for the purpose-made writable demo table.
/// </summary>
/// <remarks>
/// The shipped <c>northwind.db</c> is the Microsoft Northwind sample and has no <c>VistaMemos</c>
/// relation. <see cref="EnsureWritableMemoTable"/> adds only that table, using the EF model's own
/// SQLite DDL, and does not create, drop, or rewrite any Northwind table. The sample database is
/// SQLite; the existence probe reads <c>sqlite_master</c>.
/// </remarks>
public static class NorthwindDemoSchema
{
    private const string TableName = "VistaMemos";

    /// <summary>
    /// Creates <c>VistaMemos</c> when the configured database does not already contain it.
    /// </summary>
    /// <param name="context">The Northwind context connected to the sample database.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The EF model did not produce a <c>CREATE</c> statement for <c>VistaMemos</c>.
    /// </exception>
    public static void EnsureWritableMemoTable(NorthwindDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            connection.Open();
        }

        try
        {
            if (TableExists(connection))
            {
                return;
            }

            var created = false;
            foreach (var statement in SplitStatements(context.Database.GenerateCreateScript()))
            {
                if (!statement.Contains(TableName, StringComparison.Ordinal))
                {
                    continue;
                }

                using var command = connection.CreateCommand();
                command.CommandText = ToCreateIfNotExists(statement);
                command.ExecuteNonQuery();
                created = true;
            }

            if (!created)
            {
                throw new InvalidOperationException(
                    "The EF model did not produce a CREATE statement for the VistaMemos table.");
            }
        }
        finally
        {
            if (openedHere)
            {
                connection.Close();
            }
        }
    }

    private static bool TableExists(System.Data.Common.DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'VistaMemos' LIMIT 1;";
        return command.ExecuteScalar() is not null;
    }

    private static IEnumerable<string> SplitStatements(string script)
    {
        var parts = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Length > 0)
            {
                yield return part;
            }
        }
    }

    private static string ToCreateIfNotExists(string statement)
    {
        const string prefix = "CREATE TABLE ";
        if (statement.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return "CREATE TABLE IF NOT EXISTS " + statement[prefix.Length..];
        }

        return statement;
    }
}
