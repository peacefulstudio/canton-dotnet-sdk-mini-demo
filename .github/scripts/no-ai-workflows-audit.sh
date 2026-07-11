#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-.}"
GITHUB_DIR="$ROOT/.github"
WORKFLOWS_DIR="$GITHUB_DIR/workflows"

HITS=0

for banned_workflow in agent.yaml ci-autofix.yaml merge-conflict-autofix.yaml \
                       auto-resolve-conflicts.yaml automerge.yaml; do
  if [ -e "$WORKFLOWS_DIR/$banned_workflow" ]; then
    echo "no-AI-workflows violation: .github/workflows/$banned_workflow present on public repo"
    HITS=1
  fi
done

if [ -d "$WORKFLOWS_DIR" ] && grep -rnE \
     'uses:.*((anthropics?/(claude|codex)|github/copilot)|/(agent|ci-autofix|merge-conflict-autofix|auto-resolve-conflicts|automerge)\.ya?ml)' \
     "$WORKFLOWS_DIR"; then
  echo "no-AI-workflows violation: AI-tooling action or private-only reusable workflow referenced above"
  HITS=1
fi

if [ -d "$GITHUB_DIR" ] && grep -rnE 'peacefulstudio/[a-z0-9-]+-internal' "$GITHUB_DIR"; then
  echo "no-AI-workflows violation: -internal repository referenced above"
  HITS=1
fi

if [ "$HITS" -ne 0 ]; then
  exit 1
fi
echo "no-ai-workflows-audit: clean"
