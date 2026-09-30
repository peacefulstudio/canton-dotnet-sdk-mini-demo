#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
#
# Builds the Daml package and regenerates the committed C# bindings under
# src/MiniDemo.Contracts/Generated via dpm build + dpm codegen-cs.
# Requires:
#   - dpm  >= 1.0.20  (oci:// component URIs; https://get.digitalasset.com/install/install.sh, then `dpm install 3.5.2`)
#   - java (JDK 17+, the codegen component's bundled JVM helper decodes the DAR)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DAML_DIR="${REPO_ROOT}/daml"
CODEGEN_DIR="${REPO_ROOT}/codegen"
OUT_DIR="${REPO_ROOT}/src/MiniDemo.Contracts/Generated"

command -v dpm  >/dev/null 2>&1 || { echo "error: 'dpm' not found on PATH — install from https://get.digitalasset.com/install/install.sh (need >= 1.0.20) then 'dpm install 3.5.2'" >&2; exit 1; }
command -v java >/dev/null 2>&1 || { echo "error: 'java' not found on PATH (JDK 17+ required)" >&2; exit 1; }

DPM_VERSION="$(dpm --version 2>/dev/null | awk '/^version:/ {print $2; exit}' || true)"
if [[ -z "${DPM_VERSION}" ]]; then
  echo "warning: could not parse 'dpm --version' output — skipping the >= 1.0.20 floor check" >&2
elif [[ "$(printf '%s\n%s\n' "1.0.20" "${DPM_VERSION}" | sort -V | head -1)" != "1.0.20" ]]; then
  echo "error: 'dpm' ${DPM_VERSION} is too old — install from https://get.digitalasset.com/install/install.sh (need >= 1.0.20) then 'dpm install 3.5.2'" >&2
  exit 1
fi

COMPONENT_URI="$(sed -n 's#^[[:space:]]*-[[:space:]]*\(oci://.*dpm-codegen-cs[^[:space:]]*\).*#\1#p' "${CODEGEN_DIR}/daml.yaml" | head -1)"
[[ -n "${COMPONENT_URI}" ]] || { echo "error: no dpm-codegen-cs component pinned in ${CODEGEN_DIR}/daml.yaml" >&2; exit 1; }
COMPONENT_NAME="$(printf '%s' "${COMPONENT_URI}" | sed -e 's#^oci://##' -e 's#[:@].*$##')"
COMPONENT_CACHE="${DPM_HOME:-${HOME}/.dpm}/cache/components/${COMPONENT_NAME}"
COMPONENT_MARKER="${COMPONENT_CACHE}/.pinned-uri"
if [[ -d "${COMPONENT_CACHE}" && "$(cat "${COMPONENT_MARKER}" 2>/dev/null)" != "${COMPONENT_URI}" ]]; then
  echo "[codegen] cached ${COMPONENT_NAME} does not match the pinned ${COMPONENT_URI##*/}; refreshing it"
  # Workaround: dpm keys its component cache by component name, not tag or digest, so a bumped pin keeps running the old emitter.
  rm -rf "${COMPONENT_CACHE}"
fi

PACKAGE_NAME="$("${REPO_ROOT}/scripts/compute-package-name.sh")"
sed -i.bak "s/^name: .*/name: ${PACKAGE_NAME}/" "${DAML_DIR}/daml.yaml"
rm -f "${DAML_DIR}/daml.yaml.bak"
echo "[codegen] daml.yaml name -> ${PACKAGE_NAME}"

echo "[codegen] dpm build ${DAML_DIR}"
( cd "${DAML_DIR}" && dpm build )
DAR="$(ls -t "${DAML_DIR}/.daml/dist/"*.dar | head -1)"
echo "[codegen] built ${DAR}"

echo "[codegen] dpm codegen-cs -> ${OUT_DIR}"
TMP_OUT="$(mktemp -d "${OUT_DIR}.tmp.XXXXXX")"
trap 'rm -rf "${TMP_OUT}"' EXIT
( cd "${CODEGEN_DIR}" && DPM_AUTO_INSTALL=true dpm codegen-cs --dar "${DAR}" --out "${TMP_OUT}" --namespace MiniDemo.Asset )
printf '%s\n' "${COMPONENT_URI}" > "${COMPONENT_MARKER}"
rm -rf "${OUT_DIR}"
mkdir -p "$(dirname "${OUT_DIR}")"
mv "${TMP_OUT}" "${OUT_DIR}"
trap - EXIT

echo "[codegen] done. Generated:"
find "${OUT_DIR}" -name "*.cs"
