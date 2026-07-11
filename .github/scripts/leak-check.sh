#!/usr/bin/env bash
set -euo pipefail

# Public subset of the Peaceful Studio leak check. The full pattern set is
# private and runs on the internal side before any promotion reaches this
# repo; this gate re-checks the structural patterns that are safe to state
# publicly.

ROOT="${1:-.}"

BINARY_FIXTURE_EXCLUDES=(':!*.dar' ':!*.binpb' ':!*.png')
OWN_PATTERN_DEFINITION_EXCLUDES=(':!.github/scripts/')
PATHSPECS=('.' "${BINARY_FIXTURE_EXCLUDES[@]}" "${OWN_PATTERN_DEFINITION_EXCLUDES[@]}")

HITS=0

scan() {
  local label="$1" pattern="$2"
  local matches found=0
  matches=$(git -C "$ROOT" grep -nIiP -e "$pattern" -- "${PATHSPECS[@]}") || found=$?
  if [ "$found" -gt 1 ]; then
    echo "leak-check: git grep failed (status $found) for [$label]" >&2
    exit 2
  fi
  if [ -n "$matches" ]; then
    printf 'LEAK [%s]:\n%s\n\n' "$label" "$matches"
    HITS=1
  fi
}

scan "references to private -internal repos" \
  'peacefulstudio/[a-z0-9-]+-internal'

# The reference (daml-codegen-csharp) leak-check guards an "unreleased sibling
# namespace" here (Canton.Ledger.Grpc.Client, Daml.Runtime.Grpc). That guard
# does NOT apply to this repo: those SDK packages are the demo's published,
# public dependencies (they restore from nuget.org and are sourced from the
# public canton-ledger-api-csharp repo), so they appear by design in the
# csproj, a using directive, and the README. Guarding them would make this
# gate permanently red on the demo's own public API surface, so the scan is
# intentionally omitted.

scan "agent instruction files" \
  'CLAUDE\.md|AGENTS\.md|\.github/copilot-instructions'

scan "internal docs paths" \
  'docs/internal/'

scan "non-public peaceful.studio addresses" \
  '\b(?!(?:security|conduct)@)[a-z0-9._-]+@peaceful\.studio'

scan "ASCII-mangled legal entity name (OU instead of OÜ)" \
  'Peaceful Studio OU\b'

scan "credential fragments" \
  'sk-ant-[a-z0-9]|AKIA[A-Z0-9]{16}|-----BEGIN[A-Z ]*PRIVATE KEY'

scan "AI signatures in files" \
  'co-authored-by: claude|generated with.*claude|@ai-generated|claude\.ai/code|noreply@anthropic'

if [ "$HITS" -ne 0 ]; then
  exit 1
fi
echo "leak-check: clean"
