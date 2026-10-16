using Microsoft.Data.Sqlite;

namespace a2n.Vista.Examples;

/// <summary>
/// Creates <c>VistaMemos</c> on a working copy of the extracted Northwind template. The template file
/// is never opened.
/// </summary>
internal static class NorthwindMemoTable
{
    /// <summary>
    /// One statement, matching the EF model. Not split on semicolons.
    /// </summary>
    private const string CreateIfNotExists =
        """
        CREATE TABLE IF NOT EXISTS "VistaMemos" (
            "MemoID" INTEGER NOT NULL CONSTRAINT "PK_VistaMemos" PRIMARY KEY AUTOINCREMENT,
            "Subject" TEXT NOT NULL,
            "Body" TEXT NOT NULL,
            "RowVersion" TEXT NOT NULL
        )
        """;

    /// <summary>
    /// Runs <see cref="CreateIfNotExists"/> against <paramref name="workingDatabasePath"/> only.
    /// </summary>
    /// <param name="templatePath">The extracted template. Compared so this method cannot write it.</param>
    /// <param name="workingDatabasePath">The per-run copy returned by <c>NorthwindWorkingDatabase.CreateCopy</c>.</param>
    public static void EnsureOnCopy(string templatePath, string workingDatabasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDatabasePath);

        var template = Path.GetFullPath(templatePath);
        var copy = Path.GetFullPath(workingDatabasePath);
        if (string.Equals(template, copy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to create VistaMemos on the extracted Northwind template. Pass the per-run copy.");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = CreateIfNotExists;
        command.ExecuteNonQuery();
    }
}
