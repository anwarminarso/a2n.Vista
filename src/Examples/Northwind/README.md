# Vista Northwind sample

A small ASP.NET Core host that serves the Northwind views, including the writable `vWritableMemo` view.

## Database

`src/Examples/DB/Northwind SQLite.zip` is the shipped Microsoft database (11,096,792 bytes). Building
this project extracts it to the gitignored `src/Examples/DB/northwind.db`. The extract is skipped when
that file is already newer than the zip. The template is not committed and is never opened for writing.

On startup the host hashes the extracted template, copies it into a per-run folder under the temp
directory, and runs one `CREATE TABLE IF NOT EXISTS VistaMemos` (`MemoID`, `Subject`, `Body`,
`RowVersion`) against the copy. Memo create/update/delete and `dotnet run -- selftest` use the copy.
The copy is deleted when the process stops. Set `NORTHWIND_WORKING_DIRECTORY` to choose a different
parent folder; each run still gets its own subdirectory.

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
