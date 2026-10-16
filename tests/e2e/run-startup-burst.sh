#!/usr/bin/env bash
# Informational HTTP smoke against cold net9 and net10 Northwind hosts.
# A pass or a fail does not prove the EF lazy relational-model race
# (dotnet/efcore#39214, Vista issue #13) is fixed or present. The race is rare.
# Deterministic evidence for the warmup is the unit tests on PR #12, not this script.
# See docs/qa/vista-startup-burst-2026-10-16.md.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
if [[ -z "${DOTNET_ROOT:-}" && -d "$HOME/.dotnet" ]]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi

DB_DIR="$ROOT/src/Examples/DB"
if [[ ! -f "$DB_DIR/northwind.db" ]]; then
  echo "Extracting Northwind SQLite database..."
  unzip -o "$DB_DIR/Northwind SQLite.zip" -d "$DB_DIR"
fi

NORTHWIND="$ROOT/src/Examples/Northwind/a2n.Vista.Examples.Northwind.csproj"
for tfm in net9.0 net10.0; do
  echo "Building Northwind sample for $tfm (no warmup; VistaHostFramework opt-in)..."
  dotnet build "$NORTHWIND" -c Debug -p:VistaHostFramework="$tfm" --verbosity minimal
done

echo "Building the startup-burst smoke..."
dotnet build "$ROOT/tests/e2e/a2n.Vista.E2E.StartupBurst.csproj" -c Debug --framework net8.0

echo "Running the startup-burst smoke..."
dotnet test "$ROOT/tests/e2e/a2n.Vista.E2E.StartupBurst.csproj" -c Debug --framework net8.0 --no-build "$@"

# The opt-in build rewrites this sample's assets file for the last TFM. Put the default
# net8.0 assets back so a later solution build is not left pointing at net10.0.
echo "Restoring the Northwind sample to its default net8.0 assets..."
dotnet build "$NORTHWIND" -c Debug --framework net8.0 --verbosity minimal
