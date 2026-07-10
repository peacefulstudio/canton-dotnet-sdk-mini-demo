#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
#
# Builds the Daml package and regenerates the committed C# bindings under
# src/MiniDemo.Contracts/Generated via dpm build + dpm codegen-cs.
# Requires:
#   - dpm  >= 1.0.12  (oci:// component URIs; https://get.digitalasset.com/install/install.sh, then `dpm install 3.4.11`)
#   - java (JDK 17+, the codegen component's bundled JVM helper decodes the DAR)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DAML_DIR="${REPO_ROOT}/daml"
CODEGEN_DIR="${REPO_ROOT}/codegen"
OUT_DIR="${REPO_ROOT}/src/MiniDemo.Contracts/Generated"

command -v dpm  >/dev/null 2>&1 || { echo "error: 'dpm' not found on PATH — install from https://get.digitalasset.com/install/install.sh (need >= 1.0.12) then 'dpm install 3.4.11'" >&2; exit 1; }
command -v java >/dev/null 2>&1 || { echo "error: 'java' not found on PATH (JDK 17+ required)" >&2; exit 1; }

DPM_VERSION="$(dpm --version 2>/dev/null | awk '/^version:/ {print $2; exit}' || true)"
if [[ -z "${DPM_VERSION}" ]]; then
  echo "warning: could not parse 'dpm --version' output — skipping the >= 1.0.12 floor check" >&2
elif [[ "$(printf '%s\n%s\n' "1.0.12" "${DPM_VERSION}" | sort -V | head -1)" != "1.0.12" ]]; then
  echo "error: 'dpm' ${DPM_VERSION} is too old — install from https://get.digitalasset.com/install/install.sh (need >= 1.0.12) then 'dpm install 3.4.11'" >&2
  exit 1
fi

echo "[codegen] dpm build ${DAML_DIR}"
( cd "${DAML_DIR}" && dpm build )
DAR="$(ls -t "${DAML_DIR}/.daml/dist/"*.dar | head -1)"
echo "[codegen] built ${DAR}"

echo "[codegen] dpm codegen-cs -> ${OUT_DIR}"
TMP_OUT="$(mktemp -d "${OUT_DIR}.tmp.XXXXXX")"
trap 'rm -rf "${TMP_OUT}"' EXIT
( cd "${CODEGEN_DIR}" && DPM_AUTO_INSTALL=true dpm codegen-cs --dar "${DAR}" --out "${TMP_OUT}" --contract-identifiers )
rm -rf "${OUT_DIR}"
mkdir -p "$(dirname "${OUT_DIR}")"
mv "${TMP_OUT}" "${OUT_DIR}"
trap - EXIT

echo "[codegen] done. Generated:"
find "${OUT_DIR}" -name "*.cs"
