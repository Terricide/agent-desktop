#!/usr/bin/env bash
# Gate: the C# CLI must satisfy the Windows E2E contract, not just its unit tests.
#
# The Windows E2E harness drives the shipped binary against real apps and checks every
# effect by independent observation, never by the command's own ok: true. The C# port
# builds a binary named agent-desktop.exe, so the harness can drive it unchanged.
#
# Usage: scripts/check-csharp-e2e.sh [harness args...]
#
# Exit 0 when the harness passes against the C# binary. Exit 1 when the binary cannot be
# built, the harness cannot be found, or the harness fails.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
harness="$repo_root/tests/e2e-windows/Run-E2E.ps1"
publish_dir="$repo_root/csharp/artifacts/csharp-e2e"

if [[ ! -f "$harness" ]]; then
    echo "missing harness: $harness" >&2
    exit 1
fi

powershell -NoProfile -ExecutionPolicy Bypass -File "$repo_root/tests/fixture-app-windows/build.ps1"

dotnet publish "$repo_root/csharp/src/AgentDesktop.Cli/AgentDesktop.Cli.csproj" \
    -c Release -r win-x64 -p:PublishSingleFile=true --self-contained \
    -o "$publish_dir"

exe="$publish_dir/agent-desktop.exe"
if [[ ! -f "$exe" ]]; then
    echo "publish produced no agent-desktop.exe" >&2
    exit 1
fi

powershell -NoProfile -ExecutionPolicy Bypass -File "$harness" \
    -AgentDesktopBinary "$exe"
