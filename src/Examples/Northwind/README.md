# Vista Northwind sample

A small ASP.NET Core host that serves the Northwind views, including the writable `vWritableMemo` view.

## Database

`src/Examples/DB/northwind.db` is a read-only template committed in the repository. It is the Microsoft
Northwind sample plus `VistaMemos` (`MemoID`, `Subject`, `Body`, `RowVersion`). The host does not run
DDL and does not open that file for writing.

On startup the host copies the template into a per-run folder under the temp directory and points SQLite
at the copy. Memo create/update/delete and `dotnet run -- selftest` use the copy. The copy is deleted
when the process stops. Set `NORTHWIND_WORKING_DIRECTORY` to choose a different parent folder; each run
still gets its own subdirectory.

If the template is missing, restore `src/Examples/DB/northwind.db` from git. Do not replace it with a
fresh extract of `Northwind SQLite.zip` — that archive has no `VistaMemos` table.

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
`VistaMemos` and that the template's SHA-256 is unchanged after the write round trip. The process exits
`0` only when every self-test passes.
