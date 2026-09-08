#!/usr/bin/env bash
# Headless compile check for Assets/Scripts. No Unity installation required.
#
# Compiles the game's C# against the signature-only stubs in this directory.
# See UnityStubs.cs for exactly what this does and does not prove — in short, it
# is a fast spellchecker for the API surface, not a test suite.
#
#   bash tools/compilecheck/run.sh
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

dotnet build "$here" -v q --nologo --no-incremental

echo "OK: Assets/Scripts compiles against the Unity and Netcode stubs."
