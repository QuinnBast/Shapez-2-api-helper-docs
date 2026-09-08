#!/usr/bin/env bash
# Regenerates the API metadata from the local game install and packs it into
# api-metadata.tar.gz for upload as a release asset.
#
# Run this after a shapez 2 or ShapezShifter update, then:
#   gh release upload api-metadata api-metadata.tar.gz --clobber
# (first time: gh release create api-metadata api-metadata.tar.gz \
#      --title "API metadata" --notes "Generated DocFX metadata")
#
# No gh CLI? Upload the file to the `api-metadata` release in the browser.

set -euo pipefail

cd "$(dirname "$0")/.."

export DOTNET_ROLL_FORWARD=${DOTNET_ROLL_FORWARD:-LatestMajor}

bash scripts/write-local-config.sh

echo "==> generating metadata (a few minutes)"
rm -f api/*.yml api/.manifest
docfx metadata docfx.metadata.local.json

count=$(ls api/*.yml | wc -l)
if [ "$count" -lt 100 ]; then
  echo "error: only $count metadata files generated - something went wrong" >&2
  exit 1
fi

echo "==> packing bundle"
# api/index.md is committed source, not generated output.
tar --exclude='api/index.md' -czf api-metadata.tar.gz api

size=$(du -h api-metadata.tar.gz | cut -f1)
echo
echo "api-metadata.tar.gz  ($size, $count types)"
echo "next: gh release upload api-metadata api-metadata.tar.gz --clobber"
