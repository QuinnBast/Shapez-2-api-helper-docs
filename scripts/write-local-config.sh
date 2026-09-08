#!/usr/bin/env bash
# Produces docfx.metadata.local.json by substituting this machine's game paths
# into the committed docfx.metadata.json template.
#
# Requires:
#   SPZ2_PATH     directory containing the game's managed assemblies
#   SPZ2_SHIFTER  full path to ShapezShifter.dll
#
# Both are set automatically on Windows by running:
#   "shapez 2.exe" --set-modding-env-vars

set -euo pipefail

cd "$(dirname "$0")/.."

: "${SPZ2_PATH:?SPZ2_PATH is not set}"
: "${SPZ2_SHIFTER:?SPZ2_SHIFTER is not set}"

if [ ! -f "$SPZ2_PATH/SPZGameAssembly.dll" ]; then
  echo "error: SPZGameAssembly.dll not found in SPZ2_PATH ($SPZ2_PATH)" >&2
  exit 1
fi

if [ ! -f "$SPZ2_SHIFTER" ]; then
  echo "error: ShapezShifter.dll not found at SPZ2_SHIFTER ($SPZ2_SHIFTER)" >&2
  exit 1
fi

SHIFTER_DIR=$(dirname "$SPZ2_SHIFTER")

# DocFX does not expand environment variables in its config, so substitute.
# Forward slashes work on every platform DocFX runs on.
PATH_FWD=${SPZ2_PATH//\\//}
SHIFTER_FWD=${SHIFTER_DIR//\\//}

sed -e "s|__SPZ2_PATH__|${PATH_FWD}|g" \
    -e "s|__SPZ2_SHIFTER_DIR__|${SHIFTER_FWD}|g" \
    docfx.metadata.json > docfx.metadata.local.json

echo "wrote docfx.metadata.local.json"
echo "  assemblies : $PATH_FWD"
echo "  shifter    : $SHIFTER_FWD"
