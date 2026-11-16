#!/usr/bin/env bash
# Fetches the CC0 MakeHuman base data (the asset half of makehumancommunity/mpfb2: src/mpfb/data, licence LICENSE.ASSETS.md = CC0 1.0) at a pinned commit, for people_kit.
# Only the data folder is checked out; no MPFB program code (GPL) is used by this project. Prints the data path for ARO_MAKEHUMAN_DATA.
# Usage: tools/blender/people_kit/fetch_makehuman.sh <directory>
set -euo pipefail
COMMIT=9de0707fdc88438d7c53a1a2ac7937d9221e2431            # pinned: the people are deterministic only on the same data (docs/PEOPLE_AND_CROWDS.md)
DEST=${1:?usage: fetch_makehuman.sh <directory>}
for i in 1 2 3 4; do
  rm -rf "$DEST"; mkdir -p "$DEST"
  if ( cd "$DEST" && git init -q && git remote add origin https://github.com/makehumancommunity/mpfb2.git \
       && git sparse-checkout set --no-cone /src/mpfb/data/ /LICENSE.ASSETS.md \
       && git fetch -q --depth 1 --filter=blob:none origin "$COMMIT" && git checkout -q FETCH_HEAD ); then break; fi
  [ "$i" = 4 ] && { echo "could not fetch mpfb2 $COMMIT" >&2; exit 1; }
  echo "fetch of mpfb2 failed (attempt $i); retrying" >&2; sleep $((i * 15))
done
test -f "$DEST/src/mpfb/data/3dobjs/base.obj" && grep -q "CC0 1.0" "$DEST/LICENSE.ASSETS.md"
echo "$DEST/src/mpfb/data"
