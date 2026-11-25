#!/usr/bin/env bash
# Fetches the managed FNA sources (tModLoader's fork) at the commit pinned in
# native/build-android-natives.sh into native/src/FNA, with the submodules FNA.Core.csproj needs.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMMIT="$(sed -n 's/^FNA_COMMIT="\([0-9a-f]*\)".*/\1/p' "$ROOT/native/build-android-natives.sh")"
DIR="$ROOT/native/src/FNA"
[[ -n "$COMMIT" ]] || { echo "FNA_COMMIT not found" >&2; exit 1; }
if [[ ! -d "$DIR/.git" ]]; then
  git init -q "$DIR"
  git -C "$DIR" remote add origin https://github.com/tModLoader/FNA
fi
git -C "$DIR" fetch -q --depth 1 origin "$COMMIT"
git -C "$DIR" checkout -q FETCH_HEAD
git -C "$DIR" submodule update -q --init --depth 1 lib/SDL2-CS lib/FAudio lib/Theorafile
echo "FNA $COMMIT -> $DIR"
