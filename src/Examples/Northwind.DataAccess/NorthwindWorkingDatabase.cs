using System.Security.Cryptography;

namespace Northwind.DataAccess;

/// <summary>
/// Copies the read-only <c>northwind.db</c> template to a per-run working file.
/// </summary>
/// <remarks>
/// The file at <c>src/Examples/DB/northwind.db</c> is the gitignored extract of
/// <c>Northwind SQLite.zip</c>. <see cref="CreateCopy"/> reads it with
/// <see cref="File.Copy(string, string, bool)"/> and never opens it as a writable SQLite database.
/// Hosts create <c>VistaMemos</c> on the copy, not on this file. Set the
/// <c>NORTHWIND_WORKING_DIRECTORY</c> environment variable to choose the parent folder; each run still
/// gets its own subdirectory. <see cref="Cleanup"/> deletes that subdirectory and refuses to delete the
/// template.
/// </remarks>
public static class NorthwindWorkingDatabase
{
    /// <summary>Environment variable whose value is the parent folder for per-run working copies.</summary>
    public const string WorkingDirectoryVariable = "NORTHWIND_WORKING_DIRECTORY";

    /// <summary>A template path plus the working file created from it.</summary>
    /// <param name="TemplatePath">Absolute path of the read-only template.</param>
    /// <param name="DatabasePath">Absolute path of the per-run copy.</param>
    /// <param name="DirectoryPath">Directory that contains <paramref name="DatabasePath"/>.</param>
    public readonly record struct WorkingCopy(string TemplatePath, string DatabasePath, string DirectoryPath);

    /// <summary>
    /// Copies <paramref name="templatePath"/> into a new directory and returns that copy.
    /// </summary>
    /// <param name="templatePath">The shipped <c>northwind.db</c> template.</param>
    /// <returns>The working copy. Callers open this path, not the template.</returns>
    public static WorkingCopy CreateCopy(string templatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        var fullTemplate = Path.GetFullPath(templatePath);
        if (!File.Exists(fullTemplate))
        {
            throw new FileNotFoundException(
                "The Northwind template database was not found at " + fullTemplate + ". " +
                "ExtractNorthwindDatabase produces the gitignored northwind.db from Northwind SQLite.zip. " +
                "An empty database is not a fallback.",
                fullTemplate);
        }

        var parent = Environment.GetEnvironmentVariable(WorkingDirectoryVariable);
        var directory = string.IsNullOrWhiteSpace(parent)
            ? Path.Combine(Path.GetTempPath(), "a2n-vista-northwind", Guid.NewGuid().ToString("N"))
            : Path.Combine(Path.GetFullPath(parent), Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "northwind.db");
        File.Copy(fullTemplate, destination, overwrite: false);
        return new WorkingCopy(fullTemplate, destination, directory);
    }

    /// <summary>SHA-256 of a file, uppercase hex. Used to prove the template was not written.</summary>
    /// <param name="path">File to hash.</param>
    /// <returns>Uppercase hexadecimal SHA-256.</returns>
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>File name of the extracted template, as pinned in <c>Northwind.sha256</c>.</summary>
    public const string DatabasePinName = "northwind.db";

    /// <summary>
    /// Reads the SHA-256 pin for <paramref name="entryName"/> from a sha256sum file.
    /// </summary>
    /// <param name="checksumPath">Path to <c>Northwind.sha256</c>.</param>
    /// <param name="entryName"><see cref="DatabasePinName"/> or <c>Northwind SQLite.zip</c>.</param>
    /// <returns>The 64 hex digits, as written in the file.</returns>
    public static string ReadPinnedHash(string checksumPath, string entryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checksumPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        foreach (var raw in File.ReadLines(checksumPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            // sha256sum: 64 hex digits, two spaces, then the file name (which may contain spaces).
            if (line.Length < 66 || line[64] != ' ' || line[65] != ' ')
            {
                continue;
            }

            var name = line[66..].Trim();
            if (!string.Equals(name, entryName, StringComparison.Ordinal))
            {
                continue;
            }

            var hash = line[..64];
            if (!IsSha256Hex(hash))
            {
                throw new InvalidOperationException(
                    $"Northwind checksum file {checksumPath} has a pin for {entryName} that is not 64 hex digits.");
            }

            return hash;
        }

        throw new InvalidOperationException(
            $"Northwind checksum file {checksumPath} has no SHA-256 pin for {entryName}.");
    }

    /// <summary>
    /// Refuses to continue when the extracted template is missing or its SHA-256 is not the
    /// <see cref="DatabasePinName"/> pin. Hosts call this before <see cref="CreateCopy"/> and exit
    /// when it throws. It does not create a database.
    /// </summary>
    /// <param name="templatePath">The extracted <c>northwind.db</c>.</param>
    /// <param name="checksumPath"><c>Northwind.sha256</c> beside the zip.</param>
    /// <returns>The template hash, uppercase hex, which matches the pin.</returns>
    public static string RequirePinnedTemplate(string templatePath, string checksumPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksumPath);
        var fullTemplate = Path.GetFullPath(templatePath);
        if (!File.Exists(fullTemplate))
        {
            throw new FileNotFoundException(
                "Northwind template database was not found at " + fullTemplate + ". " +
                "Building a sample host runs ExtractNorthwindDatabase, which extracts src/Examples/DB/Northwind SQLite.zip " +
                "to the gitignored northwind.db and checks Northwind.sha256. " +
                "Refusing to start. An empty database is not a fallback.",
                fullTemplate);
        }

        var fullChecksum = Path.GetFullPath(checksumPath);
        if (!File.Exists(fullChecksum))
        {
            throw new FileNotFoundException(
                "Northwind checksum file was not found at " + fullChecksum + ". Refusing to start.",
                fullChecksum);
        }

        var expected = ReadPinnedHash(fullChecksum, DatabasePinName);
        var actual = Sha256(fullTemplate);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Northwind template hash mismatch for " + fullTemplate + ". Expected " + expected +
                ", actual " + actual + ". Refusing to start. The file is not the pinned extract of Northwind SQLite.zip. " +
                "An empty database is not a fallback.");
        }

        return actual;
    }

    private static bool IsSha256Hex(string hash)
    {
        if (hash.Length != 64)
        {
            return false;
        }

        foreach (var ch in hash)
        {
            var hex = (ch is >= '0' and <= '9') || (ch is >= 'a' and <= 'f') || (ch is >= 'A' and <= 'F');
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Deletes the per-run directory. Does nothing when that directory is the template's directory or
    /// the working file is the template itself.
    /// </summary>
    /// <param name="copy">The copy returned by <see cref="CreateCopy"/>.</param>
    public static void Cleanup(WorkingCopy copy)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(copy.TemplatePath) || string.IsNullOrWhiteSpace(copy.DatabasePath))
            {
                return;
            }

            var template = Path.GetFullPath(copy.TemplatePath);
            var database = Path.GetFullPath(copy.DatabasePath);
            if (string.Equals(template, database, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var templateDirectory = Path.GetDirectoryName(template);
            var directory = Path.GetFullPath(copy.DirectoryPath);
            if (templateDirectory is not null
                && string.Equals(Path.GetFullPath(templateDirectory), directory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Directory.Exists(directory))
            {
                // WAL keeps the log and shared-memory files beside the database. Delete those
                // names before the directory so a locked directory delete cannot leave them behind.
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    TryDelete(file + "-wal");
                    TryDelete(file + "-shm");
                }

                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
