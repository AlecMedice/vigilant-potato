#!/usr/bin/env bash
# Compiles Assets/Scripts against the API stubs, once per input backend.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
status=0

echo "== pass 1/2: legacy Input Manager =="
dotnet build "$here" -v q --nologo --no-incremental || status=1

echo "== pass 2/2: Input System package =="
dotnet build "$here" -v q --nologo --no-incremental -p:DefineConstants="ENABLE_INPUT_SYSTEM" || status=1

[ $status -eq 0 ] && echo "OK: both input backends compile." || echo "FAILED"
exit $status
