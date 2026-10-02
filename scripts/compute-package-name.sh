#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
#
# Prints the content-addressed name for the daml/daml.yaml package: a fixed
# base name plus a 12-hex-char SHA-256 prefix (letter-prefixed so it satisfies
# Daml's package-name grammar) over every file under daml/daml/, over
# daml.yaml's non-name fields, and over every *.dar under daml/dars/ (the
# data-dependencies). scripts/codegen.sh calls this to keep the committed
# name in sync with the Daml source and the vendored DARs on every
# regeneration, and CI fails when the two
# have drifted apart.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DAML_DIR="${REPO_ROOT}/daml"
BASE_NAME="canton-mini-demo"

hash="$(
  {
    find "${DAML_DIR}/daml" -type f | LC_ALL=C sort | while IFS= read -r f; do
      echo "FILE:${f#"${DAML_DIR}/"}"
      cat "${f}"
    done
    grep -v '^name:' "${DAML_DIR}/daml.yaml"
    if [[ -d "${DAML_DIR}/dars" ]]; then
      find "${DAML_DIR}/dars" -type f -name '*.dar' | LC_ALL=C sort | while IFS= read -r f; do
        echo "DAR:${f#"${DAML_DIR}/"}"
        cat "${f}"
      done
    fi
  } | shasum -a 256 | awk '{print $1}' | cut -c1-12
)"

echo "${BASE_NAME}-h${hash}"
