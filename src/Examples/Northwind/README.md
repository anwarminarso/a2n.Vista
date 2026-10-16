# Vista Northwind sample

A small ASP.NET Core host that serves the Northwind views, including the writable `vWritableMemo` view.

## Database

`src/Examples/DB/Northwind SQLite.zip` is the shipped Microsoft database (11,096,792 bytes).
`src/Examples/DB/Northwind.sha256` pins the SHA-256 of that zip and of the extracted database:

```text
78fe424288a3b07d54db7a9130b04fa29e14029dd8dfea2f842f2e4112b65bf9  Northwind SQLite.zip
2f4f5c68dfcd33ba27373eae48c7a4869800c68095ee0f9f0da494f83382a877  northwind.db
```

Building this project runs `ExtractNorthwindDatabase`, which unzips the file to the gitignored
`src/Examples/DB/northwind.db` and fails if the zip is missing or either hash does not match. It does
not create an empty database. The same target is what `tests/e2e/run.sh` and CI use. The template is
not committed and is never opened for writing.

On startup the host checks the template against the pin and exits if the file is missing or the hash
does not match. It then copies the template into a new directory named with a GUID under the temp
directory, and runs one `CREATE TABLE IF NOT EXISTS VistaMemos` (`MemoID`, `Subject`, `Body`,
`RowVersion`) against the copy. Memo create/update/delete and `dotnet run -- selftest` use the copy.
The copy is deleted when the process stops. Set `NORTHWIND_WORKING_DIRECTORY` to choose a different
parent folder; each run still gets its own GUID subdirectory.

## Run

From the repository root:

```sh
dotnet run --project src/Examples/Northwind
```

## Self-test

```sh
dotnet run --project src/Examples/Northwind -- selftest
```

The host write pass runs against the working copy. It checks that the copy's `sqlite_master` contains
`VistaMemos` and that the extracted template's SHA-256, taken before the copy, is unchanged after the
write round trip. The process exits `0` only when every self-test passes.
