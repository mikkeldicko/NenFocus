#!/usr/bin/env bash
# Stop hook: when C# code changed since the last green run, run the architecture tests
# (NenFocus.Tests/ArchitectureTests). A failure blocks the stop, so Claude fixes the drift
# before handing back. Guard Hierarchy rank 2, made automatic (TESTING_GUIDE.md → Architecture Tests).
set -u

input=$(cat)
cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0

state_file=".claude/hooks/.arch-tests-green"

# Stryker swaps mutated assemblies into NenFocus.Tests/bin; a test run now would build into it and
# report failures that aren't there (Gap 112). Skip without marking green — the next stop runs them.
if tasklist //NH //FI "IMAGENAME eq dotnet-stryker.exe" 2>/dev/null | grep -qi "dotnet-stryker"; then
  exit 0
fi

# Fingerprint every tracked and untracked .cs file; unchanged since the last green run → nothing to do.
fingerprint=$(git ls-files -co --exclude-standard -- '*.cs' | git hash-object --stdin-paths | git hash-object --stdin)
if [ -f "$state_file" ] && [ "$(cat "$state_file")" = "$fingerprint" ]; then
  exit 0
fi

output=$(dotnet test NenFocus.Tests --filter "FullyQualifiedName~ArchitectureTests" --nologo 2>&1)
if [ $? -eq 0 ]; then
  echo "$fingerprint" > "$state_file"
  exit 0
fi

failures=$(printf '%s\n' "$output" | tr -d '\r' | grep -E -A 12 "error CS|^\s+Failed " | head -80)
reason="Architecture tests failed after this turn's C# changes. Fix the drift (or, if the rule itself should change, say so to Mikkel) before finishing:

$failures"

# Already continued once because of this hook: tell the user instead of blocking again (no loop).
if [ "$(printf '%s' "$input" | jq -r '.stop_hook_active // false')" = "true" ]; then
  jq -n --arg msg "$reason" '{systemMessage: $msg}'
  exit 0
fi

jq -n --arg reason "$reason" '{decision: "block", reason: $reason}'
