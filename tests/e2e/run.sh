#!/usr/bin/env bash
# Install Chromium (and its OS libraries, when sudo works) and run the Vista Playwright suite.
# See docs/qa/e2e-playwright.md.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Same target the sample build and CI use. It checks src/Examples/DB/Northwind.sha256 and
# fails on a missing zip or a hash mismatch. It does not create an empty database.
echo "Extracting Northwind SQLite database (ExtractNorthwindDatabase)..."
dotnet msbuild "$ROOT/src/Examples/Northwind/a2n.Vista.Examples.Northwind.csproj" \
  -t:ExtractNorthwindDatabase \
  -p:ExtractNorthwindDatabase=true \
  -nologo \
  -v:m

echo "Building sample hosts and the e2e project (net8.0)..."
dotnet build "$ROOT/src/Examples/Northwind/a2n.Vista.Examples.Northwind.csproj" -c Debug --framework net8.0
dotnet build "$ROOT/src/Examples/a2n.Vista.Examples.AgGridNorthwind/a2n.Vista.Examples.AgGridNorthwind.csproj" -c Debug --framework net8.0
dotnet build "$ROOT/tests/e2e/a2n.Vista.E2E.csproj" -c Debug --framework net8.0

# Microsoft.Playwright 1.63 copies playwright.ps1 plus the Node driver under
# .playwright/. The driver is invoked directly so the script does not need PowerShell.
OUT="$ROOT/tests/e2e/bin/Debug/net8.0"
case "$(uname -m)" in
  x86_64) NODE_DIR="linux-x64" ;;
  aarch64|arm64) NODE_DIR="linux-arm64" ;;
  *)
    echo "Unsupported architecture for the Playwright driver: $(uname -m)" >&2
    exit 1
    ;;
esac
NODE="$OUT/.playwright/node/$NODE_DIR/node"
CLI="$OUT/.playwright/package/cli.js"
if [[ ! -x "$NODE" || ! -f "$CLI" ]]; then
  echo "Playwright driver was not copied to $OUT/.playwright" >&2
  exit 1
fi

echo "Installing Playwright Chromium..."
if command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
  sudo -E "$NODE" "$CLI" install-deps chromium
fi
"$NODE" "$CLI" install chromium

echo "Running e2e tests..."
dotnet test "$ROOT/tests/e2e/a2n.Vista.E2E.csproj" -c Debug --framework net8.0 --no-build "$@"
