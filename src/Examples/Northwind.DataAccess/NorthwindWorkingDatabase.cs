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
                "The Northwind template database was not found. It is the read-only file shipped at src/Examples/DB/northwind.db.",
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
}
