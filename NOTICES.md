# Third-Party Notices

`a2n.Vista` is licensed under the LGPL-3.0-or-later (see [LICENSE](./LICENSE) and
[COPYING](./COPYING)). It uses and, in the case of the sample data, redistributes
the third-party components listed below. Each remains under its own license; the
copyright and license notices below are reproduced for attribution.

## Runtime / build dependencies

| Component | License | Notes |
|-----------|---------|-------|
| Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Sqlite | MIT | © Microsoft. EF Core integration and the SQLite provider. |
| SQLitePCLRaw (transitive via the SQLite provider) | Apache-2.0 | © Eric Sink / SQLitePCL.raw contributors. |
| SQLite engine (bundled via `SQLitePCLRaw.bundle_e_sqlite3`) | Public Domain | The SQLite source is in the public domain. |
| ASP.NET Core (`Microsoft.AspNetCore.*`, shared framework) | MIT | © Microsoft .NET Foundation and contributors. |
| Microsoft.CodeAnalysis.CSharp / .Analyzers (Roslyn) | MIT | © Microsoft .NET Foundation. Used by the source generator. |
| Newtonsoft.Json | MIT | © James Newton-King. Optional, in `a2n.Vista.Newtonsoft`. |
| TUnit | MIT | © TUnit contributors. Test-only dependency. |

License texts:

- MIT: https://opensource.org/license/mit
- Apache-2.0: https://www.apache.org/licenses/LICENSE-2.0
- SQLite (public domain): https://www.sqlite.org/copyright.html

## Sample data

### Northwind sample database

The example under `src/Examples/` bundles a Northwind sample database
(`src/Examples/DB/Northwind SQLite.zip`). Northwind is a long-standing sample
dataset originally published by Microsoft and widely redistributed for
demonstration and educational use. It is included here solely to make the example
runnable and is not part of the distributable library packages.

---

If you believe an attribution is missing or incorrect, please open an issue.
