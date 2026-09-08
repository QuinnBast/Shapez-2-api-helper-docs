#!/usr/bin/env bash
# One command to get the API metadata bundle onto the `api-metadata` release, which is
# where the hosted Pages workflow downloads it from.
#
#   scripts/publish-api-metadata.sh              # pack if needed, then upload
#   scripts/publish-api-metadata.sh --refresh    # always regenerate first
#
# Run it after a shapez 2 or ShapezShifter update. Nothing else needs doing: the docs
# themselves build from source on every push, and only this bundle depends on having the
# game installed locally.

set -euo pipefail

cd "$(dirname "$0")/.."

TAG=api-metadata
FILE=api-metadata.tar.gz
REPO_URL=$(git remote get-url origin 2>/dev/null || echo "")
SLUG=$(printf '%s' "$REPO_URL" | sed -E 's#.*github\.com[:/]##; s#\.git$##')

refresh=false
if [ "${1:-}" = "--refresh" ]; then
  refresh=true
fi

if [ "$refresh" = true ] || [ ! -f "$FILE" ]; then
  echo "==> regenerating the bundle from the local game install"
  bash scripts/refresh-api-metadata.sh
else
  echo "==> using the existing $FILE ($(du -h "$FILE" | cut -f1), built $(date -r "$FILE" '+%Y-%m-%d %H:%M'))"
  echo "    pass --refresh to rebuild it from the game first"
fi

manual_instructions() {
  cat <<INSTRUCTIONS

Upload it by hand instead - it only takes a moment:

  1. open  https://github.com/$SLUG/releases/new
  2. tag:  $TAG          (pick "Create new tag" if it does not exist yet)
  3. title: API metadata
  4. attach: $(if command -v cygpath >/dev/null 2>&1; then cygpath -w "$(pwd)/$FILE"; else echo "$(pwd)/$FILE"; fi)
  5. publish

If the $TAG release already exists, edit it and replace the attached file.

INSTRUCTIONS
}

# A freshly installed gh will not be on the PATH of an already-running shell - Windows
# hands the updated PATH only to newly spawned processes - so look where it installs to as
# well as asking the PATH.
find_gh() {
  if command -v gh >/dev/null 2>&1; then
    command -v gh
    return 0
  fi

  for candidate in     "${PROGRAMFILES:-C:/Program Files}/GitHub CLI/gh.exe"     "C:/Program Files (x86)/GitHub CLI/gh.exe"     "${LOCALAPPDATA:-}/Microsoft/WinGet/Links/gh.exe"     "${LOCALAPPDATA:-}/Programs/GitHub CLI/gh.exe"
  do
    if [ -x "$candidate" ]; then
      printf '%s' "$candidate"
      return 0
    fi
  done

  return 1
}

GH=$(find_gh || true)

if [ -z "$GH" ]; then
  echo
  echo "gh (the GitHub CLI) was not found, so this script cannot upload for you."
  echo "Install it with:  winget install --id GitHub.cli -e"
  manual_instructions
  exit 1
fi

echo "==> using $GH"

if ! "$GH" auth status >/dev/null 2>&1; then
  echo
  echo "gh is installed but has no stored credentials."
  echo
  echo "Interactive login (needs a real terminal - IDE terminals often break the device"
  echo "flow, which leaves gh with no token even though the browser step succeeded):"
  echo
  echo "    \"$GH\" auth login"
  echo
  echo "Or skip the flow entirely with a token - works in any shell, including scripts."
  echo "Create a fine-grained token with 'Contents: read and write' on this repo at"
  echo "https://github.com/settings/tokens?type=beta then:"
  echo
  echo "    export GH_TOKEN=github_pat_...      # PowerShell: \$env:GH_TOKEN='github_pat_...'"
  echo "    bash scripts/publish-api-metadata.sh"
  manual_instructions
  exit 1
fi

if "$GH" release view "$TAG" >/dev/null 2>&1; then
  echo "==> replacing the asset on the existing $TAG release"
  "$GH" release upload "$TAG" "$FILE" --clobber
else
  echo "==> creating the $TAG release"
  "$GH" release create "$TAG" "$FILE"     --title "API metadata"     --notes "DocFX metadata generated from the game assemblies. Refreshed with scripts/publish-api-metadata.sh."
fi

echo
echo "done - the Pages workflow can now find $FILE."
echo "If this is the first run, also set Settings -> Pages -> Source to 'GitHub Actions',"
echo "then re-run the failed workflow."
