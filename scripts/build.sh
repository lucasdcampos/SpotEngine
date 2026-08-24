#!/usr/bin/env bash
# Compile the whole Spot solution in Release. Used standalone and by setup.sh.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: 'dotnet' was not found on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/download and try again." >&2
    exit 1
fi

echo "Building SpotEngine.slnx (Release)..."
dotnet build "$REPO/SpotEngine.slnx" -c Release

echo "Build succeeded."
