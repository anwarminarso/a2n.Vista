// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using a2n.Vista.Examples;
using Microsoft.Data.Sqlite;
using Northwind.DataAccess;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// The Northwind zip has one extraction path (<c>ExtractNorthwindDatabase</c>) and a per-run working
/// copy. A missing zip or a hash mismatch fails. The host refuses a missing or mismatched template.
/// </summary>
public sealed class NorthwindDatabaseTests
{
    private static readonly object EnvironmentGate = new();

    [Test]
    public async Task Pinned_Checksum_Matches_The_Tracked_Zip()
    {
        var root = RepoRoot();
        var zip = Path.Combine(root, "src", "Examples", "DB", "Northwind SQLite.zip");
        var checksum = Path.Combine(root, "src", "Examples", "DB", "Northwind.sha256");
        var expected = NorthwindWorkingDatabase.ReadPinnedHash(checksum, "Northwind SQLite.zip");
        await Assert.That(NorthwindWorkingDatabase.Sha256(zip)).IsEqualTo(expected.ToUpperInvariant());
        await Assert.That(new FileInfo(zip).Length).IsEqualTo(11_096_792);
    }

    [Test]
    public async Task Extraction_Fails_When_The_Zip_Is_Missing_And_Creates_No_Database()
    {
        using var workspace = new ExtractWorkspace();
        var checksum = workspace.WriteChecksum(new string('a', 64), new string('b', 64));
        var database = workspace.DatabasePath;
        var result = await workspace.ExtractAsync(Path.Combine(workspace.Root, "missing.zip"), database, checksum);

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).Contains("not found");
        await Assert.That(result.Output).Contains("empty database");
        await Assert.That(File.Exists(database)).IsFalse();
    }

    [Test]
    public async Task Extraction_Fails_On_A_Zip_Hash_Mismatch_And_Creates_No_Database()
    {
        using var workspace = new ExtractWorkspace();
        var zip = workspace.WriteFile("not-a-zip.bin", "this is not the northwind zip"u8.ToArray());
        var checksum = workspace.WriteChecksum(new string('0', 64), new string('1', 64));
        var database = workspace.DatabasePath;
        var result = await workspace.ExtractAsync(zip, database, checksum);

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).Contains("hash mismatch");
        await Assert.That(result.Output).Contains("empty database");
        await Assert.That(File.Exists(database)).IsFalse();
        await Assert.That(File.ReadAllText(TargetsPath()).Contains("EnsureCreated", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Extraction_Deletes_The_Database_When_Its_Hash_Does_Not_Match_The_Pin()
    {
        using var workspace = new ExtractWorkspace();
        var payload = "not-the-pinned-northwind"u8.ToArray();
        var zip = workspace.WriteZip(payload);
        var checksum = workspace.WriteChecksum(
            NorthwindWorkingDatabase.Sha256(zip).ToLowerInvariant(),
            new string('c', 64));
        var database = workspace.DatabasePath;
        var result = await workspace.ExtractAsync(zip, database, checksum);

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).Contains("hash mismatch");
        await Assert.That(result.Output).Contains("empty database");
        await Assert.That(File.Exists(database)).IsFalse();
    }

    [Test]
    public async Task Extraction_Writes_The_Pinned_Database_And_Nothing_Else()
    {
        using var workspace = new ExtractWorkspace();
        var payload = "northwind-template-bytes"u8.ToArray();
        var zip = workspace.WriteZip(payload);
        var database = workspace.DatabasePath;
        var checksum = workspace.WriteChecksum(
            NorthwindWorkingDatabase.Sha256(zip).ToLowerInvariant(),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant());
        var result = await workspace.ExtractAsync(zip, database, checksum);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(Convert.ToHexString(File.ReadAllBytes(database))).IsEqualTo(Convert.ToHexString(payload));
    }

    /// <summary>
    /// A database that is already the pinned file is not extracted again and is not rewritten.
    /// </summary>
    [Test]
    public async Task Extraction_Skips_When_The_Database_Already_Matches_The_Pin()
    {
        using var workspace = new ExtractWorkspace();
        var payload = "already-pinned-northwind"u8.ToArray();
        var zip = workspace.WriteZip(payload);
        var database = workspace.DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        await File.WriteAllBytesAsync(database, payload);
        var sentinel = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(database, sentinel);
        var checksum = workspace.WriteChecksum(
            NorthwindWorkingDatabase.Sha256(zip).ToLowerInvariant(),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant());

        var result = await workspace.ExtractAsync(zip, database, checksum);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output).Contains("Skipping extract.");
        await Assert.That(File.GetLastWriteTimeUtc(database)).IsEqualTo(sentinel);
        await Assert.That(Convert.ToHexString(File.ReadAllBytes(database))).IsEqualTo(Convert.ToHexString(payload));
        await Assert.That(Directory.GetDirectories(Path.GetDirectoryName(database)!, ".northwind-extract-*").Length).IsEqualTo(0);
    }

    /// <summary>
    /// A stale file that is not the pin is replaced by the verified extract. The temp directory does not remain.
    /// </summary>
    [Test]
    public async Task Extraction_Replaces_A_Mismatched_Database_With_The_Pinned_File()
    {
        using var workspace = new ExtractWorkspace();
        var payload = "replacement-northwind-bytes"u8.ToArray();
        var zip = workspace.WriteZip(payload);
        var database = workspace.DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        await File.WriteAllBytesAsync(database, "stale-wrong-database"u8.ToArray());
        var checksum = workspace.WriteChecksum(
            NorthwindWorkingDatabase.Sha256(zip).ToLowerInvariant(),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant());

        var result = await workspace.ExtractAsync(zip, database, checksum);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(database))).IsEqualTo(Convert.ToHexString(payload));
        await Assert.That(Directory.GetDirectories(Path.GetDirectoryName(database)!, ".northwind-extract-*").Length).IsEqualTo(0);
        await Assert.That(File.Exists(database + ".extract.lock")).IsFalse();
        await Assert.That(Directory.GetFiles(Path.GetDirectoryName(database)!, "northwind.db.stale-*").Length).IsEqualTo(0);
    }

    /// <summary>
    /// A database hash mismatch deletes the temp extract only. An existing <c>northwind.db</c> is left as it was.
    /// </summary>
    [Test]
    public async Task Extraction_Hash_Mismatch_Deletes_Only_The_Temp_File()
    {
        using var workspace = new ExtractWorkspace();
        var payload = "temp-file-that-fails-the-pin"u8.ToArray();
        var zip = workspace.WriteZip(payload);
        var database = workspace.DatabasePath;
        var existing = "preexisting-database-bytes"u8.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        await File.WriteAllBytesAsync(database, existing);
        var checksum = workspace.WriteChecksum(
            NorthwindWorkingDatabase.Sha256(zip).ToLowerInvariant(),
            new string('e', 64));

        var result = await workspace.ExtractAsync(zip, database, checksum);

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).Contains("hash mismatch");
        await Assert.That(result.Output).Contains("empty database");
        await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(database))).IsEqualTo(Convert.ToHexString(existing));
        await Assert.That(Directory.GetDirectories(Path.GetDirectoryName(database)!, ".northwind-extract-*").Length).IsEqualTo(0);
    }

    /// <summary>
    /// Six <c>dotnet msbuild -t:ExtractNorthwindDatabase</c> processes share one temp copy of the DB folder.
    /// Each exits 0, the final file matches the pin, and a second wave does not rewrite a correct file.
    /// </summary>
    [Test]
    [Timeout(180000)]
    public async Task Concurrent_Extraction_Places_The_Pinned_Database_Once(CancellationToken cancellationToken)
    {
        using var workspace = new ExtractWorkspace();
        var payload = new byte[64 * 1024];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 31);
        }

        var zip = workspace.WriteZip(payload);
        var database = workspace.DatabasePath;
        var checksum = workspace.WriteChecksum(
            NorthwindWorkingDatabase.Sha256(zip).ToLowerInvariant(),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant());
        var dbDir = Path.GetDirectoryName(database)!;

        var first = await workspace.ExtractManyAsync(zip, database, checksum, 6, cancellationToken);
        var firstFailures = string.Join(
            Environment.NewLine + "----" + Environment.NewLine,
            first.Where(result => result.ExitCode != 0).Select(result => result.Output));
        await Assert.That(firstFailures).IsEmpty();
        await Assert.That(File.Exists(database)).IsTrue();
        await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(database))).IsEqualTo(Convert.ToHexString(payload));
        await Assert.That(Directory.GetDirectories(dbDir, ".northwind-extract-*").Length).IsEqualTo(0);
        await Assert.That(File.Exists(database + ".extract.lock")).IsFalse();
        await Assert.That(Directory.GetFiles(dbDir, "northwind.db.stale-*").Length).IsEqualTo(0);

        var sentinel = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(database, sentinel);
        var second = await workspace.ExtractManyAsync(zip, database, checksum, 6, cancellationToken);
        var secondFailures = string.Join(
            Environment.NewLine + "----" + Environment.NewLine,
            second.Where(result => result.ExitCode != 0).Select(result => result.Output));
        await Assert.That(secondFailures).IsEmpty();
        foreach (var result in second)
        {
            await Assert.That(result.Output).Contains("Skipping extract.");
        }

        await Assert.That(File.GetLastWriteTimeUtc(database)).IsEqualTo(sentinel);
        await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(database))).IsEqualTo(Convert.ToHexString(payload));
    }

    [Test]
    public async Task Host_Refuses_A_Missing_Or_Mismatched_Template()
    {
        var root = RepoRoot();
        foreach (var relative in new[]
        {
            Path.Combine("src", "Examples", "Northwind", "Program.cs"),
            Path.Combine("src", "Examples", "a2n.Vista.Examples.AgGridNorthwind", "Program.cs"),
        })
        {
            var source = File.ReadAllText(Path.Combine(root, relative));
            await Assert.That(source).Contains("RequirePinnedTemplate");
            await Assert.That(source).Contains("Environment.ExitCode = 1");
        }

        using var workspace = new ExtractWorkspace();
        var checksum = workspace.WriteChecksum(new string('a', 64), new string('b', 64));
        var missing = Path.Combine(workspace.Root, "northwind.db");
        try
        {
            NorthwindWorkingDatabase.RequirePinnedTemplate(missing, checksum);
            throw new InvalidOperationException("A missing template must refuse to start.");
        }
        catch (FileNotFoundException ex)
        {
            await Assert.That(ex.Message).Contains("Refusing to start");
            await Assert.That(ex.Message).Contains("empty database");
        }

        var template = workspace.WriteFile("northwind.db", "wrong-template"u8.ToArray());
        try
        {
            NorthwindWorkingDatabase.RequirePinnedTemplate(template, checksum);
            throw new InvalidOperationException("A mismatched template must refuse to start.");
        }
        catch (InvalidOperationException ex)
        {
            await Assert.That(ex.Message).Contains("hash mismatch");
            await Assert.That(ex.Message).Contains("Refusing to start");
        }

        var pinned = NorthwindWorkingDatabase.Sha256(template).ToLowerInvariant();
        var matching = workspace.WriteChecksum(new string('d', 64), pinned);
        var actual = NorthwindWorkingDatabase.RequirePinnedTemplate(template, matching);
        await Assert.That(actual).IsEqualTo(pinned.ToUpperInvariant());
    }

    [Test]
    public async Task Working_Copies_Are_Unique_Guid_Directories_And_Cleanup_Deletes_Only_The_Copy()
    {
        var template = Path.Combine(Path.GetTempPath(), "a2n-vista-template-" + Guid.NewGuid().ToString("N") + ".db");
        CreateSqlite(template);
        var parent = Path.Combine(Path.GetTempPath(), "a2n-vista-wd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var distinct = false;
        var firstGuid = false;
        var secondGuid = false;
        var firstUnderParent = false;
        var secondUnderParent = false;
        var firstDeleted = false;
        var secondDeleted = false;
        var templateRemains = false;
        var templateSurvivesDirectCleanup = false;
        var copiesExisted = false;
        lock (EnvironmentGate)
        {
            var previous = Environment.GetEnvironmentVariable(NorthwindWorkingDatabase.WorkingDirectoryVariable);
            Environment.SetEnvironmentVariable(NorthwindWorkingDatabase.WorkingDirectoryVariable, parent);
            try
            {
                var first = NorthwindWorkingDatabase.CreateCopy(template);
                var second = NorthwindWorkingDatabase.CreateCopy(template);
                copiesExisted = File.Exists(first.DatabasePath) && File.Exists(second.DatabasePath);
                NorthwindWorkingDatabase.Cleanup(first);
                NorthwindWorkingDatabase.Cleanup(second);
                firstDeleted = !Directory.Exists(first.DirectoryPath);
                secondDeleted = !Directory.Exists(second.DirectoryPath);
                templateRemains = File.Exists(template);
                distinct = !string.Equals(first.DirectoryPath, second.DirectoryPath, StringComparison.OrdinalIgnoreCase);
                firstGuid = Guid.TryParseExact(Path.GetFileName(first.DirectoryPath), "N", out _);
                secondGuid = Guid.TryParseExact(Path.GetFileName(second.DirectoryPath), "N", out _);
                firstUnderParent = first.DirectoryPath.StartsWith(parent, StringComparison.Ordinal);
                secondUnderParent = second.DirectoryPath.StartsWith(parent, StringComparison.Ordinal);

                var templateAsCopy = new NorthwindWorkingDatabase.WorkingCopy(
                    template,
                    template,
                    Path.GetDirectoryName(template)!);
                NorthwindWorkingDatabase.Cleanup(templateAsCopy);
                templateSurvivesDirectCleanup = File.Exists(template);
            }
            finally
            {
                Environment.SetEnvironmentVariable(NorthwindWorkingDatabase.WorkingDirectoryVariable, previous);
                if (Directory.Exists(parent))
                {
                    Directory.Delete(parent, recursive: true);
                }

                if (File.Exists(template))
                {
                    File.Delete(template);
                }
            }
        }

        await Assert.That(copiesExisted).IsTrue();
        await Assert.That(distinct).IsTrue();
        await Assert.That(firstGuid).IsTrue();
        await Assert.That(secondGuid).IsTrue();
        await Assert.That(firstUnderParent).IsTrue();
        await Assert.That(secondUnderParent).IsTrue();
        await Assert.That(firstDeleted).IsTrue();
        await Assert.That(secondDeleted).IsTrue();
        await Assert.That(templateRemains).IsTrue();
        await Assert.That(templateSurvivesDirectCleanup).IsTrue();
    }

    [Test]
    public async Task VistaMemos_Is_Created_On_The_Copy_Only_As_One_Statement()
    {
        var root = RepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Examples", "NorthwindMemoTable.cs"));
        await Assert.That(source).Contains("CREATE TABLE IF NOT EXISTS \"VistaMemos\"");
        await Assert.That(source.Contains("Split(", StringComparison.Ordinal)).IsFalse();

        var directory = Path.Combine(Path.GetTempPath(), "a2n-vista-memo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var template = Path.Combine(directory, "template.db");
            var copy = Path.Combine(directory, "copy.db");
            CreateSqlite(template);
            File.Copy(template, copy);
            try
            {
                NorthwindMemoTable.EnsureOnCopy(template, template);
                throw new InvalidOperationException("VistaMemos must not be created on the template.");
            }
            catch (InvalidOperationException ex)
            {
                await Assert.That(ex.Message).Contains("template");
            }

            NorthwindMemoTable.EnsureOnCopy(template, copy);
            await Assert.That(TableExists(copy, "VistaMemos")).IsTrue();
            await Assert.That(TableExists(template, "VistaMemos")).IsFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreateSqlite(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE \"Customers\" (\"CustomerID\" TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    private static bool TableExists(string database, string table)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "a2n.Vista.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static string TargetsPath() =>
        Path.Combine(RepoRoot(), "src", "Examples", "Directory.Build.targets");

    private sealed class ExtractWorkspace : IDisposable
    {
        public ExtractWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "a2n-vista-extract-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "out", "northwind.db");
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public string WriteFile(string name, byte[] contents)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllBytes(path, contents);
            return path;
        }

        public string WriteZip(byte[] databaseBytes)
        {
            var path = Path.Combine(Root, "Northwind SQLite.zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            var entry = archive.CreateEntry("northwind.db");
            using var stream = entry.Open();
            stream.Write(databaseBytes);
            return path;
        }

        public string WriteChecksum(string zipHash, string databaseHash)
        {
            var path = Path.Combine(Root, "Northwind.sha256");
            File.WriteAllText(
                path,
                zipHash + "  Northwind SQLite.zip" + Environment.NewLine +
                databaseHash + "  northwind.db" + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }

        public Task<(int ExitCode, string Output)> ExtractAsync(string zip, string database, string checksum) =>
            ExtractAsync(zip, database, checksum, CancellationToken.None);

        public async Task<(int ExitCode, string Output)> ExtractAsync(
            string zip,
            string database,
            string checksum,
            CancellationToken cancellationToken)
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(database)!);
            var project = Path.Combine(Root, "extract-" + Guid.NewGuid().ToString("N") + ".proj");
            var targets = TargetsPath().Replace('\\', '/');
            await File.WriteAllTextAsync(
                project,
                "<Project>\n  <Import Project=\"" + targets + "\" />\n</Project>\n",
                cancellationToken);

            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("msbuild");
            start.ArgumentList.Add(project);
            start.ArgumentList.Add("-t:ExtractNorthwindDatabase");
            start.ArgumentList.Add("-nologo");
            start.ArgumentList.Add("-v:m");
            start.ArgumentList.Add("-p:ExtractNorthwindDatabase=true");
            start.ArgumentList.Add("-p:NorthwindSqliteZip=" + zip);
            start.ArgumentList.Add("-p:NorthwindSqliteDatabase=" + database);
            start.ArgumentList.Add("-p:NorthwindChecksumFile=" + checksum);

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Failed to start dotnet msbuild.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }

            var output = await stdout + await stderr;
            return (process.ExitCode, output);
        }

        public async Task<(int ExitCode, string Output)[]> ExtractManyAsync(
            string zip,
            string database,
            string checksum,
            int workers,
            CancellationToken cancellationToken)
        {
            var tasks = new Task<(int ExitCode, string Output)>[workers];
            for (var i = 0; i < workers; i++)
            {
                tasks[i] = ExtractAsync(zip, database, checksum, cancellationToken);
            }

            return await Task.WhenAll(tasks);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Root))
            {
                System.IO.Directory.Delete(Root, recursive: true);
            }
        }
    }
}
